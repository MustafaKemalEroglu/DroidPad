using System.Diagnostics;
using WaydroidController.Models;

namespace WaydroidController.Services;

public static class AdbService
{
    #region Değişkenler ve Sabitler
    private static Process? _shellProcess;
    private static readonly object _writeLock = new();   // Yazma işlemleri için kilit
    private static readonly object _connectLock = new(); // Bağlantı işlemleri için kilit
    private static bool _isConnecting;
    private static string _activeDevice = "";

    private const string DefaultWaydroidIp = "192.168.240.112:5555";
    private static readonly string[] SizeSeparators = { "size:", "Size:" };
    #endregion

    #region Bağlantı Yönetimi (Thread-Safe)

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
            LogManager.Log("ADB Bağlantısı başlatılıyor...");
            _activeDevice = await GetConnectedDeviceAsync();

            if (string.IsNullOrEmpty(_activeDevice))
            {
                LogManager.Log($"Cihaz bulunamadı, bağlanmaya çalışılıyor: {DefaultWaydroidIp}");

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
                LogManager.Log("HATA: Waydroid bulunamadı veya ADB bağlanamadı!", isError: true);
                return false;
            }

            LogManager.Log($"BAŞARILI: ADB Bağlantısı kuruldu -> {_activeDevice}");
            return StartInteractiveShell();
        }
        catch (Exception ex)
        {
            LogManager.Log($"ADB Bağlantı Hatası: {ex.Message}", isError: true);
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
                RedirectStandardError = true, // Hataları yakalamak için
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _shellProcess = new Process { StartInfo = psi };
            _shellProcess.Start();

            // ÇOK ÖNEMLİ: Arka planda çıktıyı sürekli oku. 
            // Eğer okumazsak, işletim sistemi tamponu dolar ve adb donar (Deadlock).
            Task.Run(() => ReadShellOutput(_shellProcess.StandardOutput));
            Task.Run(() => ReadShellOutput(_shellProcess.StandardError));

            return true;
        }
        catch (Exception ex)
        {
            LogManager.Log($"Shell Başlatma Hatası: {ex.Message}", isError: true);
            return false;
        }
    }

    private static void ReadShellOutput(System.IO.StreamReader reader)
    {
        try
        {
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                // İsteğe bağlı: adb'den gelen gizli hataları konsola veya loga yazabilirsiniz
                // if (!string.IsNullOrEmpty(line) && line.Contains("Error")) LogManager.Log($"[ADB Çıktı] {line}");
            }
        }
        catch { /* Süreç öldüğünde fırlatılan istisnayı yoksay */ }
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

    public static bool IsConnected()
    {
        return _shellProcess != null && !_shellProcess.HasExited;
    }

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

    #region Komut Gönderimi (Thread-Safe ve Sıfır Gecikme)

    public static void SendCommand(string command)
    {
        if (!IsConnected())
        {
            _ = EnsureConnectedAsync();
            return;
        }

        // Aynı anda makro ve arayüzden komut gelirse çakışmasını önleyen kilit
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
            catch (Exception ex)
            {
                LogManager.Log($"ADB Komut Gönderme Hatası: {ex.Message}", isError: true);
                Disconnect(); // Hata varsa bağlantıyı kopar ki bir sonraki istekte yeniden bağlansın
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

    // --- Kesintisiz Sürükleme (Motionevent API) ---
    public static void SendTouchDown(int x, int y) => SendCommand($"input motionevent DOWN {x} {y}");
    public static void SendTouchMove(int x, int y) => SendCommand($"input motionevent MOVE {x} {y}");
    public static void SendTouchUp(int x, int y) => SendCommand($"input motionevent UP {x} {y}");

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

                // Gelişmiş Parse: Fiziksel ve Override boyutları arasında her zaman en sonuncuyu alır
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