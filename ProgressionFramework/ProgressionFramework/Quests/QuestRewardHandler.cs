using System;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using ProgressionFramework.Trading;

namespace ProgressionFramework.Quests;

/// <summary>Built-in quest reward handlers, registered by <see cref="QuestSystem"/>.</summary>
internal static class BuiltInRewardHandlers
{
    public sealed class AddSellingItem : IQuestRewardHandler
    {
        public void Grant(QuestReward reward, IServerPlayer player, Entity npc, ICoreAPI api)
            => DispatchToTrader(npc, player.Entity, "addSellingItem", BuildShopTriggerData(reward));
    }

    public sealed class GiveItem : IQuestRewardHandler
    {
        public void Grant(QuestReward reward, IServerPlayer player, Entity npc, ICoreAPI api)
            => GiveItemDirect(reward, player, npc, api);
    }

    public sealed class AwardTrainingXP : IQuestRewardHandler
    {
        public void Grant(QuestReward reward, IServerPlayer player, Entity npc, ICoreAPI api)
        {
            DispatchToTrader(npc, player.Entity, "awardTrainingXP", new JsonObject(new JObject
            {
                ["training"] = reward.Training,
                ["xp"] = reward.Xp
            }));
        }
    }

    public sealed class IncrementEntityVariable : IQuestRewardHandler
    {
        public void Grant(QuestReward reward, IServerPlayer player, Entity npc, ICoreAPI api)
        {
            DispatchToTrader(npc, player.Entity, "incrementVariable", new JsonObject(new JObject
            {
                ["scope"] = reward.VariableScope ?? "entity",
                ["name"] = reward.VariableName,
                ["amount"] = reward.Amount,
                ["thresholdValue"] = reward.ThresholdValue,
                ["thresholdVariable"] = reward.ThresholdVariable
            }));
        }
    }

    private static void DispatchToTrader(Entity npc, EntityAgent triggeringEntity, string trigger, JsonObject data)
    {
        if (npc is EntityEvolvingTrader trader)
        {
            trader.InvokeDialogTrigger(triggeringEntity, trigger, data);
        }
    }

    private static void GiveItemDirect(QuestReward reward, IServerPlayer player, Entity npc, ICoreAPI api)
    {
        if (reward.ItemCode == null) return;

        var jstack = new JsonItemStack
        {
            Code = new AssetLocation(reward.ItemCode),
            Type = reward.ItemType == "block" ? EnumItemClass.Block : EnumItemClass.Item,
            StackSize = reward.Stacksize > 0 ? reward.Stacksize : 1,
            Attributes = reward.Attributes != null ? new JsonObject(reward.Attributes) : null
        };

        if (!jstack.Resolve(api.World, "quest giveItem reward"))
        {
            api.Logger.Warning("[Quest] giveItem could not resolve '{0}'", reward.ItemCode);
            return;
        }

        var stack = jstack.ResolvedItemstack;
        if (!player.Entity.TryGiveItemStack(stack))
        {
            api.World.SpawnItemEntity(stack, npc.Pos.XYZ);
        }
    }

    private static JsonObject BuildShopTriggerData(QuestReward reward)
    {
        var obj = new JObject
        {
            ["code"] = reward.ItemCode,
            ["type"] = reward.ItemType ?? "item",
            ["stacksize"] = reward.Stacksize,
            ["stock"] = new JObject { ["avg"] = reward.StockAvg, ["var"] = reward.StockVar },
            ["price"] = new JObject { ["avg"] = reward.PriceAvg, ["var"] = reward.PriceVar }
        };
        if (reward.Attributes != null) obj["attributes"] = reward.Attributes;
        return new JsonObject(obj);
    }
}

/// <summary>Built-in quest objective handlers, registered by <see cref="QuestSystem"/>.</summary>
internal static class BuiltInObjectiveHandlers
{
    /// <summary>Standard delivery objective: take a full set of required items
    /// from the player's inventory and advance progress by however many full
    /// sets they could cover, capped at the remaining threshold.</summary>
    public sealed class Delivery : IQuestObjectiveHandler
    {
        public void OnObjectiveStarted(IServerPlayer player, Quest quest, QuestObjective objective) { }

        public bool TryProgress(IServerPlayer player, Quest quest, QuestObjective objective, object eventData)
        {
            var ctx = (QuestProgressContext)eventData;
            int currentProgress = ctx.ObjTree.GetInt("progress");
            int remainingNeeded = objective.Required - currentProgress;
            if (remainingNeeded <= 0) return false;

            int maxTurnIns = int.MaxValue;
            foreach (var req in objective.Items)
            {
                if (req.Quantity <= 0) continue;
                int have = ctx.CountInInventory(player, req.Item);
                int canDo = have / req.Quantity;
                if (canDo < maxTurnIns) maxTurnIns = canDo;
            }

            int turnIns = Math.Min(maxTurnIns, remainingNeeded);
            if (turnIns <= 0)
            {
                ctx.Api.Logger.Notification("[Quest] TryDeliver: {0} missing items for '{1}/{2}'",
                    player.PlayerName, quest.Code, objective.Code);
                return false;
            }

            foreach (var req in objective.Items)
            {
                ctx.TakeFromInventory(player, req.Item, req.Quantity * turnIns);
            }

            int progress = currentProgress + turnIns;
            ctx.ObjTree.SetInt("progress", progress);

            ctx.Api.Logger.Notification("[Quest] TryDeliver: {0} turned in {1}x '{2}/{3}' ({4}/{5})",
                player.PlayerName, turnIns, quest.Code, objective.Code, progress, objective.Required);

            return true;
        }
    }

    /// <summary>Tracks kills of entities whose <c>Code.ToString()</c> matches
    /// the objective's <see cref="QuestObjective.Pattern"/>. On start, snapshots
    /// the global counter for the pattern as the baseline. On progress attempt,
    /// computes current - baseline and writes the delta to the objective's
    /// progress counter; returns true if anything changed.</summary>
    public sealed class Kill : IQuestObjectiveHandler
    {
        public void OnObjectiveStarted(IServerPlayer player, Quest quest, QuestObjective objective)
        {
            if (string.IsNullOrEmpty(objective.Pattern)) return;

            var qs = player.Entity.Api.ModLoader.GetModSystem<QuestSystem>();
            int baseline = qs.GetKillCount(objective.Pattern);

            var objTree = qs.GetOrAddObjectiveTreePublic(player, quest, objective.Code);
            objTree.SetInt("baseline", baseline);
            objTree.SetInt("progress", 0);
        }

        public bool TryProgress(IServerPlayer player, Quest quest, QuestObjective objective, object eventData)
        {
            var ctx = (QuestProgressContext)eventData;
            if (string.IsNullOrEmpty(objective.Pattern)) return false;

            var qs = player.Entity.Api.ModLoader.GetModSystem<QuestSystem>();
            int baseline = ctx.ObjTree.GetInt("baseline");
            int current = qs.GetKillCount(objective.Pattern);
            int newProgress = Math.Max(0, current - baseline);

            int oldProgress = ctx.ObjTree.GetInt("progress");
            if (newProgress == oldProgress) return false;

            ctx.ObjTree.SetInt("progress", newProgress);
            ctx.Api.Logger.Notification("[Quest] Kill: {0} progress {1}/{2} (pattern {3})",
                player.PlayerName, newProgress, objective.Required, objective.Pattern);
            return true;
        }
    }
}

/// <summary>Shared context the handlers receive for <c>TryProgress</c>. Lets them
/// read/write the objective's state tree and use inventory helpers without
/// duplicating them per handler.</summary>
public sealed class QuestProgressContext
{
    public ICoreServerAPI Api { get; init; } = null!;
    public ITreeAttribute ObjTree { get; init; } = null!;
    public System.Func<IServerPlayer, string, int> CountInInventory { get; init; } = null!;
    public System.Action<IServerPlayer, string, int> TakeFromInventory { get; init; } = null!;
    public Entity Npc { get; init; } = null!;
}
