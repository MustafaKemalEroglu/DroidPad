using System.Diagnostics;
using System.Text.RegularExpressions;
using WaydroidController.Models;

namespace WaydroidController.Services;

public static class AdbService
{
    #region Değişkenler ve Sabitler
    private static Process? _shellProcess;
    private static readonly object _writeLock = new();
    private static readonly object _connectLock = new();
    private static bool _isConnecting;
    private static string _activeDevice = "";

    private const string DefaultWaydroidIp = "192.168.240.112:5555";
    private static readonly string[] SizeSeparators = { "size:", "Size:" };

    // Dokunmatik Ekran (sendevent) Değişkenleri
    private static string _touchDevice = "";
    private static int _maxX = 32767;
    private static int _maxY = 32767;
    private static bool _isTouchInitialized = false;
    #endregion

    #region Bağlantı Yönetimi

    public static async Task<bool> EnsureConnectedAsync()
    {
        if (IsConnected()) return true;

        lock (_connectLock)
        {
            if (_isConnecting) return false;
            _isConnecting = true;
        }

        try
        {
            LogService.Info("ADB Bağlantısı başlatılıyor...");
            _activeDevice = await GetConnectedDeviceAsync();

            if (string.IsNullOrEmpty(_activeDevice))
            {
                LogService.Info($"Cihaz bulunamadı, bağlanmaya çalışılıyor: {DefaultWaydroidIp}");

                using var connectProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = "adb",
                    Arguments = $"connect {DefaultWaydroidIp}",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                if (connectProcess != null) await connectProcess.WaitForExitAsync();
                _activeDevice = await GetConnectedDeviceAsync();
            }

            if (string.IsNullOrEmpty(_activeDevice))
            {
                LogService.Error("HATA: Waydroid bulunamadı veya ADB bağlanamadı!");
                return false;
            }

            LogService.Success($"BAŞARILI: ADB Bağlantısı kuruldu -> {_activeDevice}");
            return StartInteractiveShell();
        }
        catch (Exception ex)
        {
            LogService.Error($"ADB Bağlantı Hatası: {ex.Message}");
            return false;
        }
        finally
        {
            _isConnecting = false;
        }
    }

    private static bool StartInteractiveShell()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = $"-s {_activeDevice} shell",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _shellProcess = new Process { StartInfo = psi };
            _shellProcess.Start();

            // Çıktıyı sürekli oku (Deadlock önlemi)
            Task.Run(() => ReadShellOutput(_shellProcess.StandardOutput));
            Task.Run(() => ReadShellOutput(_shellProcess.StandardError));

            return true;
        }
        catch (Exception ex)
        {
            LogService.Error($"Shell Başlatma Hatası: {ex.Message}");
            return false;
        }
    }

    private static void ReadShellOutput(System.IO.StreamReader reader)
    {
        try
        {
            while (!reader.EndOfStream) reader.ReadLine();
        }
        catch { }
    }

    private static async Task<string> GetConnectedDeviceAsync()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = "devices",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (p != null)
            {
                var output = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();

                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.Contains("device") && !line.Contains("List of devices") && !line.Contains("offline"))
                    {
                        var parts = line.Split('\t', ' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0) return parts[0];
                    }
                }
            }
        }
        catch { }
        return "";
    }

    public static bool IsConnected() => _shellProcess != null && !_shellProcess.HasExited;

    public static void Disconnect()
    {
        lock (_writeLock)
        {
            if (_shellProcess != null && !_shellProcess.HasExited)
            {
                try { _shellProcess.Kill(); } catch { }
            }
            _shellProcess?.Dispose();
            _shellProcess = null;
        }
    }

    #endregion

    #region Dokunmatik Ekran Başlatma (Sendevent)

    public static async Task InitializeTouchDeviceAsync()
    {
        if (_isTouchInitialized) return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = $"-s {_activeDevice} shell getevent -pl",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return;

            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();

            // /dev/input/eventX yolunu bul
            var deviceMatch = Regex.Match(output, @"add device \d+: (/dev/input/event\d+)");
            if (deviceMatch.Success) _touchDevice = deviceMatch.Groups[1].Value;

            // Maksimum X ve Y değerlerini bul (ABS_MT_POSITION_X / Y)
            var xMatch = Regex.Match(output, @"0035  : value \d+, min \d+, max (\d+)");
            if (xMatch.Success) _maxX = int.Parse(xMatch.Groups[1].Value);

            var yMatch = Regex.Match(output, @"0036  : value \d+, min \d+, max (\d+)");
            if (yMatch.Success) _maxY = int.Parse(yMatch.Groups[1].Value);

            _isTouchInitialized = !string.IsNullOrEmpty(_touchDevice);
            if (_isTouchInitialized)
                LogService.Info($"[ADB] Sendevent Hazır: {_touchDevice} | X: {_maxX}, Y: {_maxY}");
            else
                LogService.Warning("[ADB] UYARI: Dokunmatik ekran bulunamadı, input komutuna dönülecek.");
        }
        catch (Exception ex)
        {
            LogService.Error($"[ADB] Dokunmatik başlatma hatası: {ex.Message}");
        }
    }

    #endregion

    #region Komut Gönderimi

    public static void SendCommand(string command)
    {
        if (!IsConnected())
        {
            _ = EnsureConnectedAsync();
            return;
        }

        lock (_writeLock)
        {
            try
            {
                if (_shellProcess != null && !_shellProcess.HasExited)
                {
                    _shellProcess.StandardInput.WriteLine(command);
                    _shellProcess.StandardInput.Flush();
                }
            }
            catch
            {
                Disconnect();
            }
        }
    }

    #endregion

    #region Girdi Simülasyonları (Dokunma ve Tuşlar)

    public static Task TapAsync(int x, int y)
    {
        SendCommand($"input tap {x} {y}");
        return Task.CompletedTask;
    }

    public static Task SwipeAsync(int x1, int y1, int x2, int y2, int durationMs = 300)
    {
        SendCommand($"input swipe {x1} {y1} {x2} {y2} {durationMs}");
        return Task.CompletedTask;
    }

    public static Task KeyEventAsync(string keycode)
    {
        SendCommand($"input keyevent {keycode}");
        return Task.CompletedTask;
    }

    // --- Sendevent Tabanlı Akıcı Dokunma ---

    public static void SendTouchDown(int screenX, int screenY, int targetWidth, int targetHeight)
    {
        if (!_isTouchInitialized) { SendCommand($"input motionevent DOWN {screenX} {screenY}"); return; }

        int rawX = (int)((long)screenX * _maxX / targetWidth);
        int rawY = (int)((long)screenY * _maxY / targetHeight);

        SendCommand($"sendevent {_touchDevice} 3 57 0");    // TRACKING_ID
        SendCommand($"sendevent {_touchDevice} 3 53 {rawX}"); // X
        SendCommand($"sendevent {_touchDevice} 3 54 {rawY}"); // Y
        SendCommand($"sendevent {_touchDevice} 1 330 1");   // BTN_TOUCH DOWN
        SendCommand($"sendevent {_touchDevice} 0 0 0");     // SYN_REPORT
    }

    public static void SendTouchMove(int screenX, int screenY, int targetWidth, int targetHeight)
    {
        if (!_isTouchInitialized) { SendCommand($"input motionevent MOVE {screenX} {screenY}"); return; }

        int rawX = (int)((long)screenX * _maxX / targetWidth);
        int rawY = (int)((long)screenY * _maxY / targetHeight);

        SendCommand($"sendevent {_touchDevice} 3 53 {rawX}");
        SendCommand($"sendevent {_touchDevice} 3 54 {rawY}");
        SendCommand($"sendevent {_touchDevice} 0 0 0");     // SYN_REPORT
    }

    public static void SendTouchUp(int screenX, int screenY, int targetWidth, int targetHeight)
    {
        if (!_isTouchInitialized) { SendCommand($"input motionevent UP {screenX} {screenY}"); return; }

        SendCommand($"sendevent {_touchDevice} 3 57 -1");   // TRACKING_ID -1
        SendCommand($"sendevent {_touchDevice} 1 330 0");   // BTN_TOUCH UP
        SendCommand($"sendevent {_touchDevice} 0 0 0");     // SYN_REPORT
    }

    #endregion

    #region Ekran Çözünürlüğü

    public static async Task<ResolutionDto> GetResolutionAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_activeDevice))
            {
                _activeDevice = await GetConnectedDeviceAsync();
                if (string.IsNullOrEmpty(_activeDevice)) return new ResolutionDto { Width = 1920, Height = 1080 };
            }

            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = $"-s {_activeDevice} shell wm size",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (p != null)
            {
                var output = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();

                var parts = output.Split(SizeSeparators, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    var sizeStr = parts[^1].Trim();
                    var dims = sizeStr.Split('x');
                    if (dims.Length == 2 && int.TryParse(dims[0].Trim(), out var w) && int.TryParse(dims[1].Trim(), out var h))
                        return new ResolutionDto { Width = w, Height = h };
                }
            }
        }
        catch { }

        return new ResolutionDto { Width = 1920, Height = 1080 };
    }

    #endregion
}