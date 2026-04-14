using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace ProgressionFramework.Training;

public class TrainingSystem : ModSystem
{
    public const string TrainingTreeKey = "progressionframework:training";

    public List<Profession> Professions { get; private set; } = new();
    public List<TrainingXpSource> XpSources { get; private set; } = new();
    public TrainingConfig Config { get; private set; } = new();

    public Dictionary<string, Training> TrainingsByCode { get; private set; } = new();
    public Dictionary<string, Profession> ProfessionByTrainingCode { get; private set; } = new();

    private ICoreServerAPI? sapi;

    public override bool ShouldLoad(EnumAppSide side) => true;

    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
    }

    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);
        LoadConfigs(api);
    }

    private void LoadConfigs(ICoreAPI api)
    {
        Professions = new List<Profession>();
        XpSources = new List<TrainingXpSource>();
        Config = new TrainingConfig();

        // Scan every mod domain for config/training/*.json.
        // Convention:
        //   <domain>:config/training/professions.json   -> appended to Professions
        //   <domain>:config/training/xp.json            -> appended to XpSources
        //   <domain>:config/training/config.json        -> LAST writer wins (global toggles)
        //   <domain>:config/training/*.json (any other) -> inspected by top-level key:
        //       { "professions": [...] } / { "xpSources": [...] } / { "config": {...} }
        //       or a bare array (treated as XP sources for back-compat)
        var assets = api.Assets.GetMany("config/training/");
        foreach (var asset in assets)
        {
            try
            {
                var token = JToken.Parse(asset.ToText());
                string fileName = asset.Location.Path.Split('/').Last();

                if (token is JArray arr)
                {
                    // Bare array — treat filename as hint
                    if (fileName.Equals("professions.json", StringComparison.OrdinalIgnoreCase))
                    {
                        Professions.AddRange(arr.ToObject<List<Profession>>() ?? new());
                    }
                    else
                    {
                        // Default bare-array treatment: XP sources
                        XpSources.AddRange(arr.ToObject<List<TrainingXpSource>>() ?? new());
                    }
                }
                else if (token is JObject obj)
                {
                    if (obj["professions"] is JArray profs)
                        Professions.AddRange(profs.ToObject<List<Profession>>() ?? new());
                    if (obj["xpSources"] is JArray xps)
                        XpSources.AddRange(xps.ToObject<List<TrainingXpSource>>() ?? new());
                    if (obj["config"] is JObject cfg)
                        Config = cfg.ToObject<TrainingConfig>() ?? Config;
                    // Legacy: filename "config.json" with top-level TrainingConfig shape
                    if (fileName.Equals("config.json", StringComparison.OrdinalIgnoreCase)
                        && obj["showAllTrainings"] != null)
                    {
                        Config = obj.ToObject<TrainingConfig>() ?? Config;
                    }
                }
            }
            catch (Exception e)
            {
                api.Logger.Error("[Training] Failed to load {0}: {1}", asset.Location, e.Message);
            }
        }

        // Build indexes
        TrainingsByCode.Clear();
        ProfessionByTrainingCode.Clear();
        foreach (var prof in Professions)
        {
            foreach (var training in prof.Trainings)
            {
                TrainingsByCode[training.Code] = training;
                ProfessionByTrainingCode[training.Code] = prof;
            }
        }

        api.Logger.Notification("[Training] Loaded {0} professions, {1} trainings, {2} XP sources from {3} file(s)",
            Professions.Count, TrainingsByCode.Count, XpSources.Count, assets.Count());
    }

    /// <summary>
    /// Award XP to a player for a specific training. Handles level-ups and trait grants.
    /// Call from server side only.
    /// </summary>
    public void AwardXP(IServerPlayer player, string trainingCode, float xp)
    {
        if (!TrainingsByCode.TryGetValue(trainingCode, out var training))
        {
            sapi?.Logger.Warning("[Training] Unknown training code: {0}", trainingCode);
            return;
        }

        var tree = player.Entity.WatchedAttributes
            .GetOrAddTreeAttribute(TrainingTreeKey);

        float currentXp = tree.GetFloat(trainingCode + "-xp", 0);
        int currentLevel = tree.GetInt(trainingCode + "-level", 0);

        currentXp += xp;
        tree.SetFloat(trainingCode + "-xp", currentXp);

        // Check for level-ups
        while (currentLevel < training.Levels.Count)
        {
            var nextLevel = training.Levels[currentLevel];
            if (currentXp < nextLevel.Xp) break;

            currentLevel++;
            tree.SetInt(trainingCode + "-level", currentLevel);

            // Grant trait for recipe unlocks
            AddExtraTrait(player, nextLevel.Trait);

            // Notify player
            string title = Lang.Get("seafarer:training-title-" + nextLevel.Trait,
                nextLevel.Title);
            sapi?.SendMessage(player, 0,
                Lang.Get("progressionframework:training-levelup", title),
                EnumChatType.Notification);

            sapi?.Logger.Notification("[Training] {0} reached {1} level {2} ({3})",
                player.PlayerName, trainingCode, currentLevel, nextLevel.Title);
        }

        player.Entity.WatchedAttributes.MarkPathDirty(TrainingTreeKey);
    }

    private void AddExtraTrait(IServerPlayer player, string trait)
    {
        var existing = player.Entity.WatchedAttributes
            .GetStringArray("extraTraits") ?? Array.Empty<string>();

        if (existing.Contains(trait)) return;

        var updated = new string[existing.Length + 1];
        existing.CopyTo(updated, 0);
        updated[existing.Length] = trait;
        player.Entity.WatchedAttributes.SetStringArray("extraTraits", updated);
        player.Entity.WatchedAttributes.MarkPathDirty("extraTraits");
    }

    /// <summary>
    /// Find XP sources matching a crafted recipe output code.
    /// Supports wildcard patterns like "game:pie-*".
    /// </summary>
    public IEnumerable<TrainingXpSource> GetCraftXpSources(string outputCode)
    {
        foreach (var source in XpSources)
        {
            if (source.Type != "craft" || source.Recipe == null) continue;

            if (source.Recipe.Contains('*'))
            {
                string pattern = "^" + Regex.Escape(source.Recipe).Replace("\\*", ".*") + "$";
                if (Regex.IsMatch(outputCode, pattern, RegexOptions.IgnoreCase))
                    yield return source;
            }
            else if (source.Recipe.Equals(outputCode, StringComparison.OrdinalIgnoreCase))
            {
                yield return source;
            }
        }
    }

    /// <summary>
    /// Find XP sources matching a dialogue trigger name.
    /// </summary>
    public TrainingXpSource? GetDialogueXpSource(string triggerName)
    {
        return XpSources.FirstOrDefault(s =>
            s.Type == "dialogue" && s.Trigger == triggerName);
    }

    /// <summary>
    /// Find XP sources matching a book item code.
    /// </summary>
    public TrainingXpSource? GetBookXpSource(string itemCode)
    {
        return XpSources.FirstOrDefault(s =>
            s.Type == "book" && s.Item == itemCode);
    }
}
