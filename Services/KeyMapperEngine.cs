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

    // Gelişmiş Joystick Durum ve Akıcı Sınır Kaydırma Yönetimi
    public async Task UpdateJoystickStateAsync(IReadOnlyList<KeyBinding> allBindings, IReadOnlyCollection<string> pressedKeys, ResolutionDto profileRes, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_adbTarget))
        {
            await InitializeAsync(ct);
        }
        if (_adbTarget is null) return;

        var joystickBindings = allBindings.Where(b => b.Type == KeyBindingType.Joystick).ToList();
        if (!joystickBindings.Any()) return;

        var sample = joystickBindings.First();
        int baseCenterX = sample.X;
        int baseCenterY = sample.Y;
        int radius = sample.Radius;

        var (cw, ch) = await AdbService.GetResolutionAsync(_adbTarget, ct);
        var scaleX = profileRes.Width > 0 ? (double)cw / profileRes.Width : 1.0;
        var scaleY = profileRes.Height > 0 ? (double)ch / profileRes.Height : 1.0;

        int centerX = (int)(baseCenterX * scaleX);
        int centerY = (int)(baseCenterY * scaleX); // Ölçek koruma
        int scaledRadius = (int)(radius * scaleX);

        double dx = 0;
        double dy = 0;

        foreach (var jb in joystickBindings)
        {
            if (pressedKeys.Contains(jb.KeyName))
            {
                if (jb.Direction == JoystickDirection.Up) dy -= 1.0;
                else if (jb.Direction == JoystickDirection.Down) dy += 1.0;
                else if (jb.Direction == JoystickDirection.Left) dx -= 1.0;
                else if (jb.Direction == JoystickDirection.Right) dx += 1.0;
            }
        }

        // Hiçbir joystick tuşuna basılmıyorsa parmağı kaldır (merkeze tap)
        if (dx == 0 && dy == 0)
        {
            if (_isJoystickActive)
            {
                var (okTap, errTap) = await AdbService.TapAsync(_adbTarget, centerX, centerY, ct);
                if (!okTap) _log($"Joystick Bırakma Hatası: {errTap}");
                _isJoystickActive = false;
                _currentTouchX = null;
                _currentTouchY = null;
            }
            return;
        }

        // Çift tuş (diagonal / ara yön: WA, WD, SD, SA) normalizasyonu
        if (dx != 0 && dy != 0)
        {
            double factor = 1.0 / Math.Sqrt(2);
            dx *= factor;
            dy *= factor;
        }

        // Çemberin sınırındaki hedef nokta
        int targetX = centerX + (int)(dx * scaledRadius);
        int targetY = centerY + (int)(dy * scaledRadius);

        if (!_isJoystickActive || _currentTouchX == null || _currentTouchY == null)
        {
            // İlk dokunuş: Merkezden çember dışına doğru kaydır ve basılı tut
            var (okSwipe, errSwipe) = await AdbService.SwipeAsync(_adbTarget, centerX, centerY, targetX, targetY, 20000, ct);
            if (!okSwipe) _log($"Joystick Başlangıç Kaydırma Hatası: {errSwipe}");
            _isJoystickActive = true;
        }
        else
        {
            // Farklı yöne geçiş: Parmak kaldırılmadan mevcut konumdan yeni konuma kaydır
            var (okMove, errMove) = await AdbService.SwipeAsync(_adbTarget, _currentTouchX.Value, _currentTouchY.Value, targetX, targetY, 20000, ct);
            if (!okMove) _log($"Joystick Yön Değiştirme Hatası: {errMove}");
        }

        _currentTouchX = targetX;
        _currentTouchY = targetY;
    }
}