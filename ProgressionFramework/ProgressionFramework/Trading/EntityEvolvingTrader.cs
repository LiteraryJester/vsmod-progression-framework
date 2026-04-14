using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using ProgressionFramework.Quests;

namespace ProgressionFramework.Trading;

public class EntityEvolvingTrader : EntityTrader
{
    private const string PatchAttrKey = "evolving_tradePatches";

    /// <summary>
    /// The original TradeProps loaded from JSON, before any patches.
    /// Stored so we can re-apply patches cleanly on each refresh.
    /// </summary>
    private TradeProperties? baseTradeProps;

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);

        // Store the original TradeProps loaded by base class
        if (api.Side == EnumAppSide.Server && TradeProps != null)
        {
            baseTradeProps = DeepCopyTradeProps(TradeProps);
            ApplyStoredPatches();
        }
    }

    public override void OnEntityLoaded()
    {
        base.OnEntityLoaded();

        if (Api.Side == EnumAppSide.Server && TradeProps != null && baseTradeProps == null)
        {
            baseTradeProps = DeepCopyTradeProps(TradeProps);
            ApplyStoredPatches();
        }
    }

    /// <summary>
    /// Handle dialogue triggers. Calls base for "opentrade", adds our custom triggers.
    /// </summary>
    protected override int Dialog_DialogTriggers(EntityAgent triggeringEntity, string value, JsonObject data)
    {
        // Let base handle opentrade and other standard triggers
        int baseResult = base.Dialog_DialogTriggers(triggeringEntity, value, data);
        if (baseResult != -1 && value == "opentrade") return baseResult;

        switch (value)
        {
            case "addSellingItem":
                return HandleAddItem(data, "addSelling");
            case "addBuyingItem":
                return HandleAddItem(data, "addBuying");
            case "removeSellingItem":
                return HandleRemoveItem(data, "removeSelling");
            case "removeBuyingItem":
                return HandleRemoveItem(data, "removeBuying");
            case "setMoney":
                return HandleSetMoney(data);
            case "incrementVariable":
                return HandleAdjustVariable(triggeringEntity, data, increment: true);
            case "decrementVariable":
                return HandleAdjustVariable(triggeringEntity, data, increment: false);
            case "awardTrainingXP":
                return HandleAwardTrainingXP(triggeringEntity, data);
            case "questStart":
                return QuestDialogTriggers.HandleQuestStart(this, triggeringEntity, data);
            case "questDeliver":
                return QuestDialogTriggers.HandleQuestDeliver(this, triggeringEntity, data);
        }

        return -1;
    }

    /// <summary>
    /// Public entry point so <see cref="Quests.QuestRewardHandler"/> can re-dispatch
    /// the legacy trigger implementations (addSellingItem, giveitemstack, etc.)
    /// when applying quest rewards, rather than duplicating the logic.
    /// </summary>
    public int InvokeDialogTrigger(EntityAgent triggeringEntity, string value, JsonObject data)
    {
        return Dialog_DialogTriggers(triggeringEntity, value, data);
    }

    private int HandleAddItem(JsonObject data, string op)
    {
        if (World.Side != EnumAppSide.Server) return -1;
        var patch = new TradePatch
        {
            Op = op,
            ItemData = data.AsObject<JsonItemStack>(),
            Price = data["price"]?.AsObject<NatFloat>(),
            Stock = data["stock"]?.AsObject<NatFloat>()
        };

        var patches = GetStoredPatches();
        if (TradePatch.HasDuplicate(patches, patch))
        {
            Api.Logger.Debug("[EvolvingTrader] Duplicate patch skipped: {0} {1}", op, patch.ItemData?.Code);
            return -1;
        }

        patches.Add(patch);
        StorePatches(patches);
        ApplyStoredPatches();

        // Push the new item straight into an empty live slot instead of calling
        // RefreshBuyingSellingInventory. The base refresh shuffles + reassigns
        // slot contents destructively: replaceTradeItems writes picked items to
        // slots[i] without checking whether the slot is already populated, so
        // calling it once per patch causes earlier patches in the same burst
        // (e.g. a quest's six addSellingItem rewards) to get overwritten by
        // later ones. This keeps the existing shop stock intact and just
        // appends the newcomer to a free slot.
        PlaceInEmptySlot(op, patch);

        Api.Logger.Notification("[EvolvingTrader] {0}: {1}", op, patch.ItemData?.Code);
        return -1;
    }

    private void PersistTradeStore()
    {
        // Mirror what EntityTradingHumanoid.GetOrCreateTradeStore does internally
        // so client inventory sync picks up the newly-placed slot.
        if (!WatchedAttributes.HasAttribute("traderInventory"))
        {
            var tree = new TreeAttribute();
            Inventory.ToTreeAttributes(tree);
            WatchedAttributes["traderInventory"] = tree;
        }
        else if (WatchedAttributes["traderInventory"] is ITreeAttribute existing)
        {
            Inventory.ToTreeAttributes(existing);
        }
        WatchedAttributes.MarkAllDirty();
    }

    private void PlaceInEmptySlot(string op, TradePatch patch)
    {
        if (Inventory == null || patch.ItemData == null) return;

        ItemSlotTrade[] slots = op == "addSelling"
            ? Inventory.SellingSlots
            : op == "addBuying"
                ? Inventory.BuyingSlots
                : null!;
        if (slots == null) return;

        // Resolve the new trade item from the stored base list so we use the
        // same TradeItem instance ApplyStoredPatches just appended.
        var list = op == "addSelling" ? TradeProps?.Selling.List : TradeProps?.Buying.List;
        if (list == null) return;
        var codeStr = patch.ItemData.Code?.ToString();
        var newTradeItem = list.LastOrDefault(it =>
            it.Code?.ToString()?.Equals(codeStr, StringComparison.OrdinalIgnoreCase) == true);
        if (newTradeItem == null) return;
        if (!newTradeItem.Resolve(World, "evolvingtrader add-slot")) return;

        // Skip if it's already present and in stock — no need to double-place.
        foreach (var s in slots)
        {
            if (s?.Itemstack?.Collectible?.Code == null) continue;
            if (s.Itemstack.Collectible.Code.ToString().Equals(codeStr, StringComparison.OrdinalIgnoreCase)
                && s.TradeItem != null && s.TradeItem.Stock > 0)
            {
                return;
            }
        }

        // Find an empty slot (no itemstack) or one whose trade item is sold out.
        foreach (var s in slots)
        {
            if (s == null) continue;
            if (s.Itemstack == null || (s.TradeItem != null && s.TradeItem.Stock == 0))
            {
                var resolved = newTradeItem.Resolve(World);
                if (resolved.Stock <= 0) return;
                s.SetTradeItem(resolved);
                s.MarkDirty();
                PersistTradeStore();
                return;
            }
        }

        // All slots full — leave the item in the base list. It will surface on
        // the next natural refresh cycle (~daily).
        Api.Logger.Notification("[EvolvingTrader] {0} list grew but no empty slot for {1}; waiting for natural refresh",
            op, codeStr);
    }

    private int HandleRemoveItem(JsonObject data, string op)
    {
        if (World.Side != EnumAppSide.Server) return -1;
        var code = data["code"]?.AsString();
        if (string.IsNullOrEmpty(code)) return -1;

        var patch = new TradePatch
        {
            Op = op,
            ItemData = new JsonItemStack { Code = new AssetLocation(code), Type = EnumItemClass.Item }
        };

        var patches = GetStoredPatches();
        patches.Add(patch);
        StorePatches(patches);
        ApplyStoredPatches();
        RefreshBuyingSellingInventory();

        Api.Logger.Notification("[EvolvingTrader] {0}: {1}", op, code);
        return -1;
    }

    private int HandleSetMoney(JsonObject data)
    {
        if (World.Side != EnumAppSide.Server) return -1;
        var money = data.AsObject<NatFloat>();
        if (money == null) return -1;

        var patch = new TradePatch { Op = "setMoney", Money = money };

        var patches = GetStoredPatches();
        // Replace existing setMoney patch if present
        patches.RemoveAll(p => p.Op == "setMoney");
        patches.Add(patch);
        StorePatches(patches);
        ApplyStoredPatches();

        Api.Logger.Notification("[EvolvingTrader] setMoney: avg={0}, var={1}", money.avg, money.var);
        return -1;
    }

    private int HandleAwardTrainingXP(EntityAgent triggeringEntity, JsonObject data)
    {
        if (World.Side != EnumAppSide.Server) return -1;
        if (triggeringEntity is not EntityPlayer eplr) return -1;

        var player = Api.World.PlayerByUid(eplr.PlayerUID) as IServerPlayer;
        if (player == null) return -1;

        string? training = data["training"]?.AsString();
        float xp = data["xp"]?.AsFloat(0) ?? 0;
        if (string.IsNullOrEmpty(training) || xp <= 0) return -1;

        var system = Api.ModLoader.GetModSystem<ProgressionFramework.Training.TrainingSystem>();
        system?.AwardXP(player, training, xp);

        return -1;
    }

    private int HandleAdjustVariable(EntityAgent triggeringEntity, JsonObject data, bool increment)
    {
        var scopeStr = data["scope"]?.AsString("entity") ?? "entity";
        var name = data["name"]?.AsString();
        if (string.IsNullOrEmpty(name)) return -1;

        int amount = data["amount"]?.AsInt(1) ?? 1;

        var scope = ScopeFromString(scopeStr);
        Entity callingEntity = scope == EnumActivityVariableScope.Player ? triggeringEntity : this;

        var varSys = Api.ModLoader.GetModSystem<VariablesModSystem>();
        if (varSys == null) return -1;

        string? currentStr = varSys.GetVariable(scope, name, callingEntity);
        int current = int.TryParse(currentStr, out int parsed) ? parsed : 0;

        int newValue = increment ? current + amount : Math.Max(0, current - amount);
        varSys.SetVariable(callingEntity, scope, name, newValue.ToString());

        Api.Logger.Notification("[EvolvingTrader] {0}Variable: {1}.{2} = {3}",
            increment ? "increment" : "decrement", scopeStr, name, newValue);

        // Threshold check
        if (data["thresholdValue"].Exists && data["thresholdVariable"].Exists)
        {
            int threshold = data["thresholdValue"].AsInt(0);
            string? thresholdVar = data["thresholdVariable"].AsString();
            if (newValue >= threshold && !string.IsNullOrEmpty(thresholdVar))
            {
                var parts = thresholdVar.Split('.', 2);
                if (parts.Length == 2)
                {
                    var tvScope = ScopeFromString(parts[0]);
                    Entity tvEntity = tvScope == EnumActivityVariableScope.Player ? triggeringEntity : this;
                    varSys.SetVariable(tvEntity, tvScope, parts[1], "true");
                    Api.Logger.Notification("[EvolvingTrader] threshold reached: {0} -> true", thresholdVar);
                }
            }
        }

        return -1;
    }

    private static EnumActivityVariableScope ScopeFromString(string scope)
    {
        return scope switch
        {
            "entity" => EnumActivityVariableScope.Entity,
            "player" => EnumActivityVariableScope.Player,
            "global" => EnumActivityVariableScope.Global,
            "group" => EnumActivityVariableScope.Group,
            _ => EnumActivityVariableScope.Entity
        };
    }

    /// <summary>
    /// Load stored patches from WatchedAttributes, apply to a fresh copy of base TradeProps.
    /// </summary>
    private void ApplyStoredPatches()
    {
        if (baseTradeProps == null) return;

        // Start from a fresh copy of base props
        TradeProps = DeepCopyTradeProps(baseTradeProps);

        var patches = GetStoredPatches();
        if (patches.Count > 0)
        {
            TradePatch.ApplyPatches(patches, TradeProps, World);
        }
    }

    private List<TradePatch> GetStoredPatches()
    {
        var json = WatchedAttributes.GetString(PatchAttrKey, "");
        return TradePatch.Deserialize(json);
    }

    private void StorePatches(List<TradePatch> patches)
    {
        WatchedAttributes.SetString(PatchAttrKey, TradePatch.Serialize(patches));
        WatchedAttributes.MarkPathDirty(PatchAttrKey);
    }

    private static TradeProperties DeepCopyTradeProps(TradeProperties source)
    {
        var copy = new TradeProperties
        {
            Money = source.Money != null
                ? new NatFloat(source.Money.avg, source.Money.var, source.Money.dist)
                : new NatFloat(10, 2, EnumDistribution.UNIFORM),
            Selling = new TradeList
            {
                MaxItems = source.Selling.MaxItems,
                List = source.Selling.List.ToArray() // Shallow copy of array (TradeItems are not mutated)
            },
            Buying = new TradeList
            {
                MaxItems = source.Buying.MaxItems,
                List = source.Buying.List.ToArray()
            }
        };
        return copy;
    }
}
