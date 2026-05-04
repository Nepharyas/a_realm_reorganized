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
        Func<InventoryEntry, bool> filter)
    {
        // Dedupe by item id, preserving first-seen Source. If any copy is HQ,
        // promote the kept entry's IsHq to true so the caller can surface it.
        var byItemId = new Dictionary<uint, InventoryEntry>();
        var firstSeenOrder = new List<uint>();
        foreach (var entry in entries)
        {
            if (!byItemId.TryGetValue(entry.ItemId, out var existing))
            {
                byItemId[entry.ItemId] = entry;
                firstSeenOrder.Add(entry.ItemId);
            }
            else if (entry.IsHq && !existing.IsHq)
            {
                byItemId[entry.ItemId] = existing with { IsHq = true };
            }
        }

        var deduped = new List<InventoryEntry>();
        var bySource = new Dictionary<InventorySource, List<InventoryEntry>>();
        foreach (var itemId in firstSeenOrder)
        {
            var entry = byItemId[itemId];
            if (!filter(entry)) continue;
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
