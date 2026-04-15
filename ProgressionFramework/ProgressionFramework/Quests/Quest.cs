using System.Collections.Generic;

namespace ProgressionFramework.Quests;

/// <summary>
/// Status of a quest for a specific player. Stored per-player in WatchedAttributes
/// and mirrored to flat player attributes ("player.quest-{code}-status") so existing
/// dialog conditions can read it.
/// </summary>
public enum QuestStatus
{
    /// <summary>Player hasn't encountered the quest or prereqs aren't met.</summary>
    Unavailable,
    /// <summary>Prereqs met; player can start this quest when they talk to the giver.</summary>
    Available,
    /// <summary>Player has accepted the quest and is working on it.</summary>
    Active,
    /// <summary>All objectives done and rewards granted.</summary>
    Completed,
    /// <summary>Quest was globally disabled. Existing state preserved but cannot progress.</summary>
    Disabled
}

/// <summary>
/// Decides whose progress counter is incremented by a turn-in.
/// </summary>
public enum QuestScope
{
    /// <summary>Progress and completion tracked per player (default).</summary>
    Player,
    /// <summary>One shared progress counter for the whole server; any player can push it,
    /// everyone sees the same state. Good for town-wide rebuild efforts like a single
    /// shared ship being built.</summary>
    Server
}

/// <summary>
/// A single item requirement inside a Delivery objective. Quantity is taken per turn-in.
/// </summary>
public class QuestItemRequirement
{
    public string Item { get; set; } = "";
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// One atomic goal within a quest. Multi-item deliveries (varnish: 18 resin + 6 fat) are
/// expressed by adding multiple entries to <see cref="Items"/>; all must be present to
/// turn in once.
/// </summary>
public class QuestObjective
{
    public string Code { get; set; } = "";
    public string? Title { get; set; }
    public string Type { get; set; } = "delivery";
    public List<QuestItemRequirement> Items { get; set; } = new();
    /// <summary>Number of successful turn-ins needed to finish this objective. Defaults to 1.</summary>
    public int Required { get; set; } = 1;
    /// <summary>Rewards granted the moment this objective flips to complete.</summary>
    public List<QuestReward> Rewards { get; set; } = new();
}

/// <summary>
/// A data-driven reward entry. Different <see cref="Type"/>s read different fields.
/// </summary>
public class QuestReward
{
    public string Type { get; set; } = "";

    // AddSellingItem / GiveItem
    public string? ItemCode { get; set; }
    public string? ItemType { get; set; }          // "item" | "block"
    public int Stacksize { get; set; } = 1;
    public Newtonsoft.Json.Linq.JObject? Attributes { get; set; }

    // AddSellingItem
    public float StockAvg { get; set; } = 1;
    public float StockVar { get; set; } = 0;
    public float PriceAvg { get; set; } = 1;
    public float PriceVar { get; set; } = 0;

    // AwardTrainingXP
    public string? Training { get; set; }
    public float Xp { get; set; }

    // IncrementEntityVariable
    public string? VariableName { get; set; }
    public int Amount { get; set; } = 1;
    public int ThresholdValue { get; set; }
    public string? ThresholdVariable { get; set; }
    /// <summary>Scope for the target variable. Valid values: "entity" (default),
    /// "player", "global", "group". Defaults to "entity" so existing quests
    /// keep working unchanged.</summary>
    public string? VariableScope { get; set; }
}

/// <summary>
/// A quest definition loaded from JSON. One file per quest under
/// <c>assets/seafarer/config/quests/*.json</c>.
/// </summary>
public class Quest
{
    /// <summary>Fully-qualified quest code including mod prefix (e.g. <c>seafarer:drake-tradeship</c>).
    /// If the JSON specifies a bare code without a colon, <see cref="QuestSystem"/> prepends
    /// <c>{ModId}:</c> automatically at load time to prevent cross-mod clashes.</summary>
    public string Code { get; set; } = "";
    /// <summary>NPC code that owns this quest (e.g. "drake"). Used to target rewards
    /// that affect the giver's shop/state.</summary>
    public string Npc { get; set; } = "";
    /// <summary>Whether progress is tracked per-player or shared server-wide.
    /// Defaults to <see cref="QuestScope.Player"/>.</summary>
    public QuestScope Scope { get; set; } = QuestScope.Player;
    /// <summary>Lang key whose resolved string is used to group quests in the logbook
    /// GUI (e.g. <c>quest-group-rebuilding-tortuga</c> → "Rebuilding Tortuga"). Quests
    /// without a group land in a default bucket.</summary>
    public string? GroupLangKey { get; set; }
    public string? TitleLangKey { get; set; }
    public string? DescriptionLangKey { get; set; }
    /// <summary>If true, the quest becomes Available as soon as prereqs are satisfied.
    /// If false, it must be explicitly enabled via <see cref="QuestSystem.EnableQuest"/>.</summary>
    public bool AutoEnable { get; set; } = true;
    /// <summary>Codes of other quests that must be Completed before this one is Available.</summary>
    public List<string> Prerequisites { get; set; } = new();
    /// <summary>Optional completion threshold. When set, the quest completes
    /// once this many objectives are complete rather than requiring all of
    /// them. Null (default) keeps AND-all behavior. Objectives that remain
    /// incomplete when the quest finishes stay pending and further turn-ins
    /// are rejected because the quest is no longer Active.</summary>
    public int? RequiredObjectiveCount { get; set; }
    public List<QuestObjective> Objectives { get; set; } = new();
    /// <summary>Rewards granted when all objectives reach their Required threshold.</summary>
    public List<QuestReward> Rewards { get; set; } = new();
}
