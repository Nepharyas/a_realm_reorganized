using System.Collections.Generic;
using ARealmReorganized.Logic;
using ARealmReorganized.Models;

namespace ARealmReorganized.Services;

internal sealed class StubCabinetService : ICabinetService
{
    private readonly ArmoireEligibility eligibility;

    public StubCabinetService(ArmoireEligibility eligibility)
    {
        this.eligibility = eligibility;
    }

    public bool IsAvailable => false;
    public bool IsAlreadyStored(uint itemId) => false;
    public StoreResult Store(uint itemId) => StoreResult.WindowClosed;

    public IReadOnlyList<uint> ListStorable(IEnumerable<DresserItem> dresserItems)
    {
        var result = new List<uint>();
        var seen = new HashSet<uint>();
        foreach (var item in dresserItems)
        {
            if (!eligibility.IsEligible(item.ItemId)) continue;
            if (IsAlreadyStored(item.ItemId)) continue;
            if (seen.Add(item.ItemId)) result.Add(item.ItemId);
        }
        return result;
    }
}
