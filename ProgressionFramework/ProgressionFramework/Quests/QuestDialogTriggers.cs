using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace ProgressionFramework.Quests;

/// <summary>
/// Adapters called from <see cref="EntityEvolvingTrader.Dialog_DialogTriggers"/>
/// when a dialog node fires a <c>questStart</c> or <c>questDeliver</c> trigger.
/// Each handler translates the trigger JSON into a <see cref="QuestSystem"/>
/// call on the specific player.
/// </summary>
internal static class QuestDialogTriggers
{
    /// <summary>
    /// Trigger data: <c>{ code: "&lt;quest-code&gt;" }</c>. Moves the quest to Active
    /// for the triggering player if prereqs are satisfied.
    /// </summary>
    public static int HandleQuestStart(Entity npc, EntityAgent triggeringEntity, JsonObject data)
    {
        if (triggeringEntity is not EntityPlayer eplr) return -1;

        string? code = data["code"]?.AsString();
        if (string.IsNullOrEmpty(code)) return -1;

        var system = npc.Api.ModLoader.GetModSystem<QuestSystem>();
        if (system == null) return -1;

        if (npc.World.Side == EnumAppSide.Server)
        {
            if (npc.World.PlayerByUid(eplr.PlayerUID) is not IServerPlayer player) return -1;
            system.StartQuest(player, code);
        }
        else
        {
            system.MirrorStartOnClient(eplr, code);
        }
        return -1;
    }

    /// <summary>
    /// Trigger data: <c>{ code: "&lt;quest&gt;", objective: "&lt;obj&gt;" }</c>.
    /// Performs one delivery turn-in — takes all required items, increments progress,
    /// applies per-objective + whole-quest rewards when thresholds are reached.
    ///
    /// This trigger is called on both client and server because it fires from inside
    /// <see cref="DialogueComponent.setVars"/>, which runs on both sides. The server
    /// does the authoritative work (item take, state tree write, reward application);
    /// the client only mirrors the flat-variable outcome into local
    /// <see cref="VariablesModSystem.VarData"/> so the very next
    /// <see cref="DlgConditionComponent"/> in the dialog graph sees the updated value
    /// without waiting for the server's VarData broadcast to round-trip.
    /// </summary>
    public static int HandleQuestDeliver(Entity npc, EntityAgent triggeringEntity, JsonObject data)
    {
        if (triggeringEntity is not EntityPlayer eplr) return -1;

        string? quest = data["code"]?.AsString();
        string? objective = data["objective"]?.AsString();
        if (string.IsNullOrEmpty(quest) || string.IsNullOrEmpty(objective)) return -1;

        var system = npc.Api.ModLoader.GetModSystem<QuestSystem>();
        if (system == null) return -1;

        if (npc.World.Side == EnumAppSide.Server)
        {
            if (npc.World.PlayerByUid(eplr.PlayerUID) is not IServerPlayer player) return -1;
            system.TryDeliver(player, quest, objective, npc);
        }
        else
        {
            system.MirrorDeliveryOnClient(eplr, quest, objective);
        }
        return -1;
    }
}
