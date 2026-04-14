using System.Collections.Generic;

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
}

public class Training
{
    public string Code { get; set; } = "";
    public List<TrainingLevel> Levels { get; set; } = new();
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
