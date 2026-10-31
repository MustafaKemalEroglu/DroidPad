using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WaydroidController.ViewModels;

namespace WaydroidController.Views;

public partial class OverlayWindow : Window
{
    private bool _isDragging;
    private Point _dragStartPoint;
    private Point _lastMovePoint;

    public OverlayWindow()
    {
        InitializeComponent();
    }

    // --- BORDER OLAYLARI ---

    public void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.State.IsPanelOpen) return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(this);
            _lastMovePoint = _dragStartPoint;
            _isDragging = true;

            // DOWN gönder
            _ = vm.StartTouchAsync(_dragStartPoint.X, _dragStartPoint.Y, Bounds.Width, Bounds.Height, RenderScaling);

            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    public void OnBorderPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!_isDragging || vm.State.IsPanelOpen) return;

        var currentPoint = e.GetPosition(this);

        // MOVE gönder
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

        // UP gönder
        vm.EndTouch(endPoint.X, endPoint.Y, RenderScaling);

        e.Pointer.Capture(null);
        e.Handled = true;
    }

    // --- GRID OLAYLARI ---

    public void OnGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        OnBorderPointerPressed(sender, e);
    }

    public void OnGridPointerMoved(object? sender, PointerEventArgs e)
    {
        OnBorderPointerMoved(sender, e);
    }

    public void OnGridPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        OnBorderPointerReleased(sender, e);
    }

    // --- DİĞER OLAYLAR ---

    public void OnStartWaitingForKeyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.StartWaitingForKey();
    }
}