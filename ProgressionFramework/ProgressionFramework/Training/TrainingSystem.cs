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

    private readonly Dictionary<string, IXpSourceHandler> xpHandlers = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterXpSourceKind(string kind, IXpSourceHandler handler)
    {
        if (xpHandlers.ContainsKey(kind))
            sapi?.Logger.Warning("[Training] XP source kind '{0}' re-registered; overwriting.", kind);
        xpHandlers[kind] = handler;
    }

    public override bool ShouldLoad(EnumAppSide side) => true;

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        RegisterXpSourceKind("craft", new CraftXpHandler());
        RegisterXpSourceKind("dialogue", new DialogueXpHandler());
        RegisterXpSourceKind("book", new BookXpHandler());
    }

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
                        AddProfessions(arr.ToObject<List<Profession>>(), asset.Location.Domain);
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
                        AddProfessions(profs.ToObject<List<Profession>>(), asset.Location.Domain);
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
    /// Appends professions to the registry, stamping the source asset domain onto each
    /// profession and its trainings so display-name lang keys resolve in the owning mod's
    /// domain rather than a hardcoded one.
    /// </summary>
    private void AddProfessions(System.Collections.Generic.List<Profession>? list, string domain)
    {
        if (list == null) return;
        foreach (var prof in list)
        {
            prof.Domain = domain;
            foreach (var training in prof.Trainings) training.Domain = domain;
            Professions.Add(prof);
        }
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

            // Notify player. Prefer a <domain>:training-title-<trait> lang override if one
            // exists; otherwise use the level's Title straight from the profession JSON.
            // (Lang.Get returns the raw key, not the args, when a key is missing — so the
            // second arg cannot serve as a fallback default.)
            string titleKey = training.Domain + ":training-title-" + nextLevel.Trait;
            string title = Lang.HasTranslation(titleKey, false, false)
                ? Lang.Get(titleKey)
                : nextLevel.Title;
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
    /// Enumerates every XP source whose registered handler matches
    /// <paramref name="eventData"/> for the given source kind.
    /// </summary>
    public IEnumerable<TrainingXpSource> FindMatchingSources(string kind, object eventData)
    {
        if (!xpHandlers.TryGetValue(kind, out var handler))
            yield break;

        foreach (var source in XpSources)
        {
            if (!source.Type.Equals(kind, StringComparison.OrdinalIgnoreCase)) continue;
            if (handler.Matches(source, eventData)) yield return source;
        }
    }

    public IEnumerable<TrainingXpSource> GetCraftXpSources(string outputCode) => FindMatchingSources("craft", outputCode);
    public TrainingXpSource? GetDialogueXpSource(string triggerName) => FindMatchingSources("dialogue", triggerName).FirstOrDefault();
    public TrainingXpSource? GetBookXpSource(string itemCode) => FindMatchingSources("book", itemCode).FirstOrDefault();
}

internal sealed class CraftXpHandler : IXpSourceHandler
{
    public bool Matches(TrainingXpSource source, object eventData)
    {
        if (eventData is not string outputCode) return false;
        if (source.Recipe == null) return false;

        if (source.Recipe.Contains('*'))
        {
            string pattern = "^" + System.Text.RegularExpressions.Regex.Escape(source.Recipe).Replace("\\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(outputCode, pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        return source.Recipe.Equals(outputCode, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class DialogueXpHandler : IXpSourceHandler
{
    public bool Matches(TrainingXpSource source, object eventData)
    {
        return eventData is string trigger
            && !string.IsNullOrEmpty(source.Trigger)
            && source.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class BookXpHandler : IXpSourceHandler
{
    public bool Matches(TrainingXpSource source, object eventData)
    {
        return eventData is string itemCode
            && !string.IsNullOrEmpty(source.Item)
            && source.Item.Equals(itemCode, StringComparison.OrdinalIgnoreCase);
    }
}
