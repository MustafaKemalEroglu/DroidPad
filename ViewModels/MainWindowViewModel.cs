using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaydroidController.Managers;
using WaydroidController.Models;
using WaydroidController.Services;
using KeyBinding = WaydroidController.Models.KeyBinding;

namespace WaydroidController.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // --- Sorumluluk Yöneticileri (Managers & State) ---
    private readonly CoordinateManager _coordManager = new();
    private readonly KeyMappingManager _keyMappingManager = new();
    private readonly MacroManager _macroManager = new();
    private readonly ProfileManager _profileManager;
    private readonly AutomationManager _automationManager;
    private readonly KeyInputManager _keyInputManager;

    // UI Durum Nesnesi (EditorState)
    public EditorState State { get; } = new();

    // --- XAML Uyumluluğu İçin Forwarding (Köprü) Özellikleri ---
    public bool IsPanelOpen { get => State.IsPanelOpen; set => State.IsPanelOpen = value; }
    public bool IsRunning { get => State.IsRunning; set => State.IsRunning = value; }
    public bool IsPickingCoordinate { get => State.IsPickingCoordinate; set => State.IsPickingCoordinate = value; }
    public string CoordName { get => State.CoordName; set => State.CoordName = value; }
    public string CoordX { get => State.CoordX; set => State.CoordX = value; }
    public string CoordY { get => State.CoordY; set => State.CoordY = value; }
    public string CmdType { get => State.CmdType; set => State.CmdType = value; }
    public string CmdValue { get => State.CmdValue; set => State.CmdValue = value; }
    public Coordinate? SelectedCoordinate { get => State.SelectedCoordinate; set => State.SelectedCoordinate = value; }
    public MacroCommand? SelectedCommand { get => State.SelectedCommand; set => State.SelectedCommand = value; }
    public KeyBinding? SelectedKeyBinding { get => State.SelectedKeyBinding; set => State.SelectedKeyBinding = value; }
    public bool IsContextMenuOpen { get => State.IsContextMenuOpen; set => State.IsContextMenuOpen = value; }
    public double ContextMenuX { get => State.ContextMenuX; set => State.ContextMenuX = value; }
    public double ContextMenuY { get => State.ContextMenuY; set => State.ContextMenuY = value; }
    public List<string> JoystickPresets { get => State.JoystickPresets; set => State.JoystickPresets = value; }
    public string JoystickPreset { get => State.JoystickPreset; set => State.JoystickPreset = value; }
    public int JoystickRadius { get => State.JoystickRadius; set => State.JoystickRadius = value; }
    public string NewTapKey { get => State.NewTapKey; set => State.NewTapKey = value; }

    // --- UI Koleksiyon Köprüleri ---
    public ObservableCollection<Coordinate> Coordinates => _coordManager.Coordinates;
    public ObservableCollection<KeyBinding> KeyBindings => _keyMappingManager.KeyBindings;
    public ObservableCollection<JoystickGroup> JoystickGroups => _keyMappingManager.JoystickGroups;
    public ObservableCollection<MacroCommand> Commands => _macroManager.Commands;
    public ObservableCollection<string> LogLines { get; } = new();

    // --- Geri Sayım (Countdown) State Alanları ---
    public bool EnableCountdown
    {
        get => _automationManager.EnableCountdown;
        set { _automationManager.EnableCountdown = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanEditCountdown)); }
    }
    public bool StopCountdownOnStop
    {
        get => _automationManager.StopCountdownOnStop;
        set { _automationManager.StopCountdownOnStop = value; OnPropertyChanged(); }
    }
    public int CdHours
    {
        get => _automationManager.CdHours;
        set { _automationManager.CdHours = value; OnPropertyChanged(); }
    }
    public int CdMinutes
    {
        get => _automationManager.CdMinutes;
        set { _automationManager.CdMinutes = value; OnPropertyChanged(); }
    }
    public int CdSeconds
    {
        get => _automationManager.CdSeconds;
        set { _automationManager.CdSeconds = value; OnPropertyChanged(); }
    }
    public string MiniCountdownText => _automationManager.MiniCountdownText;
    public bool CanEditCountdown => _automationManager.CanEditCountdown;

    public ResolutionDto ProfileResolution { get; } = new();
    public string[] KeyBindingTypes { get; } = { "tap", "joystick" };
    public string[] CmdTypes { get; } = { "click", "swipe", "wait", "random_wait", "key", "loop_start", "loop_end" };

    public event Action? MiniWindowRequested;

    // Sınıf alanlarına ekleyin:
    private readonly KeyMapperEngine _keyMapperEngine;

    // Constructor içinde:
    public MainWindowViewModel()
    {
        var engine = new MacroEngine(Log);
        _keyMapperEngine = new KeyMapperEngine(Log);

        _profileManager = new ProfileManager(_coordManager, _keyMappingManager, _macroManager, JsonOptions);
        _automationManager = new AutomationManager(engine, Log);
        _keyInputManager = new KeyInputManager(_keyMapperEngine, Log);

        State.PropertyChanged += (s, e) => OnPropertyChanged(e.PropertyName);

        _automationManager.StateChanged += () =>
        {
            State.IsRunning = _automationManager.IsRunning;
            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(MiniCountdownText));
            OnPropertyChanged(nameof(CanEditCountdown));
        };
    }

    // Anlık ve akıcı kaydırma (Swipe) komutunu interaktif ADB kabuğu üzerinden iletir
    public async void ExecuteInstantSwipe(double startX, double startY, double endX, double endY, double windowWidth, double windowHeight, double scaling)
    {
        if (windowWidth <= 0 || windowHeight <= 0) return;

        // Waydroid gerçek çözünürlüğünü dinamik olarak al
        var resolution = await AdbService.GetWaydroidResolutionAsync(new ResolutionDto { Width = 1440, Height = 786 });

        double scaleX = (double)resolution.Width / windowWidth;
        double scaleY = (double)resolution.Height / windowHeight;

        int x1 = (int)(startX * scaleX * scaling);
        int y1 = (int)(startY * scaleY * scaling);
        int x2 = (int)(endX * scaleX * scaling);
        int y2 = (int)(endY * scaleY * scaling);

        x1 = Math.Clamp(x1, 0, resolution.Width);
        y1 = Math.Clamp(y1, 0, resolution.Height);
        x2 = Math.Clamp(x2, 0, resolution.Width);
        y2 = Math.Clamp(y2, 0, resolution.Height);

        var adbTarget = await AdbService.FindTargetAsync(default) ?? "127.0.0.1:5555";
        AdbService.SendInstantSwipe(adbTarget, x1, y1, x2, y2, 15);
    }

    // Anlık ve akıcı dokunma (Tap) komutunu interaktif ADB kabuğu üzerinden iletir
    public async void ExecuteInstantTap(double windowX, double windowY, double windowWidth, double windowHeight, double scaling)
    {
        if (windowWidth <= 0 || windowHeight <= 0) return;

        var resolution = await AdbService.GetWaydroidResolutionAsync(new ResolutionDto { Width = 1440, Height = 786 });

        double scaleX = (double)resolution.Width / windowWidth;
        double scaleY = (double)resolution.Height / windowHeight;

        int x = (int)(windowX * scaleX * scaling);
        int y = (int)(windowY * scaleY * scaling);

        x = Math.Clamp(x, 0, resolution.Width);
        y = Math.Clamp(y, 0, resolution.Height);

        var adbTarget = await AdbService.FindTargetAsync(default) ?? "127.0.0.1:5555";
        AdbService.SendInstantTap(adbTarget, x, y);
    }

    public async Task ExecuteTouchOrSwipeAsync(double startX, double startY, double endX, double endY, double actualWidth, double actualHeight, double scaling)
    {
        await _keyMapperEngine.ExecuteTouchOrSwipeAsync(startX, startY, endX, endY, actualWidth, actualHeight, ProfileResolution, scaling);
    }

    // --- Panel & Mod Kontrolleri ---
    [RelayCommand] private void TogglePanel() => IsPanelOpen = !IsPanelOpen;

    [RelayCommand]
    private void StartPickingCoordinate()
    {
        IsPickingCoordinate = true;
        Log("🎯 Koordinat seçme modu aktif: Şeffaf ekranda hedef noktaya tıklayın.");
    }

    public void SetPickedCoordinates(int x, int y)
    {
        CoordX = x.ToString();
        CoordY = y.ToString();
        IsPickingCoordinate = false;
        Log($"✅ Koordinat seçildi: X={x}, Y={y}");
    }

    // --- Koordinat, Tuş ve Makro İşlemleri ---
    [RelayCommand] private void AddCoordinate() => _coordManager.AddOrUpdate(CoordName, CoordX, CoordY, Log);
    [RelayCommand] private void RemoveCoordinate() => _coordManager.Remove(SelectedCoordinate, Log);

    [RelayCommand] private void AddTapBinding() => _keyMappingManager.AddTapBinding(NewTapKey, CoordX, CoordY, Log);
    [RelayCommand] private void AddTapAtPosition(Point coord) => _keyMappingManager.AddTapAtPosition(coord, IsPanelOpen, Log);
    [RelayCommand] private void AddMuMuJoystick() => _keyMappingManager.AddJoystickGroup(JoystickPreset, JoystickRadius, CoordX, CoordY, Log);
    [RelayCommand] private void RemoveKeyBinding() => _keyMappingManager.RemoveBinding(SelectedKeyBinding, Log);

    [RelayCommand] private void AddCommand() => _macroManager.AddCommand(CmdType, CmdValue);
    [RelayCommand] private void RemoveCommand() => _macroManager.Remove(SelectedCommand);
    [RelayCommand] private void MoveCommandUp() => _macroManager.MoveUp(SelectedCommand);
    [RelayCommand] private void MoveCommandDown() => _macroManager.MoveDown(SelectedCommand);

    // --- Sağ Tık Menü ve Dinleme ---
    public void OpenContextMenu(double x, double y)
    {
        ContextMenuX = x;
        ContextMenuY = y;
        IsContextMenuOpen = true;
    }

    [RelayCommand]
    private void AddTapFromContextMenu()
    {
        IsContextMenuOpen = false;
        var newBinding = new KeyBinding
        {
            KeyName = "...",
            Type = KeyBindingType.Tap,
            X = (int)ContextMenuX,
            Y = (int)ContextMenuY
        };
        KeyBindings.Add(newBinding);
        _keyInputManager.StartWaitingForKey(newBinding);
    }

    [RelayCommand]
    private void AddJoystickFromContextMenu()
    {
        IsContextMenuOpen = false;
        _keyMappingManager.AddJoystickGroup("WASD", JoystickRadius, ContextMenuX.ToString(), ContextMenuY.ToString(), Log);
    }

    public void StartWaitingForKey(KeyBinding binding) => _keyInputManager.StartWaitingForKey(binding);
    public bool HandleKeyAssignment(KeyEventArgs e) => _keyInputManager.HandleKeyAssignment(e);

    // --- Makro Çalıştırma & Durdurma ---
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var coordsDict = Coordinates.ToDictionary(c => c.Name, c => c);
        await _automationManager.StartAsync(Commands, coordsDict, ProfileResolution, () =>
        {
            IsRunning = true;
            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        });
    }
    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _automationManager.Stop();
    private bool CanStop() => IsRunning;

    [RelayCommand] private void OpenMiniWindow() => MiniWindowRequested?.Invoke();

    // --- Profil Kaydetme & Yükleme ---
    public async Task SaveProfileAsync(string path)
    {
        try
        {
            await _profileManager.SaveAsync(path, ProfileResolution);
            Log($"Profil kaydedildi: {path}");
        }
        catch (Exception ex) { Log($"Kaydetme hatası: {ex.Message}"); }
    }

    public async Task LoadProfileAsync(string path)
    {
        try
        {
            await _profileManager.LoadAsync(path);
            Log($"Profil yüklendi: {path}");
        }
        catch (Exception ex) { Log($"Yükleme hatası: {ex.Message}"); }
    }

    // --- Klavye Dinleyicileri ---
    public async Task OnKeyPressedAsync(string keyName) => await _keyInputManager.OnKeyPressedAsync(keyName, KeyBindings, JoystickGroups, ProfileResolution);
    public async Task OnKeyReleasedAsync(string keyName) => await _keyInputManager.OnKeyReleasedAsync(keyName, KeyBindings, JoystickGroups, ProfileResolution);

    // --- Loglama Yardımcısı ---
    private void Log(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            if (LogLines.Count > 1000) LogLines.RemoveAt(0);
        });
    }
}