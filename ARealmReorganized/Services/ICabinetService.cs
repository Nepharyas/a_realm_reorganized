using System.Collections.Generic;
using ARealmReorganized.Models;

namespace ARealmReorganized.Services;

public enum StoreResult
{
    Stored,
    AlreadyStored,
    NotEligible,
    WindowClosed,
    Error,
}

public interface ICabinetService
{
    bool IsAvailable { get; }
    bool IsFresh { get; }
    bool IsActivatable { get; }
    bool IsAlreadyStored(uint itemId);
    bool IsStorable(uint itemId);
    StoreResult Store(uint itemId);
    IReadOnlyList<uint> ListStorable(IEnumerable<DresserItem> dresserItems);
    void RefreshCacheIfLive();
}
