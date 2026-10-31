using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WaydroidController.Models;

namespace WaydroidController.Services
{
    public static class AdbService
    {
        private static Process? _interactiveShell;
        private static StreamWriter? _shellInput;
        private static string? _currentTarget;

        // Sürükleme başlangıcı (Parmağı ekrana basılı tut)
        public static void SendTouchDown(string adbTarget, int x, int y)
        {
            // Android InputManager / sendevent veya scrcpy benzeri akış için DOWN sinyali
            SendInstantCommand(adbTarget, $"input touchscreen down {x} {y}");
        }

        // Sürükleme sırasında parmağın yerini güncelle (Kaldırmadan)
        public static void SendTouchMove(string adbTarget, int x, int y)
        {
            SendInstantCommand(adbTarget, $"input touchscreen move {x} {y}");
        }

        // Sürükleme bitişi (Parmağı kaldır)
        public static void SendTouchUp(string adbTarget, int x, int y)
        {
            SendInstantCommand(adbTarget, $"input touchscreen up {x} {y}");
        }

        // İnteraktif ADB kabuğunu bir kez başlatır ve açık tutar (Süreç başlatma maliyetini sıfırlar)
        public static void EnsureInteractiveSession(string adbTarget)
        {
            if (_interactiveShell == null || _interactiveShell.HasExited || _currentTarget != adbTarget)
            {
                try
                {
                    _interactiveShell?.Kill();
                }
                catch { }

                _currentTarget = adbTarget;
                var startInfo = new ProcessStartInfo
                {
                    FileName = "adb",
                    Arguments = $"-s {adbTarget} shell",
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _interactiveShell = Process.Start(startInfo);
                if (_interactiveShell != null)
                {
                    _shellInput = _interactiveShell.StandardInput;
                    Console.WriteLine($"🚀 [AdbService] İnteraktif ADB oturumu başlatıldı: {adbTarget}");
                }
            }
        }

        // Komutu anında açık olan kabuğa yazar
        public static void SendInstantCommand(string adbTarget, string command)
        {
            EnsureInteractiveSession(adbTarget);
            try
            {
                if (_shellInput != null && !_interactiveShell!.HasExited)
                {
                    _shellInput.WriteLine(command);
                    _shellInput.Flush();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ İnteraktif komut gönderilemedi: {ex.Message}");
            }
        }

        // Hızlı ve akıcı Swipe (Kaydırma / Sürükleme)
        public static void SendInstantSwipe(string adbTarget, int x1, int y1, int x2, int y2, int durationMs = 15)
        {
            SendInstantCommand(adbTarget, $"input swipe {x1} {y1} {x2} {y2} {durationMs}");
        }

        // Hızlı ve akıcı Tap (Dokunma)
        public static void SendInstantTap(string adbTarget, int x, int y)
        {
            SendInstantCommand(adbTarget, $"input tap {x} {y}");
        }

        public static async Task<string?> FindTargetAsync(CancellationToken ct)
        {
            try
            {
                var output = await ExecuteAdbCommandAndGetOutputAsync("devices", ct);
                if (!string.IsNullOrEmpty(output))
                {
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.EndsWith("device", StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = line.Split(new[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 0 && parts[0] != "List")
                            {
                                return parts[0];
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static async Task TrySequentialAsyncPublicAsync(IEnumerable<IEnumerable<string>> commandGroups, CancellationToken ct)
        {
            foreach (var group in commandGroups)
            {
                foreach (var cmd in group)
                {
                    try
                    {
                        await ExecuteAdbCommandAndGetOutputAsync(cmd, ct);
                    }
                    catch { }
                }
            }
        }

        public static async Task<(int Width, int Height)> GetResolutionAsync(string adbTarget, CancellationToken ct)
        {
            try
            {
                var output = await ExecuteAdbCommandAndGetOutputAsync($"-s {adbTarget} shell wm size", ct);

                if (!string.IsNullOrEmpty(output))
                {
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    string? targetLine = null;

                    // 1. ÖNCELİK: Override size (Waydroid'in aktif ölçeklendirilmiş render çözünürlüğü: 1440x786)
                    foreach (var line in lines)
                    {
                        if (line.Contains("Override size:", StringComparison.OrdinalIgnoreCase))
                        {
                            targetLine = line;
                            break;
                        }
                    }

                    // 2. Override yoksa Physical size'a bak
                    if (targetLine == null)
                    {
                        foreach (var line in lines)
                        {
                            if (line.Contains("Physical size:", StringComparison.OrdinalIgnoreCase))
                            {
                                targetLine = line;
                                break;
                            }
                        }
                    }

                    if (targetLine != null)
                    {
                        var parts = targetLine.Split(':');
                        if (parts.Length > 1)
                        {
                            var resStr = parts[1].Trim();
                            var dim = resStr.Split('x');
                            if (dim.Length == 2 && int.TryParse(dim[0].Trim(), out int w) && int.TryParse(dim[1].Trim(), out int h))
                            {
                                Console.WriteLine($"📱 [AdbService] Doğru Çözünürlük (Override) Baz Alındı: {w}x{h}");
                                return (w, h);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Çözünürlük alınırken hata oluştu: {ex.Message}");
            }

            return (1440, 786); // Waydroid aktif çözünürlüğü için güvenli fallback
        }

        public static async Task<ResolutionDto> GetWaydroidResolutionAsync(ResolutionDto fallbackResolution, CancellationToken ct = default)
        {
            // 1. Aktif ADB hedef cihazını dinamik olarak bul
            var target = await FindTargetAsync(ct);

            if (string.IsNullOrEmpty(target))
            {
                // Hedef bulunamadıysa Waydroid için TCP bağlantısını otomatik dene
                Console.WriteLine("🔌 ADB hedefi bulunamadı, 127.0.0.1:5555 bağlantısı deneniyor...");
                await TrySequentialAsyncPublicAsync(new[] { new[] { "connect", "127.0.0.1:5555" } }, ct);
                await Task.Delay(500, ct);

                target = await FindTargetAsync(ct) ?? "127.0.0.1:5555";
            }

            // 2. Bulunan aktif hedef üzerinden gerçek çözünürlüğü al
            var (w, h) = await GetResolutionAsync(target, ct);
            if (w > 0 && h > 0)
            {
                Console.WriteLine($"📱 [AdbService] Aktif Cihaz ({target}) Çözünürlüğü: {w}x{h}");
                return new ResolutionDto { Width = w, Height = h };
            }

            return fallbackResolution;
        }

        public static async Task<(bool Success, string Error)> TapAsync(string adbTarget, int x, int y, CancellationToken ct)
        {
            return await ExecuteCommandAsync($"-s {adbTarget} shell input tap {x} {y}", ct);
        }

        public static async Task<(bool Success, string Error)> SwipeAsync(string adbTarget, int x1, int y1, int x2, int y2, int durationMs, CancellationToken ct)
        {
            return await ExecuteCommandAsync($"-s {adbTarget} shell input touchscreen swipe {x1} {y1} {x2} {y2} {durationMs}", ct);
        }

        public static async Task<(bool Success, string Error)> KeyEventAsync(string adbTarget, string keycode, CancellationToken ct)
        {
            return await ExecuteCommandAsync($"-s {adbTarget} shell input keyevent {keycode}", ct);
        }

        private static async Task<string> ExecuteAdbCommandAndGetOutputAsync(string arguments, CancellationToken ct)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return string.Empty;

            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync(ct);
            return output.Trim();
        }

        private static async Task<(bool Success, string Error)> ExecuteCommandAsync(string arguments, CancellationToken ct)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "adb",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return (false, "Process başlatılamadı.");

                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(ct);

                if (!string.IsNullOrEmpty(error))
                {
                    return (false, error.Trim());
                }
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}