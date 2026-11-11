using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using DroidPad.Models;

namespace DroidPad.Services;

public static class LogService
{
    private const int MaxEntries = 500;

    private static readonly ConcurrentQueue<LogEntry> Pending = new();
    private static int _flushScheduled;

    public static ObservableCollection<LogEntry> Entries { get; } = new();

    public static void Info(string message) => Write(message, LogLevel.Info);
    public static void Success(string message) => Write(message, LogLevel.Success);
    public static void Warning(string message) => Write(message, LogLevel.Warning);
    public static void Error(string message) => Write(message, LogLevel.Error);
    public static void Debug(string message) => Write(message, LogLevel.Debug);

    public static void Write(string message, bool success) => Write(message, success ? LogLevel.Success : LogLevel.Error);

    private static void Write(string message, LogLevel level)
    {
        Pending.Enqueue(new LogEntry(message, level));
        // Yoğun yazımda tek bir UI gönderimi birçok kaydı toplu işler
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 0) Dispatcher.UIThread.Post(Flush);
    }

    private static void Flush()
    {
        Volatile.Write(ref _flushScheduled, 0);
        while (Pending.TryDequeue(out var entry)) Entries.Add(entry);
        while (Entries.Count > MaxEntries) Entries.RemoveAt(0);
    }

    public static void Clear() => Dispatcher.UIThread.Post(() =>
    {
        while (Pending.TryDequeue(out _)) { }
        Entries.Clear();
    });
}
