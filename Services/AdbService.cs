using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WaydroidController.Services;

public static class AdbService
{
    public static async Task<string?> FindTargetAsync(CancellationToken ct = default)
    {
        try
        {
            var output = await RunAdbAsync(new[] { "devices" }, ct);
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
            {
                if (line.Contains("device") && !line.Contains("offline"))
                {
                    var parts = line.Split('\t', ' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) return parts[0];
                }
            }
        }
        catch { }
        return null;
    }

    public static async Task<(int W, int H)> GetResolutionAsync(string target, CancellationToken ct = default)
    {
        try
        {
            var output = await RunAdbAsync(new[] { "-s", target, "shell", "wm", "size" }, ct);

            // Python'daki: output.split('size:')[-1].strip()
            // Çıktıyı "size:" ifadesinden böler ve her zaman son elemanı alır.
            var parts = output.Split("size:", StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length > 0)
            {
                // En sondaki boyut bilgisini al (örneğin " 1280x720" veya " 1920x1080")
                var sizeStr = parts[^1].Trim();

                var dims = sizeStr.Split('x');
                if (dims.Length == 2 &&
                    int.TryParse(dims[0].Trim(), out var w) &&
                    int.TryParse(dims[1].Trim(), out var h))
                {
                    return (w, h);
                }
            }
        }
        catch { }
        return (1920, 1080);
    }

    public static async Task<(bool Ok, string Err)> TapAsync(string target, int x, int y, CancellationToken ct = default)
        => await TrySequentialAsync(new[]
        {
            new[] { "-s", target, "shell", "input", "tap", x.ToString(), y.ToString() },
            new[] { "shell", "input", "tap", x.ToString(), y.ToString() },
            new[] { "-s", target, "shell", "input", "-d", "0", "tap", x.ToString(), y.ToString() }
        }, ct);

    public static async Task<(bool Ok, string Err)> SwipeAsync(string target, int x1, int y1, int x2, int y2, int duration = 300, CancellationToken ct = default)
        => await TrySequentialAsync(new[]
        {
            new[] { "-s", target, "shell", "input", "swipe", x1.ToString(), y1.ToString(), x2.ToString(), y2.ToString(), duration.ToString() },
            new[] { "shell", "input", "swipe", x1.ToString(), y1.ToString(), x2.ToString(), y2.ToString(), duration.ToString() }
        }, ct);

    public static async Task<(bool Ok, string Err)> KeyEventAsync(string target, string keycode, CancellationToken ct = default)
        => await TrySequentialAsync(new[]
        {
            new[] { "-s", target, "shell", "input", "keyevent", keycode },
            new[] { "shell", "input", "keyevent", keycode }
        }, ct);

    public static Task<(bool, string)> TrySequentialAsyncPublicAsync(string[][] attempts, CancellationToken ct)
        => TrySequentialAsync(attempts, ct);

    private static async Task<(bool, string)> TrySequentialAsync(string[][] attempts, CancellationToken ct)
    {
        var lastErr = "";
        foreach (var args in attempts)
        {
            try
            {
                var (exitCode, _, stderr) = await RunAdbFullAsync(args, ct);
                if (exitCode == 0) return (true, "OK");
                lastErr = stderr;
            }
            catch (Exception ex) { lastErr = ex.Message; }
        }
        return (false, string.IsNullOrWhiteSpace(lastErr) ? "Tüm ADB komutları başarısız." : lastErr);
    }

    private static async Task<string> RunAdbAsync(string[] args, CancellationToken ct)
    {
        var (exit, stdout, stderr) = await RunAdbFullAsync(args, ct);
        if (exit != 0) throw new InvalidOperationException(stderr);
        return stdout;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAdbFullAsync(string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("adb")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        p.Start();
        var outTask = p.StandardOutput.ReadToEndAsync(ct);
        var errTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        return (p.ExitCode, await outTask, await errTask);
    }
}