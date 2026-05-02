using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace ARealmReorganized.Logic;

public sealed class ArmoireEligibility
{
    private readonly Dictionary<uint, uint> itemToCabinetId = new();

    public ArmoireEligibility()
    {
        var sheet = Service.DataManager.GetExcelSheet<Cabinet>();
        if (sheet is null) return;
        foreach (var row in sheet)
        {
            var itemId = row.Item.RowId;
            if (itemId != 0) itemToCabinetId[itemId] = row.RowId;
        }
        Service.Log.Information($"Loaded {itemToCabinetId.Count} armoire-eligible items.");
    }

    public bool IsEligible(uint itemId) => itemToCabinetId.ContainsKey(itemId);

    public bool TryGetCabinetId(uint itemId, out uint cabinetId) =>
        itemToCabinetId.TryGetValue(itemId, out cabinetId);

    public IEnumerable<uint> AllCabinetIds() => itemToCabinetId.Values;

    public int Count => itemToCabinetId.Count;
}
