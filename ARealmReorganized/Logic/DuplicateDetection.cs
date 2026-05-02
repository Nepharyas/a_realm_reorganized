using System.Collections.Generic;
using System.Linq;
using ARealmReorganized.Models;
using ARealmReorganized.Services;

namespace ARealmReorganized.Logic;

public static class DuplicateDetection
{
    public sealed class Result
    {
        public required IReadOnlyList<DresserItem> ArmoireRedundant { get; init; }
        public required IReadOnlyList<DresserItem> MultipleCopies { get; init; }

        public Result WithSlotsRemoved(HashSet<ushort> removed)
        {
            if (removed.Count == 0) return this;
            var keptMultiple = MultipleCopies.Where(d => !removed.Contains(d.SlotIndex)).ToList();
            var countByItemId = keptMultiple.ToLookup(d => d.ItemId);
            return new Result
            {
                ArmoireRedundant = ArmoireRedundant.Where(d => !removed.Contains(d.SlotIndex)).ToList(),
                MultipleCopies = keptMultiple.Where(d => countByItemId[d.ItemId].Count() >= 2).ToList(),
            };
        }
    }

    public static Result Find(IEnumerable<DresserItem> items, ICabinetService cabinet)
    {
        var byItemId = new Dictionary<uint, List<DresserItem>>();
        foreach (var item in items)
        {
            if (!byItemId.TryGetValue(item.ItemId, out var list))
                byItemId[item.ItemId] = list = new List<DresserItem>();
            list.Add(item);
        }

        var multiple = new List<DresserItem>();
        var redundant = new List<DresserItem>();

        foreach (var (_, copies) in byItemId)
        {
            if (copies.Count >= 2)
            {
                multiple.AddRange(copies);
            }
            else
            {
                var single = copies[0];
                if (single.Stain0 == 0 && single.Stain1 == 0 && cabinet.IsAlreadyStored(single.ItemId))
                    redundant.Add(single);
            }
        }

        multiple.Sort((a, b) =>
        {
            var byId = a.ItemId.CompareTo(b.ItemId);
            return byId != 0 ? byId : a.SlotIndex.CompareTo(b.SlotIndex);
        });

        return new Result { MultipleCopies = multiple, ArmoireRedundant = redundant };
    }
}
