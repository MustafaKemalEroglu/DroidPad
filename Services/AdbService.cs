using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DroidPad.Models;

namespace DroidPad.Services;

/// <summary>
/// Waydroid'e kalıcı tek bir "adb shell" oturumu üzerinden komut gönderir.
/// Komutlar ayrı bir yazıcı iş parçacığında sıraya alınır; böylece arayüz asla bloklanmaz
/// ve ardışık MOVE olayları birleştirilir (en güncel konum kazanır).
/// </summary>
public static partial class AdbService
{
    private const string DefaultAddress = "192.168.240.112:5555";
    private const string MoveCommand = "input motionevent MOVE";

    private static readonly SemaphoreSlim ConnectGate = new(1, 1);
    private static Session? _session;
    private static string _device = "";
    private static ResolutionDto? _resolution;
    private static DateTime _nextAttempt = DateTime.MinValue;

    public static event Action<bool>? ConnectionChanged;

    public static bool IsConnected => _session?.IsAlive == true;

    // ---------------- Bağlantı ----------------

    public static async Task<bool> EnsureConnectedAsync()
    {
        if (IsConnected) return true;

        await ConnectGate.WaitAsync();
        try
        {
            if (IsConnected) return true;

            var device = await FindDeviceAsync();
            if (device is null)
            {
                var address = await DiscoverAddressAsync();
                LogService.Info($"Cihaz bulunamadı, bağlanılıyor: {address}");
                var (code, _) = await RunAsync("adb", $"connect {address}", 8000);
                if (code < 0)
                {
                    LogService.Error("'adb' çalıştırılamadı. Kurulu olduğundan emin olun (android-tools).");
                    return Fail();
                }
                device = await FindDeviceAsync();
            }

            if (device is null)
            {
                LogService.Error("Waydroid bulunamadı. 'waydroid session start' ile oturumu başlatın.");
                return Fail();
            }

            var psi = new ProcessStartInfo("adb")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add(device);
            psi.ArgumentList.Add("shell");

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += static (_, _) => { };
            proc.ErrorDataReceived += static (_, _) => { };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            _device = device;
            _resolution = null;
            var session = new Session(proc);
            _session = session;
            proc.Exited += (_, _) =>
            {
                if (ReferenceEquals(_session, session))
                {
                    _session = null;
                    LogService.Warning("ADB bağlantısı koptu.");
                    ConnectionChanged?.Invoke(false);
                }
            };

            LogService.Success($"ADB bağlandı → {device}");
            ConnectionChanged?.Invoke(true);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error($"ADB bağlantı hatası: {ex.Message}");
            return Fail();
        }
        finally
        {
            ConnectGate.Release();
        }
    }

    private static bool Fail()
    {
        _nextAttempt = DateTime.UtcNow.AddSeconds(3);
        return false;
    }

    public static void Disconnect()
    {
        var s = _session;
        _session = null;
        s?.Close();
    }

    private static async Task<string?> FindDeviceAsync()
    {
        var (_, output) = await RunAsync("adb", "devices", 5000);
        string? first = null;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || parts[1] != "device") continue;
            if (parts[0].StartsWith("192.168.", StringComparison.Ordinal)) return parts[0];
            first ??= parts[0];
        }
        return first;
    }

    private static async Task<string> DiscoverAddressAsync()
    {
        var (_, output) = await RunAsync("waydroid", "status", 4000);
        var m = IpRegex().Match(output);
        return m.Success ? $"{m.Groups[1].Value}:5555" : DefaultAddress;
    }

    [GeneratedRegex(@"IP address:\s*(\d{1,3}(?:\.\d{1,3}){3})")]
    private static partial Regex IpRegex();

    [GeneratedRegex(@"(\d+)x(\d+)")]
    private static partial Regex SizeRegex();

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex KeycodeRegex();

    private static async Task<(int Exit, string Output)> RunAsync(string file, string args, int timeoutMs)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(file, args)
            {
                RedirectStandardInput = true, // miras alınan stdin'de 'adb shell' EOF bekleyip takılır
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p is null) return (-1, "");
            p.StandardInput.Close();

            using var cts = new CancellationTokenSource(timeoutMs);
            var stderr = p.StandardError.ReadToEndAsync(cts.Token);
            try
            {
                var stdout = await p.StandardOutput.ReadToEndAsync(cts.Token);
                await p.WaitForExitAsync(cts.Token);
                await stderr;
                return (p.ExitCode, stdout);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch { }
                return (-2, "");
            }
        }
        catch (Win32Exception) { return (-1, ""); }
        catch (Exception) { return (-1, ""); }
    }

    // ---------------- Çözünürlük ----------------

    /// <summary>Android ekran çözünürlüğü (wm size; "Override size" varsa o geçerlidir). Önbelleğe alınır.</summary>
    public static async Task<ResolutionDto?> GetResolutionAsync(bool force = false)
    {
        if (!force && _resolution is not null) return _resolution;
        if (_device.Length == 0 && !await EnsureConnectedAsync()) return null;

        var (_, output) = await RunAsync("adb", $"-s {_device} shell wm size", 5000);
        Match? last = null;
        foreach (var line in output.Split('\n'))
        {
            if (!line.Contains("size:", StringComparison.OrdinalIgnoreCase)) continue;
            var m = SizeRegex().Match(line);
            if (m.Success) last = m;
        }
        if (last is null) return null;

        _resolution = new ResolutionDto { Width = int.Parse(last.Groups[1].Value), Height = int.Parse(last.Groups[2].Value) };
        return _resolution;
    }

    /// <summary>Ön plandaki uygulamanın paket adı (ör. com.foo.game); bilinmiyorsa null.</summary>
    public static async Task<string?> GetForegroundPackageAsync()
    {
        if (!IsConnected) return null;
        var (_, output) = await RunAsync("adb", $"-s {_device} shell dumpsys window | grep mCurrentFocus", 4000);
        var m = FocusRegex().Match(output);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex(@"mCurrentFocus=Window\{\S+ \S+ ([A-Za-z0-9_.]+)/")]
    private static partial Regex FocusRegex();

    // ---------------- Komutlar ----------------

    public static void Send(string command)
    {
        var s = _session;
        if (s is { IsAlive: true })
        {
            s.Enqueue(command);
        }
        else if (DateTime.UtcNow >= _nextAttempt)
        {
            _nextAttempt = DateTime.UtcNow.AddSeconds(3);
            _ = EnsureConnectedAsync();
        }
    }

    /// <summary>background=true: komut cihazda arka planda koşar, sıradaki komutu beklemez.</summary>
    public static void Tap(int x, int y, bool background = true) => Send($"input tap {x} {y}{(background ? " &" : "")}");

    public static void Swipe(int x1, int y1, int x2, int y2, int durationMs = 300) =>
        Send($"input swipe {x1} {y1} {x2} {y2} {durationMs} &");

    public static void KeyEvent(string keycode)
    {
        if (!KeycodeRegex().IsMatch(keycode))
        {
            LogService.Error($"Geçersiz keycode: '{keycode}'");
            return;
        }
        Send($"input keyevent {keycode} &");
    }

    public static void TouchDown(int x, int y) => Send($"input motionevent DOWN {x} {y}");
    public static void TouchMove(int x, int y) => Send($"{MoveCommand} {x} {y}");
    public static void TouchUp(int x, int y) => Send($"input motionevent UP {x} {y}");

    // ---------------- Oturum ----------------

    private sealed class Session
    {
        private readonly Process _proc;
        private readonly LinkedList<string> _queue = new();
        private readonly object _lock = new();
        private bool _closing;
        private readonly Thread _thread;

        public Session(Process proc)
        {
            _proc = proc;
            _thread = new Thread(Loop) { IsBackground = true, Name = "adb-writer" };
            _thread.Start();
        }

        public bool IsAlive
        {
            get
            {
                try { return !_closing && !_proc.HasExited; }
                catch { return false; }
            }
        }

        public void Enqueue(string command)
        {
            lock (_lock)
            {
                if (_closing) return;
                if (command.StartsWith(MoveCommand, StringComparison.Ordinal) &&
                    _queue.Last?.Value.StartsWith(MoveCommand, StringComparison.Ordinal) == true)
                {
                    _queue.Last.Value = command; // eski konumu ez
                }
                else
                {
                    if (_queue.Count >= 512) _queue.RemoveFirst();
                    _queue.AddLast(command);
                }
                Monitor.Pulse(_lock);
            }
        }

        private void Loop()
        {
            try
            {
                while (true)
                {
                    string cmd;
                    lock (_lock)
                    {
                        while (_queue.Count == 0)
                        {
                            if (_closing) return;
                            Monitor.Wait(_lock);
                        }
                        cmd = _queue.First!.Value;
                        _queue.RemoveFirst();
                    }
                    _proc.StandardInput.WriteLine(cmd);
                    _proc.StandardInput.Flush();
                }
            }
            catch { /* süreç kapandı */ }
        }

        /// <summary>Kuyruğu kısa süre boşaltıp (ör. son UP olayı) oturumu kapatır.</summary>
        public void Close()
        {
            lock (_lock)
            {
                _closing = true;
                Monitor.PulseAll(_lock);
            }
            _thread.Join(400);
            try { if (!_proc.HasExited) _proc.Kill(); } catch { }
            _proc.Dispose();
        }
    }
}
