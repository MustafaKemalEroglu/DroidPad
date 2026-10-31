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

    // Joystick aktif parmak pozisyonu takibi (Çift tuş / ara yön geçişleri için)
    private int? _currentTouchX;
    private int? _currentTouchY;
    private bool _isJoystickActive;

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
        if (binding == null || binding.Type != KeyBindingType.Tap) return;

        var (cw, ch) = await AdbService.GetResolutionAsync(_adbTarget, ct);
        var scaleX = profileRes.Width > 0 ? (double)cw / profileRes.Width : 1.0;
        var scaleY = profileRes.Height > 0 ? (double)ch / profileRes.Height : 1.0;

        int targetX = (int)(binding.X * scaleX);
        int targetY = (int)(binding.Y * scaleY);

        var (ok, err) = await AdbService.TapAsync(_adbTarget, targetX, targetY, ct);
        if (!ok) _log($"Tuş Hatası [{keyName}]: {err}");
    }

    public async Task UpdateJoystickGroupStateAsync(JoystickGroup jg, ResolutionDto resolution)
    {
        int targetX = jg.X;
        int targetY = jg.Y;

        int offsetX = 0;
        int offsetY = 0;

        if (jg.IsUpPressed) offsetY -= jg.Radius;
        if (jg.IsDownPressed) offsetY += jg.Radius;
        if (jg.IsLeftPressed) offsetX -= jg.Radius;
        if (jg.IsRightPressed) offsetX += jg.Radius;

        if (offsetX != 0 || offsetY != 0)
        {
            targetX += offsetX;
            targetY += offsetY;
            _log($"[Joystick] Merkez ({jg.X}, {jg.Y}) -> Hedef Yön ({targetX}, {targetY}) [Kaydırma Aktif]");
            // ADB üzerinden merkezden hedef yöne kaydırma komutu burada tetiklenir
        }
        else
        {
            _log($"[Joystick] Merkez ({jg.X}, {jg.Y}) [Bırakıldı]");
        }
        await Task.CompletedTask;
    }
}