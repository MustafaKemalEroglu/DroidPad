using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using WaydroidController.Models;
using WaydroidController.ViewModels;
using KeyBinding = WaydroidController.Models.KeyBinding;

namespace WaydroidController.Views;

public partial class OverlayWindow : Window
{
    private KeyBinding? _draggedBinding;
    private Point _tapPressPoint;
    private Point _tapInitialCoord;
    private bool _tapHasMoved = false;

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

    private Point? _dragStartPoint;
    private Point? _lastMovePoint;
    private bool _isDragging = false;
    private const double DragThreshold = 8.0; // Sürükleme algılama eşiği

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(this);
            _lastMovePoint = _dragStartPoint;
            _isDragging = false;
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_dragStartPoint.HasValue && DataContext is MainWindowViewModel vm)
        {
            var currentPoint = e.GetPosition(this);

            if (!_isDragging)
            {
                var distance = Math.Sqrt(Math.Pow(currentPoint.X - _dragStartPoint.Value.X, 2) + Math.Pow(currentPoint.Y - _dragStartPoint.Value.Y, 2));
                if (distance > DragThreshold)
                {
                    _isDragging = true;
                }
            }

            if (_isDragging && _lastMovePoint.HasValue)
            {
                // Son konumdan yeni konuma anlık akıcı swipe gönder
                vm.ExecuteInstantSwipe(_lastMovePoint.Value.X, _lastMovePoint.Value.Y, currentPoint.X, currentPoint.Y, Bounds.Width, Bounds.Height, RenderScaling);
                _lastMovePoint = currentPoint;
            }

            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragStartPoint.HasValue && DataContext is MainWindowViewModel vm)
        {
            var endPoint = e.GetPosition(this);
            var start = _dragStartPoint.Value;

            _dragStartPoint = null;
            _lastMovePoint = null;
            e.Pointer.Capture(null);

            if (!_isDragging)
            {
                // Sürükleme eşiği aşılmadıysa saf bir Tıklama (Tap)
                vm.ExecuteInstantTap(start.X, start.Y, Bounds.Width, Bounds.Height, RenderScaling);
            }

            _isDragging = false;
            e.Handled = true;
        }
    }

    // --- Tap Tuşu Sürükleme ve Tıklama (Tuş Atama) Yönetimi ---
    private void OnKeyBadgePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !vm.State.IsPanelOpen) return;
        if (sender is Border border && border.Tag is KeyBinding binding)
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
        if (_draggedBinding == null || DataContext is not MainWindowViewModel vm || !vm.State.IsPanelOpen) return;

        var currentPoint = e.GetPosition(this);
        var diff = currentPoint - _tapPressPoint;

        // Eşik 8 piksele çıkarıldı: Tıklama esnasındaki mikro titremelerin sürükleme sanılması engellendi
        if (Math.Abs(diff.X) > 8 || Math.Abs(diff.Y) > 8)
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

            // Eğer hareket eşiği aşılmadıysa bu bir tıklamadır -> Tuş atama modunu (...) başlat
            if (!_tapHasMoved && DataContext is MainWindowViewModel vm && vm.State.IsPanelOpen)
            {
                vm.StartWaitingForKey(_draggedBinding);
            }

            _draggedBinding = null;
            _tapHasMoved = false;
            e.Handled = true;
        }
    }

    // --- Joystick Sürükleme Yönetimi ---
    private void OnJoystickPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || !vm.State.IsPanelOpen) return;
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
        if (_draggedJoystickGroup == null || DataContext is not MainWindowViewModel vm || !vm.State.IsPanelOpen) return;

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
}