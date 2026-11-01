using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WaydroidController.Services;
using WaydroidController.ViewModels;

namespace WaydroidController.Views;

public partial class OverlayWindow : Window
{
    private bool _isDragging;
    private Point _dragStartPoint;
    private Point _lastMovePoint;
    private DateTime _lastMoveTime = DateTime.MinValue; // Frenleme için zaman tutucu

    public OverlayWindow()
    {
        InitializeComponent();

        // Olayları kod tarafında bağla
        this.PointerPressed += OnBorderPointerPressed;
        this.PointerMoved += OnBorderPointerMoved;
        this.PointerReleased += OnBorderPointerReleased;
    }

    public void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.State.IsPanelOpen) return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(this);
            _lastMovePoint = _dragStartPoint;
            _isDragging = true;

            _ = vm.StartTouchAsync(_dragStartPoint.X, _dragStartPoint.Y, Bounds.Width, Bounds.Height, RenderScaling);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    public void OnBorderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!_isDragging || vm.State.IsPanelOpen) return;

        // --- FRENLEME (THROTTLE) MANTIĞI ---
        // Saniyede 120+ komut göndermek yerine, 16ms'de bir (60 FPS) komut gönder.
        var now = DateTime.Now;
        if ((now - _lastMoveTime).TotalMilliseconds < 16)
            return;

        _lastMoveTime = now;

        var currentPoint = e.GetPosition(this);
        vm.MoveTouch(currentPoint.X, currentPoint.Y, RenderScaling);
        _lastMovePoint = currentPoint;
        e.Handled = true;
    }

    public void OnBorderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!_isDragging) return;

        _isDragging = false;
        var endPoint = e.GetPosition(this);

        vm.EndTouch(endPoint.X, endPoint.Y, RenderScaling);

        e.Pointer.Capture(null);
        e.Handled = true;
    }

    // XAML'den hala çağrılıyorsa diye eski köprüler
    public void OnGridPointerPressed(object? sender, PointerPressedEventArgs e) => OnBorderPointerPressed(sender, e);
    public void OnGridPointerMoved(object? sender, PointerEventArgs e) => OnBorderPointerMoved(sender, e);
    public void OnGridPointerReleased(object? sender, PointerReleasedEventArgs e) => OnBorderPointerReleased(sender, e);

    public void OnStartWaitingForKeyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.StartWaitingForKey();
    }
}