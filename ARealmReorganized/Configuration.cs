using System;
using Dalamud.Configuration;

namespace ARealmReorganized;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool DryRun { get; set; } = true;
    public bool ConfirmEachAction { get; set; } = false;
    public int ActionDelayMs { get; set; } = 200;
    public bool MoveJobRelics { get; set; } = true;
    public bool MoveDungeonGear { get; set; } = true;
    public bool RegroupSets { get; set; } = true;
    public int MinPiecesForSet { get; set; } = 2;

    public void Save() => Service.PluginInterface.SavePluginConfig(this);
}
