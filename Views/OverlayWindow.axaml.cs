using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KeyBinding = DroidPad.Models.KeyBinding;
using DroidPad.Models;
using DroidPad.Services;
using DroidPad.ViewModels;

namespace DroidPad.Views;

public partial class OverlayWindow : Window
{
    private static readonly FilePickerFileType JsonType = new("Profil (*.json)") { Patterns = new[] { "*.json" } };

    // Oyun modu dokunma durumu
    private bool _touching;
    private Point _touchStart;
    private long _lastMoveTicks;

    // Öğe sürükleme durumu
    private object? _dragItem;
    private Point _dragOrigin;
    private double _dragStartX, _dragStartY;

    public OverlayWindow()
    {
        InitializeComponent();
        Focusable = true;

        Surface.PointerPressed += OnSurfacePressed;
        Surface.PointerMoved += OnSurfaceMoved;
        Surface.PointerReleased += OnSurfaceReleased;
        Surface.PointerCaptureLost += (_, _) => CancelTouch();

        ItemsLayer.AddHandler(PointerPressedEvent, OnItemPressed, RoutingStrategies.Bubble);
        ItemsLayer.AddHandler(PointerMovedEvent, OnItemMoved, RoutingStrategies.Bubble);
        ItemsLayer.AddHandler(PointerReleasedEvent, OnItemReleased, RoutingStrategies.Bubble);

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Bubble);
        AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Bubble);
        Deactivated += (_, _) => Vm?.ReleaseAllKeys();
        Closing += (_, _) => Vm?.Shutdown();

        LogService.Entries.CollectionChanged += OnLogChanged;
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    // ---------- Koordinat dönüşümü: pencere → Android ekranı ----------

    private Point ToDevice(Point p)
    {
        var vm = Vm;
        var w = Root.Bounds.Width;
        var h = Root.Bounds.Height;
        if (vm is null || w <= 0 || h <= 0) return p;
        return new Point(
            Math.Clamp(p.X * vm.DeviceWidth / w, 0, vm.DeviceWidth - 1),
            Math.Clamp(p.Y * vm.DeviceHeight / h, 0, vm.DeviceHeight - 1));
    }

    // ---------- Oyun yüzeyi ----------

    private void OnSurfacePressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var point = e.GetCurrentPoint(Root);
        var state = vm.State;

        if (point.Properties.IsRightButtonPressed)
        {
            if (!state.IsEditMode) return;
            var dev = ToDevice(point.Position);
            state.ContextMenuX = Math.Clamp(point.Position.X, 0, Math.Max(0, Root.Bounds.Width - 200));
            state.ContextMenuY = Math.Clamp(point.Position.Y, 0, Math.Max(0, Root.Bounds.Height - 150));
            state.ContextDeviceX = dev.X;
            state.ContextDeviceY = dev.Y;
            state.IsContextMenuOpen = true;
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;
        Focus();

        if (state.IsContextMenuOpen)
        {
            state.IsContextMenuOpen = false;
            e.Handled = true;
            return;
        }

        var d = ToDevice(point.Position);

        if (state.IsEditMode)
        {
            if (state.PendingTool is { } tool) vm.PlaceTool(tool, d.X, d.Y);
            else vm.Select(null);
            e.Handled = true;
            return;
        }

        if (state.IsPicking)
        {
            vm.CompletePick(d.X, d.Y);
            e.Handled = true;
            return;
        }

        _touching = true;
        _touchStart = d;
        vm.TouchDown(d.X, d.Y);
        e.Pointer.Capture(Surface);
        e.Handled = true;
    }

    private void OnSurfaceMoved(object? sender, PointerEventArgs e)
    {
        if (!_touching || Vm is not { } vm) return;

        // ~60 Hz ile sınırla; kuyruk zaten ardışık MOVE'ları birleştiriyor
        var now = Environment.TickCount64;
        if (now - _lastMoveTicks < 16) return;
        _lastMoveTicks = now;

        var d = ToDevice(e.GetPosition(Root));
        vm.TouchMove(d.X, d.Y);
    }

    private void OnSurfaceReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_touching || Vm is not { } vm) return;
        _touching = false;
        var d = ToDevice(e.GetPosition(Root));
        vm.TouchUp(d.X, d.Y, _touchStart.X, _touchStart.Y);
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void CancelTouch()
    {
        if (!_touching) return;
        _touching = false;
        Vm?.TouchUp(_touchStart.X, _touchStart.Y, _touchStart.X, _touchStart.Y);
    }

    // ---------- Tuş / joystick sürükleme (düzenleme modu) ----------

    private static object? FindItem(object? source)
    {
        for (var v = source as Visual; v is not null; v = v.GetVisualParent())
            if (v is StyledElement { DataContext: KeyBinding or JoystickGroup } el)
                return el.DataContext;
        return null;
    }

    private void OnItemPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not { } vm || !vm.State.IsEditMode) return;
        if (!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed) return;

        var item = FindItem(e.Source);
        if (item is null) return;

        vm.State.IsContextMenuOpen = false;
        vm.State.PendingTool = null;
        vm.Select(item);

        _dragItem = item;
        _dragOrigin = ToDevice(e.GetPosition(Root));
        (_dragStartX, _dragStartY) = item switch
        {
            KeyBinding k => (k.X, k.Y),
            JoystickGroup j => (j.X, j.Y),
            _ => (0.0, 0.0)
        };
        e.Pointer.Capture(ItemsLayer);
        e.Handled = true;
    }

    private void OnItemMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem is null || Vm is not { } vm) return;

        var cur = ToDevice(e.GetPosition(Root));
        var x = Math.Clamp(_dragStartX + cur.X - _dragOrigin.X, 0, vm.DeviceWidth);
        var y = Math.Clamp(_dragStartY + cur.Y - _dragOrigin.Y, 0, vm.DeviceHeight);

        switch (_dragItem)
        {
            case KeyBinding k: k.X = x; k.Y = y; break;
            case JoystickGroup j: j.X = x; j.Y = y; break;
        }
        e.Handled = true;
    }

    private void OnItemReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragItem is null) return;
        _dragItem = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    // ---------- Klavye ----------

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm || e.Key == Key.None) return;

        var typing = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox or NumericUpDown or ComboBox;
        if (typing && e.Key == Key.Escape)
        {
            Focus();
            e.Handled = true;
            return;
        }

        if (vm.HandleKeyDown(KeyNames.Name(e.Key), typing)) e.Handled = true;
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm || e.Key == Key.None) return;
        vm.HandleKeyUp(KeyNames.Name(e.Key));
    }

    // ---------- Panel butonları ----------

    private async void OnSaveProfileClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Profili Kaydet",
            DefaultExtension = "json",
            SuggestedFileName = "profil.json",
            FileTypeChoices = new[] { JsonType }
        });
        if (file?.TryGetLocalPath() is { } path) await vm.SaveProfileAsync(path);
    }

    private async void OnLoadProfileClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Profil Yükle",
            AllowMultiple = false,
            FileTypeFilter = new[] { JsonType }
        });
        if (files.Count == 1 && files[0].TryGetLocalPath() is { } path) await vm.LoadProfileAsync(path);
    }

    private void OnClearLogClick(object? sender, RoutedEventArgs e) => LogService.Clear();
    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || Vm?.State.IsLogPanel != true) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (LogList.ItemCount > 0) LogList.ScrollIntoView(LogList.ItemCount - 1);
        }, DispatcherPriority.Background);
    }
}
