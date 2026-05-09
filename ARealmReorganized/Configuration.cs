using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace ARealmReorganized;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool DryRun { get; set; } = true;
    public int MultiRoundThreshold { get; set; } = 10;

    public DresserCache CachedDresser { get; set; } = new();
    public CabinetCache CachedCabinet { get; set; } = new();
    public Dictionary<ulong, RetainerInventoryCache> CachedRetainers { get; set; } = new();

    public void Save() => Service.PluginInterface.SavePluginConfig(this);
}

[Serializable]
public sealed class DresserCache
{
    public List<CachedDresserSlot> Slots { get; set; } = new();
    public DateTime RefreshedAt { get; set; } = DateTime.MinValue;
}

[Serializable]
public sealed class CachedDresserSlot
{
    public ushort Slot { get; set; }
    public uint ItemId { get; set; }
    public byte Stain0 { get; set; }
    public byte Stain1 { get; set; }
}

[Serializable]
public sealed class CabinetCache
{
    public HashSet<uint> StoredIds { get; set; } = new();
    public DateTime RefreshedAt { get; set; } = DateTime.MinValue;
}
