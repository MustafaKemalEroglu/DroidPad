using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using WaydroidController.Models;
using WaydroidController.ViewModels;

namespace WaydroidController.Views;

public partial class OverlayWindow : Window
{
    // Tap Tuşu Sürükleme State Alanları
    private WaydroidController.Models.KeyBinding? _draggedBinding;
    private Point _tapPressPoint;
    private Point _tapInitialCoord;
    private bool _tapHasMoved = false;

    // Joystick Sürükleme State Alanları
    private JoystickGroup? _draggedJoystickGroup;
    private Point _joystickPressPoint;
    private Point _joystickInitialCoord;

    public OverlayWindow()
    {
        InitializeComponent();

        this.KeyDown += OnWindowKeyDown;
        this.KeyUp += OnWindowKeyUp;
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (e.Key == Key.F12)
        {
            vm.TogglePanelCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
            return;

        // Tuş atama (dinleme) modu aktifse basılan tuşu ata
        if (vm.HandleKeyAssignment(e))
        {
            e.Handled = true;
            return;
        }

        var keyName = e.Key.ToString();
        await vm.OnKeyPressedAsync(keyName);
    }

    private async void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var keyName = e.Key.ToString();

        if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
            return;

        await vm.OnKeyReleasedAsync(keyName);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (DataContext is not MainWindowViewModel vm) return;

        if (vm.IsContextMenuOpen)
        {
            vm.IsContextMenuOpen = false;
        }

        var point = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;

        if (vm.IsPanelOpen && properties.IsRightButtonPressed)
        {
            vm.OpenContextMenu(point.X, point.Y);
            e.Handled = true;
        }
    }

    // --- Tap Tuşu Sürükleme ve Tıklama (Düzenleme) Yönetimi ---
    private void OnKeyBadgePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !vm.IsPanelOpen) return;
        if (sender is Border border && border.Tag is WaydroidController.Models.KeyBinding binding)
        {
            var properties = e.GetCurrentPoint(this).Properties;
            if (properties.IsLeftButtonPressed)
            {
                _draggedBinding = binding;
                _tapPressPoint = e.GetPosition(this);
                _tapInitialCoord = new Point(binding.X, binding.Y);
                _tapHasMoved = false;

                e.Pointer.Capture(border);
                e.Handled = true;
            }
        }
    }

    private void OnKeyBadgePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedBinding == null || DataContext is not MainWindowViewModel vm || !vm.IsPanelOpen) return;

        var currentPoint = e.GetPosition(this);
        var diff = currentPoint - _tapPressPoint;

        if (Math.Abs(diff.X) > 3 || Math.Abs(diff.Y) > 3)
        {
            _tapHasMoved = true;
        }

        if (_tapHasMoved)
        {
            _draggedBinding.X = Math.Max(0, (int)(_tapInitialCoord.X + diff.X));
            _draggedBinding.Y = Math.Max(0, (int)(_tapInitialCoord.Y + diff.Y));
        }

        e.Handled = true;
    }

    private void OnKeyBadgePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggedBinding != null)
        {
            e.Pointer.Capture(null);

            // Hareket etmediyse bu bir tıklamadır -> Tuş düzenleme (dinleme) modunu başlat
            if (!_tapHasMoved && DataContext is MainWindowViewModel vm && vm.IsPanelOpen)
            {
                vm.StartWaitingForKey(_draggedBinding);
            }

            _draggedBinding = null;
            _tapHasMoved = false;
            e.Handled = true;
        }
    }

    // --- Çember Joystick Sürükleme Yönetimi ---
    private void OnJoystickPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !vm.IsPanelOpen) return;
        if (sender is Grid grid && grid.Tag is JoystickGroup joystick)
        {
            var properties = e.GetCurrentPoint(this).Properties;
            if (properties.IsLeftButtonPressed)
            {
                _draggedJoystickGroup = joystick;
                _joystickPressPoint = e.GetPosition(this);
                _joystickInitialCoord = new Point(joystick.X, joystick.Y);

                e.Pointer.Capture(grid);
                e.Handled = true;
            }
        }
    }

    private void OnJoystickPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggedJoystickGroup == null || DataContext is not MainWindowViewModel vm || !vm.IsPanelOpen) return;

        var currentPoint = e.GetPosition(this);
        var diff = currentPoint - _joystickPressPoint;

        _draggedJoystickGroup.X = Math.Max(70, (int)(_joystickInitialCoord.X + diff.X));
        _draggedJoystickGroup.Y = Math.Max(70, (int)(_joystickInitialCoord.Y + diff.Y));

        e.Handled = true;
    }

    private void OnJoystickPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggedJoystickGroup != null)
        {
            e.Pointer.Capture(null);
            _draggedJoystickGroup = null;
            e.Handled = true;
        }
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e) { }
    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e) { }
    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e) { }
}