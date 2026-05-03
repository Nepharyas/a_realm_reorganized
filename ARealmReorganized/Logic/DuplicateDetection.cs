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

        /// <summary>
        /// Returns a new <see cref="Result"/> with items occupying any of the given dresser slots
        /// removed. Entries in <see cref="MultipleCopies"/> whose item has only one copy left after
        /// removal are also dropped, since they are no longer duplicates.
        /// </summary>
        public Result WithSlotsRemoved(HashSet<ushort> removed)
        {
            if (removed.Count == 0) return this;

            var countByItemId = new Dictionary<uint, int>();
            foreach (var d in MultipleCopies)
                if (!removed.Contains(d.SlotIndex))
                    countByItemId[d.ItemId] = countByItemId.GetValueOrDefault(d.ItemId) + 1;

            return new Result
            {
                ArmoireRedundant = ArmoireRedundant.Where(d => !removed.Contains(d.SlotIndex)).ToList(),
                MultipleCopies = MultipleCopies
                    .Where(d => !removed.Contains(d.SlotIndex) && countByItemId[d.ItemId] >= 2)
                    .ToList(),
            };
        }
    }

    /// <summary>
    /// Removes selected duplicate slots from the dresser via <paramref name="executor"/> and
    /// returns the updated result and the set of slots that were successfully removed.
    /// Failed removals do not count against <paramref name="willRemove"/>.
    /// </summary>
    public static (Result duplicates, IReadOnlySet<ushort> removed) Apply(
        Result duplicates,
        IReadOnlySet<ushort> selected,
        int willRemove,
        IActionExecutor executor)
    {
        var removed = new HashSet<ushort>();

        foreach (var d in duplicates.ArmoireRedundant)
        {
            if (removed.Count >= willRemove) break;
            if (!selected.Contains(d.SlotIndex)) continue;
            if (executor.RemoveFromDresser(d) == ActionResult.Success)
                removed.Add(d.SlotIndex);
        }
        foreach (var d in duplicates.MultipleCopies)
        {
            if (removed.Count >= willRemove) break;
            if (!selected.Contains(d.SlotIndex)) continue;
            if (executor.RemoveFromDresser(d) == ActionResult.Success)
                removed.Add(d.SlotIndex);
        }

        return (duplicates.WithSlotsRemoved(removed), removed);
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
