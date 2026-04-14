using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ProgressionFramework.Training;

public class GuiDialogTrainingLedger : GuiDialog
{
    private readonly TrainingSystem system;
    private readonly Dictionary<string, float[]> barValues = new();

    public override string ToggleKeyCombinationCode => "seafarertrainingledger";
    public override bool PrefersUngrabbedMouse => false;

    public GuiDialogTrainingLedger(ICoreClientAPI capi, TrainingSystem system) : base(capi)
    {
        this.system = system;
    }

    public override void OnOwnPlayerDataReceived()
    {
        base.OnOwnPlayerDataReceived();
        capi.World.Player.Entity.WatchedAttributes
            .RegisterModifiedListener(TrainingSystem.TrainingTreeKey, OnTrainingChanged);
    }

    private void OnTrainingChanged()
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

        double width = 420;
        double rowH = 22;
        double barH = 10;
        double sectionGap = 12;

        bool showAll = system.Config.ShowAllTrainings;
        bool showNumbers = system.Config.ShowXpNumbers;

        // Calculate total height
        double contentHeight = 0;
        foreach (var prof in system.Professions)
        {
            if (!showAll && !HasAnyProgress(prof)) continue;
            contentHeight += 28;
            foreach (var training in prof.Trainings)
            {
                if (!showAll && GetTrainingXp(training.Code) <= 0
                    && GetTrainingLevel(training.Code) <= 0) continue;
                contentHeight += rowH + barH + 8;
            }
            contentHeight += sectionGap;
        }

        if (contentHeight < 50) contentHeight = 50;

        var bgBounds = ElementBounds.Fill
            .WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;

        var dialogBounds = ElementStdBounds.AutosizedMainDialog
            .WithAlignment(EnumDialogArea.CenterMiddle)
            .WithFixedAlignmentOffset(0, 0);

        var composer = capi.Gui.CreateCompo("seafarertrainingledger", dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar(Lang.Get("seafarer:training-ledger-title"), () => TryClose())
            .BeginChildElements(bgBounds);

        double y = 0;

        foreach (var prof in system.Professions)
        {
            if (!showAll && !HasAnyProgress(prof)) continue;

            var profColor = HexToDoubles(prof.Color);
            var headerFont = CairoFont.WhiteSmallishText().WithColor(profColor);
            var headerBounds = ElementBounds.Fixed(0, y, width, 24);
            string profName = Lang.Get("seafarer:profession-" + prof.Code);
            composer.AddStaticText(profName, headerFont, headerBounds);
            y += 28;

            foreach (var training in prof.Trainings)
            {
                float xp = GetTrainingXp(training.Code);
                int level = GetTrainingLevel(training.Code);

                if (!showAll && xp <= 0 && level <= 0) continue;

                var currentLevel = level > 0 ? training.Levels[level - 1] : null;
                var nextLevel = level < training.Levels.Count ? training.Levels[level] : null;
                bool completed = level >= training.Levels.Count;
                float maxXp = nextLevel?.Xp ?? currentLevel?.Xp ?? 100;

                string icon = completed ? "\u2605" : "\u25CB";
                string trainingName = Lang.Get("seafarer:training-" + training.Code);
                string title = currentLevel?.Title ?? "";
                string iconColor = completed ? "#D4AF37" : "#808080";
                string titleColor = currentLevel?.Color ?? (completed ? "#7ACC7A" : prof.Color);

                string richText = $"<font color=\"{iconColor}\">{icon}</font>  {trainingName}";
                if (!string.IsNullOrEmpty(title))
                {
                    richText += $"  <font color=\"{titleColor}\">- {title}</font>";
                }

                var nameBounds = ElementBounds.Fixed(12, y, width - 24, rowH);
                var nameFont = CairoFont.WhiteSmallText();
                composer.AddRichtext(richText, nameFont, nameBounds);
                y += rowH;

                var barBounds = ElementBounds.Fixed(12, y, width - 24, barH);
                string barKey = "bar-" + training.Code;
                composer.AddStatbar(barBounds, profColor, barKey);

                if (showNumbers)
                {
                    string xpText = completed
                        ? Lang.Get("seafarer:training-complete")
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

        composer.EndChildElements();
        SingleComposer = composer.Compose();

        foreach (var kv in barValues)
        {
            var bar = SingleComposer.GetStatbar(kv.Key);
            if (bar != null)
            {
                bar.SetValues(kv.Value[0], kv.Value[1], kv.Value[2]);
            }
        }
    }

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
        foreach (var training in prof.Trainings)
        {
            if (GetTrainingXp(training.Code) > 0) return true;
            if (GetTrainingLevel(training.Code) > 0) return true;
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
