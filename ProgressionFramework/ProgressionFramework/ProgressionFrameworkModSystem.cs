using System.Text;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using ProgressionFramework.Training;
using ProgressionFramework.Quests;
using ProgressionFramework.Trading;

namespace ProgressionFramework
{
    public class ProgressionFrameworkModSystem : ModSystem
    {
        private const string HarmonyId = "progressionframework";
        private Harmony? harmony;
        private GuiDialogTrainingLedger? trainingDialog;

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
            api.Input.RegisterHotKey("progressionframeworktrainingledger",
                Lang.Get("progressionframework:training-ledger-title"), GlKeys.L,
                HotkeyType.GUIOrOtherControls);

            var trainingSystem = api.ModLoader.GetModSystem<TrainingSystem>();
            trainingDialog = new GuiDialogTrainingLedger(api, trainingSystem);
        }
    }
}
