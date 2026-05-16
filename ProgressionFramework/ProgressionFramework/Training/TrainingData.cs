using System.Collections.Generic;
using Newtonsoft.Json;

namespace ProgressionFramework.Training;

public class TrainingConfig
{
    public bool ShowAllTrainings { get; set; } = false;
    public bool ShowXpNumbers { get; set; } = true;
}

public class Profession
{
    public string Code { get; set; } = "";
    public string Color { get; set; } = "#C8B691";
    public List<Training> Trainings { get; set; } = new();

    /// <summary>Asset domain this profession was loaded from (the consuming mod's modid).
    /// Set at load time, not from JSON. Used to resolve display-name lang keys in the
    /// owning mod's domain.</summary>
    [JsonIgnore]
    public string Domain { get; set; } = "game";
}

public class Training
{
    public string Code { get; set; } = "";
    public List<TrainingLevel> Levels { get; set; } = new();

    /// <summary>Asset domain this training was loaded from. Set at load time, not from JSON.</summary>
    [JsonIgnore]
    public string Domain { get; set; } = "game";
}

public class TrainingLevel
{
    public int Level { get; set; }
    public float Xp { get; set; }
    public string Title { get; set; } = "";
    public string Trait { get; set; } = "";
    public string? Color { get; set; }
}

public class TrainingXpSource
{
    public string Type { get; set; } = "";
    public string Training { get; set; } = "";
    public string? Recipe { get; set; }
    public string? Item { get; set; }
    public string? Trigger { get; set; }
    public float Xp { get; set; }
}
