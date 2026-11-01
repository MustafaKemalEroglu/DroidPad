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
    private DateTime _lastMoveTime = DateTime.MinValue;

    public OverlayWindow()
    {
        InitializeComponent();
        this.PointerPressed += OnBorderPointerPressed;
        this.PointerMoved += OnBorderPointerMoved;
        this.PointerReleased += OnBorderPointerReleased;

        // YENİ: Klavye olayları
        this.KeyDown += OnWindowKeyDown;
        this.KeyUp += OnWindowKeyUp;
        this.Focusable = true;
    }

    public void OnBorderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.State.IsPanelOpen) return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = e.GetPosition(this);
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

        // 33ms Throttle (Saniyede ~30 kare)
        var now = DateTime.Now;
        if ((now - _lastMoveTime).TotalMilliseconds < 33)
            return;
        _lastMoveTime = now;

        var currentPoint = e.GetPosition(this);
        vm.MoveTouch(currentPoint.X, currentPoint.Y, RenderScaling);
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

    public void OnGridPointerPressed(object? sender, PointerPressedEventArgs e) => OnBorderPointerPressed(sender, e);
    public void OnGridPointerMoved(object? sender, PointerEventArgs e) => OnBorderPointerMoved(sender, e);
    public void OnGridPointerReleased(object? sender, PointerReleasedEventArgs e) => OnBorderPointerReleased(sender, e);

    public void OnStartWaitingForKeyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.StartWaitingForKey();
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