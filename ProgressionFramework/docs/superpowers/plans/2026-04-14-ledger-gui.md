# Ledger GUI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the existing training-only `GuiDialogTrainingLedger` with a single tabbed `GuiDialogLedger` (Training + Quest Log), fix the flaky `L` hotkey via proper dialog registration, and sync server-scope quest state to clients.

**Architecture:** One Vintage Story `GuiDialog` subclass owns the shell (title bar, tab toggle, recompose). Two per-tab compose helpers build the tab body into the shared composer. `QuestSystem` grows client-side state (mirror dict for server-scope quests) plus a narrow network channel that ships `TreeAttribute` byte blobs straight from the authoritative server dict. Client-facing accessor methods (`GetStatus`, `GetObjectiveProgress`) are widened from `IServerPlayer?` to `IPlayer?` so the GUI uses the same API the server already uses.

**Tech Stack:** C# / .NET 10, Vintagestory API (`ICoreClientAPI`, `GuiDialog`, `GuiComposer`, `INetworkChannel`), HarmonyLib, Newtonsoft.Json, protobuf-net (via `[ProtoContract]`).

**Testing note:** This project has no unit-test harness and GUI code is impractical to unit-test without a running game. Verification uses `dotnet build` for compile checks and scripted in-game manual checks at task boundaries. When a task can be verified without launching the game, it says so; otherwise the manual steps are listed.

**Reference spec:** `docs/superpowers/specs/2026-04-14-ledger-gui-design.md`

---

## File Structure

New files:
- `ProgressionFramework/Client/GuiDialogLedger.cs` — dialog shell, tab state, `ComposeTrainingTab`, delegation to the quest-log helper.
- `ProgressionFramework/Client/LedgerQuestLogTab.cs` — stateless helper with one method `Compose(ICoreClientAPI, QuestSystem, GuiComposer, double width, ref double y)`.
- `ProgressionFramework/Quests/ServerQuestSyncPackets.cs` — two `[ProtoContract]` DTOs.

Modified:
- `ProgressionFramework/Quests/QuestSystem.cs` — widen `IServerPlayer?` accessors to `IPlayer?`, add `clientServerQuestState`, register network channel, add `MarkServerQuestDirty` + `ServerQuestStateChanged` event, route mutations.
- `ProgressionFramework/ProgressionFrameworkModSystem.cs` — rename hotkey id, construct `GuiDialogLedger`, call `api.Gui.RegisterDialog`.
- `ProgressionFramework/assets/progressionframework/lang/en.json` — lang keys.

Removed:
- `ProgressionFramework/Training/Client/GuiDialogTrainingLedger.cs`. Directory `Training/Client/` removed if empty.

All build commands are run from `/mnt/d/Development/vs/vsmod-progression-framework/ProgressionFramework` unless stated otherwise. Export required env var once per shell:

```bash
export VINTAGE_STORY="/mnt/c/Users/alexa/AppData/Roaming/Vintagestory"
```

---

## Task 1: Lang keys

**Files:**
- Modify: `ProgressionFramework/ProgressionFramework/assets/progressionframework/lang/en.json`

- [ ] **Step 1: Replace `en.json` contents**

```json
{
    "ledger-title": "Ledger",
    "ledger-tab-training": "Training",
    "ledger-tab-questlog": "Quest Log",
    "questlog-empty": "No active or completed quests.",
    "questlog-group-other": "Other",
    "questlog-status-active": "Active",
    "questlog-status-completed": "Completed",
    "questlog-shared-subtitle": "(shared)",
    "training-levelup": "You reached: {0}",
    "training-complete": "(Max level)",
    "trainingbook-nosource": "This book has no registered training.",
    "heldhelp-readtolearn": "Read to learn"
}
```

Note: `training-ledger-title` is intentionally removed — only the old `GuiDialogTrainingLedger` referenced it, and that file is deleted in Task 4.

- [ ] **Step 2: Commit**

```bash
git add ProgressionFramework/ProgressionFramework/assets/progressionframework/lang/en.json
git commit -m "Ledger lang keys (rename title, add tab + questlog entries)"
```

---

## Task 2: Widen `QuestSystem` accessors to `IPlayer?`

**Files:**
- Modify: `ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs`

Goal: allow the client to call the same readers the server uses. Three public methods change signatures (`IServerPlayer?` → `IPlayer?`). A new private reader `GetQuestNodeReadonly` replaces the read paths of `GetQuestNode` for the new signatures.

- [ ] **Step 1: Change signatures and add scope-aware reader**

Change the three public readers around lines 255–275 and add a new private helper:

```csharp
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
/// Read-only, scope- and side-aware tree lookup. Player-scope reads go through
/// <see cref="EntityPlayer.WatchedAttributes"/> (identical tree on server and
/// client). Server-scope reads go through <see cref="serverQuestState"/> on the
/// server and <see cref="clientServerQuestState"/> on the client.
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
```

Also declare the client mirror dictionary near the other fields (around line 33):

```csharp
/// <summary>Client-side mirror of <see cref="serverQuestState"/>. Populated by
/// <see cref="ServerQuestSnapshot"/> and <see cref="ServerQuestUpdate"/> packets.
/// Empty on the server.</summary>
private readonly Dictionary<string, TreeAttribute> clientServerQuestState = new();
```

And keep the existing `GetQuestNode(IServerPlayer?, string, bool create)` untouched — it stays private, used only for the mutating server-side paths.

- [ ] **Step 2: Fix any call sites that pass `IServerPlayer?` and rely on method overload resolution**

`IServerPlayer : IPlayer`, so existing callers compile as-is. But one call site uses a parameter typed `IServerPlayer?` directly:

- `IsAvailable` (around line 283) calls `GetStatus(player, code)` and `GetStatus(player, prereq)` — fine, `IServerPlayer` implicitly converts to `IPlayer`.
- `TryDeliver` (around line 337) calls `GetStatus(player, questCode)` — same.
- `IsObjectiveComplete` call in `TryDeliver` (around line 350) — fine.

No other changes needed.

- [ ] **Step 3: Build to confirm compile**

```bash
dotnet build ProgressionFramework/ProgressionFramework.csproj
```

Expected: `Build succeeded` with no errors in `QuestSystem.cs`.

- [ ] **Step 4: Commit**

```bash
git add ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs
git commit -m "Quests: widen read accessors to IPlayer for client-side use"
```

---

## Task 3: Server-scope quest sync — packets + channel + push

**Files:**
- Create: `ProgressionFramework/ProgressionFramework/Quests/ServerQuestSyncPackets.cs`
- Modify: `ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs`

- [ ] **Step 1: Create the packet types**

Create `ProgressionFramework/ProgressionFramework/Quests/ServerQuestSyncPackets.cs`:

```csharp
using System.Collections.Generic;
using ProtoBuf;

namespace ProgressionFramework.Quests;

/// <summary>Full snapshot of all server-scope quest state trees, sent to a
/// client on join. Each value is <see cref="Vintagestory.API.Datastructures.TreeAttribute.ToBytes"/>.</summary>
[ProtoContract]
public class ServerQuestSnapshot
{
    [ProtoMember(1)]
    public Dictionary<string, byte[]> States { get; set; } = new();
}

/// <summary>Incremental update for a single server-scope quest. <see cref="StateBytes"/>
/// null means the quest entry was removed server-side.</summary>
[ProtoContract]
public class ServerQuestUpdate
{
    [ProtoMember(1)]
    public string Code { get; set; } = "";
    [ProtoMember(2)]
    public byte[]? StateBytes { get; set; }
}
```

- [ ] **Step 2: Register the channel in `QuestSystem.Start` / `StartClientSide`**

Edit `Start(ICoreAPI api)` (around line 58). After the handler registrations, add a channel registration that runs on both sides so the message types are known:

```csharp
api.Network.RegisterChannel("progressionframework:questsync")
    .RegisterMessageType<ServerQuestSnapshot>()
    .RegisterMessageType<ServerQuestUpdate>();
```

`ModSystem.Start` runs on both sides before `StartServerSide` / `StartClientSide`, and `ICoreAPI.Network.RegisterChannel` exists on both surfaces.

- [ ] **Step 3: Add client-side message handlers**

`QuestSystem` doesn't currently override `StartClientSide`. Add it below `StartServerSide`:

```csharp
public override void StartClientSide(ICoreClientAPI api)
{
    var channel = api.Network.GetChannel("progressionframework:questsync");
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
```

(Add `using Vintagestory.API.Client;` at the top if not already present.)

- [ ] **Step 4: Add server-side push (`MarkServerQuestDirty`) and join snapshot**

Extend `StartServerSide` (around line 81):

```csharp
public override void StartServerSide(ICoreServerAPI api)
{
    sapi = api;
    api.Event.SaveGameLoaded += LoadServerState;
    api.Event.GameWorldSave += SaveServerState;
    api.Event.PlayerJoin += OnPlayerJoinSendSnapshot;
}

private void OnPlayerJoinSendSnapshot(IServerPlayer player)
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
/// Called from every site that mutates <see cref="serverQuestState"/>.</summary>
private void MarkServerQuestDirty(string code)
{
    if (sapi == null) return;
    var channel = sapi.Network.GetChannel("progressionframework:questsync");
    if (channel == null) return;

    byte[]? bytes = serverQuestState.TryGetValue(code, out var tree)
        ? SerializeTree(tree) : null;
    channel.BroadcastPacket(new ServerQuestUpdate { Code = code, StateBytes = bytes });
}
```

- [ ] **Step 5: Route every server-scope mutation through `MarkServerQuestDirty`**

The only places that write into `serverQuestState` today are inside `GetQuestNode(... create: true)` and `GetOrAddObjectiveTree`, both reached via `StartQuest`, `TryDeliver`, and `CompleteQuest`. Rather than adding a call to all the getters, piggyback on the existing `MarkDirty` method (around line 699) which is already called at every mutation boundary:

```csharp
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
```

That's the only change — `MarkDirty(player, quest)` is already called from `StartQuest`, `TryDeliver`, and `CompleteQuest`.

- [ ] **Step 6: Build**

```bash
dotnet build ProgressionFramework/ProgressionFramework.csproj
```

Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add ProgressionFramework/ProgressionFramework/Quests/QuestSystem.cs \
        ProgressionFramework/ProgressionFramework/Quests/ServerQuestSyncPackets.cs
git commit -m "Quests: sync server-scope state to clients via network channel"
```

---

## Task 4: Shell — `GuiDialogLedger` with tabs, Training tab only

We rebuild the shell and migrate the training compose logic verbatim. Quest Log tab renders only the empty-state string at this point; the real Quest Log rendering comes in Task 5. This lets us verify hotkey + tab switching independently.

**Files:**
- Create: `ProgressionFramework/ProgressionFramework/Client/GuiDialogLedger.cs`
- Delete: `ProgressionFramework/ProgressionFramework/Training/Client/GuiDialogTrainingLedger.cs`
- Modify: `ProgressionFramework/ProgressionFramework/ProgressionFrameworkModSystem.cs`

- [ ] **Step 1: Create `GuiDialogLedger`**

Create `ProgressionFramework/ProgressionFramework/Client/GuiDialogLedger.cs`:

```csharp
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using ProgressionFramework.Training;
using ProgressionFramework.Quests;

namespace ProgressionFramework.Client;

public class GuiDialogLedger : GuiDialog
{
    private enum LedgerTab { Training, QuestLog }

    private readonly TrainingSystem training;
    private readonly QuestSystem quests;
    private readonly Dictionary<string, float[]> barValues = new();

    private LedgerTab currentTab = LedgerTab.Training;

    public override string ToggleKeyCombinationCode => "progressionframeworkledger";
    public override bool PrefersUngrabbedMouse => false;

    public GuiDialogLedger(ICoreClientAPI capi, TrainingSystem training, QuestSystem quests)
        : base(capi)
    {
        this.training = training;
        this.quests = quests;
    }

    public override void OnOwnPlayerDataReceived()
    {
        base.OnOwnPlayerDataReceived();
        capi.World.Player.Entity.WatchedAttributes
            .RegisterModifiedListener(TrainingSystem.TrainingTreeKey, OnDataChanged);
        capi.World.Player.Entity.WatchedAttributes
            .RegisterModifiedListener(QuestSystem.PlayerQuestTreeKey, OnDataChanged);
        quests.ServerQuestStateChanged += OnDataChanged;
    }

    private void OnDataChanged()
    {
        if (IsOpened()) ComposeDialog();
    }

    public override bool TryOpen(bool withFocus = true)
    {
        ComposeDialog();
        return base.TryOpen(withFocus);
    }

    private void ComposeDialog()
    {
        barValues.Clear();
        const double width = 520;

        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        var dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle)
            .WithFixedAlignmentOffset(0, 0);

        var composer = capi.Gui.CreateCompo("progressionframeworkledger", dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(Lang.Get("progressionframework:ledger-title"), () => TryClose())
            .BeginChildElements(bgBounds);

        double y = 0;

        // Tab bar
        var trainingTabBounds = ElementBounds.Fixed(0, y, 140, 26);
        var questlogTabBounds = ElementBounds.Fixed(148, y, 140, 26);
        composer.AddToggleButton(
            Lang.Get("progressionframework:ledger-tab-training"),
            CairoFont.WhiteSmallishText(),
            on => { if (on) SwitchTab(LedgerTab.Training); },
            trainingTabBounds,
            "tab-training");
        composer.AddToggleButton(
            Lang.Get("progressionframework:ledger-tab-questlog"),
            CairoFont.WhiteSmallishText(),
            on => { if (on) SwitchTab(LedgerTab.QuestLog); },
            questlogTabBounds,
            "tab-questlog");
        y += 36;

        if (currentTab == LedgerTab.Training)
        {
            ComposeTrainingTab(composer, width, ref y);
        }
        else
        {
            ComposeQuestLogTab(composer, width, ref y);
        }

        composer.EndChildElements();
        SingleComposer = composer.Compose();

        // Reflect current tab on the toggle buttons
        SingleComposer.GetToggleButton("tab-training").SetValue(currentTab == LedgerTab.Training);
        SingleComposer.GetToggleButton("tab-questlog").SetValue(currentTab == LedgerTab.QuestLog);

        foreach (var kv in barValues)
        {
            var bar = SingleComposer.GetStatbar(kv.Key);
            if (bar != null) bar.SetValues(kv.Value[0], kv.Value[1], kv.Value[2]);
        }
    }

    private void SwitchTab(LedgerTab tab)
    {
        if (currentTab == tab) return;
        currentTab = tab;
        ComposeDialog();
    }

    // ------- Training tab (moved verbatim from GuiDialogTrainingLedger) -------

    private void ComposeTrainingTab(GuiComposer composer, double width, ref double y)
    {
        const double rowH = 22;
        const double barH = 10;
        const double sectionGap = 12;

        bool showAll = training.Config.ShowAllTrainings;
        bool showNumbers = training.Config.ShowXpNumbers;

        foreach (var prof in training.Professions)
        {
            if (!showAll && !HasAnyProgress(prof)) continue;

            var profColor = HexToDoubles(prof.Color);
            var headerFont = CairoFont.WhiteSmallishText().WithColor(profColor);
            var headerBounds = ElementBounds.Fixed(0, y, width, 24);
            string profName = Lang.Get("seafarer:profession-" + prof.Code);
            composer.AddStaticText(profName, headerFont, headerBounds);
            y += 28;

            foreach (var t in prof.Trainings)
            {
                float xp = GetTrainingXp(t.Code);
                int level = GetTrainingLevel(t.Code);
                if (!showAll && xp <= 0 && level <= 0) continue;

                var currentLevel = level > 0 ? t.Levels[level - 1] : null;
                var nextLevel = level < t.Levels.Count ? t.Levels[level] : null;
                bool completed = level >= t.Levels.Count;
                float maxXp = nextLevel?.Xp ?? currentLevel?.Xp ?? 100;

                string icon = completed ? "\u2605" : "\u25CB";
                string trainingName = Lang.Get("seafarer:training-" + t.Code);
                string title = currentLevel?.Title ?? "";
                string iconColor = completed ? "#D4AF37" : "#808080";
                string titleColor = currentLevel?.Color ?? (completed ? "#7ACC7A" : prof.Color);

                string richText = $"<font color=\"{iconColor}\">{icon}</font>  {trainingName}";
                if (!string.IsNullOrEmpty(title))
                {
                    richText += $"  <font color=\"{titleColor}\">- {title}</font>";
                }

                var nameBounds = ElementBounds.Fixed(12, y, width - 24, rowH);
                composer.AddRichtext(richText, CairoFont.WhiteSmallText(), nameBounds);
                y += rowH;

                var barBounds = ElementBounds.Fixed(12, y, width - 24, barH);
                string barKey = "bar-" + t.Code;
                composer.AddStatbar(barBounds, profColor, barKey);

                if (showNumbers)
                {
                    string xpText = completed
                        ? Lang.Get("progressionframework:training-complete")
                        : $"{(int)xp} / {(int)maxXp}";
                    var xpFont = CairoFont.WhiteDetailText().WithFontSize(10);
                    var xpBounds = ElementBounds.Fixed(12, y - 1, width - 24, barH + 2);
                    composer.AddStaticText(xpText, xpFont, EnumTextOrientation.Center, xpBounds);
                }

                barValues[barKey] = new float[] { xp, 0, maxXp };
                y += barH + 8;
            }
            y += sectionGap;
        }
    }

    // ------- Quest Log tab (filled in Task 5) -------

    private void ComposeQuestLogTab(GuiComposer composer, double width, ref double y)
    {
        var font = CairoFont.WhiteSmallText();
        var bounds = ElementBounds.Fixed(0, y, width, 24);
        composer.AddStaticText(
            Lang.Get("progressionframework:questlog-empty"),
            font, EnumTextOrientation.Center, bounds);
        y += 30;
    }

    // ------- Training tab helpers -------

    private float GetTrainingXp(string code)
    {
        var tree = capi.World.Player.Entity.WatchedAttributes
            .GetTreeAttribute(TrainingSystem.TrainingTreeKey);
        return tree?.GetFloat(code + "-xp", 0) ?? 0;
    }

    private int GetTrainingLevel(string code)
    {
        var tree = capi.World.Player.Entity.WatchedAttributes
            .GetTreeAttribute(TrainingSystem.TrainingTreeKey);
        return tree?.GetInt(code + "-level", 0) ?? 0;
    }

    private bool HasAnyProgress(Profession prof)
    {
        foreach (var t in prof.Trainings)
        {
            if (GetTrainingXp(t.Code) > 0) return true;
            if (GetTrainingLevel(t.Code) > 0) return true;
        }
        return false;
    }

    private static double[] HexToDoubles(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex[0] != '#' || hex.Length < 7)
            return new double[] { 0.78, 0.73, 0.57, 1.0 };
        int r = Convert.ToInt32(hex.Substring(1, 2), 16);
        int g = Convert.ToInt32(hex.Substring(3, 2), 16);
        int b = Convert.ToInt32(hex.Substring(5, 2), 16);
        return new double[] { r / 255.0, g / 255.0, b / 255.0, 1.0 };
    }
}
```

- [ ] **Step 2: Delete the old dialog file**

```bash
git rm ProgressionFramework/ProgressionFramework/Training/Client/GuiDialogTrainingLedger.cs
```

If the directory `ProgressionFramework/ProgressionFramework/Training/Client/` is now empty, remove it (`rmdir`) so the tree stays clean.

- [ ] **Step 3: Update `ProgressionFrameworkModSystem.StartClientSide`**

Open `ProgressionFramework/ProgressionFramework/ProgressionFrameworkModSystem.cs`. Replace `StartClientSide` and the field:

```csharp
using ProgressionFramework.Client;
using ProgressionFramework.Training;
using ProgressionFramework.Quests;
// ...

public class ProgressionFrameworkModSystem : ModSystem
{
    private const string HarmonyId = "progressionframework";
    private Harmony? harmony;
    private GuiDialogLedger? ledgerDialog;

    // Start() and Dispose() unchanged

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Input.RegisterHotKey("progressionframeworkledger",
            Lang.Get("progressionframework:ledger-title"), GlKeys.L,
            HotkeyType.GUIOrOtherControls);

        var trainingSystem = api.ModLoader.GetModSystem<TrainingSystem>();
        var questSystem = api.ModLoader.GetModSystem<QuestSystem>();
        ledgerDialog = new GuiDialogLedger(api, trainingSystem, questSystem);
        api.Gui.RegisterDialog(ledgerDialog);
    }
}
```

The critical addition is the `api.Gui.RegisterDialog(ledgerDialog)` call — without it, `ToggleKeyCombinationCode` is not bound to the hotkey and pressing `L` does nothing (the `CLAUDE.md` known-bug).

- [ ] **Step 4: Build**

```bash
dotnet build ProgressionFramework/ProgressionFramework.csproj
```

Expected: build succeeds.

- [ ] **Step 5: Manual verification in-game**

Launch Vintage Story with the Seafarer mod loaded (Seafarer depends on this framework).

1. Spawn in a world.
2. Press `L`. A dialog titled "Ledger" opens.
3. The Training tab is highlighted; training entries render as before (verify visually against prior behaviour — same profession headers, training rows, progress bars).
4. Click the Quest Log tab button. The tab highlight swaps; the body shows the centered text "No active or completed quests." (or whatever state your world has).
5. Click back to Training. Content returns.
6. Press `L` again. Dialog closes.
7. Press `L` a third time. Dialog reopens on whichever tab was last active.

If `L` does nothing, the `api.Gui.RegisterDialog` call was likely missed — re-check Step 3.

- [ ] **Step 6: Commit**

```bash
git add ProgressionFramework/ProgressionFramework/Client/GuiDialogLedger.cs \
        ProgressionFramework/ProgressionFramework/ProgressionFrameworkModSystem.cs \
        ProgressionFramework/ProgressionFramework/Training/Client/GuiDialogTrainingLedger.cs
git commit -m "Ledger: tabbed dialog shell with Training tab; fix L hotkey via RegisterDialog"
```

---

## Task 5: Quest Log tab body

Replace the placeholder `ComposeQuestLogTab` with the real rendering: filter to Active + Completed, group by `GroupLangKey`, render title + description + objectives + progress bar.

**Files:**
- Create: `ProgressionFramework/ProgressionFramework/Client/LedgerQuestLogTab.cs`
- Modify: `ProgressionFramework/ProgressionFramework/Client/GuiDialogLedger.cs`

- [ ] **Step 1: Create the tab helper**

Create `ProgressionFramework/ProgressionFramework/Client/LedgerQuestLogTab.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using ProgressionFramework.Quests;

namespace ProgressionFramework.Client;

/// <summary>
/// Stateless composer for the Quest Log tab. Returns bar values that the
/// enclosing dialog applies with <see cref="GuiElementStatbar.SetValues"/>
/// after <see cref="GuiComposer.Compose"/>.
/// </summary>
internal static class LedgerQuestLogTab
{
    public static void Compose(
        ICoreClientAPI capi,
        QuestSystem quests,
        GuiComposer composer,
        double width,
        ref double y,
        Dictionary<string, float[]> barValues)
    {
        var player = capi.World.Player;

        var shown = new List<(Quest quest, QuestStatus status, string groupName)>();
        foreach (var quest in quests.QuestsByCode.Values)
        {
            var status = quests.GetStatus(player, quest.Code);
            if (status != QuestStatus.Active && status != QuestStatus.Completed) continue;
            string groupName = !string.IsNullOrEmpty(quest.GroupLangKey)
                ? Lang.Get(quest.GroupLangKey)
                : Lang.Get("progressionframework:questlog-group-other");
            shown.Add((quest, status, groupName));
        }

        if (shown.Count == 0)
        {
            var bounds = ElementBounds.Fixed(0, y, width, 24);
            composer.AddStaticText(
                Lang.Get("progressionframework:questlog-empty"),
                CairoFont.WhiteSmallText(), EnumTextOrientation.Center, bounds);
            y += 30;
            return;
        }

        string otherGroupName = Lang.Get("progressionframework:questlog-group-other");
        var grouped = shown.GroupBy(s => s.groupName)
            .OrderBy(g => g.Key == otherGroupName ? 1 : 0)     // "Other" pinned to bottom
            .ThenBy(g => g.Key, StringComparer.CurrentCulture);

        foreach (var group in grouped)
        {
            var headerFont = CairoFont.WhiteSmallishText().WithColor(new double[] { 0.85, 0.80, 0.60, 1.0 });
            var headerBounds = ElementBounds.Fixed(0, y, width, 24);
            composer.AddStaticText(group.Key, headerFont, headerBounds);
            y += 28;

            var sorted = group
                .OrderBy(s => s.status == QuestStatus.Active ? 0 : 1)
                .ThenBy(s => ResolveTitle(s.quest), StringComparer.CurrentCulture);

            foreach (var (quest, status, _) in sorted)
            {
                ComposeQuestEntry(capi, quests, player, quest, status,
                    composer, width, ref y, barValues);
            }

            y += 12;
        }
    }

    private static void ComposeQuestEntry(
        ICoreClientAPI capi, QuestSystem quests, IPlayer player,
        Quest quest, QuestStatus status,
        GuiComposer composer, double width, ref double y,
        Dictionary<string, float[]> barValues)
    {
        string title = ResolveTitle(quest);
        string statusLabel = status == QuestStatus.Completed
            ? "\u2713 " + Lang.Get("progressionframework:questlog-status-completed")
            : Lang.Get("progressionframework:questlog-status-active");
        string statusColor = status == QuestStatus.Completed ? "#D4AF37" : "#C0C0C0";

        string titleLine = $"<strong>{title}</strong>   <font color=\"{statusColor}\">{statusLabel}</font>";
        var titleBounds = ElementBounds.Fixed(12, y, width - 24, 22);
        composer.AddRichtext(titleLine, CairoFont.WhiteSmallText(), titleBounds);
        y += 22;

        if (quest.Scope == QuestScope.Server)
        {
            var subtitleFont = CairoFont.WhiteDetailText().WithFontSize(10);
            var subtitleBounds = ElementBounds.Fixed(24, y, width - 36, 14);
            composer.AddStaticText(
                Lang.Get("progressionframework:questlog-shared-subtitle"),
                subtitleFont, subtitleBounds);
            y += 16;
        }

        if (!string.IsNullOrEmpty(quest.DescriptionLangKey))
        {
            var descFont = CairoFont.WhiteDetailText();
            var descBounds = ElementBounds.Fixed(24, y, width - 36, 36);
            composer.AddRichtext(Lang.Get(quest.DescriptionLangKey), descFont, descBounds);
            y += 40;
        }

        foreach (var obj in quest.Objectives)
        {
            ComposeObjective(quests, player, quest, obj, composer, width, ref y, barValues);
        }

        y += 6;
    }

    private static void ComposeObjective(
        QuestSystem quests, IPlayer player,
        Quest quest, QuestObjective obj,
        GuiComposer composer, double width, ref double y,
        Dictionary<string, float[]> barValues)
    {
        string header = !string.IsNullOrEmpty(obj.Title) ? obj.Title! : obj.Code;
        if (obj.Type.Equals("delivery", StringComparison.OrdinalIgnoreCase) && obj.Items.Count > 0)
        {
            var parts = obj.Items.Select(r => $"{r.Quantity}x {ShortItemName(r.Item)}");
            header += "  (" + string.Join(", ", parts) + ")";
        }

        var headerBounds = ElementBounds.Fixed(36, y, width - 48, 18);
        composer.AddStaticText("• " + header, CairoFont.WhiteDetailText(), headerBounds);
        y += 20;

        int progress = quests.GetObjectiveProgress(player, quest.Code, obj.Code);
        int required = Math.Max(1, obj.Required);

        var barBounds = ElementBounds.Fixed(48, y, width - 64, 10);
        string barKey = $"questbar-{quest.Code}-{obj.Code}";
        var barColor = new double[] { 0.60, 0.78, 0.60, 1.0 };
        composer.AddStatbar(barBounds, barColor, barKey);

        var numFont = CairoFont.WhiteDetailText().WithFontSize(10);
        var numBounds = ElementBounds.Fixed(48, y - 1, width - 64, 12);
        composer.AddStaticText($"{progress} / {required}", numFont, EnumTextOrientation.Center, numBounds);

        barValues[barKey] = new float[] { progress, 0, required };
        y += 16;
    }

    private static string ResolveTitle(Quest quest) =>
        !string.IsNullOrEmpty(quest.TitleLangKey) ? Lang.Get(quest.TitleLangKey) : quest.Code;

    /// <summary>Last path segment of an asset code, so "game:item/resource/rope" becomes "rope".
    /// Prefer <c>Lang.Get("item-" + stem)</c> if the lang key exists.</summary>
    private static string ShortItemName(string itemCode)
    {
        int slash = itemCode.LastIndexOf('/');
        string stem = slash >= 0 ? itemCode[(slash + 1)..] : itemCode;
        int colon = stem.IndexOf(':');
        if (colon >= 0) stem = stem[(colon + 1)..];
        string langKey = "item-" + stem;
        string translated = Lang.Get(langKey);
        return translated == langKey ? stem : translated;
    }
}
```

- [ ] **Step 2: Delegate from `GuiDialogLedger.ComposeQuestLogTab`**

Replace the placeholder body in `Client/GuiDialogLedger.cs`:

```csharp
private void ComposeQuestLogTab(GuiComposer composer, double width, ref double y)
{
    LedgerQuestLogTab.Compose(capi, quests, composer, width, ref y, barValues);
}
```

- [ ] **Step 3: Build**

```bash
dotnet build ProgressionFramework/ProgressionFramework.csproj
```

Expected: build succeeds.

- [ ] **Step 4: Manual verification with Seafarer quests**

Launch Vintage Story with Seafarer. The quest you exercise depends on what's active in-world; the generic checks:

1. Press `L`, switch to Quest Log. If no quest is Active/Completed, the empty state renders.
2. Talk to a Seafarer NPC who starts a delivery quest; accept it. Reopen the Ledger; Quest Log tab now shows the quest under its group header. Title, description, objective bullets and the 0/N progress bar all render.
3. Deliver some items to the NPC (partial turn-in). Reopen the Ledger — the bar advances. Close and reopen again — same state.
4. Finish all turn-ins. Status flips to "Completed" in gold with a check mark; quest remains visible in the log.
5. If you have two quests with the same `GroupLangKey`, both appear under that header.

Server-scope quests are not exercised here because Seafarer currently ships none. A stub check:

6. With no server-scope quests defined, confirm the log shows only player-scope quests and nothing crashes.

If anything in the body renders oddly (overlapping text, cut-off bars), the likely culprit is `y` drifting between sections — verify increments after each element match the element heights.

- [ ] **Step 5: Commit**

```bash
git add ProgressionFramework/ProgressionFramework/Client/LedgerQuestLogTab.cs \
        ProgressionFramework/ProgressionFramework/Client/GuiDialogLedger.cs
git commit -m "Ledger: Quest Log tab rendering (grouped, active + completed only)"
```

---

## Task 6: Final acceptance sweep

Run the full spec acceptance checklist and fix anything broken.

**Files:** none (verification only).

- [ ] **Step 1: Acceptance checklist (from the spec)**

1. Press `L` in a fresh client — dialog titled "Ledger" opens with Training active.
2. Press `L` again — dialog closes.
3. Press `L` a third time — dialog reopens on Training (first open) or Quest Log (if that was last).
4. Training tab matches pre-refactor behaviour.
5. Quest Log empty state renders when nothing active.
6. Active quest: grouped under `GroupLangKey`, title + description + objective bars present.
7. Progress advances when items are delivered; bar updates on next open.
8. Completed quest: gold "✓ Completed" status, remains visible.
9. No error log output from `progressionframework` during any of the above.
10. Unused `training-ledger-title` key is gone from `en.json` (`grep -n training-ledger-title ProgressionFramework/ProgressionFramework/assets/progressionframework/lang/en.json` → no results).

- [ ] **Step 2: Server-scope smoke (deferred to Seafarer)**

Note in the PR / commit message: server-scope quest display cannot be exercised until a server-scope quest JSON exists in a consumer mod. Snapshot and update code paths have been compiled and registered but are not manually validated in-game in this branch.

- [ ] **Step 3: Update `CLAUDE.md` follow-up**

The existing `CLAUDE.md` "Known Follow-ups" section mentions the L-hotkey bug. Delete that bullet (it's fixed) and leave a short note on the new ledger:

Edit `ProgressionFramework/CLAUDE.md` — replace the `## Known Follow-ups` block with:

```markdown
## Known Follow-ups

- Server-scope quest state is synced to clients via the
  `progressionframework:questsync` network channel but is not manually
  validated in-game yet — Seafarer currently ships no server-scope quests.
  Validate once a consumer mod adds one.
```

Also update the hotkey id in the earlier "Registered identifiers" list: `progressionframeworktrainingledger` → `progressionframeworkledger`.

- [ ] **Step 4: Commit**

```bash
git add ProgressionFramework/CLAUDE.md
git commit -m "Docs: drop fixed L-hotkey follow-up; rename to progressionframeworkledger"
```

---

## Self-review pass

- **Spec coverage:**
  - Shell + hotkey fix → Task 4. ✓
  - Training tab migrated → Task 4 (same behaviour, verified visually). ✓
  - Quest Log layout/filter/grouping/rendering → Task 5. ✓
  - Client helpers on `QuestSystem` (`GetStatus`, `GetObjectiveProgress`) widened to `IPlayer?` → Task 2. ✓
  - `ServerQuestSnapshot` / `ServerQuestUpdate` + channel + handlers → Task 3. ✓
  - `MarkServerQuestDirty` routing via `MarkDirty` → Task 3. ✓
  - Lang key additions and `training-ledger-title` removal → Task 1. ✓
  - File layout matches the spec's File Changes section. ✓
- **Placeholder scan:** no TBDs, no "add appropriate error handling", every code step shows code.
- **Type consistency:** `GetStatus(IPlayer?, string)` is used identically in Task 2 (definition), Task 5 (call site). `GetObjectiveProgress(IPlayer?, string, string)` same. `GuiDialogLedger` constructor signature (`ICoreClientAPI, TrainingSystem, QuestSystem`) matches across Tasks 4 and 5. Hotkey id `progressionframeworkledger` used consistently in `ToggleKeyCombinationCode`, `RegisterHotKey`, and `CLAUDE.md` note.
- **Network channel id** `progressionframework:questsync` used consistently in register / get on both sides.
- **Field name** `clientServerQuestState` used identically in Tasks 2 and 3.
