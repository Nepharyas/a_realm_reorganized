using System;
using System.Collections.Generic;

namespace ARealmReorganized;

[Serializable]
public sealed class RetainerInventoryCache
{
    public string Name { get; set; } = "";
    public DateTime RefreshedAt { get; set; } = DateTime.MinValue;
    public List<CachedInventoryEntry> Entries { get; set; } = new();
}

[Serializable]
public sealed class CachedInventoryEntry
{
    public uint ItemId { get; set; }
    public bool IsHq { get; set; }
}
