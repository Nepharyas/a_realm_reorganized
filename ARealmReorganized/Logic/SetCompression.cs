using System;
using System.Collections.Generic;
using ARealmReorganized.Models;
using Lumina.Excel.Sheets;

namespace ARealmReorganized.Logic;

public static class SetCompression
{
    // Returns the union of itemIds belonging to dresser sets that are partially present,
    // i.e. for any set with at least one piece in the dresser AND at least one piece missing,
    // the missing piece itemIds. Used to highlight items elsewhere (inventory/retainer/etc.)
    // that the player could move into the dresser to complete a set there.
    public static IReadOnlyList<uint> GetMissingPieceItemIds(IEnumerable<DresserItem> dresserItems)
    {
        var setSheet = Service.DataManager.GetExcelSheet<MirageStoreSetItem>();
        if (setSheet is null) return Array.Empty<uint>();

        var dresserItemIds = new HashSet<uint>();
        foreach (var dresserItem in dresserItems) dresserItemIds.Add(dresserItem.ItemId);

        var missing = new HashSet<uint>();
        foreach (var setRow in setSheet)
        {
            uint[] memberIds =
            [
                setRow.MainHand.RowId, setRow.OffHand.RowId, setRow.Head.RowId,
                setRow.Body.RowId, setRow.Hands.RowId, setRow.Legs.RowId,
                setRow.Feet.RowId, setRow.Earrings.RowId, setRow.Necklace.RowId,
                setRow.Bracelets.RowId, setRow.Ring.RowId,
            ];

            var presentInDresser = 0;
            var realMembers = 0;
            foreach (var memberId in memberIds)
            {
                if (memberId == 0) continue;
                realMembers++;
                if (dresserItemIds.Contains(memberId)) presentInDresser++;
            }
            if (presentInDresser == 0 || presentInDresser == realMembers) continue;

            foreach (var memberId in memberIds)
            {
                if (memberId == 0) continue;
                if (!dresserItemIds.Contains(memberId)) missing.Add(memberId);
            }
        }

        var result = new List<uint>(missing);
        return result;
    }

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
