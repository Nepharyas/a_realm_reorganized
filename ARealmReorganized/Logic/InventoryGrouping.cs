using System;
using System.Collections.Generic;
using ARealmReorganized.Models;

namespace ARealmReorganized.Logic;

public static class InventoryGrouping
{
    public sealed record Result(
        IReadOnlyList<InventoryEntry> Deduped,
        IReadOnlyDictionary<InventorySource, IReadOnlyList<InventoryEntry>> BySource);

    public static Result FilterAndGroup(
        IEnumerable<InventoryEntry> entries,
        Func<uint, bool> isStorable)
    {
        var deduped = new List<InventoryEntry>();
        var seenItemIds = new HashSet<uint>();
        var bySource = new Dictionary<InventorySource, List<InventoryEntry>>();

        foreach (var entry in entries)
        {
            if (!isStorable(entry.ItemId)) continue;
            if (!seenItemIds.Add(entry.ItemId)) continue;
            deduped.Add(entry);
            if (!bySource.TryGetValue(entry.Source, out var sectionList))
                bySource[entry.Source] = sectionList = [];
            sectionList.Add(entry);
        }

        var bySourceReadOnly = new Dictionary<InventorySource, IReadOnlyList<InventoryEntry>>(bySource.Count);
        foreach (var (source, sectionList) in bySource)
            bySourceReadOnly[source] = sectionList;

        return new Result(deduped, bySourceReadOnly);
    }
}
