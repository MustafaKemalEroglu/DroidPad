using System;

namespace WaydroidController.Models;

public enum LogLevel { Info, Success, Warning, Error, Debug }

public sealed class LogEntry(string message, LogLevel level)
{
    public string Message { get; } = message;
    public LogLevel Level { get; } = level;
    public string Text { get; } = $"{DateTime.Now:HH:mm:ss}  {message}";

    public string ColorHex => Level switch
    {
        LogLevel.Error => "#FF6B6B",
        LogLevel.Warning => "#FFC857",
        LogLevel.Success => "#4ADE80",
        LogLevel.Debug => "#7C8594",
        _ => "#9CC9FF"
    };
}
