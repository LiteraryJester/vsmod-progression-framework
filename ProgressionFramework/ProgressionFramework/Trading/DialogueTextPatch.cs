using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace ProgressionFramework.Trading;

/// <summary>
/// Harmony patch that adds {entity.varname}, {player.varname}, and {global.varname}
/// variable interpolation to dialogue text rendered by DlgTalkComponent.
///
/// Uses a postfix on genText() to walk the returned rich text components and resolve
/// variable patterns in their display text after all base game processing is complete.
/// </summary>
[HarmonyPatch(typeof(DlgTalkComponent), "genText")]
public static class DialogueTextPatch
{
    private static readonly Regex VarPattern = new Regex(
        @"\{(entity|player|global)\.([^}]+)\}",
        RegexOptions.Compiled);

    // Cache the controller field accessor
    private static readonly FieldInfo? ControllerField =
        typeof(DialogueComponent).GetField("controller",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

    [HarmonyPostfix]
    public static void Postfix(DlgTalkComponent __instance, ref RichTextComponentBase[] __result)
    {
        if (__result == null || __result.Length == 0) return;

        // Access the protected 'controller' field
        var controller = ControllerField?.GetValue(__instance) as DialogueController;
        if (controller?.VarSys == null) return;

        // Walk all components and resolve variable patterns in their text
        for (int i = 0; i < __result.Length; i++)
        {
            var comp = __result[i];
            if (comp == null) continue;

            if (comp is RichTextComponent rtc)
            {
                if (rtc.DisplayText != null && rtc.DisplayText.Contains("{"))
                {
                    rtc.DisplayText = ResolveVariables(rtc.DisplayText, controller);
                }
            }
            else if (comp is LinkTextComponent ltc)
            {
                if (ltc.DisplayText != null && ltc.DisplayText.Contains("{"))
                {
                    ltc.DisplayText = ResolveVariables(ltc.DisplayText, controller);
                }
            }
        }
    }

    /// <summary>
    /// Resolves {entity.varname}, {player.varname}, and {global.varname} patterns in text.
    /// </summary>
    public static string ResolveVariables(string text, DialogueController controller)
    {
        return VarPattern.Replace(text, match =>
        {
            var scopeStr = match.Groups[1].Value;
            var varName = match.Groups[2].Value;

            var scope = scopeStr switch
            {
                "entity" => EnumActivityVariableScope.Entity,
                "player" => EnumActivityVariableScope.Player,
                "global" => EnumActivityVariableScope.Global,
                _ => EnumActivityVariableScope.Entity
            };

            Entity callingEntity = scope == EnumActivityVariableScope.Player
                ? (Entity)controller.PlayerEntity
                : (Entity)controller.NPCEntity;

            var value = controller.VarSys.GetVariable(scope, varName, callingEntity);
            return value ?? "0";
        });
    }
}
