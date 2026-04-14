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
    public const string TrainingTreeKey = "seafarer-training";

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
        // Load professions
        var professionsAsset = api.Assets.TryGet(
            new AssetLocation("seafarer:config/professions.json"));
        if (professionsAsset != null)
        {
            try
            {
                var token = JToken.Parse(professionsAsset.ToText());
                Professions = token.ToObject<List<Profession>>() ?? new();
            }
            catch (Exception e)
            {
                api.Logger.Error("[Training] Failed to load professions.json: {0}", e.Message);
            }
        }

        // Load XP sources
        var xpAsset = api.Assets.TryGet(
            new AssetLocation("seafarer:config/training-xp.json"));
        if (xpAsset != null)
        {
            try
            {
                var token = JToken.Parse(xpAsset.ToText());
                XpSources = token.ToObject<List<TrainingXpSource>>() ?? new();
            }
            catch (Exception e)
            {
                api.Logger.Error("[Training] Failed to load training-xp.json: {0}", e.Message);
            }
        }

        // Load config
        var configAsset = api.Assets.TryGet(
            new AssetLocation("seafarer:config/training-config.json"));
        if (configAsset != null)
        {
            try
            {
                var token = JToken.Parse(configAsset.ToText());
                Config = token.ToObject<TrainingConfig>() ?? new();
            }
            catch (Exception e)
            {
                api.Logger.Error("[Training] Failed to load training-config.json: {0}", e.Message);
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

        api.Logger.Notification("[Training] Loaded {0} professions, {1} trainings, {2} XP sources",
            Professions.Count, TrainingsByCode.Count, XpSources.Count);
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
                Lang.Get("seafarer:training-levelup", title),
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
