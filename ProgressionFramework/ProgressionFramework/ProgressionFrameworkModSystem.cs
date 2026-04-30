using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using ProgressionFramework.Client;
using ProgressionFramework.Training;
using ProgressionFramework.Quests;
using ProgressionFramework.Trading;

namespace ProgressionFramework
{
    public class ProgressionFrameworkModSystem : ModSystem
    {
        private const string HarmonyId = "progressionframework";
        private Harmony? harmony;
        private GuiDialogLedger? ledgerDialog;

        public override void Start(ICoreAPI api)
        {
            api.RegisterItemClass("ItemTrainingBook", typeof(ItemTrainingBook));
            api.RegisterEntity("EntityEvolvingTrader", typeof(EntityEvolvingTrader));

            harmony = new Harmony(HarmonyId);
            harmony.PatchAll(typeof(ProgressionFrameworkModSystem).Assembly);
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            base.Dispose();
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            api.Input.RegisterHotKey("progressionframeworkledger",
                Lang.Get("progressionframework:ledger-title"), GlKeys.L,
                HotkeyType.GUIOrOtherControls);

            var trainingSystem = api.ModLoader.GetModSystem<TrainingSystem>();
            var questSystem = api.ModLoader.GetModSystem<QuestSystem>();
            ledgerDialog = new GuiDialogLedger(api, trainingSystem, questSystem);
            api.Gui.RegisterDialog(ledgerDialog);
        }
    }
}
