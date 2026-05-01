using System;
using Dalamud.Configuration;

namespace ARealmReorganized;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool DryRun { get; set; } = true;

    public void Save() => Service.PluginInterface.SavePluginConfig(this);
}
