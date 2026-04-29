using System;
using System.Collections.Generic;
using ARealmReorganized.Models;

namespace ARealmReorganized.Services;

internal sealed class StubCabinetService : ICabinetService
{
    public bool IsAvailable => false;
    public bool IsAlreadyStored(uint itemId) => false;
    public StoreResult Store(uint itemId) => StoreResult.WindowClosed;
    public IReadOnlyList<uint> ListStorable(IEnumerable<DresserItem> dresserItems) => Array.Empty<uint>();
}
