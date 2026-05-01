using System;
using System.Collections.Generic;
using ARealmReorganized.Models;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace ARealmReorganized.Services;

internal sealed unsafe class GlamourDresserService : IGlamourDresserService
{
    public bool IsAvailable => MirageManager.Instance() != null;

    public IReadOnlyList<DresserItem> Snapshot()
    {
        var manager = MirageManager.Instance();
        if (manager == null) return Array.Empty<DresserItem>();

        var ids = manager->PrismBoxItemIds;
        var stain0 = manager->PrismBoxStain0Ids;
        var stain1 = manager->PrismBoxStain1Ids;

        var result = new List<DresserItem>();
        for (int i = 0; i < ids.Length; i++)
        {
            var itemId = ids[i];
            if (itemId == 0) continue;
            result.Add(new DresserItem(itemId, (ushort)i, stain0[i], stain1[i]));
        }
        return result;
    }

    public bool Remove(DresserItem item) => false;
}
