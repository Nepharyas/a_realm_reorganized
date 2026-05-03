using System;
using System.Collections.Generic;
using System.Text;

namespace ARealmReorganized.Logic;

public enum LogLevel
{
    Info,
    DryRun,
    Warning,
}

public sealed class LogEntry
{
    public required DateTime Timestamp { get; init; }
    public required LogLevel Level { get; init; }
    public required string Message { get; init; }

    public string Format() => $"{Timestamp:HH:mm:ss} {Message}";
}

public sealed class PluginLogBuffer
{
    private const int Capacity = 200;
    private readonly LinkedList<LogEntry> entries = new();
    private readonly object gate = new();

    public void Add(string message, LogLevel level = LogLevel.Info)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
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
        var sb = new StringBuilder();
        foreach (var e in Snapshot()) sb.AppendLine(e.Format());
        return sb.ToString();
    }

    public void Clear()
    {
        lock (gate) entries.Clear();
    }
}
