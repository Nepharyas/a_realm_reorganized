using System.Collections.Generic;
using ARealmReorganized.Models;

namespace ARealmReorganized.Services;

public interface IGlamourDresserService
{
    IReadOnlyList<DresserItem> Snapshot();
    void RefreshCacheIfLive();
}
