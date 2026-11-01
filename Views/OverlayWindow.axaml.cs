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

    public OverlayWindow()
    {
        InitializeComponent();
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
            _isDragging = true;

            // Scrcpy üzerinden DOWN
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

        // Scrcpy üzerinden MOVE (Frenleme yok, her kare anında gidiyor)
        vm.MoveTouch(currentPoint.X, currentPoint.Y, RenderScaling);

        e.Handled = true;
    }

    public void OnBorderPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!_isDragging) return;

        _isDragging = false;
        var endPoint = e.GetPosition(this);

        // Scrcpy üzerinden UP
        vm.EndTouch(endPoint.X, endPoint.Y, RenderScaling);

        e.Pointer.Capture(null);
        e.Handled = true;
    }

    // Köprüler
    public void OnGridPointerPressed(object? sender, PointerPressedEventArgs e) => OnBorderPointerPressed(sender, e);
    public void OnGridPointerMoved(object? sender, PointerEventArgs e) => OnBorderPointerMoved(sender, e);
    public void OnGridPointerReleased(object? sender, PointerReleasedEventArgs e) => OnBorderPointerReleased(sender, e);
    public void OnStartWaitingForKeyClick(object? sender, RoutedEventArgs e) => (DataContext as MainWindowViewModel)?.StartWaitingForKey();
}