using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WaydroidController.Models;

namespace WaydroidController.Services;

public sealed class KeyMapperEngine
{
    private readonly Action<string> _log;
    private string? _adbTarget;

    public KeyMapperEngine(Action<string> log)
    {
        _log = log;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _adbTarget = await AdbService.FindTargetAsync(ct);
        if (_adbTarget is null)
        {
            _adbTarget = "127.0.0.1:5555";
        }
    }

    public async Task HandleKeyPressAsync(string keyName, IReadOnlyList<KeyBinding> bindings, ResolutionDto profileRes, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_adbTarget))
        {
            await InitializeAsync(ct);
        }
        if (_adbTarget is null) return;

        var binding = bindings.FirstOrDefault(b => b.KeyName.Equals(keyName, StringComparison.OrdinalIgnoreCase));
        if (binding == null) return;

        // Gerçek Waydroid çözünürlüğünü al ve ölçeklendirme faktörlerini hesapla
        var (cw, ch) = await AdbService.GetResolutionAsync(_adbTarget, ct);
        var scaleX = profileRes.Width > 0 ? (double)cw / profileRes.Width : 1.0;
        var scaleY = profileRes.Height > 0 ? (double)ch / profileRes.Height : 1.0;

        int targetX = (int)(binding.X * scaleX);
        int targetY = (int)(binding.Y * scaleY);

        if (binding.Type == KeyBindingType.Tap)
        {
            var (ok, err) = await AdbService.TapAsync(_adbTarget, targetX, targetY, ct);
            if (!ok) _log($"Tuş Hatası [{keyName}]: {err}");
        }
        else if (binding.Type == KeyBindingType.Joystick)
        {
            int radius = (int)(binding.Radius * scaleX);
            int endX = targetX;
            int endY = targetY;

            if (keyName.Equals("W", StringComparison.OrdinalIgnoreCase)) endY -= radius;
            else if (keyName.Equals("S", StringComparison.OrdinalIgnoreCase)) endY += radius;
            else if (keyName.Equals("A", StringComparison.OrdinalIgnoreCase)) endX -= radius;
            else if (keyName.Equals("D", StringComparison.OrdinalIgnoreCase)) endX += radius;

            var (ok, err) = await AdbService.SwipeAsync(_adbTarget, targetX, targetY, endX, endY, 80, ct);
            if (!ok) _log($"Joystick Hatası [{keyName}]: {err}");
        }
    }
}