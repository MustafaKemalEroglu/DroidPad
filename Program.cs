using Avalonia;
using System;
using System.IO;

namespace DroidPad;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Tek kopya: kilit dosyası açıkken ikinci kopya hemen çıkar. Süreç ölünce kilit işletim sistemi tarafından bırakılır.
        using var instanceLock = TryAcquireInstanceLock();
        if (instanceLock is null)
        {
            Console.Error.WriteLine("DroidPad zaten çalışıyor.");
            return 1;
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static FileStream? TryAcquireInstanceLock()
    {
        try
        {
            var dir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) dir = Path.GetTempPath();
            return new FileStream(Path.Combine(dir, "droidpad.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) { return null; }          // başka kopya kilidi tutuyor
        catch (UnauthorizedAccessException) { return new FileStream(Path.GetTempFileName(), FileMode.Create); } // kilit kurulamadı: engelleme
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}