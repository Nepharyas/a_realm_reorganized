using System;
using System.Collections.Generic;
using ARealmReorganized.Models;

namespace ARealmReorganized.Logic;

// Finds gear you own more than once, looking everywhere a copy can hide: the dresser,
// the armoire, your bags/armoury/saddlebag and the retainers.
//
// Two buckets. "Multiple copies" is any item with two or more copies in hand, wherever
// they sit. "Armoire redundant" is a single copy of something the armoire already
// stores forever; a dyed dresser copy doesn't count as redundant since the armoire's
// copy is undyed. Bag and retainer copies don't tell us their dye, so those get listed
// and the player judges.
public static class DuplicateDetection
{
    public sealed record RetainerCopies(ulong RetainerId, string RetainerName, int Count);

    public sealed class DuplicatedItem
    {
        public required uint ItemId { get; init; }
        public required bool InArmoire { get; init; }
        public required IReadOnlyList<DresserItem> DresserCopies { get; init; }
        public required IReadOnlyDictionary<InventorySource, int> BagCopies { get; init; }
        public required IReadOnlyList<RetainerCopies> RetainerCopies { get; init; }
        public required int TotalCopies { get; init; }
    }

    public sealed class Result
    {
        public required IReadOnlyList<DuplicatedItem> MultipleCopies { get; init; }
        public required IReadOnlyList<DuplicatedItem> ArmoireRedundant { get; init; }
    }

    // isGear keeps stacks of consumables and materials out of the report; everything in
    // the dresser is gear already so it only gates bag and retainer entries.
    public static Result Find(
        IEnumerable<DresserItem> dresserItems,
        IEnumerable<InventoryEntry> bagEntries,
        IReadOnlyDictionary<ulong, RetainerInventoryCache> retainers,
        Func<uint, bool> isInArmoire,
        Func<uint, bool> isGear)
    {
        var dresserByItem = new Dictionary<uint, List<DresserItem>>();
        foreach (var dresserItem in dresserItems)
        {
            if (!dresserByItem.TryGetValue(dresserItem.ItemId, out var copies))
                dresserByItem[dresserItem.ItemId] = copies = new List<DresserItem>();
            copies.Add(dresserItem);
        }

        var bagsByItem = new Dictionary<uint, Dictionary<InventorySource, int>>();
        foreach (var entry in bagEntries)
        {
            if (!isGear(entry.ItemId)) continue;
            if (!bagsByItem.TryGetValue(entry.ItemId, out var bySource))
                bagsByItem[entry.ItemId] = bySource = new Dictionary<InventorySource, int>();
            bySource[entry.Source] = bySource.GetValueOrDefault(entry.Source) + 1;
        }

        var retainersByItem = new Dictionary<uint, List<RetainerCopies>>();
        foreach (var (retainerId, cache) in retainers)
        {
            var countByItem = new Dictionary<uint, int>();
            foreach (var entry in cache.Entries)
            {
                if (!isGear(entry.ItemId)) continue;
                countByItem[entry.ItemId] = countByItem.GetValueOrDefault(entry.ItemId) + 1;
            }
            foreach (var (itemId, count) in countByItem)
            {
                if (!retainersByItem.TryGetValue(itemId, out var perRetainer))
                    retainersByItem[itemId] = perRetainer = new List<RetainerCopies>();
                perRetainer.Add(new RetainerCopies(retainerId, cache.Name, count));
            }
        }

        var allItemIds = new HashSet<uint>(dresserByItem.Keys);
        allItemIds.UnionWith(bagsByItem.Keys);
        allItemIds.UnionWith(retainersByItem.Keys);

        var multiple = new List<DuplicatedItem>();
        var redundant = new List<DuplicatedItem>();
        foreach (var itemId in allItemIds)
        {
            var dresserCopies = dresserByItem.GetValueOrDefault(itemId) ?? [];
            var bagCopies = bagsByItem.GetValueOrDefault(itemId) ?? [];
            var retainerCopies = retainersByItem.GetValueOrDefault(itemId) ?? [];

            var total = dresserCopies.Count;
            foreach (var count in bagCopies.Values) total += count;
            foreach (var retainerCopy in retainerCopies) total += retainerCopy.Count;
            if (total == 0) continue;

            var inArmoire = isInArmoire(itemId);
            var item = new DuplicatedItem
            {
                ItemId = itemId,
                InArmoire = inArmoire,
                DresserCopies = dresserCopies,
                BagCopies = bagCopies,
                RetainerCopies = retainerCopies,
                TotalCopies = total,
            };

            if (total >= 2) multiple.Add(item);
            else if (inArmoire && !IsLoneDyedDresserCopy(dresserCopies)) redundant.Add(item);
        }

        multiple.Sort((a, b) => a.ItemId.CompareTo(b.ItemId));
        redundant.Sort((a, b) => a.ItemId.CompareTo(b.ItemId));
        return new Result { MultipleCopies = multiple, ArmoireRedundant = redundant };
    }

    private static bool IsLoneDyedDresserCopy(IReadOnlyList<DresserItem> dresserCopies) =>
        dresserCopies.Count == 1 && (dresserCopies[0].Stain0 != 0 || dresserCopies[0].Stain1 != 0);
}
