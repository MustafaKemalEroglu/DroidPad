using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace WaydroidController.Services;

public static class LogManager
{
    public static ObservableCollection<string> Logs { get; } = new();
    public static event Action<string>? OnLogAdded;

    public static void Log(string message, bool isError = false)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var prefix = isError ? "❌ [HATA] " : "ℹ️ ";
        var finalMessage = $"[{timestamp}] {prefix}{message}";

        Dispatcher.UIThread.Post(() =>
        {
            Logs.Add(finalMessage);
            if (Logs.Count > 1000) Logs.RemoveAt(0);

            // Arayüzün bu logu görmesini sağla
            OnLogAdded?.Invoke(finalMessage);
        });
    }
}