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
    /// Attempt to progress the objective in response to an event. The meaning of
    /// <paramref name="eventData"/> depends on the objective type. Returns true
    /// if the objective is now fully complete.
    /// </summary>
    bool TryProgress(IServerPlayer player, Quest quest, QuestObjective objective, object eventData);
}
