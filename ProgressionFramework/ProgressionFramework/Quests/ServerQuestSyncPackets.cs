using System.Collections.Generic;
using ProtoBuf;

namespace ProgressionFramework.Quests;

/// <summary>Full snapshot of all server-scope quest state trees, sent to a
/// client on join. Each value is <see cref="Vintagestory.API.Datastructures.TreeAttribute.ToBytes"/>.</summary>
[ProtoContract]
public class ServerQuestSnapshot
{
    [ProtoMember(1)]
    public Dictionary<string, byte[]> States { get; set; } = new();
}

/// <summary>Incremental update for a single server-scope quest. <see cref="StateBytes"/>
/// null means the quest entry was removed server-side.</summary>
[ProtoContract]
public class ServerQuestUpdate
{
    [ProtoMember(1)]
    public string Code { get; set; } = "";
    [ProtoMember(2)]
    public byte[]? StateBytes { get; set; }
}
