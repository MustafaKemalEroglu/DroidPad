using System.Collections.ObjectModel;
using Avalonia.Threading;
using WaydroidController.Models;

namespace WaydroidController.Services;

public static class LogService
{
    // Arayüz (UI) doğrudan bu koleksiyonu dinleyecek
    public static ObservableCollection<LogEntry> Entries { get; } = new();

    // --- Kullanımı Kolay Özel Metotlar ---
    public static void Info(string message) => Write(message, LogLevel.Info);
    public static void Success(string message) => Write(message, LogLevel.Success);
    public static void Warning(string message) => Write(message, LogLevel.Warning);
    public static void Error(string message) => Write(message, LogLevel.Error);
    public static void Debug(string message) => Write(message, LogLevel.Debug);

    // --- Ana Yazma Metodu ---
    private static void Write(string message, LogLevel level)
    {
        // UI iş parçacığında güvenli bir şekilde çalıştır
        Dispatcher.UIThread.Post(() =>
        {
            Entries.Add(new LogEntry(message, level));

            // Bellek şişmesini önlemek için maksimum log sayısını sınırla
            if (Entries.Count > 1500)
                Entries.RemoveAt(0);
        });
    }

    public static void Clear() => Dispatcher.UIThread.Post(() => Entries.Clear());
}