using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WaydroidController.Services; // LogManager için
using WaydroidController.ViewModels;

namespace WaydroidController.Views;

public partial class OverlayWindow : Window
{
    private bool _isDragging;
    private Point _dragStartPoint;
    private Point _lastMovePoint;
    private DateTime _lastMoveTime = DateTime.MinValue;

    public OverlayWindow()
    {
        InitializeComponent();

        // XAML bağlantılarını atlayıp doğrudan pencereye olayları bağlıyoruz
        this.PointerPressed += OnBorderPointerPressed;
        this.PointerMoved += OnBorderPointerMoved;
        this.PointerReleased += OnBorderPointerReleased;

        // Test için log
        LogService.Info("[Overlay] Pencere hazır. Tıklama bekleniyor...");
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

            // Tıklamanın algılandığını logla
            LogService.Info($"[Overlay] TIKLAMA ALGILANDI. Başlangıç: {_dragStartPoint.X}, {_dragStartPoint.Y}");

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

        // --- THROTTLE MANTIĞI ---
        // Fare deli gibi hızlı hareket etse bile, ADB'ye en fazla 60 FPS hızında komut gönder.
        var now = DateTime.Now;
        if ((now - _lastMoveTime).TotalMilliseconds < 16)
            return; // 16ms (saniyenin 1/60'ı) geçmediyse bu hareketi atla

        _lastMoveTime = now;

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

        LogService.Info($"[Overlay] BIRAKILDI. Bitiş: {endPoint.X}, {endPoint.Y}");

        // UP gönder
        vm.EndTouch(endPoint.X, endPoint.Y, RenderScaling);

        e.Pointer.Capture(null);
        e.Handled = true;
    }

    // Grid veya Border olayları için eski köprüler (XAML'den hala çağrılıyorsa diye bırakıldı)
    public void OnGridPointerPressed(object? sender, PointerPressedEventArgs e) => OnBorderPointerPressed(sender, e);
    public void OnGridPointerMoved(object? sender, PointerEventArgs e) => OnBorderPointerMoved(sender, e);
    public void OnGridPointerReleased(object? sender, PointerReleasedEventArgs e) => OnBorderPointerReleased(sender, e);

    public void OnStartWaitingForKeyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.StartWaitingForKey();
    }
}