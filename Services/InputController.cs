using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using DroidPad.Models;

namespace DroidPad.Services;

/// <summary>
/// Klavye/fare olaylarını Android dokunmasına çevirir.
/// Not: "input motionevent" tek işaretçi (tek parmak) destekler; bu yüzden aynı anda
/// yalnızca bir joystick tutulabilir ve tuş dokunuşları joystick'i kısa süre keser/geri yükler.
/// </summary>
public sealed class InputController
{
    private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);
    private JoystickGroup? _stick;
    private int _stickX, _stickY;
    private bool _mouseDown;
    private readonly Dictionary<KeyBinding, DispatcherTimer> _turbo = new();

    // ---------- Klavye ----------

    public void KeyDown(string key, IEnumerable<KeyBinding> taps, IReadOnlyList<JoystickGroup> sticks)
    {
        if (!_held.Add(key)) return; // tuş tekrarını (auto-repeat) yoksay

        foreach (var t in taps)
        {
            if (!KeyNames.Same(t.KeyName, key)) continue;
            t.IsPressed = true;
            switch (t.Kind)
            {
                case BindingKind.Swipe:
                    WithStickRestore(() => AdbService.Swipe((int)t.X, (int)t.Y, (int)t.EndX, (int)t.EndY, 250, background: false));
                    break;
                case BindingKind.Turbo:
                    StartTurbo(t);
                    break;
                default:
                    TapWithStickRestore((int)Math.Round(t.X), (int)Math.Round(t.Y));
                    break;
            }
        }

        foreach (var j in sticks)
            if (j.SetKey(key, true)) UpdateStick(j, sticks);
    }

    public void KeyUp(string key, IEnumerable<KeyBinding> taps, IReadOnlyList<JoystickGroup> sticks)
    {
        if (!_held.Remove(key)) return;

        foreach (var t in taps)
        {
            if (!KeyNames.Same(t.KeyName, key)) continue;
            t.IsPressed = false;
            StopTurbo(t);
        }

        foreach (var j in sticks)
            if (j.SetKey(key, false)) UpdateStick(j, sticks);
    }

    /// <summary>Pencere odağı kaybedilince takılı kalan tuşları bırakır.</summary>
    public void ReleaseAll(IEnumerable<KeyBinding> taps, IReadOnlyList<JoystickGroup> sticks)
    {
        _held.Clear();
        foreach (var t in taps) t.IsPressed = false;
        foreach (var t in _turbo.Keys.ToList()) StopTurbo(t);
        foreach (var j in sticks) j.ReleaseAll();
        if (_stick is not null)
        {
            AdbService.TouchUp(_stickX, _stickY);
            _stick = null;
        }
        if (_mouseDown)
        {
            _mouseDown = false;
        }
    }

    private void UpdateStick(JoystickGroup j, IReadOnlyList<JoystickGroup> all)
    {
        var (dx, dy) = j.GetOffset();
        if (j.IsActive && (dx != 0 || dy != 0))
        {
            int cx = (int)Math.Round(j.X), cy = (int)Math.Round(j.Y);
            int tx = cx + (int)Math.Round(dx), ty = cy + (int)Math.Round(dy);

            if (_stick is null)
            {
                _stick = j;
                AdbService.TouchDown(cx, cy);
            }
            else if (!ReferenceEquals(_stick, j))
            {
                return; // tek dokunuş: başka joystick zaten tutuluyor
            }

            AdbService.TouchMove(tx, ty);
            _stickX = tx; _stickY = ty;
        }
        else if (ReferenceEquals(_stick, j))
        {
            AdbService.TouchUp(_stickX, _stickY);
            _stick = null;

            foreach (var other in all)               // beklemede başka joystick varsa devral
                if (!ReferenceEquals(other, j) && other.IsActive) { UpdateStick(other, all); break; }
        }
    }

    private void TapWithStickRestore(int x, int y)
    {
        if (_stick is null) AdbService.Tap(x, y);
        else WithStickRestore(() => AdbService.Tap(x, y, background: false));
    }

    /// <summary>Joystick basılıyken: bırak → eylem (sıralı, arka plansız) → geri yükle.</summary>
    private void WithStickRestore(Action action)
    {
        if (_stick is null)
        {
            action();
            return;
        }

        var s = _stick;
        AdbService.TouchUp(_stickX, _stickY);
        action();
        AdbService.TouchDown((int)Math.Round(s.X), (int)Math.Round(s.Y));
        AdbService.TouchMove(_stickX, _stickY);
    }

    // ---------- Turbo: tuş basılıyken hızlı tekrar ----------

    private void StartTurbo(KeyBinding t)
    {
        StopTurbo(t);
        void Fire() => TapWithStickRestore((int)Math.Round(t.X), (int)Math.Round(t.Y));
        Fire();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Math.Clamp(t.IntervalMs, KeyBinding.MinTurboMs, 1000)) };
        timer.Tick += (_, _) => Fire();
        _turbo[t] = timer;
        timer.Start();
    }

    private void StopTurbo(KeyBinding t)
    {
        if (!_turbo.Remove(t, out var timer)) return;
        timer.Stop();
    }

    // ---------- Fare (oyun modu) ----------

    public void MouseDown(int x, int y) { _mouseDown = true; AdbService.TouchDown(x, y); }
    public void MouseMove(int x, int y) { if (_mouseDown) AdbService.TouchMove(x, y); }
    public void MouseUp(int x, int y) { if (!_mouseDown) return; _mouseDown = false; AdbService.TouchUp(x, y); }
}
