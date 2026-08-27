using System.Collections.Generic;
using ARealmReorganized.Models;

namespace ARealmReorganized.Services;

public interface IGlamourDresserService
{
    // True while the game still has the prism box in memory, so what we report is
    // current rather than remembered.
    bool IsLive { get; }

    IReadOnlyList<DresserItem> Snapshot();
    void RefreshCacheIfLive();
}
