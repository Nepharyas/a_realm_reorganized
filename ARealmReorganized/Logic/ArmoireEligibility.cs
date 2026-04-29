using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace ARealmReorganized.Logic;

public sealed class ArmoireEligibility
{
    private readonly HashSet<uint> eligibleItemIds = new();

    public ArmoireEligibility()
    {
        var sheet = Service.DataManager.GetExcelSheet<Cabinet>();
        if (sheet is null) return;
        foreach (var row in sheet)
        {
            var itemId = row.Item.RowId;
            if (itemId != 0) eligibleItemIds.Add(itemId);
        }
        Service.Log.Information($"Loaded {eligibleItemIds.Count} armoire-eligible items.");
    }

    public bool IsEligible(uint itemId) => eligibleItemIds.Contains(itemId);

    public int Count => eligibleItemIds.Count;
}
