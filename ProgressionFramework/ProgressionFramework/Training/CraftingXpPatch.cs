using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ProgressionFramework.Training;

[HarmonyPatch(typeof(GridRecipe), nameof(GridRecipe.ConsumeInput))]
public static class CraftingXpPatch
{
    [HarmonyPostfix]
    public static void Postfix(GridRecipe __instance, IPlayer byPlayer, bool __result)
    {
        if (!__result) return;
        if (byPlayer?.Entity?.Api?.Side != EnumAppSide.Server) return;

        var outputCode = __instance.Output?.Code?.ToString();
        if (string.IsNullOrEmpty(outputCode)) return;

        var system = byPlayer.Entity.Api.ModLoader.GetModSystem<TrainingSystem>();
        if (system == null) return;

        var serverPlayer = byPlayer as IServerPlayer;
        if (serverPlayer == null) return;

        foreach (var source in system.GetCraftXpSources(outputCode))
        {
            system.AwardXP(serverPlayer, source.Training, source.Xp);
        }
    }
}
