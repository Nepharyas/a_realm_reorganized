using System.Collections.Generic;
using ARealmReorganized.Models;

namespace ARealmReorganized.Services;

public interface IGlamourDresserService
{
    bool IsAvailable { get; }
    IReadOnlyList<DresserItem> Snapshot();
    bool Remove(DresserItem item);
}
