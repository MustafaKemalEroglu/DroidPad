using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using WaydroidController.Models;

namespace WaydroidController.Services;

public class KeyMapperEngine
{
    private readonly Action<string> _logger;

    public KeyMapperEngine(Action<string> logger)
    {
        _logger = logger;
    }

    public async Task HandleKeyPressAsync(string keyName, IList<KeyBinding> keyBindings, ResolutionDto resolution)
    {
        var binding = keyBindings.FirstOrDefault(b => b.KeyName.Equals(keyName, StringComparison.OrdinalIgnoreCase));
        if (binding != null && binding.Type == KeyBindingType.Tap)
        {
            _logger($"[Tap] '{binding.KeyName}' tetiklendi -> X: {binding.X}, Y: {binding.Y}");
            await ExecuteAdbCommandAsync($"shell input tap {binding.X} {binding.Y}");
        }
    }

    public async Task UpdateJoystickStateAsync(IList<KeyBinding> keyBindings, HashSet<string> activePressedKeys, ResolutionDto resolution)
    {
        await Task.CompletedTask;
    }

    // Joystick merkezinden çemberin kıyısına doğru kaydırma yönetimi
    public async Task UpdateJoystickGroupStateAsync(JoystickGroup jg, ResolutionDto resolution)
    {
        double offsetX = 0;
        double offsetY = 0;

        if (jg.IsUpPressed) offsetY -= jg.Radius;
        if (jg.IsDownPressed) offsetY += jg.Radius;
        if (jg.IsLeftPressed) offsetX -= jg.Radius;
        if (jg.IsRightPressed) offsetX += jg.Radius;

        int centerX = jg.X;
        int centerY = jg.Y;

        if (offsetX != 0 || offsetY != 0)
        {
            // Çapraz (Diagonal) basımlarda yarıçapın (Radius) dışına taşmaması için vektör normalizasyonu
            if (offsetX != 0 && offsetY != 0)
            {
                double length = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
                offsetX = (offsetX / length) * jg.Radius;
                offsetY = (offsetY / length) * jg.Radius;
            }

            int targetX = centerX + (int)offsetX;
            int targetY = centerY + (int)offsetY;

            _logger($"🎮 [Joystick] Merkez ({centerX}, {centerY}) -> Çember Kıyısı ({targetX}, {targetY}) [Kaydırma Aktif]");

            // ADB ile merkezden hedef çember kıyısına kaydırma simülasyonu
            await ExecuteAdbCommandAsync($"shell input touchscreen swipe {centerX} {centerY} {targetX} {targetY} 50");
        }
        else
        {
            _logger($"🛑 [Joystick] Merkez ({centerX}, {centerY}) [Bırakıldı]");
            // Tuşlar bırakıldığında joystick merkezine resetleme dokunuşu
            await ExecuteAdbCommandAsync($"shell input tap {centerX} {centerY}");
        }
    }

    private async Task ExecuteAdbCommandAsync(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            _logger($"❌ [ADB Hatası]: {ex.Message}");
        }
    }

    public async Task ExecuteTouchOrSwipeAsync(double windowX, double windowY, double endWindowX, double endWindowY, double windowWidth, double windowHeight, ResolutionDto fallbackResolution, double scaling)
    {
        if (windowWidth <= 0 || windowHeight <= 0) return;

        // Waydroid'in o anki gerçek çözünürlüğünü ADB'den al (yanlış profile resolution ihtimalini sıfırla)
        ResolutionDto resolution = await AdbService.GetWaydroidResolutionAsync(fallbackResolution);

        double scaleX = (double)resolution.Width / windowWidth;
        double scaleY = (double)resolution.Height / windowHeight;

        int x1 = (int)(windowX * scaleX * scaling);
        int y1 = (int)(windowY * scaleY * scaling);
        int x2 = (int)(endWindowX * scaleX * scaling);
        int y2 = (int)(endWindowY * scaleY * scaling);

        x1 = Math.Clamp(x1, 0, resolution.Width);
        y1 = Math.Clamp(y1, 0, resolution.Height);
        x2 = Math.Clamp(x2, 0, resolution.Width);
        y2 = Math.Clamp(y2, 0, resolution.Height);

        string logMessage = $"🎯 [RealResDebug] Waydroid Res: {resolution.Width}x{resolution.Height} | Ham: ({windowX:F0}, {windowY:F0}) | ADB Hedef: ({x1}, {y1})";
        Console.WriteLine(logMessage);
        _logger(logMessage);

        double distance = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));

        if (distance < 8)
        {
            await ExecuteAdbCommandAsync($"shell input tap {x1} {y1}");
        }
        else
        {
            await ExecuteAdbCommandAsync($"shell input touchscreen swipe {x1} {y1} {x2} {y2} 100");
        }
    }
}