using System;

namespace WaydroidController.Models;

// Kendi LogLevel tanımımız
public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error,
    Debug
}

public class LogEntry
{
    public DateTime Timestamp { get; }
    public string Message { get; }
    public LogLevel Level { get; }

    public string FormattedTime => Timestamp.ToString("HH:mm:ss");

    // Arayüzde anında renklendirme için
    public string ColorHex => Level switch
    {
        LogLevel.Error => "#ff4444",     // Kırmızı
        LogLevel.Warning => "#ffbb33",   // Turuncu
        LogLevel.Success => "#00C851",   // Yeşil
        LogLevel.Debug => "#888888",     // Gri
        _ => "#00ffcc"                   // Turkuaz (Info)
    };

    public LogEntry(string message, LogLevel level)
    {
        Timestamp = DateTime.Now;
        Message = message;
        Level = level;
    }
}