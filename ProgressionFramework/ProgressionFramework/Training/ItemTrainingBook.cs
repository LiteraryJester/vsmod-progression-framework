using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ProgressionFramework.Training
{
    public class ItemTrainingBook : ItemBook
    {
        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (byEntity.Controls.ShiftKey)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            if (byEntity.World.Side != EnumAppSide.Server)
            {
                handling = EnumHandHandling.PreventDefault;
                return;
            }

            if (byEntity is not EntityPlayer epl) return;
            if (byEntity.World.PlayerByUid(epl.PlayerUID) is not IServerPlayer player) return;

            var system = byEntity.World.Api.ModLoader.GetModSystem<TrainingSystem>();
            if (system == null) return;

            var source = system.GetBookXpSource(Code.ToString());
            if (source == null)
            {
                player.SendIngameError("notrainingbook", Lang.Get("seafarer:trainingbook-nosource"));
                return;
            }

            system.AwardXP(player, source.Training, source.Xp);

            var bookAttrs = slot.Itemstack?.Collectible?.Attributes?["trainingBook"];
            bool consume = bookAttrs?["consumeOnRead"].AsBool(true) ?? true;
            if (consume)
            {
                slot.TakeOut(1);
                slot.MarkDirty();
            }

            byEntity.World.PlaySoundAt(new AssetLocation("sounds/effect/writing"), byEntity, player);
            handling = EnumHandHandling.PreventDefault;
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new WorldInteraction[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "seafarer:heldhelp-readtolearn",
                    MouseButton = EnumMouseButton.Right
                }
            }.Append(base.GetHeldInteractionHelp(inSlot));
        }
    }
}
