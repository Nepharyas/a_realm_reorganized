using System;
using System.Collections.Generic;
using ARealmReorganized.Models;
using Lumina.Excel.Sheets;

namespace ARealmReorganized.Services;

internal sealed class StubGlamourDresserService : IGlamourDresserService
{
    private readonly Lazy<IReadOnlyList<DresserItem>> snapshot;

    public StubGlamourDresserService()
    {
        snapshot = new Lazy<IReadOnlyList<DresserItem>>(BuildFakeSnapshot);
    }

    public bool IsAvailable => false;
    public IReadOnlyList<DresserItem> Snapshot() => snapshot.Value;
    public bool Remove(DresserItem item) => false;

    private static IReadOnlyList<DresserItem> BuildFakeSnapshot()
    {
        var result = new List<DresserItem>();
        ushort slot = 0;

        var cabinet = Service.DataManager.GetExcelSheet<Cabinet>();
        if (cabinet is not null)
        {
            int taken = 0;
            foreach (var row in cabinet)
            {
                if (taken >= 10) break;
                var itemId = row.Item.RowId;
                if (itemId == 0) continue;
                result.Add(new DresserItem(itemId, slot++, 0, 0));
                taken++;
            }
        }

        var items = Service.DataManager.GetExcelSheet<Item>();
        if (items is not null)
        {
            var bySeries = new Dictionary<uint, List<uint>>();
            int iter = 0;
            foreach (var item in items)
            {
                if (iter++ > 5000) break;
                var seriesId = item.ItemSeries.RowId;
                if (seriesId == 0) continue;
                if (!bySeries.TryGetValue(seriesId, out var list))
                    bySeries[seriesId] = list = new List<uint>();
                if (list.Count < 5) list.Add(item.RowId);
            }

            int seriesPicked = 0;
            foreach (var ids in bySeries.Values)
            {
                if (seriesPicked >= 2) break;
                if (ids.Count < 4) continue;
                foreach (var id in ids) result.Add(new DresserItem(id, slot++, 0, 0));
                seriesPicked++;
            }
        }

        return result;
    }
}
