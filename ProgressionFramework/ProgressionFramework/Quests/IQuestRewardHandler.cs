using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace ProgressionFramework.Quests;

/// <summary>
/// Grants a quest reward to the player. Built-in kinds: "addSellingItem",
/// "giveItem", "awardTrainingXP", "incrementEntityVariable". Register custom
/// kinds via <see cref="QuestSystem.RegisterRewardType"/>.
/// </summary>
public interface IQuestRewardHandler
{
    void Grant(QuestReward reward, IServerPlayer player, Entity npc, ICoreAPI api);
}
