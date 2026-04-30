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
        Dictionary<string, float[]> barValues,
        HashSet<string> collapsedQuests,
        Action<string> onToggleCollapse)
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
            // Richtext (not static text) so the group header scrolls with the rest
            // of the list — static elements render into a shared cached bitmap
            // that's not repositioned when the parent bounds shift on scroll.
            var headerFont = CairoFont.WhiteSmallishText().WithColor(new double[] { 0.85, 0.80, 0.60, 1.0 });
            var headerBounds = ElementBounds.Fixed(0, y, width, 24);
            composer.AddRichtext(group.Key, headerFont, headerBounds);
            y += 28;

            var sorted = group
                .OrderBy(s => s.status == QuestStatus.Active ? 0 : 1)
                .ThenBy(s => ResolveTitle(s.quest), StringComparer.CurrentCulture);

            foreach (var (quest, status, _) in sorted)
            {
                ComposeQuestEntry(capi, quests, player, quest, status,
                    composer, width, ref y, barValues,
                    collapsedQuests, onToggleCollapse);
            }

            y += 12;
        }
    }

    private static void ComposeQuestEntry(
        ICoreClientAPI capi, QuestSystem quests, IPlayer player,
        Quest quest, QuestStatus status,
        GuiComposer composer, double width, ref double y,
        Dictionary<string, float[]> barValues,
        HashSet<string> collapsedQuests, Action<string> onToggleCollapse)
    {
        bool collapsed = collapsedQuests.Contains(quest.Code);
        string title = ResolveTitle(quest);
        // ★ works in VS's default font; ✓ (\u2713) renders as a missing-glyph box,
        // so match the training tab's "completed" visual (gold star).
        string statusLabel = status == QuestStatus.Completed
            ? "\u2605 " + Lang.Get("progressionframework:questlog-status-completed")
            : Lang.Get("progressionframework:questlog-status-active");
        string statusColor = status == QuestStatus.Completed ? "#D4AF37" : "#C0C0C0";

        // ASCII +/- for the collapse toggle — triangles (▶▼) aren't in the default
        // button font and render as empty boxes. Plain glyphs avoid any font-dependency.
        var toggleBounds = ElementBounds.Fixed(12, y + 1, 20, 20);
        string toggleLabel = collapsed ? "+" : "-";
        string questCode = quest.Code;
        composer.AddSmallButton(
            toggleLabel,
            () => { onToggleCollapse(questCode); return true; },
            toggleBounds,
            EnumButtonStyle.Small,
            "questlog-toggle-" + SanitizeKey(quest.Code));

        string titleLine = $"<strong>{title}</strong>   <font color=\"{statusColor}\">{statusLabel}</font>";
        var titleBounds = ElementBounds.Fixed(36, y, width - 48, 22);
        composer.AddRichtext(titleLine, CairoFont.WhiteSmallText(), titleBounds);
        y += 24;

        if (collapsed)
        {
            y += 4;
            return;
        }

        if (quest.Scope == QuestScope.Server)
        {
            var subtitleFont = CairoFont.WhiteDetailText().WithFontSize(10);
            var subtitleBounds = ElementBounds.Fixed(36, y, width - 48, 14);
            composer.AddRichtext(
                Lang.Get("progressionframework:questlog-shared-subtitle"),
                subtitleFont, subtitleBounds);
            y += 16;
        }

        if (!string.IsNullOrEmpty(quest.DescriptionLangKey))
        {
            var descFont = CairoFont.WhiteDetailText();
            var descBounds = ElementBounds.Fixed(36, y, width - 48, 36);
            composer.AddRichtext(Lang.Get(quest.DescriptionLangKey), descFont, descBounds);
            y += 40;
        }

        foreach (var obj in quest.Objectives)
        {
            ComposeObjective(capi, quests, player, quest, obj, composer, width, ref y, barValues);
        }

        y += 6;
    }

    /// <summary>Quest codes contain <c>:</c> (domain prefix) which GuiComposer
    /// rejects in element keys. Replace with <c>-</c> so button lookups work.</summary>
    private static string SanitizeKey(string code) => code.Replace(':', '-');

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

        int progress = quests.GetObjectiveProgress(player, quest.Code, obj.Code);
        int required = Math.Max(1, obj.Required);

        string rawTitle = !string.IsNullOrEmpty(obj.Title) ? obj.Title! : obj.Code;
        string title = CapitalizeFirst(rawTitle);

        string modifiers = "";
        if (obj.Type.Equals("delivery", StringComparison.OrdinalIgnoreCase) && obj.Items.Count > 0)
        {
            var parts = obj.Items
                .Select(r => ExtractParens(ShortItemName(capi, r.Item)))
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();
            if (parts.Count > 0)
            {
                string inner = string.Join(", ", parts.Select(p => p.Trim('(', ')')));
                modifiers = " (" + inner + ")";
            }
        }

        string header = $"{title}{modifiers} {progress} of {required}";

        var headerBounds = ElementBounds.Fixed(36, y, width - 48, 18);
        composer.AddRichtext("• " + header, CairoFont.WhiteDetailText(), headerBounds);
        y += 20;

        var barBounds = ElementBounds.Fixed(48, y, width - 64, 10);
        string barKey = $"questbar|{quest.Code}|{obj.Code}";
        var barColor = new double[] { 0.60, 0.78, 0.60, 1.0 };
        composer.AddStatbar(barBounds, barColor, barKey);

        barValues[barKey] = new float[] { progress, 0, required };
        y += 16;
    }

    private static string CapitalizeFirst(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (char.IsUpper(s[0])) return s;
        return char.ToUpper(s[0]) + s[1..];
    }

    /// <summary>Returns the last parenthesized segment of a string, including the
    /// parens. Examples: "Board (Birch)" → "(Birch)"; "Linen (Normal stitches)" →
    /// "(Normal stitches)"; "Coil of rope" → "". Nested parens aren't expected here.</summary>
    private static string ExtractParens(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        int close = name.LastIndexOf(')');
        if (close < 0) return "";
        int open = name.LastIndexOf('(', close);
        if (open < 0) return "";
        return name.Substring(open, close - open + 1);
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
