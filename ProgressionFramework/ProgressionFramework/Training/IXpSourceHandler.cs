using Vintagestory.API.Server;

namespace ProgressionFramework.Training;

/// <summary>
/// Resolves XP sources of a specific <see cref="TrainingXpSource.Type"/> into
/// actual XP awards. Built-in kinds: "craft", "dialogue", "book".
/// Third-party mods can register new kinds via
/// <see cref="TrainingSystem.RegisterXpSourceKind"/>.
/// </summary>
public interface IXpSourceHandler
{
    /// <summary>
    /// True if <paramref name="source"/> matches the current event and should
    /// trigger an XP award. <paramref name="eventData"/> is kind-specific:
    /// - "craft": outputCode (string)
    /// - "dialogue": triggerName (string)
    /// - "book": itemCode (string)
    /// </summary>
    bool Matches(TrainingXpSource source, object eventData);
}
