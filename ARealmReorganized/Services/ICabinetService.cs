using System.Collections.Generic;
using ARealmReorganized.Models;

namespace ARealmReorganized.Services;

public interface ICabinetService
{
    bool IsFresh { get; }

    // True while the cabinet itself is loaded, as opposed to us working from the cache.
    bool IsLive { get; }
    bool IsAlreadyStored(uint itemId);
    bool IsStorable(uint itemId);
    IReadOnlyList<uint> ListStorable(IEnumerable<DresserItem> dresserItems);
    void RefreshCacheIfLive();
}
