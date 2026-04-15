using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ProgressionFramework.Quests;

/// <summary>
/// Loads quest definitions from <c>config/quests/*.json</c> and tracks state.
///
/// Per-quest <see cref="QuestScope"/> decides where state lives:
///  - <b>Player</b>: in the player's <c>WatchedAttributes["progressionframework:questlog"]</c>
///    tree. Mirrored to player-scope <see cref="VariablesModSystem"/> variables so
///    dialog conditions like <c>player.quest-&lt;code&gt;-status</c> work.
///  - <b>Server</b>: in the system's own <see cref="serverQuestState"/> dictionary,
///    persisted via <see cref="Vintagestory.API.Server.IWorldManagerAPI.SaveGame"/>.
///    Mirrored to global-scope variables so dialog conditions like
///    <c>global.quest-&lt;code&gt;-status</c> work.
/// </summary>
public class QuestSystem : ModSystem
{
    public const string PlayerQuestTreeKey = "progressionframework:questlog";
    private const string ServerSaveKey = "progressionframework:serverquests";

    public Dictionary<string, Quest> QuestsByCode { get; private set; } = new();

    /// <summary>Codes of quests that have been globally disabled at runtime.</summary>
    private readonly HashSet<string> disabledQuests = new();

    /// <summary>Source-of-truth state for server-scoped quests, keyed by quest code.</summary>
    private readonly Dictionary<string, TreeAttribute> serverQuestState = new();

    /// <summary>Client-side mirror of <see cref="serverQuestState"/>. Populated by
    /// <see cref="ServerQuestSnapshot"/> and <see cref="ServerQuestUpdate"/> packets
    /// registered in Task 3. Empty on the server.</summary>
    private readonly Dictionary<string, TreeAttribute> clientServerQuestState = new();

    private readonly Dictionary<string, IQuestObjectiveHandler> objectiveHandlers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IQuestRewardHandler> rewardHandlers =
        new(StringComparer.OrdinalIgnoreCase);

    private ICoreAPI? api;
    private ICoreServerAPI? sapi;

    // --- Hooks for future logbook GUI / notifications ---
    public event Action<IServerPlayer, Quest>? QuestStarted;
    public event Action<IServerPlayer, Quest, QuestObjective>? ObjectiveProgressed;
    public event Action<IServerPlayer, Quest, QuestObjective>? ObjectiveCompleted;
    public event Action<IServerPlayer, Quest>? QuestCompleted;

    public override bool ShouldLoad(EnumAppSide side) => true;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        RegisterRewardType("addSellingItem", new BuiltInRewardHandlers.AddSellingItem());
        RegisterRewardType("giveItem", new BuiltInRewardHandlers.GiveItem());
        RegisterRewardType("awardTrainingXP", new BuiltInRewardHandlers.AwardTrainingXP());
        RegisterRewardType("incrementEntityVariable", new BuiltInRewardHandlers.IncrementEntityVariable());

        RegisterObjectiveType("delivery", new BuiltInObjectiveHandlers.Delivery());

        api.Network.RegisterChannel("progressionframework:questsync")
            .RegisterMessageType<ServerQuestSnapshot>()
            .RegisterMessageType<ServerQuestUpdate>();
    }

    public void RegisterObjectiveType(string type, IQuestObjectiveHandler handler)
    {
        if (objectiveHandlers.ContainsKey(type))
            api?.Logger.Warning("[Quest] Objective type '{0}' re-registered; overwriting.", type);
        objectiveHandlers[type] = handler;
    }

    public void RegisterRewardType(string type, IQuestRewardHandler handler)
    {
        if (rewardHandlers.ContainsKey(type))
            api?.Logger.Warning("[Quest] Reward type '{0}' re-registered; overwriting.", type);
        rewardHandlers[type] = handler;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        api.Event.SaveGameLoaded += LoadServerState;
        api.Event.GameWorldSave += SaveServerState;
        api.Event.PlayerNowPlaying += OnPlayerNowPlayingSendSnapshot;
    }

    private void OnPlayerNowPlayingSendSnapshot(IServerPlayer player)
    {
        if (sapi == null) return;
        var channel = sapi.Network.GetChannel("progressionframework:questsync");
        if (channel == null) return;

        var msg = new ServerQuestSnapshot();
        foreach (var (code, tree) in serverQuestState)
        {
            msg.States[code] = SerializeTree(tree);
        }
        channel.SendPacket(msg, player);
    }

    /// <summary>Broadcast an updated server-quest tree to all connected players.
    /// Called from <see cref="MarkDirty"/> for server-scope quests.</summary>
    private void MarkServerQuestDirty(string code)
    {
        if (sapi == null) return;
        var channel = sapi.Network.GetChannel("progressionframework:questsync");
        if (channel == null) return;

        byte[]? bytes = serverQuestState.TryGetValue(code, out var tree)
            ? SerializeTree(tree) : null;
        channel.BroadcastPacket(new ServerQuestUpdate { Code = code, StateBytes = bytes });
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        var channel = api.Network.GetChannel("progressionframework:questsync");
        if (channel == null) return;
        channel.SetMessageHandler<ServerQuestSnapshot>(OnSnapshotReceived);
        channel.SetMessageHandler<ServerQuestUpdate>(OnUpdateReceived);
    }

    /// <summary>Fired after client-side <see cref="clientServerQuestState"/> changes
    /// due to an incoming snapshot or update. GUI listens for this to recompose.</summary>
    public event System.Action? ServerQuestStateChanged;

    private void OnSnapshotReceived(ServerQuestSnapshot msg)
    {
        clientServerQuestState.Clear();
        foreach (var (code, bytes) in msg.States)
        {
            clientServerQuestState[code] = DeserializeTree(bytes);
        }
        ServerQuestStateChanged?.Invoke();
    }

    private void OnUpdateReceived(ServerQuestUpdate msg)
    {
        if (msg.StateBytes == null) clientServerQuestState.Remove(msg.Code);
        else clientServerQuestState[msg.Code] = DeserializeTree(msg.StateBytes);
        ServerQuestStateChanged?.Invoke();
    }

    private static TreeAttribute DeserializeTree(byte[] bytes)
    {
        var tree = new TreeAttribute();
        using var ms = new System.IO.MemoryStream(bytes);
        using var reader = new System.IO.BinaryReader(ms);
        tree.FromBytes(reader);
        return tree;
    }

    private static byte[] SerializeTree(TreeAttribute tree)
    {
        using var ms = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(ms);
        tree.ToBytes(writer);
        return ms.ToArray();
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);
        this.api = api;
        LoadQuests(api);
    }

    private void LoadQuests(ICoreAPI api)
    {
        QuestsByCode.Clear();

        var assets = api.Assets.GetMany("config/quests/");
        int loaded = 0;
        foreach (var asset in assets)
        {
            try
            {
                var token = JToken.Parse(asset.ToText());
                var quest = token.ToObject<Quest>();
                if (quest == null || string.IsNullOrEmpty(quest.Code))
                {
                    api.Logger.Warning("[Quest] Skipping malformed quest at {0}", asset.Location);
                    continue;
                }

                // Bare codes get prefixed with the *source* asset's domain so cross-mod
                // clashes are impossible. (Previously the system's own ModID was used,
                // which broke when loading quests from a different mod.)
                if (!quest.Code.Contains(':'))
                {
                    quest.Code = asset.Location.Domain + ":" + quest.Code;
                }

                if (QuestsByCode.ContainsKey(quest.Code))
                {
                    api.Logger.Warning("[Quest] Duplicate quest code '{0}' at {1}; keeping first-loaded.",
                        quest.Code, asset.Location);
                    continue;
                }

                QuestsByCode[quest.Code] = quest;
                loaded++;
            }
            catch (Exception e)
            {
                api.Logger.Error("[Quest] Failed to parse {0}: {1}", asset.Location, e.Message);
            }
        }

        api.Logger.Notification("[Quest] Loaded {0} quests", loaded);
    }

    /// <summary>
    /// Resolves a quest code that may or may not carry a mod prefix. Bare codes
    /// are looked up across all loaded mod domains in <see cref="QuestsByCode"/>;
    /// if exactly one match is found it is returned, otherwise the bare code is
    /// returned unchanged (the caller's lookup will fail gracefully).
    /// </summary>
    private string ResolveCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return code;
        if (code.Contains(':')) return code;

        // Bare code — try to disambiguate across loaded mod domains.
        // Matches "<anyDomain>:<code>" exactly (not suffix-wise, to avoid false
        // positives like "foo-bar" matching "bar").
        string suffix = ":" + code;
        string? match = null;
        foreach (var key in QuestsByCode.Keys)
        {
            if (!key.EndsWith(suffix, StringComparison.Ordinal)) continue;
            // Ensure the part before suffix has no colon (i.e., key is exactly "<domain>:<code>")
            int boundary = key.Length - suffix.Length;
            if (boundary < 1) continue;
            if (key.IndexOf(':', 0, boundary) >= 0) continue;

            if (match != null)
            {
                // Ambiguous — two different domains define the same bare code.
                api?.Logger.Warning("[Quest] Bare code '{0}' is ambiguous across domains; pass a fully-qualified code.", code);
                return code;
            }
            match = key;
        }
        return match ?? code;
    }

    /// <summary>
    /// Converts a quest code into a form safe to use as a <see cref="VariablesModSystem"/>
    /// variable name (colons replaced with dashes). Used when mirroring quest state to
    /// flat <c>player.*</c> or <c>global.*</c> variables so dialog conditions stay readable.
    /// </summary>
    private static string CodeToVarName(string code) => code.Replace(':', '-');

    // -----------------------------------------------------------------
    // Server-scope state persistence
    // -----------------------------------------------------------------

    private void LoadServerState()
    {
        if (sapi == null) return;
        byte[]? bytes;
        try { bytes = sapi.WorldManager.SaveGame.GetData(ServerSaveKey); }
        catch { bytes = null; }
        if (bytes == null || bytes.Length == 0) return;

        try
        {
            var tree = new TreeAttribute();
            using (var ms = new System.IO.MemoryStream(bytes))
            using (var reader = new System.IO.BinaryReader(ms))
            {
                tree.FromBytes(reader);
            }

            foreach (var key in tree.Keys.ToArray())
            {
                if (tree[key] is TreeAttribute sub) serverQuestState[key] = sub;
            }
            sapi.Logger.Notification("[Quest] Loaded {0} server quest entries", serverQuestState.Count);

            // Rehydrate global variables so dialog conditions see the current state
            foreach (var (code, _) in serverQuestState)
            {
                if (QuestsByCode.TryGetValue(code, out var quest) && quest.Scope == QuestScope.Server)
                {
                    SyncFlat(null, quest);
                }
            }
        }
        catch (Exception e)
        {
            sapi.Logger.Error("[Quest] Failed to load server quest state: {0}\n{1}", e.Message, e.StackTrace);
        }
    }

    private void SaveServerState()
    {
        if (sapi == null) return;
        var root = new TreeAttribute();
        foreach (var (code, tree) in serverQuestState)
        {
            root[code] = tree;
        }
        using var ms = new System.IO.MemoryStream();
        using (var writer = new System.IO.BinaryWriter(ms))
        {
            root.ToBytes(writer);
        }
        sapi.WorldManager.SaveGame.StoreData(ServerSaveKey, ms.ToArray());
    }

    // -----------------------------------------------------------------
    // Enable / disable
    // -----------------------------------------------------------------

    public void EnableQuest(string code) => disabledQuests.Remove(ResolveCode(code));
    public void DisableQuest(string code) => disabledQuests.Add(ResolveCode(code));
    public bool IsDisabled(string code) => disabledQuests.Contains(ResolveCode(code));

    // -----------------------------------------------------------------
    // State access
    // -----------------------------------------------------------------

    public Quest? GetQuest(string code) =>
        QuestsByCode.TryGetValue(ResolveCode(code), out var q) ? q : null;

    public QuestStatus GetStatus(IPlayer? player, string code)
    {
        code = ResolveCode(code);
        if (IsDisabled(code)) return QuestStatus.Disabled;
        var tree = GetQuestNodeReadonly(player, code);
        if (tree == null) return QuestStatus.Unavailable;
        return ParseStatus(tree.GetString("status", "unavailable"));
    }

    public int GetObjectiveProgress(IPlayer? player, string questCode, string objectiveCode)
    {
        var tree = GetQuestNodeReadonly(player, ResolveCode(questCode));
        return tree?.GetTreeAttribute("objectives")?.GetTreeAttribute(objectiveCode)?.GetInt("progress") ?? 0;
    }

    public bool IsObjectiveComplete(IPlayer? player, string questCode, string objectiveCode)
    {
        var tree = GetQuestNodeReadonly(player, ResolveCode(questCode));
        var objTree = tree?.GetTreeAttribute("objectives")?.GetTreeAttribute(objectiveCode);
        return objTree?.GetString("status") == "completed";
    }

    /// <summary>
    /// True when the quest can be started for this player. For server quests, the
    /// current global status must not already be Active/Completed and prereqs must
    /// be satisfied (server prereqs check server status; player prereqs check this
    /// player's status).
    /// </summary>
    public bool IsAvailable(IServerPlayer player, string code)
    {
        var quest = GetQuest(code);
        if (quest == null || IsDisabled(code)) return false;

        var status = GetStatus(player, code);
        if (status == QuestStatus.Active || status == QuestStatus.Completed) return false;

        foreach (var prereq in quest.Prerequisites)
        {
            var prereqQuest = GetQuest(prereq);
            if (prereqQuest == null) return false;
            // Prereq status is looked up in that quest's own scope — ask with this player;
            // Server-scoped prereqs will ignore the player arg internally.
            if (GetStatus(player, prereq) != QuestStatus.Completed) return false;
        }
        return true;
    }

    // -----------------------------------------------------------------
    // State transitions
    // -----------------------------------------------------------------

    public bool StartQuest(IServerPlayer player, string code)
    {
        code = ResolveCode(code);
        var quest = GetQuest(code);
        if (quest == null) return false;

        var status = GetStatus(player, code);
        if (status == QuestStatus.Active || status == QuestStatus.Completed) return false;
        if (IsDisabled(code)) return false;

        var tree = GetQuestNode(player, code, create: true)!;
        tree.SetString("status", "active");
        tree.SetLong("startedAt", player.Entity.World.ElapsedMilliseconds);
        MarkDirty(player, quest);
        SyncFlat(player, quest);

        QuestStarted?.Invoke(player, quest);
        api?.Logger.Notification("[Quest] {0} started by {1} (scope={2})",
            code, player.PlayerName, quest.Scope);
        return true;
    }

    public bool TryDeliver(IServerPlayer player, string questCode, string objectiveCode, Entity npc)
    {
        questCode = ResolveCode(questCode);
        var quest = GetQuest(questCode);
        if (quest == null)
        {
            api?.Logger.Warning("[Quest] TryDeliver: unknown quest '{0}'", questCode);
            return false;
        }
        var status = GetStatus(player, questCode);
        if (status != QuestStatus.Active)
        {
            api?.Logger.Warning("[Quest] TryDeliver: quest '{0}' is {1}, not active", questCode, status);
            return false;
        }

        var objective = quest.Objectives.Find(o => o.Code == objectiveCode);
        if (objective == null)
        {
            api?.Logger.Warning("[Quest] TryDeliver: no objective '{0}' in quest '{1}'", objectiveCode, questCode);
            return false;
        }
        if (IsObjectiveComplete(player, questCode, objectiveCode))
        {
            api?.Logger.Notification("[Quest] TryDeliver: objective '{0}/{1}' already complete", questCode, objectiveCode);
            return false;
        }
        if (!objective.Type.Equals("delivery", StringComparison.OrdinalIgnoreCase))
        {
            api?.Logger.Warning("[Quest] TryDeliver: objective '{0}/{1}' is type {2}, not Delivery",
                questCode, objectiveCode, objective.Type);
            return false;
        }

        var objTree = GetOrAddObjectiveTree(player, quest, objectiveCode);
        int currentProgress = objTree.GetInt("progress");
        int remainingNeeded = objective.Required - currentProgress;

        // Batch the turn-in: take as many full sets of required items as the
        // player can cover, capped at the remaining threshold. One click on
        // "deliver boards" with 64 planks in the bag counts as 64 deliveries,
        // not 64 clicks.
        int maxTurnIns = int.MaxValue;
        foreach (var req in objective.Items)
        {
            if (req.Quantity <= 0) continue;
            int have = CountInInventory(player, req.Item);
            int canDo = have / req.Quantity;
            if (canDo < maxTurnIns) maxTurnIns = canDo;
        }

        int turnIns = Math.Min(maxTurnIns, remainingNeeded);
        if (turnIns <= 0)
        {
            api?.Logger.Notification("[Quest] TryDeliver: {0} missing items for '{1}/{2}'",
                player.PlayerName, questCode, objectiveCode);
            return false;
        }

        foreach (var req in objective.Items)
        {
            TakeFromInventory(player, req.Item, req.Quantity * turnIns);
        }

        int progress = currentProgress + turnIns;
        objTree.SetInt("progress", progress);

        api?.Logger.Notification("[Quest] TryDeliver: {0} turned in {1}x '{2}/{3}' ({4}/{5})",
            player.PlayerName, turnIns, questCode, objectiveCode, progress, objective.Required);

        ObjectiveProgressed?.Invoke(player, quest, objective);

        if (progress >= objective.Required)
        {
            objTree.SetString("status", "completed");
            ApplyRewards(player, objective.Rewards, quest, npc);
            ObjectiveCompleted?.Invoke(player, quest, objective);
        }

        MarkDirty(player, quest);
        SyncFlat(player, quest);

        if (AllObjectivesComplete(player, quest))
        {
            CompleteQuest(player, quest, npc);
        }

        return true;
    }

    private void CompleteQuest(IServerPlayer player, Quest quest, Entity npc)
    {
        var tree = GetQuestNode(player, quest.Code, create: true)!;
        if (tree.GetString("status") == "completed") return;

        tree.SetString("status", "completed");
        tree.SetLong("completedAt", player.Entity.World.ElapsedMilliseconds);
        MarkDirty(player, quest);
        SyncFlat(player, quest);

        ApplyRewards(player, quest.Rewards, quest, npc);
        QuestCompleted?.Invoke(player, quest);

        api?.Logger.Notification("[Quest] {0} completed quest {1}", player.PlayerName, quest.Code);
        sapi?.SendMessage(player, 0,
            Lang.Get("seafarer:quest-completed", Lang.Get(quest.TitleLangKey ?? quest.Code)),
            EnumChatType.Notification);
    }

    private bool AllObjectivesComplete(IServerPlayer player, Quest quest)
    {
        foreach (var obj in quest.Objectives)
        {
            if (!IsObjectiveComplete(player, quest.Code, obj.Code)) return false;
        }
        return true;
    }

    // -----------------------------------------------------------------
    // Client-side optimistic mirror
    //
    // Dialog execution runs on both client and server. Our triggers do the
    // authoritative work server-side and then broadcast VarData, but the
    // broadcast is asynchronous — by the time it arrives, the client has
    // already marched past the next condition node in the dialog graph. To
    // avoid the race, the client runs these mirror methods in-place inside
    // the trigger dispatch, writing the same flat-variable outcome to its
    // local VarData so the subsequent condition sees the correct value.
    //
    // Authoritative state (the quest tree, inventory, shop patches) still
    // lives server-side only. If client and server ever disagree on the
    // computed batch size (net lag), the server's next VarData broadcast
    // reconciles the discrepancy.
    // -----------------------------------------------------------------

    public void MirrorStartOnClient(EntityPlayer playerEntity, string code)
    {
        code = ResolveCode(code);
        var quest = GetQuest(code);
        if (quest == null) return;

        var varSys = api?.ModLoader.GetModSystem<VariablesModSystem>();
        if (varSys == null) return;

        string varName = $"quest-{CodeToVarName(code)}-status";
        WriteFlatVar(varSys, playerEntity, quest, varName, "active");
    }

    public void MirrorDeliveryOnClient(EntityPlayer playerEntity, string questCode, string objectiveCode)
    {
        questCode = ResolveCode(questCode);
        var quest = GetQuest(questCode);
        if (quest == null) return;
        var objective = quest.Objectives.Find(o => o.Code == objectiveCode);
        if (objective == null || !objective.Type.Equals("delivery", StringComparison.OrdinalIgnoreCase)) return;

        var varSys = api?.ModLoader.GetModSystem<VariablesModSystem>();
        if (varSys == null) return;

        int currentProgress = ReadFlatInt(varSys, playerEntity, quest,
            $"quest-{CodeToVarName(quest.Code)}-{objectiveCode}-progress");
        int remainingNeeded = objective.Required - currentProgress;
        if (remainingNeeded <= 0) return;

        int maxTurnIns = int.MaxValue;
        foreach (var req in objective.Items)
        {
            if (req.Quantity <= 0) continue;
            int have = CountInInventoryEntity(playerEntity, req.Item);
            int canDo = have / req.Quantity;
            if (canDo < maxTurnIns) maxTurnIns = canDo;
        }
        int turnIns = Math.Min(maxTurnIns, remainingNeeded);
        if (turnIns <= 0) return;

        int newProgress = currentProgress + turnIns;
        bool objectiveDone = newProgress >= objective.Required;

        WriteFlatVar(varSys, playerEntity, quest,
            $"quest-{CodeToVarName(quest.Code)}-{objectiveCode}-progress", newProgress.ToString());
        if (objectiveDone)
        {
            WriteFlatVar(varSys, playerEntity, quest,
                $"quest-{CodeToVarName(quest.Code)}-{objectiveCode}-status", "completed");

            // Best-effort: if every objective is now complete in the client's mirror
            // view, flip the quest status. The server will do the same and broadcast;
            // if they disagree, the broadcast wins.
            bool allDone = true;
            foreach (var obj in quest.Objectives)
            {
                string objStatusVar = $"quest-{CodeToVarName(quest.Code)}-{obj.Code}-status";
                string localStatus = obj.Code == objectiveCode
                    ? "completed"
                    : ReadFlatString(varSys, playerEntity, quest, objStatusVar);
                if (localStatus != "completed") { allDone = false; break; }
            }
            if (allDone)
            {
                WriteFlatVar(varSys, playerEntity, quest,
                    $"quest-{CodeToVarName(quest.Code)}-status", "completed");
            }
        }
    }

    private static int CountInInventoryEntity(EntityPlayer playerEntity, string itemCode)
    {
        int count = 0;
        var loc = new AssetLocation(itemCode);
        playerEntity.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Collectible?.Code == loc) count += slot.Itemstack.StackSize;
            return true;
        });
        return count;
    }

    private static void WriteFlatVar(VariablesModSystem varSys, EntityPlayer player, Quest quest, string name, string value)
    {
        if (quest.Scope == QuestScope.Server)
        {
            varSys.VarData.GlobalVariables[name] = value;
        }
        else
        {
            varSys.SetPlayerVariable(player.PlayerUID, name, value);
        }
    }

    private static string ReadFlatString(VariablesModSystem varSys, EntityPlayer player, Quest quest, string name)
    {
        if (quest.Scope == QuestScope.Server)
        {
            return varSys.VarData.GlobalVariables[name] ?? "";
        }
        else
        {
            if (!varSys.VarData.PlayerVariables.TryGetValue(player.PlayerUID, out var pvars)) return "";
            return pvars[name] ?? "";
        }
    }

    private static int ReadFlatInt(VariablesModSystem varSys, EntityPlayer player, Quest quest, string name)
    {
        var s = ReadFlatString(varSys, player, quest, name);
        return int.TryParse(s, out int v) ? v : 0;
    }

    // -----------------------------------------------------------------
    // Reward application
    // -----------------------------------------------------------------

    private void ApplyRewards(IServerPlayer player, List<QuestReward> rewards, Quest quest, Entity npc)
    {
        foreach (var reward in rewards)
        {
            try
            {
                if (rewardHandlers.TryGetValue(reward.Type, out var h))
                {
                    h.Grant(reward, player, npc, api!);
                }
                else
                {
                    api?.Logger.Warning("[Quest] Unknown reward type '{0}' in quest {1}", reward.Type, quest.Code);
                }
            }
            catch (Exception e)
            {
                api?.Logger.Error("[Quest] Failed to apply reward {0} for quest {1}: {2}\n{3}",
                    reward.Type, quest.Code, e.Message, e.StackTrace);
            }
        }
    }

    // -----------------------------------------------------------------
    // Inventory helpers
    // -----------------------------------------------------------------

    private static int CountInInventory(IServerPlayer player, string itemCode)
    {
        int count = 0;
        var loc = new AssetLocation(itemCode);

        // Entity.WalkInventory hits hotbar + backpacks + mouse cursor on the
        // player, and sidesteps quirks in InventoryPlayerCreative whose Count
        // getter NREs when iterated directly.
        player.Entity.WalkInventory(slot =>
        {
            if (slot?.Itemstack?.Collectible?.Code == loc)
            {
                count += slot.Itemstack.StackSize;
            }
            return true;
        });
        return count;
    }

    private static void TakeFromInventory(IServerPlayer player, string itemCode, int quantity)
    {
        var loc = new AssetLocation(itemCode);
        int remaining = quantity;
        player.Entity.WalkInventory(slot =>
        {
            if (remaining <= 0) return false;
            if (slot?.Itemstack?.Collectible?.Code != loc) return true;

            int take = Math.Min(slot.Itemstack.StackSize, remaining);
            slot.TakeOut(take);
            slot.MarkDirty();
            remaining -= take;
            return remaining > 0;
        });
    }

    // -----------------------------------------------------------------
    // State tree helpers — scope-aware
    // -----------------------------------------------------------------

    /// <summary>
    /// Read-only, scope- and side-aware tree lookup for quest state.
    /// Player-scope reads go through <see cref="EntityPlayer.WatchedAttributes"/>
    /// (identical tree on server and client). Server-scope reads go through
    /// <see cref="serverQuestState"/> on the server and
    /// <see cref="clientServerQuestState"/> on the client.
    /// </summary>
    private ITreeAttribute? GetQuestNodeReadonly(IPlayer? player, string code)
    {
        var quest = GetQuest(code);
        if (quest == null) return null;

        if (quest.Scope == QuestScope.Server)
        {
            if (api?.Side == EnumAppSide.Client)
            {
                return clientServerQuestState.TryGetValue(code, out var t) ? t : null;
            }
            return serverQuestState.TryGetValue(code, out var srv) ? srv : null;
        }

        if (player == null) return null;
        var root = player.Entity.WatchedAttributes.GetTreeAttribute(PlayerQuestTreeKey);
        return root?.GetTreeAttribute(code);
    }

    private ITreeAttribute? GetQuestNode(IServerPlayer? player, string code, bool create)
    {
        var quest = GetQuest(code);
        if (quest == null) return null;

        if (quest.Scope == QuestScope.Server)
        {
            if (!serverQuestState.TryGetValue(code, out var srvTree))
            {
                if (!create) return null;
                srvTree = new TreeAttribute();
                serverQuestState[code] = srvTree;
            }
            return srvTree;
        }

        // Player scope
        if (player == null) return null;
        var root = create
            ? player.Entity.WatchedAttributes.GetOrAddTreeAttribute(PlayerQuestTreeKey)
            : player.Entity.WatchedAttributes.GetTreeAttribute(PlayerQuestTreeKey);
        if (root == null) return null;

        var node = root.GetTreeAttribute(code);
        if (node == null && create)
        {
            node = new TreeAttribute();
            root[code] = node;
        }
        return node;
    }

    private ITreeAttribute GetOrAddObjectiveTree(IServerPlayer player, Quest quest, string objectiveCode)
    {
        var questNode = GetQuestNode(player, quest.Code, create: true)!;
        var objectives = questNode.GetTreeAttribute("objectives");
        if (objectives == null)
        {
            objectives = new TreeAttribute();
            questNode["objectives"] = objectives;
        }
        var obj = objectives.GetTreeAttribute(objectiveCode);
        if (obj == null)
        {
            obj = new TreeAttribute();
            obj.SetString("status", "pending");
            obj.SetInt("progress", 0);
            objectives[objectiveCode] = obj;
        }
        return obj;
    }

    private void MarkDirty(IServerPlayer player, Quest quest)
    {
        if (quest.Scope == QuestScope.Player)
        {
            player.Entity.WatchedAttributes.MarkPathDirty(PlayerQuestTreeKey);
        }
        else // QuestScope.Server
        {
            MarkServerQuestDirty(quest.Code);
        }
    }

    /// <summary>
    /// Writes a denormalised copy of quest state to <see cref="VariablesModSystem"/>
    /// (player scope for Player quests, global scope for Server quests) so the dialog
    /// engine's <c>player.&lt;name&gt;</c> / <c>global.&lt;name&gt;</c> condition
    /// syntax can read it.
    ///
    /// The dialog engine evaluates conditions on both client and server against each
    /// side's local <see cref="VariablesModSystem.VarData"/>. Base-game
    /// <c>setVariables</c> directives run on both sides and stay in sync naturally.
    /// Our C# writes only run on the server, so after updating we broadcast the
    /// freshly-changed <see cref="VariableData"/> over the existing "variable"
    /// channel — to the specific player for Player quests, to everyone for Server
    /// quests.
    /// </summary>
    private void SyncFlat(IServerPlayer? player, Quest quest)
    {
        var varSys = api?.ModLoader.GetModSystem<VariablesModSystem>();
        if (varSys == null) return;

        // VarData / GlobalVariables may not be initialised yet at SaveGameLoaded
        // time — base-game VariablesModSystem populates them during its own start-up.
        // Silently skip sync in that window; state will be re-pushed when quests
        // next mutate (AssignQuest, CompleteObjective, etc.) after join.
        if (varSys.VarData == null || varSys.VarData.GlobalVariables == null) return;

        var node = GetQuestNode(player, quest.Code, create: false);
        string status = node?.GetString("status", "unavailable") ?? "unavailable";

        if (quest.Scope == QuestScope.Server)
        {
            varSys.VarData.GlobalVariables[$"quest-{CodeToVarName(quest.Code)}-status"] = status;
        }
        else if (player != null)
        {
            varSys.SetPlayerVariable(player.PlayerUID, $"quest-{CodeToVarName(quest.Code)}-status", status);
        }

        if (node != null)
        {
            var objectives = node.GetTreeAttribute("objectives");
            if (objectives != null)
            {
                foreach (var obj in quest.Objectives)
                {
                    var objTree = objectives.GetTreeAttribute(obj.Code);
                    if (objTree == null) continue;

                    string objStatus = objTree.GetString("status", "pending");
                    string progress = objTree.GetInt("progress").ToString();

                    if (quest.Scope == QuestScope.Server)
                    {
                        varSys.VarData.GlobalVariables[$"quest-{CodeToVarName(quest.Code)}-{obj.Code}-status"] = objStatus;
                        varSys.VarData.GlobalVariables[$"quest-{CodeToVarName(quest.Code)}-{obj.Code}-progress"] = progress;
                    }
                    else if (player != null)
                    {
                        varSys.SetPlayerVariable(player.PlayerUID, $"quest-{CodeToVarName(quest.Code)}-{obj.Code}-status", objStatus);
                        varSys.SetPlayerVariable(player.PlayerUID, $"quest-{CodeToVarName(quest.Code)}-{obj.Code}-progress", progress);
                    }
                }
            }
        }

        BroadcastVarData(varSys, quest, player);
    }

    /// <summary>
    /// Pushes the just-modified <see cref="VariableData"/> down to the affected
    /// client(s) over the "variable" network channel that <see cref="VariablesModSystem"/>
    /// registers on startup. Without this, dialog conditions like
    /// <c>global.quest-...-status</c> only reflect the value snapshotted at
    /// <c>PlayerJoin</c> and never update mid-session.
    /// </summary>
    private void BroadcastVarData(VariablesModSystem varSys, Quest quest, IServerPlayer? player)
    {
        if (sapi == null) return;
        if (varSys.VarData == null) return;  // same reason as in SyncFlat
        var channel = sapi.Network.GetChannel("variable");
        if (channel == null) return;

        if (quest.Scope == QuestScope.Server)
        {
            channel.BroadcastPacket(varSys.VarData);
        }
        else if (player != null)
        {
            channel.SendPacket(varSys.VarData, player);
        }
    }

    private static QuestStatus ParseStatus(string s) => s switch
    {
        "active" => QuestStatus.Active,
        "completed" => QuestStatus.Completed,
        "available" => QuestStatus.Available,
        "disabled" => QuestStatus.Disabled,
        _ => QuestStatus.Unavailable
    };
}
