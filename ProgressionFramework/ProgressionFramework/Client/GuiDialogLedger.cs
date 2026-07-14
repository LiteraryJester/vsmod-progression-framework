using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using ProgressionFramework.Training;
using ProgressionFramework.Quests;

namespace ProgressionFramework.Client;

public class GuiDialogLedger : GuiDialog
{
    private enum LedgerTab { Training, QuestLog }

    private readonly TrainingSystem training;
    private readonly QuestSystem quests;
    private readonly Dictionary<string, float[]> barValues = new();

    private LedgerTab currentTab = LedgerTab.Training;

    private const double QuestLogViewportHeight = 520;
    private readonly HashSet<string> collapsedQuests = new();
    private bool defaultsApplied;
    private ElementBounds? questLogContentBounds;
    private double questLogContentHeight;

    public override string ToggleKeyCombinationCode => "progressionframeworkledger";
    public override bool PrefersUngrabbedMouse => false;

    public GuiDialogLedger(ICoreClientAPI capi, TrainingSystem training, QuestSystem quests)
        : base(capi)
    {
        this.training = training;
        this.quests = quests;
    }

    public override void OnOwnPlayerDataReceived()
    {
        base.OnOwnPlayerDataReceived();

        // Re-subscription guard: the engine may call this multiple times per session
        // (respawn, dimension change). Strip prior bindings before adding again so
        // OnDataChanged is registered exactly once with each source.
        var attrs = capi.World.Player.Entity.WatchedAttributes;
        attrs.UnregisterListener(OnDataChanged);
        attrs.RegisterModifiedListener(TrainingSystem.TrainingTreeKey, OnDataChanged);
        attrs.RegisterModifiedListener(QuestSystem.PlayerQuestTreeKey, OnDataChanged);

        quests.ServerQuestStateChanged -= OnDataChanged;
        quests.ServerQuestStateChanged += OnDataChanged;
    }

    private void OnDataChanged()
    {
        if (IsOpened()) ComposeDialog();
    }

    public override bool TryOpen(bool withFocus = true)
    {
        ComposeDialog();
        return base.TryOpen(withFocus);
    }

    private void ComposeDialog()
    {
        barValues.Clear();
        ApplyDefaultCollapseState();
        const double width = 520;

        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        var dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle)
            .WithFixedAlignmentOffset(0, 0);

        var composer = capi.Gui.CreateCompo("progressionframeworkledger", dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(Lang.Get("progressionframework:ledger-title"), () => TryClose())
            .BeginChildElements(bgBounds);

        // Reserve space for the dialog title bar before placing tab buttons.
        double y = GuiStyle.TitleBarHeight;

        var trainingTabBounds = ElementBounds.Fixed(0, y, 140, 26);
        var questlogTabBounds = ElementBounds.Fixed(148, y, 140, 26);
        composer.AddToggleButton(
            Lang.Get("progressionframework:ledger-tab-training"),
            CairoFont.WhiteSmallishText(),
            _ => SwitchTab(LedgerTab.Training),
            trainingTabBounds,
            "tab-training");
        composer.AddToggleButton(
            Lang.Get("progressionframework:ledger-tab-questlog"),
            CairoFont.WhiteSmallishText(),
            _ => SwitchTab(LedgerTab.QuestLog),
            questlogTabBounds,
            "tab-questlog");
        y += 36;

        if (currentTab == LedgerTab.Training)
        {
            ComposeTrainingTab(composer, width, ref y);
        }
        else
        {
            ComposeQuestLogTab(composer, width, ref y);
        }

        composer.EndChildElements();
        SingleComposer = composer.Compose();

        SingleComposer.GetToggleButton("tab-training").SetValue(currentTab == LedgerTab.Training);
        SingleComposer.GetToggleButton("tab-questlog").SetValue(currentTab == LedgerTab.QuestLog);

        foreach (var kv in barValues)
        {
            var bar = SingleComposer.GetStatbar(kv.Key);
            if (bar != null) bar.SetValues(kv.Value[0], kv.Value[1], kv.Value[2]);
        }

        if (currentTab == LedgerTab.QuestLog)
        {
            var scrollbar = SingleComposer.GetScrollbar("questlog-scrollbar");
            if (scrollbar != null)
            {
                scrollbar.SetHeights((float)QuestLogViewportHeight, (float)questLogContentHeight);
                // Reset scroll to top on each recompose — collapse/expand shifts content
                // enough that preserving position would land the user in an unrelated spot.
                scrollbar.CurrentYPosition = 0;
                OnQuestLogScroll(0);
            }
        }
    }

    private void SwitchTab(LedgerTab tab)
    {
        // Always recompose, even when clicking the already-active tab — that path
        // re-applies the toggle button states (the base toggle flips Off when the
        // active tab is clicked, leaving the bar visually empty without a recompose).
        currentTab = tab;
        ComposeDialog();
    }

    // ------- Training tab (moved verbatim from GuiDialogTrainingLedger) -------

    private void ComposeTrainingTab(GuiComposer composer, double width, ref double y)
    {
        const double rowH = 22;
        const double barH = 10;
        const double sectionGap = 12;

        bool showAll = training.Config.ShowAllTrainings;
        bool showNumbers = training.Config.ShowXpNumbers;

        foreach (var prof in training.Professions)
        {
            if (!showAll && !HasAnyProgress(prof)) continue;

            var profColor = HexToDoubles(prof.Color);
            var headerFont = CairoFont.WhiteSmallishText().WithColor(profColor);
            var headerBounds = ElementBounds.Fixed(0, y, width, 24);
            string profName = Lang.Get(prof.Domain + ":profession-" + prof.Code);
            composer.AddStaticText(profName, headerFont, headerBounds);
            y += 28;

            foreach (var t in prof.Trainings)
            {
                float xp = GetTrainingXp(t.Code);
                int level = GetTrainingLevel(t.Code);
                if (!showAll && xp <= 0 && level <= 0) continue;

                var currentLevel = level > 0 ? t.Levels[level - 1] : null;
                var nextLevel = level < t.Levels.Count ? t.Levels[level] : null;
                bool completed = level >= t.Levels.Count;
                float maxXp = nextLevel?.Xp ?? currentLevel?.Xp ?? 100;

                string icon = completed ? "\u2605" : "\u25CB";
                string trainingName = Lang.Get(t.Domain + ":training-" + t.Code);
                string title = currentLevel?.Title ?? "";
                string iconColor = completed ? "#D4AF37" : "#808080";
                string titleColor = currentLevel?.Color ?? (completed ? "#7ACC7A" : prof.Color);

                string richText = $"<font color=\"{iconColor}\">{icon}</font>  {trainingName}";
                if (!string.IsNullOrEmpty(title))
                {
                    richText += $"  <font color=\"{titleColor}\">- {title}</font>";
                }

                var nameBounds = ElementBounds.Fixed(12, y, width - 24, rowH);
                composer.AddRichtext(richText, CairoFont.WhiteSmallText(), nameBounds);
                y += rowH;

                var barBounds = ElementBounds.Fixed(12, y, width - 24, barH);
                string barKey = "bar-" + t.Code;
                composer.AddStatbar(barBounds, profColor, barKey);

                if (showNumbers)
                {
                    string xpText = completed
                        ? Lang.Get("progressionframework:training-complete")
                        : $"{(int)xp} / {(int)maxXp}";
                    var xpFont = CairoFont.WhiteDetailText().WithFontSize(10);
                    var xpBounds = ElementBounds.Fixed(12, y - 1, width - 24, barH + 2);
                    composer.AddStaticText(xpText, xpFont, EnumTextOrientation.Center, xpBounds);
                }

                barValues[barKey] = new float[] { xp, 0, maxXp };
                y += barH + 8;
            }
            y += sectionGap;
        }
    }

    // ------- Quest Log tab -------

    private void ComposeQuestLogTab(GuiComposer composer, double width, ref double y)
    {
        const double scrollbarWidth = 20;
        const double gapBetweenViewportAndScrollbar = 3;
        double contentWidth = width - scrollbarWidth - gapBetweenViewportAndScrollbar;

        var clipBounds = ElementBounds.Fixed(0, y, contentWidth, QuestLogViewportHeight);
        var insetBounds = clipBounds.FlatCopy().FixedGrow(6).WithFixedOffset(-3, -3);
        var scrollbarBounds = ElementBounds.Fixed(
            contentWidth + gapBetweenViewportAndScrollbar, y,
            scrollbarWidth, QuestLogViewportHeight);

        questLogContentBounds = ElementBounds.Fixed(0, 0, contentWidth, 1);

        composer.AddInset(insetBounds, 3);

        composer
            .BeginClip(clipBounds)
                .BeginChildElements(questLogContentBounds);

        double contentY = 0;
        LedgerQuestLogTab.Compose(
            capi, quests, composer, contentWidth, ref contentY,
            barValues, collapsedQuests, OnToggleQuestCollapse);
        questLogContentHeight = contentY;

        // Give the content bounds a real height so ForkBoundingParent/layout can
        // work if anything queries it later.
        questLogContentBounds.fixedHeight = questLogContentHeight;

        composer
                .EndChildElements()
            .EndClip()
            .AddVerticalScrollbar(OnQuestLogScroll, scrollbarBounds, "questlog-scrollbar");

        y += QuestLogViewportHeight + 10;
    }

    private void OnQuestLogScroll(float value)
    {
        if (questLogContentBounds == null) return;
        questLogContentBounds.fixedY = -value;
        questLogContentBounds.CalcWorldBounds();
    }

    private void OnToggleQuestCollapse(string questCode)
    {
        if (!collapsedQuests.Add(questCode)) collapsedQuests.Remove(questCode);
        ComposeDialog();
    }

    /// <summary>Collapse completed quests by default on first open — they clutter
    /// the active view and users rarely need to re-expand them. Runs exactly once
    /// per dialog lifetime; user overrides (expanding) persist for that lifetime.</summary>
    private void ApplyDefaultCollapseState()
    {
        if (defaultsApplied) return;
        defaultsApplied = true;

        var player = capi.World.Player;
        foreach (var quest in quests.QuestsByCode.Values)
        {
            if (quests.GetStatus(player, quest.Code) == QuestStatus.Completed)
            {
                collapsedQuests.Add(quest.Code);
            }
        }
    }

    // ------- Training tab helpers -------

    private float GetTrainingXp(string code)
    {
        var tree = capi.World.Player.Entity.WatchedAttributes
            .GetTreeAttribute(TrainingSystem.TrainingTreeKey);
        return tree?.GetFloat(code + "-xp", 0) ?? 0;
    }

    private int GetTrainingLevel(string code)
    {
        var tree = capi.World.Player.Entity.WatchedAttributes
            .GetTreeAttribute(TrainingSystem.TrainingTreeKey);
        return tree?.GetInt(code + "-level", 0) ?? 0;
    }

    private bool HasAnyProgress(Profession prof)
    {
        foreach (var t in prof.Trainings)
        {
            if (GetTrainingXp(t.Code) > 0) return true;
            if (GetTrainingLevel(t.Code) > 0) return true;
        }
        return false;
    }

    private static double[] HexToDoubles(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex[0] != '#' || hex.Length < 7)
            return new double[] { 0.78, 0.73, 0.57, 1.0 };
        int r = Convert.ToInt32(hex.Substring(1, 2), 16);
        int g = Convert.ToInt32(hex.Substring(3, 2), 16);
        int b = Convert.ToInt32(hex.Substring(5, 2), 16);
        return new double[] { r / 255.0, g / 255.0, b / 255.0, 1.0 };
    }
}
