using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    private const string AckMarker = "@@";

    private static readonly SemaphoreSlim ConnectGate = new(1, 1);
    private static Session? _session;
    private static Session? _touch; // cihazdaki çok dokunuşlu yardımcı (yoksa null → "input" komutlarına düşülür)
    private static string _device = "";
    private static ResolutionDto? _resolution;
    private static DateTime _nextAttempt = DateTime.MinValue;

    public static event Action<bool>? ConnectionChanged;

    public static bool IsConnected => _session?.IsAlive == true;

    /// <summary>Cihaz yardımcısı çalışıyorsa gerçek çok dokunuş (aynı anda joystick + tuş) kullanılabilir.</summary>
    public static bool HasMultiTouch => _touch?.IsAlive == true;

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
                if (code == -1)
                {
                    LogService.Error("'adb' çalıştırılamadı. Kurulu olduğundan emin olun (android-tools).");
                    return Fail();
                }
                if (code == -2) LogService.Warning("'adb connect' zaman aşımına uğradı.");
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
            Session? sessionRef = null;
            proc.OutputDataReceived += (_, a) => { if (a.Data == AckMarker) sessionRef?.Ack(); };
            proc.ErrorDataReceived += static (_, _) => { };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            _device = device;
            _resolution = null;
            var session = new Session(proc, helper: false);
            sessionRef = session;
            _session = session;
            proc.Exited += (_, _) =>
            {
                if (ReferenceEquals(_session, session))
                {
                    _session = null;
                    LogService.Warning("ADB bağlantısı koptu.");
                    ConnectionChanged?.Invoke(false);
                    session.Close(); // yazıcı iş parçacığı ve süreç sızmasın
                }
            };

            LogService.Success($"ADB bağlandı → {device}");
            await StartHelperAsync(device);
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

    // ---------------- Cihaz yardımcısı (çok dokunuş) ----------------

    private const string HelperClass = "DroidPadInput";

    /// <summary>Gömülü .dex'i cihaza (içeriğine göre adlandırılmış olarak) yükler ve kalıcı süreç olarak başlatır. Başarısızsa sessizce "input" yoluna düşülür.</summary>
    private static async Task StartHelperAsync(string device)
    {
        try
        {
            using var res = typeof(AdbService).Assembly.GetManifestResourceStream("droidpad-input.dex");
            if (res is null) return;
            using var ms = new MemoryStream();
            await res.CopyToAsync(ms);
            var bytes = ms.ToArray();
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..10].ToLowerInvariant();
            var remote = $"/data/local/tmp/droidpad-input-{hash}.dex";

            var (exists, _) = await RunAsync("adb", new[] { "-s", device, "shell", "ls", remote }, 5000);
            if (exists != 0)
            {
                var local = Path.Combine(Path.GetTempPath(), $"droidpad-input-{hash}.dex");
                await File.WriteAllBytesAsync(local, bytes);
                var (push, pushOut) = await RunAsync("adb", new[] { "-s", device, "push", local, remote }, 15000);
                try { File.Delete(local); } catch { }
                if (push != 0)
                {
                    LogService.Warning($"Girdi yardımcısı yüklenemedi (standart girdi kullanılacak): {pushOut.Trim()}");
                    return;
                }
            }

            var psi = new ProcessStartInfo("adb")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in new[] { "-s", device, "shell", $"CLASSPATH={remote} app_process / {HelperClass}" }) psi.ArgumentList.Add(a);

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            proc.OutputDataReceived += (_, a) => { if (a.Data == AckMarker) ready.TrySetResult(); };
            proc.ErrorDataReceived += (_, a) => { if (!string.IsNullOrWhiteSpace(a.Data)) LogService.Debug($"yardımcı: {a.Data}"); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            var session = new Session(proc, helper: true);
            session.Enqueue("s"); // hazır olduğunda "@@" döner
            if (await Task.WhenAny(ready.Task, Task.Delay(5000)) != ready.Task)
            {
                session.Close();
                LogService.Warning("Girdi yardımcısı başlatılamadı; standart girdi (tek dokunuş) kullanılacak.");
                return;
            }

            _touch = session;
            proc.Exited += (_, _) =>
            {
                if (ReferenceEquals(_touch, session))
                {
                    _touch = null;
                    LogService.Warning("Girdi yardımcısı sonlandı; standart girdiye dönüldü.");
                }
                session.Close();
            };
            LogService.Success("Çok dokunuşlu girdi yardımcısı etkin.");
        }
        catch (Exception ex)
        {
            LogService.Warning($"Girdi yardımcısı kullanılamıyor: {ex.Message}");
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
        var t = _touch;
        _session = null;
        _touch = null;
        t?.Enqueue("r"); // açık dokunuşlar bırakılsın
        t?.Close();
        if (s is null) return;
        s.Close();
        ConnectionChanged?.Invoke(false);
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

    private static Task<(int Exit, string Output)> RunAsync(string file, string args, int timeoutMs) =>
        RunCoreAsync(new ProcessStartInfo(file, args), timeoutMs);

    private static Task<(int Exit, string Output)> RunAsync(string file, IEnumerable<string> args, int timeoutMs)
    {
        var psi = new ProcessStartInfo(file);
        foreach (var a in args) psi.ArgumentList.Add(a);
        return RunCoreAsync(psi, timeoutMs);
    }

    private static async Task<(int Exit, string Output)> RunCoreAsync(ProcessStartInfo psi, int timeoutMs)
    {
        try
        {
            psi.RedirectStandardInput = true; // miras alınan stdin'de 'adb shell' EOF bekleyip takılır
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using var p = Process.Start(psi);
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

    /// <summary>Ekran klavyesi açık mı (metin girişi bekleniyor mu)? Okunamazsa null.</summary>
    public static async Task<bool?> IsKeyboardShownAsync()
    {
        if (!IsConnected) return null;
        var (exit, output) = await RunAsync("adb", $"-s {_device} shell \"dumpsys input_method | grep -E 'mInputShown|mImeWindowVis'\"", 2000);
        if (exit != 0 || output.Length == 0) return exit == 0 ? false : null;
        return output.Contains("mInputShown=true", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"mCurrentFocus=Window\{\S+ \S+ ([A-Za-z0-9_.]+)/")]
    private static partial Regex FocusRegex();

    // ---------------- Araçlar (ekran görüntüsü, APK) ----------------

    /// <summary>Ekran görüntüsünü PNG olarak kaydeder; başarılıysa dosya yolunu döndürür.</summary>
    public static async Task<string?> ScreenshotAsync(string directory)
    {
        if (!await EnsureConnectedAsync()) return null;
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"DroidPad_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            var psi = new ProcessStartInfo("adb")
            {
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in new[] { "-s", _device, "exec-out", "screencap", "-p" }) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return null;
            p.StandardInput.Close();

            using var cts = new CancellationTokenSource(10000);
            await using (var fs = File.Create(path))
                await p.StandardOutput.BaseStream.CopyToAsync(fs, cts.Token);
            await p.WaitForExitAsync(cts.Token);
            if (p.ExitCode == 0 && new FileInfo(path).Length > 0) return path;
            File.Delete(path);
            return null;
        }
        catch (Exception ex)
        {
            LogService.Error($"Ekran görüntüsü hatası: {ex.Message}");
            return null;
        }
    }

    public static async Task<bool> InstallApkAsync(string apkPath)
    {
        if (!await EnsureConnectedAsync()) return false;
        var (code, output) = await RunAsync("adb", new[] { "-s", _device, "install", "-r", apkPath }, 180000);
        if (code == 0 && output.Contains("Success", StringComparison.OrdinalIgnoreCase)) return true;
        LogService.Error($"APK kurulamadı: {output.Trim()}");
        return false;
    }

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

    // Yardımcı varsa her eylem kendi işaretçi numarasıyla gider; böylece birbirini kesmez.
    public const int PtrMouse = 0, PtrStick = 1, PtrKey = 2, PtrKeySwipe = 3, PtrMacro = 4;

    private static void SendTouch(string helperCmd)
    {
        var t = _touch;
        if (t is { IsAlive: true }) t.Enqueue(helperCmd);
    }

    /// <summary>background=true: (yalnızca "input" yolunda) komut cihazda arka planda koşar, sıradaki komutu beklemez.</summary>
    public static void Tap(int x, int y, bool background = true, int pointer = PtrMacro)
    {
        if (HasMultiTouch) SendTouch($"t {pointer} {x} {y}");
        else Send($"input tap {x} {y}{(background ? " &" : "")}");
    }

    public static void Swipe(int x1, int y1, int x2, int y2, int durationMs = 300, bool background = true, int pointer = PtrMacro)
    {
        if (HasMultiTouch) SendTouch($"p {pointer} {x1} {y1} {x2} {y2} {durationMs}");
        else Send($"input swipe {x1} {y1} {x2} {y2} {durationMs}{(background ? " &" : "")}");
    }

    /// <summary>Aynı noktada uzun süre kalan "swipe" uzun basma sayılır.</summary>
    public static void LongPress(int x, int y, int durationMs, int pointer = PtrMacro) => Swipe(x, y, x, y, durationMs, true, pointer);

    /// <summary>"input text" yalnızca ASCII destekler; boşluk %s olur ("%" için kaçış yoktur), kabuk özel karakterleri tırnaklanır.</summary>
    public static void Text(string text)
    {
        var ascii = new string(text.Where(c => c is >= ' ' and <= '~').ToArray());
        if (ascii.Length != text.Length) LogService.Warning("Metindeki ASCII olmayan karakterler atlandı.");
        if (ascii.Length == 0) return;
        var escaped = ascii.Replace(" ", "%s").Replace("'", "'\\''");
        Send($"input text '{escaped}'");
    }

    /// <param name="background">false: sıralı çalışır (yazma modunda Backspace/Enter, metinle yarışmasın).</param>
    public static void KeyEvent(string keycode, bool background = true)
    {
        if (!KeycodeRegex().IsMatch(keycode))
        {
            LogService.Error($"Geçersiz keycode: '{keycode}'");
            return;
        }
        Send($"input keyevent {keycode}{(background ? " &" : "")}");
    }

    public static void TouchDown(int x, int y, int pointer = PtrMouse)
    {
        if (HasMultiTouch) SendTouch($"d {pointer} {x} {y}");
        else Send($"input motionevent DOWN {x} {y}");
    }

    public static void TouchMove(int x, int y, int pointer = PtrMouse)
    {
        if (HasMultiTouch) SendTouch($"m {pointer} {x} {y}");
        else Send($"{MoveCommand} {x} {y}");
    }

    public static void TouchUp(int x, int y, int pointer = PtrMouse)
    {
        if (HasMultiTouch) SendTouch($"u {pointer}");
        else Send($"input motionevent UP {x} {y}");
    }

    // ---------------- Oturum ----------------

    private sealed class Session
    {
        private readonly Process _proc;
        private readonly LinkedList<string> _queue = new();
        private readonly object _lock = new();
        private bool _closing;
        private int _pendingMoves;
        private readonly bool _helper;
        private readonly Thread _thread;

        /// <summary>Art arda gelen MOVE'ları birleştirmek için anahtar; MOVE değilse null. Yardımcıda işaretçi başına ayrı.</summary>
        private string? MoveKey(string cmd)
        {
            if (_helper)
                return cmd.Length > 2 && cmd[0] == 'm' && cmd[1] == ' '
                    ? cmd[..Math.Max(2, cmd.IndexOf(' ', 2))] : null;
            return cmd.StartsWith(MoveCommand, StringComparison.Ordinal) ? MoveCommand : null;
        }

        private bool IsContact(string cmd) => _helper
            ? cmd.StartsWith("d ", StringComparison.Ordinal) || cmd.StartsWith("u ", StringComparison.Ordinal) || cmd == "r"
            : cmd.StartsWith("input motionevent DOWN", StringComparison.Ordinal) || cmd.StartsWith("input motionevent UP", StringComparison.Ordinal);

        /// <summary>Kabuk bir MOVE'u işleyince çağrılır; bir sonraki (en güncel) MOVE gönderilebilir.</summary>
        public void Ack()
        {
            lock (_lock)
            {
                if (_pendingMoves > 0) _pendingMoves--;
                Monitor.PulseAll(_lock);
            }
        }

        public Session(Process proc, bool helper)
        {
            _proc = proc;
            _helper = helper;
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
                var key = MoveKey(command);
                if (key is not null && _queue.Last is { } last && MoveKey(last.Value) == key)
                {
                    _queue.Last.Value = command; // eski konumu ez
                }
                else
                {
                    if (_queue.Count >= 2048) DropOldest();
                    _queue.AddLast(command);
                }
                Monitor.Pulse(_lock);
            }
        }

        /// <summary>Taşmada en eski komutu atar; DOWN/UP atılırsa dokunuş cihazda takılı kalır, bu yüzden onlar korunur.</summary>
        private void DropOldest()
        {
            for (var n = _queue.First; n is not null; n = n.Next)
            {
                if (IsContact(n.Value)) continue;
                _queue.Remove(n);
                return;
            }
            _queue.RemoveFirst();
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
                        while (true)
                        {
                            if (_queue.Count == 0)
                            {
                                if (_closing) return;
                                Monitor.Wait(_lock);
                                continue;
                            }
                            // Kabuk önceki MOVE'u bitirmeden yenisini yazma: bekleyenler birleşsin, gecikme birikmesin
                            if (!_helper && _queue.First!.Value.StartsWith(MoveCommand, StringComparison.Ordinal) && _pendingMoves >= 1 && !_closing)
                            {
                                if (!Monitor.Wait(_lock, 250)) _pendingMoves = 0; // onay kaybolduysa takılma
                                continue;
                            }
                            break;
                        }
                        cmd = _queue.First!.Value;
                        _queue.RemoveFirst();
                        if (!_helper && cmd.StartsWith(MoveCommand, StringComparison.Ordinal))
                        {
                            _pendingMoves++;
                            cmd += $"; echo {AckMarker}";
                        }
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
