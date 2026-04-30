using Vintagestory.API.Server;

namespace ProgressionFramework.Quests;

/// <summary>
/// Handles the semantics of a quest objective type (e.g. "delivery"). Built-in
/// types: "delivery". Third-party mods register custom types via
/// <see cref="QuestSystem.RegisterObjectiveType"/>.
/// </summary>
public interface IQuestObjectiveHandler
{
    /// <summary>Called when the quest becomes Active for this player.</summary>
    void OnObjectiveStarted(IServerPlayer player, Quest quest, QuestObjective objective);

    /// <summary>
    /// Attempts to advance the objective's state based on the current event. Returns
    /// true if the objective state was advanced (e.g., items taken, counter bumped).
    /// Returning false causes <c>TryDeliver</c> to abort without persisting or
    /// broadcasting state. The framework checks <c>progress &gt;= Required</c>
    /// separately to detect full completion after this returns.
    /// </summary>
    bool TryProgress(IServerPlayer player, Quest quest, QuestObjective objective, object eventData);
}
