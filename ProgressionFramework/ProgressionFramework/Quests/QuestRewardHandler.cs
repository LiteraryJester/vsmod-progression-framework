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

/// <summary>
/// Minimal stub objective handler for "delivery". Delivery flow is still
/// driven by hand-in paths inside <see cref="QuestSystem"/>; this handler
/// exists so the registry isn't missing the "delivery" kind.
/// </summary>
internal static class BuiltInObjectiveHandlers
{
    public sealed class Delivery : IQuestObjectiveHandler
    {
        public void OnObjectiveStarted(IServerPlayer player, Quest quest, QuestObjective objective) { }
        public bool TryProgress(IServerPlayer player, Quest quest, QuestObjective objective, object eventData) => false;
    }
}
