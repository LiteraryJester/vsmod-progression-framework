using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using ProgressionFramework.Quests;

namespace ProgressionFramework.Client;

/// <summary>
/// Stateless composer for the Quest Log tab. Returns bar values via the supplied
/// dictionary so the enclosing dialog can apply them with
/// <see cref="GuiElementStatbar.SetValues"/> after <see cref="GuiComposer.Compose"/>.
/// </summary>
internal static class LedgerQuestLogTab
{
    public static void Compose(
        ICoreClientAPI capi,
        QuestSystem quests,
        GuiComposer composer,
        double width,
        ref double y,
        Dictionary<string, float[]> barValues)
    {
        var player = capi.World.Player;

        var shown = new List<(Quest quest, QuestStatus status, string groupName)>();
        foreach (var quest in quests.QuestsByCode.Values)
        {
            var status = quests.GetStatus(player, quest.Code);
            if (status != QuestStatus.Active && status != QuestStatus.Completed) continue;

            string groupName = !string.IsNullOrEmpty(quest.GroupLangKey)
                ? Lang.Get(quest.GroupLangKey)
                : Lang.Get("progressionframework:questlog-group-other");
            shown.Add((quest, status, groupName));
        }

        if (shown.Count == 0)
        {
            var bounds = ElementBounds.Fixed(0, y, width, 24);
            composer.AddStaticText(
                Lang.Get("progressionframework:questlog-empty"),
                CairoFont.WhiteSmallText(), EnumTextOrientation.Center, bounds);
            y += 30;
            return;
        }

        string otherGroupName = Lang.Get("progressionframework:questlog-group-other");
        var grouped = shown
            .GroupBy(s => s.groupName)
            .OrderBy(g => g.Key == otherGroupName ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.CurrentCulture);

        foreach (var group in grouped)
        {
            var headerFont = CairoFont.WhiteSmallishText().WithColor(new double[] { 0.85, 0.80, 0.60, 1.0 });
            var headerBounds = ElementBounds.Fixed(0, y, width, 24);
            composer.AddStaticText(group.Key, headerFont, headerBounds);
            y += 28;

            var sorted = group
                .OrderBy(s => s.status == QuestStatus.Active ? 0 : 1)
                .ThenBy(s => ResolveTitle(s.quest), StringComparer.CurrentCulture);

            foreach (var (quest, status, _) in sorted)
            {
                ComposeQuestEntry(capi, quests, player, quest, status,
                    composer, width, ref y, barValues);
            }

            y += 12;
        }
    }

    private static void ComposeQuestEntry(
        ICoreClientAPI capi, QuestSystem quests, IPlayer player,
        Quest quest, QuestStatus status,
        GuiComposer composer, double width, ref double y,
        Dictionary<string, float[]> barValues)
    {
        string title = ResolveTitle(quest);
        string statusLabel = status == QuestStatus.Completed
            ? "\u2713 " + Lang.Get("progressionframework:questlog-status-completed")
            : Lang.Get("progressionframework:questlog-status-active");
        string statusColor = status == QuestStatus.Completed ? "#D4AF37" : "#C0C0C0";

        string titleLine = $"<strong>{title}</strong>   <font color=\"{statusColor}\">{statusLabel}</font>";
        var titleBounds = ElementBounds.Fixed(12, y, width - 24, 22);
        composer.AddRichtext(titleLine, CairoFont.WhiteSmallText(), titleBounds);
        y += 22;

        if (quest.Scope == QuestScope.Server)
        {
            var subtitleFont = CairoFont.WhiteDetailText().WithFontSize(10);
            var subtitleBounds = ElementBounds.Fixed(24, y, width - 36, 14);
            composer.AddStaticText(
                Lang.Get("progressionframework:questlog-shared-subtitle"),
                subtitleFont, subtitleBounds);
            y += 16;
        }

        if (!string.IsNullOrEmpty(quest.DescriptionLangKey))
        {
            var descFont = CairoFont.WhiteDetailText();
            var descBounds = ElementBounds.Fixed(24, y, width - 36, 36);
            composer.AddRichtext(Lang.Get(quest.DescriptionLangKey), descFont, descBounds);
            y += 40;
        }

        foreach (var obj in quest.Objectives)
        {
            ComposeObjective(capi, quests, player, quest, obj, composer, width, ref y, barValues);
        }

        y += 6;
    }

    private static void ComposeObjective(
        ICoreClientAPI capi, QuestSystem quests, IPlayer player,
        Quest quest, QuestObjective obj,
        GuiComposer composer, double width, ref double y,
        Dictionary<string, float[]> barValues)
    {
        if (string.IsNullOrEmpty(obj.Code))
        {
            // Malformed quest JSON: objective has no code. Skip silently — this is
            // never expected in practice and rendering would silently overwrite the
            // previous objective's bar in barValues.
            return;
        }

        string header = !string.IsNullOrEmpty(obj.Title) ? obj.Title! : obj.Code;
        if (obj.Type.Equals("delivery", StringComparison.OrdinalIgnoreCase) && obj.Items.Count > 0)
        {
            var parts = obj.Items.Select(r => $"{r.Quantity}x {ShortItemName(capi, r.Item)}");
            header += "  (" + string.Join(", ", parts) + ")";
        }

        var headerBounds = ElementBounds.Fixed(36, y, width - 48, 18);
        composer.AddStaticText("• " + header, CairoFont.WhiteDetailText(), headerBounds);
        y += 20;

        int progress = quests.GetObjectiveProgress(player, quest.Code, obj.Code);
        int required = Math.Max(1, obj.Required);

        var barBounds = ElementBounds.Fixed(48, y, width - 64, 10);
        string barKey = $"questbar|{quest.Code}|{obj.Code}";
        var barColor = new double[] { 0.60, 0.78, 0.60, 1.0 };
        composer.AddStatbar(barBounds, barColor, barKey);

        var numFont = CairoFont.WhiteDetailText().WithFontSize(10);
        var numBounds = ElementBounds.Fixed(48, y - 1, width - 64, 12);
        composer.AddStaticText($"{progress} / {required}", numFont, EnumTextOrientation.Center, numBounds);

        barValues[barKey] = new float[] { progress, 0, required };
        y += 16;
    }

    private static string ResolveTitle(Quest quest) =>
        !string.IsNullOrEmpty(quest.TitleLangKey) ? Lang.Get(quest.TitleLangKey) : quest.Code;

    /// <summary>Resolves an asset code to a display name via the VS Item/Block
    /// registries. Falls back to the bare stem if neither registry matches.</summary>
    private static string ShortItemName(ICoreClientAPI capi, string itemCode)
    {
        var loc = new AssetLocation(itemCode);
        var item = capi.World.GetItem(loc);
        if (item != null) return item.GetHeldItemName(new ItemStack(item));
        var block = capi.World.GetBlock(loc);
        if (block != null) return block.GetHeldItemName(new ItemStack(block));

        int slash = itemCode.LastIndexOf('/');
        string stem = slash >= 0 ? itemCode[(slash + 1)..] : itemCode;
        int colon = stem.IndexOf(':');
        if (colon >= 0) stem = stem[(colon + 1)..];
        return stem;
    }
}
