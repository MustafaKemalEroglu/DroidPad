using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WaydroidController.Models;
using WaydroidController.ViewModels;

namespace WaydroidController.Views;

public partial class OverlayWindow : Window
{
    private bool _isDraggingItem;
    private object? _draggedItem;          // KeyBinding veya JoystickGroup
    private Point _dragStartPointer;
    private int _dragStartX, _dragStartY;
    private bool _isDragging;
    private Point _dragStartPoint;
    private DateTime _lastMoveTime = DateTime.MinValue;

    public OverlayWindow()
    {
        InitializeComponent();
        this.PointerPressed += OnMainPointerPressed;
        this.PointerMoved += OnMainPointerMoved;
        this.PointerReleased += OnMainPointerReleased;

        // YENİ: Klavye olayları
        this.KeyDown += OnWindowKeyDown;
        this.KeyUp += OnWindowKeyUp;
        this.Focusable = true;
    }

    public void OnMainPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var point = e.GetCurrentPoint(this);

        // --- SAĞ TIK: Düzenleme modunda menü aç ---
        if (point.Properties.IsRightButtonPressed && vm.State.IsPanelOpen)
        {
            vm.State.ContextMenuX = point.Position.X;
            vm.State.ContextMenuY = point.Position.Y;
            vm.State.IsContextMenuOpen = true;
            e.Handled = true;
            return;
        }

        // --- SOL TIK ---
        if (point.Properties.IsLeftButtonPressed)
        {
            // Menü açıksa sadece kapat
            if (vm.State.IsContextMenuOpen)
            {
                vm.State.IsContextMenuOpen = false;
                e.Handled = true;
                return;
            }

            // Oyun modu (panel kapalı) – mevcut davranış
            if (!vm.State.IsPanelOpen)
            {
                _dragStartPoint = e.GetPosition(this);
                _isDragging = true;
                _ = vm.StartTouchAsync(_dragStartPoint.X, _dragStartPoint.Y,
                                       Bounds.Width, Bounds.Height, RenderScaling);
                e.Pointer.Capture(this);
                e.Handled = true;
            }
        }
    }

    public void OnMainPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!_isDragging || vm.State.IsPanelOpen) return;

        // 33ms Throttle (Saniyede ~30 kare)
        var now = DateTime.Now;
        if ((now - _lastMoveTime).TotalMilliseconds < 33)
            return;
        _lastMoveTime = now;

        var currentPoint = e.GetPosition(this);
        vm.MoveTouch(currentPoint.X, currentPoint.Y, RenderScaling);
        e.Handled = true;
    }

    public void OnMainPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!_isDragging) return;

        _isDragging = false;
        var endPoint = e.GetPosition(this);

        vm.EndTouch(endPoint.X, endPoint.Y, RenderScaling);

        e.Pointer.Capture(null);
        e.Handled = true;
    }

    public void OnStartWaitingForKeyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.StartWaitingForKey();
    }

    // Öğe (Tap / Joystick) sürükleme başlangıcı
    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!vm.State.IsPanelOpen) return;               // Sadece düzenleme modunda
        if (sender is not Control control || control.DataContext is null) return;

        _isDraggingItem = true;
        _draggedItem = control.DataContext;
        _dragStartPointer = e.GetPosition(this);

        if (_draggedItem is Models.KeyBinding kb) { _dragStartX = kb.X; _dragStartY = kb.Y; }
        else if (_draggedItem is JoystickGroup jg) { _dragStartX = jg.X; _dragStartY = jg.Y; }

        e.Pointer.Capture(control);
        vm.State.IsContextMenuOpen = false;
        e.Handled = true;
    }

    // Öğe sürükleme
    private void OnItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingItem || _draggedItem is null) return;
        if (!Equals(e.Pointer.Captured, sender)) return;

        var current = e.GetPosition(this);
        double dx = current.X - _dragStartPointer.X;
        double dy = current.Y - _dragStartPointer.Y;

        if (_draggedItem is Models.KeyBinding kb)
        {
            kb.X = _dragStartX + (int)Math.Round(dx);
            kb.Y = _dragStartY + (int)Math.Round(dy);
        }
        else if (_draggedItem is Models.JoystickGroup jg)
        {
            jg.X = _dragStartX + (int)Math.Round(dx);
            jg.Y = _dragStartY + (int)Math.Round(dy);
        }
        e.Handled = true;
    }

    // Öğe sürükleme bitişi
    private void OnItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDraggingItem) return;
        _isDraggingItem = false;
        _draggedItem = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.State.IsPanelOpen) return; // Panel açıkken tuşları oyuna gönderme

        // F1-F12 gibi fonksiyon tuşlarını hariç tut (F12 panel aç/kapat için kullanılıyor)
        if (e.KeyModifiers == KeyModifiers.None && !e.Key.ToString().StartsWith("F"))
        {
            vm.ProcessKeyDown(e.Key.ToString());
            e.Handled = true;
        }
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        vm.ProcessKeyUp(e.Key.ToString());
        e.Handled = true;
    }
}