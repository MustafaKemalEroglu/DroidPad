using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WaydroidController.Models;

namespace WaydroidController.Services;

public static class AdbService
{
    private static Process? _shellProcess;
    private static readonly object _writeLock = new();
    private static readonly object _connectLock = new();
    private static bool _isConnecting;
    private static string _activeDevice = "";

    private const string DefaultWaydroidIp = "192.168.240.112:5555";
    private static readonly string[] SizeSeparators = { "size:", "Size:" };

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

            // Deadlock önlemi: Çıktıyı sürekli oku
            Task.Run(() => { try { while (!_shellProcess.StandardOutput.EndOfStream) _shellProcess.StandardOutput.ReadLine(); } catch { } });
            Task.Run(() => { try { while (!_shellProcess.StandardError.EndOfStream) _shellProcess.StandardError.ReadLine(); } catch { } });

            return true;
        }
        catch (Exception ex)
        {
            LogService.Error($"Shell Başlatma Hatası: {ex.Message}");
            return false;
        }
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

    // --- Kesintisiz Dokunma (motionevent) ---
    public static void SendTouchDown(int x, int y) => SendCommand($"input motionevent DOWN {x} {y}");
    public static void SendTouchMove(int x, int y) => SendCommand($"input motionevent MOVE {x} {y}");
    public static void SendTouchUp(int x, int y) => SendCommand($"input motionevent UP {x} {y}");

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
}