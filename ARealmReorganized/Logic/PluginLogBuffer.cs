using System;
using System.Collections.Generic;
using System.Text;

namespace ARealmReorganized.Logic;

public sealed class LogEntry
{
    public required DateTime Timestamp { get; init; }
    public required string Message { get; init; }

    public string Format() => $"{Timestamp:HH:mm:ss} {Message}";
}

public sealed class PluginLogBuffer
{
    private const int Capacity = 200;
    private readonly LinkedList<LogEntry> entries = new();
    private readonly object gate = new();

    public void Add(string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Message = message,
        };
        lock (gate)
        {
            entries.AddLast(entry);
            while (entries.Count > Capacity) entries.RemoveFirst();
        }
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (gate) return new List<LogEntry>(entries);
    }

    public void ForEach(Action<LogEntry> visit)
    {
        lock (gate)
            foreach (var entry in entries) visit(entry);
    }

    public string AsText()
    {
        var stringBuilder = new StringBuilder();
        foreach (var entry in Snapshot()) stringBuilder.AppendLine(entry.Format());
        return stringBuilder.ToString();
    }

    public void Clear()
    {
        lock (gate) entries.Clear();
    }
}
