using System.Collections.Generic;
using ARealmReorganized.Models;
using Lumina.Excel.Sheets;

namespace ARealmReorganized.Logic;

public static class SetCompression
{
    // Groups of dresser pieces belonging to the same set, plus the pieces that would
    // finish off any set the dresser has only part of. Both come out of one pass over
    // the set sheet since they need the same per-set matching.
    public sealed record Analysis(
        IReadOnlyList<SetGroup> Groups,
        IReadOnlyList<uint> MissingPieceItemIds);

    // The 11 gear slots a set covers, in column order. A RowId of 0 means the set has no
    // piece for that slot.
    private static uint[] SetMemberIds(MirageStoreSetItem setRow) =>
    [
        setRow.MainHand.RowId, setRow.OffHand.RowId, setRow.Head.RowId,
        setRow.Body.RowId, setRow.Hands.RowId, setRow.Legs.RowId,
        setRow.Feet.RowId, setRow.Earrings.RowId, setRow.Necklace.RowId,
        setRow.Bracelets.RowId, setRow.Ring.RowId,
    ];

    public static Analysis Analyze(IEnumerable<DresserItem> items, int minPiecesForSet)
    {
        var itemSheet = Service.DataManager.GetExcelSheet<Item>();
        var setSheet = Service.DataManager.GetExcelSheet<MirageStoreSetItem>();
        if (itemSheet is null || setSheet is null) return new Analysis([], []);

        var dresserByItemId = new Dictionary<uint, DresserItem>();
        foreach (var dresserItem in items)
        {
            if (!dresserByItemId.ContainsKey(dresserItem.ItemId))
                dresserByItemId[dresserItem.ItemId] = dresserItem;
        }

        var groups = new List<SetGroup>();
        var missingPieces = new HashSet<uint>();

        foreach (var setRow in setSheet)
        {
            var slotIds = SetMemberIds(setRow);

            var matched = new List<DresserItem>();
            var totalSlots = 0;
            foreach (var slotId in slotIds)
            {
                if (slotId == 0) continue;
                totalSlots++;
                if (dresserByItemId.TryGetValue(slotId, out var dresserItem)) matched.Add(dresserItem);
            }

            if (matched.Count == 0) continue;

            // Part of the set is here, so the rest is worth pointing at wherever it sits.
            // Counted even for sets too small to list below, one piece still hints at a set.
            if (matched.Count < totalSlots)
            {
                foreach (var slotId in slotIds)
                {
                    if (slotId == 0) continue;
                    if (!dresserByItemId.ContainsKey(slotId)) missingPieces.Add(slotId);
                }
            }

            // Name resolution is a sheet lookup, so it waits until we know we'll list it.
            if (matched.Count < minPiecesForSet) continue;

            var name = $"Set {setRow.RowId}";
            var setItemRow = itemSheet.GetRowOrDefault(setRow.RowId);
            if (setItemRow is not null)
            {
                var text = setItemRow.Value.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(text)) name = text;
            }

            groups.Add(new SetGroup
            {
                SeriesId = setRow.RowId,
                Name = name,
                Pieces = matched,
                TotalPieces = totalSlots,
            });
        }

        groups.Sort((a, b) =>
        {
            var ratioA = (double)a.Pieces.Count / a.TotalPieces;
            var ratioB = (double)b.Pieces.Count / b.TotalPieces;
            var byRatio = ratioB.CompareTo(ratioA);
            if (byRatio != 0) return byRatio;
            return b.TotalPieces.CompareTo(a.TotalPieces);
        });

        return new Analysis(groups, [.. missingPieces]);
    }
}
