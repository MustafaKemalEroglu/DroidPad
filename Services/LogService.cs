using System.Collections.ObjectModel;
using Avalonia.Threading;
using DroidPad.Models;

namespace DroidPad.Services;

public static class LogService
{
    private const int MaxEntries = 500;

    public static ObservableCollection<LogEntry> Entries { get; } = new();

    public static void Info(string message) => Write(message, LogLevel.Info);
    public static void Success(string message) => Write(message, LogLevel.Success);
    public static void Warning(string message) => Write(message, LogLevel.Warning);
    public static void Error(string message) => Write(message, LogLevel.Error);
    public static void Debug(string message) => Write(message, LogLevel.Debug);

    public static void Write(string message, bool success) => Write(message, success ? LogLevel.Success : LogLevel.Error);

    private static void Write(string message, LogLevel level)
    {
        var entry = new LogEntry(message, level);
        Dispatcher.UIThread.Post(() =>
        {
            Entries.Add(entry);
            while (Entries.Count > MaxEntries) Entries.RemoveAt(0);
        });
    }

    public static void Clear() => Dispatcher.UIThread.Post(Entries.Clear);
}
