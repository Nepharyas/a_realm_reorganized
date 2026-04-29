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
        var seriesSheet = Service.DataManager.GetExcelSheet<ItemSeries>();
        var result = new List<SetGroup>();
        if (itemSheet is null) return result;

        var bySeries = new Dictionary<uint, List<DresserItem>>();
        foreach (var di in items)
        {
            var row = itemSheet.GetRowOrDefault(di.ItemId);
            if (row is null) continue;
            var seriesId = row.Value.ItemSeries.RowId;
            if (seriesId == 0) continue;

            if (!bySeries.TryGetValue(seriesId, out var list))
                bySeries[seriesId] = list = new List<DresserItem>();
            list.Add(di);
        }

        foreach (var (seriesId, pieces) in bySeries)
        {
            if (pieces.Count < minPiecesForSet) continue;

            var name = $"Series {seriesId}";
            if (seriesSheet is not null)
            {
                var s = seriesSheet.GetRowOrDefault(seriesId);
                if (s is not null)
                {
                    var text = s.Value.Name.ExtractText();
                    if (!string.IsNullOrWhiteSpace(text)) name = text;
                }
            }

            result.Add(new SetGroup
            {
                SeriesId = seriesId,
                Name = name,
                Pieces = pieces,
            });
        }

        result.Sort((a, b) => b.Pieces.Count.CompareTo(a.Pieces.Count));
        return result;
    }
}
