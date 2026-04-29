using System;
using System.Collections.Generic;
using ARealmReorganized.Models;

namespace ARealmReorganized.Services;

internal sealed class StubGlamourDresserService : IGlamourDresserService
{
    public bool IsAvailable => false;
    public IReadOnlyList<DresserItem> Snapshot() => Array.Empty<DresserItem>();
    public bool Remove(DresserItem item) => false;
}
