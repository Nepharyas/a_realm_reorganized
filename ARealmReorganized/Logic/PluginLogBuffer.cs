using System;
using System.Collections.Generic;

namespace ARealmReorganized.Logic;

public sealed class PluginLogBuffer
{
    private const int Capacity = 200;
    private readonly LinkedList<string> entries = new();
    private readonly object gate = new();

    public void Add(string line)
    {
        var stamped = $"{DateTime.Now:HH:mm:ss} {line}";
        lock (gate)
        {
            entries.AddLast(stamped);
            while (entries.Count > Capacity) entries.RemoveFirst();
        }
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (gate) return new List<string>(entries);
    }

    public void Clear()
    {
        lock (gate) entries.Clear();
    }
}
