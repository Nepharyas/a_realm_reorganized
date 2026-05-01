using System.Collections.Generic;
using ARealmReorganized.Models;
using Lumina.Excel.Sheets;

namespace ARealmReorganized.Logic;

public static class SetCompression
{
    public static IReadOnlyList<SetGroup> GroupBySeries(
        IEnumerable<DresserItem> items,
        int minPiecesForSet)
    {
        var itemSheet = Service.DataManager.GetExcelSheet<Item>();
        var setSheet = Service.DataManager.GetExcelSheet<MirageStoreSetItem>();
        var result = new List<SetGroup>();
        if (itemSheet is null || setSheet is null) return result;

        var dresserByItemId = new Dictionary<uint, DresserItem>();
        foreach (var di in items)
        {
            if (!dresserByItemId.ContainsKey(di.ItemId))
                dresserByItemId[di.ItemId] = di;
        }

        foreach (var setRow in setSheet)
        {
            var slotIds = new[]
            {
                setRow.MainHand.RowId, setRow.OffHand.RowId, setRow.Head.RowId,
                setRow.Body.RowId, setRow.Hands.RowId, setRow.Legs.RowId,
                setRow.Feet.RowId, setRow.Earrings.RowId, setRow.Necklace.RowId,
                setRow.Bracelets.RowId, setRow.Ring.RowId,
            };

            var matched = new List<DresserItem>();
            int totalSlots = 0;
            foreach (var slotId in slotIds)
            {
                if (slotId == 0) continue;
                totalSlots++;
                if (dresserByItemId.TryGetValue(slotId, out var di)) matched.Add(di);
            }

            if (matched.Count < minPiecesForSet) continue;

            var name = $"Set {setRow.RowId}";
            var setItemRow = itemSheet.GetRowOrDefault(setRow.RowId);
            if (setItemRow is not null)
            {
                var text = setItemRow.Value.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(text)) name = text;
            }

            result.Add(new SetGroup
            {
                SeriesId = setRow.RowId,
                Name = name,
                Pieces = matched,
                TotalPieces = totalSlots,
            });
        }

        result.Sort((a, b) =>
        {
            var ratioA = (double)a.Pieces.Count / a.TotalPieces;
            var ratioB = (double)b.Pieces.Count / b.TotalPieces;
            var byRatio = ratioB.CompareTo(ratioA);
            if (byRatio != 0) return byRatio;
            return b.TotalPieces.CompareTo(a.TotalPieces);
        });
        return result;
    }
}
