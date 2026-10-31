using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaydroidController.Managers;
using WaydroidController.Models;
using WaydroidController.Services;

namespace WaydroidController.ViewModels;

public partial class EditorState : ObservableObject
{
    [ObservableProperty] private bool _isPanelOpen;
    [ObservableProperty] private bool _isWaitingForKey;
    [ObservableProperty] private bool _isContextMenuOpen;
    [ObservableProperty] private double _contextMenuX;
    [ObservableProperty] private double _contextMenuY;
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly WorkspaceManager _workspace;
    private readonly AutomationService _automation;
    private readonly KeyMappingManager _keyMappingManager; // HATA ÇÖZÜMÜ: Eklendi

    // --- Koleksiyonlar ---
    public ObservableCollection<Coordinate> Coordinates => _workspace.Coordinates;
    public ObservableCollection<MacroCommand> Commands => _workspace.Commands;
    public ObservableCollection<string> LogLines => LogManager.Logs;
    public ResolutionDto ProfileResolution { get; } = new();
    public string[] CmdTypes { get; } = { "click", "swipe", "wait", "random_wait", "key", "loop_start", "loop_end" };

    // ControlPanel.axaml için gerekli koleksiyonlar
    public ObservableCollection<KeyBinding> KeyBindings => _keyMappingManager.KeyBindings;
    public ObservableCollection<JoystickGroup> JoystickGroups => _keyMappingManager.JoystickGroups;

    public EditorState State { get; } = new();
    public IRelayCommand TogglePanelCommand { get; }

    // --- UI State Değişkenleri ---
    [ObservableProperty] private string _coordName = "TargetClick";
    [ObservableProperty] private string _coordX = "1690";
    [ObservableProperty] private string _coordY = "877";
    [ObservableProperty] private string _cmdType = "click";
    [ObservableProperty] private string _cmdValue = "TargetClick";
    [ObservableProperty] private Coordinate? _selectedCoordinate;
    [ObservableProperty] private MacroCommand? _selectedCommand;

    // ControlPanel.axaml için gerekli değişkenler
    [ObservableProperty] private string _newTapKey = "";
    [ObservableProperty] private string _joystickRadius = "100";
    [ObservableProperty] private string _joystickPreset = "Default";
    [ObservableProperty] private KeyBinding? _selectedKeyBinding;
    public string[] JoystickPresets { get; } = { "Default", "WASD", "Custom" };

    [ObservableProperty] private bool _enableCountdown;
    [ObservableProperty] private bool _stopCountdownOnStop;
    [ObservableProperty] private int _cdHours = 1;
    [ObservableProperty] private int _cdMinutes;
    [ObservableProperty] private int _cdSeconds;
    [ObservableProperty] private string _miniCountdownText = "01:00:00";

    public bool IsRunning => _automation.IsRunning;
    public bool CanEditCountdown => EnableCountdown && !IsRunning;
    public event Action? MiniWindowRequested;

    public MainWindowViewModel()
    {
        _workspace = new WorkspaceManager();
        _keyMappingManager = new KeyMappingManager();
        _automation = new AutomationService(new MacroEngine(Log));
        _ = AdbService.EnsureConnectedAsync();

        _automation.OnLog += Log;
        _automation.OnCountdownTick += OnCountdownTick;
        _automation.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AutomationService.IsRunning))
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(CanEditCountdown));
                StartCommand.NotifyCanExecuteChanged();
                StopCommand.NotifyCanExecuteChanged();
            }
        };

        TogglePanelCommand = new RelayCommand(() => State.IsPanelOpen = !State.IsPanelOpen);
        _ = AdbService.EnsureConnectedAsync(); // Uygulama açılırken ADB'ye bağlan
        LogManager.Log("Uygulama başlatıldı. ADB dinleniyor...");
    }

    // --- ControlPanel ve OverlayWindow için Eksik Komutlar ---

    [RelayCommand] private void StartPickingCoordinate() => Log("Koordinat seçimi başlatıldı...");
    [RelayCommand] private void AddTapBinding() => Log($"Tap tuşu eklendi: {NewTapKey}");
    [RelayCommand] private void AddMuMuJoystick() => Log($"Joystick eklendi: {JoystickPreset}, Yarıçap: {JoystickRadius}");
    [RelayCommand] private void RemoveKeyBinding() { if (SelectedKeyBinding != null) KeyBindings.Remove(SelectedKeyBinding); }
    [RelayCommand] private void AddTapFromContextMenu() => Log("Sağ tık menüsünden Tap eklendi.");
    [RelayCommand] private void AddJoystickFromContextMenu() => Log("Sağ tık menüsünden Joystick eklendi.");

    // --- OverlayWindow için Eksik Metotlar ---

    public void StartWaitingForKey()
    {
        State.IsWaitingForKey = true;
        Log("Tuş ataması bekleniyor...");
    }

    public void HandleKeyAssignment(string keyName)
    {
        if (!State.IsWaitingForKey) return;
        State.IsWaitingForKey = false;
        Log($"Atanan tuş: {keyName}");
    }

    // --- Kesintisiz Dokunma (Motionevent) İçin Değişkenler ---
    private double _touchScaleX = 1.0;
    private double _touchScaleY = 1.0;

    public async Task StartTouchAsync(double windowX, double windowY, double windowWidth, double windowHeight, double scaling)
    {
        if (windowWidth <= 0 || windowHeight <= 0) return;

        // Çözünürlüğü çek (Waydroid'in gerçek ekran boyutu)
        var resolution = await AdbService.GetResolutionAsync();

        // Ekran koordinatlarını Waydroid koordinatlarına ölçekle
        _touchScaleX = (double)resolution.Width / windowWidth;
        _touchScaleY = (double)resolution.Height / windowHeight;

        int tx = (int)(windowX * _touchScaleX);
        int ty = (int)(windowY * _touchScaleY);

        // Sınırların dışına çıkmayı engelle
        tx = Math.Clamp(tx, 0, resolution.Width);
        ty = Math.Clamp(ty, 0, resolution.Height);

        AdbService.SendTouchDown(tx, ty);
    }

    public void MoveTouch(double windowX, double windowY, double scaling)
    {
        int tx = (int)(windowX * _touchScaleX);
        int ty = (int)(windowY * _touchScaleY);

        AdbService.SendTouchMove(tx, ty);
    }

    public void EndTouch(double windowX, double windowY, double scaling)
    {
        int tx = (int)(windowX * _touchScaleX);
        int ty = (int)(windowY * _touchScaleY);

        AdbService.SendTouchUp(tx, ty);
    }

    public async Task OnKeyPressedAsync(string keyName) => await Task.CompletedTask;
    public async Task OnKeyReleasedAsync(string keyName) => await Task.CompletedTask;
    public async Task ExecuteInstantTap(double x, double y, double windowWidth, double windowHeight)
    {
        if (windowWidth <= 0 || windowHeight <= 0) return;

        // Waydroid'in hedef çözünürlüğünü al (Profil çözünürlüğü veya varsayılan 1920x1080)
        double targetW = ProfileResolution.Width > 0 ? ProfileResolution.Width : 1920;
        double targetH = ProfileResolution.Height > 0 ? ProfileResolution.Height : 1080;

        // Ekrandaki (Overlay) koordinatları Waydroid ekranına ölçekle
        int tx = (int)(x * (targetW / windowWidth));
        int ty = (int)(y * (targetH / windowHeight));

        Log($"[Overlay] Tıklama: {tx}, {ty}");
        await AdbService.TapAsync(tx, ty);
    }

    public async Task ExecuteInstantSwipe(double x1, double y1, double x2, double y2, double windowWidth, double windowHeight)
    {
        if (windowWidth <= 0 || windowHeight <= 0) return;

        double targetW = ProfileResolution.Width > 0 ? ProfileResolution.Width : 1920;
        double targetH = ProfileResolution.Height > 0 ? ProfileResolution.Height : 1080;

        int tx1 = (int)(x1 * (targetW / windowWidth));
        int ty1 = (int)(y1 * (targetH / windowHeight));
        int tx2 = (int)(x2 * (targetW / windowWidth));
        int ty2 = (int)(y2 * (targetH / windowHeight));

        Log($"[Overlay] Kaydırma: {tx1},{ty1} -> {tx2},{ty2}");
        await AdbService.SwipeAsync(tx1, ty1, tx2, ty2, 200);
    }

    // --- Makro ve UI Komutları ---

    [RelayCommand]
    private void AddCoordinate()
    {
        if (!int.TryParse(CoordX, out var x) || !int.TryParse(CoordY, out var y)) return;
        if (string.IsNullOrWhiteSpace(CoordName)) return;
        _workspace.AddOrUpdateCoordinate(CoordName, x, y);
        Log($"Koordinat eklendi: {CoordName} ({x}, {y})");
    }

    [RelayCommand] private void RemoveCoordinate() { if (SelectedCoordinate is not null) Coordinates.Remove(SelectedCoordinate); }
    [RelayCommand] private void RemoveCommand() { if (SelectedCommand is not null) Commands.Remove(SelectedCommand); }

    [RelayCommand]
    private void MoveCommandUp()
    {
        if (SelectedCommand is null) return;
        var idx = Commands.IndexOf(SelectedCommand);
        if (idx > 0) Commands.Move(idx, idx - 1);
    }

    [RelayCommand]
    private void MoveCommandDown()
    {
        if (SelectedCommand is null) return;
        var idx = Commands.IndexOf(SelectedCommand);
        if (idx < Commands.Count - 1) Commands.Move(idx, idx + 1);
    }

    [RelayCommand]
    private void AddCommand()
    {
        MacroCommand? cmd = CmdType switch
        {
            "click" => new ClickCommand { Target = CmdValue.Trim() },
            "swipe" => ParseSwipe(CmdValue),
            "wait" => double.TryParse(CmdValue, out var s) ? new WaitCommand { Seconds = s } : null,
            "random_wait" => ParseRandomWait(CmdValue),
            "key" => new KeyCommand { Keycode = CmdValue.Trim() },
            "loop_start" => int.TryParse(CmdValue, out var c) ? new LoopStartCommand { Count = c } : new LoopStartCommand { Count = -1 },
            "loop_end" => new LoopEndCommand(),
            _ => null
        };
        if (cmd is not null) Commands.Add(cmd);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var totalSeconds = CdHours * 3600 + CdMinutes * 60 + CdSeconds;
        await _automation.StartAsync(_workspace, ProfileResolution, EnableCountdown, totalSeconds, StopCountdownOnStop);
    }

    private bool CanStart() => !IsRunning;
    [RelayCommand(CanExecute = nameof(CanStop))] private void Stop() => _automation.Stop();
    private bool CanStop() => IsRunning;
    [RelayCommand] private void OpenMiniWindow() => MiniWindowRequested?.Invoke();

    public async Task SaveProfileAsync(string path)
    {
        var profile = new Profile
        {
            Resolution = ProfileResolution,
            Coordinates = Coordinates.ToDictionary(c => c.Name, c => c),
            Commands = Commands.ToList()
        };
        var (success, msg) = await ProfileService.SaveAsync(path, profile);
        Log(msg);
    }

    public async Task LoadProfileAsync(string path)
    {
        var (success, msg, profile) = await ProfileService.LoadAsync(path);
        Log(msg);
        if (success && profile != null)
        {
            _workspace.LoadData(profile.Coordinates, profile.Commands);
            if (profile.Resolution != null)
            {
                ProfileResolution.Width = profile.Resolution.Width;
                ProfileResolution.Height = profile.Resolution.Height;
            }
        }
    }

    // --- Yardımcı Metotlar ---

    partial void OnEnableCountdownChanged(bool value) => OnPropertyChanged(nameof(CanEditCountdown));
    partial void OnCmdTypeChanged(string value)
    {
        CmdValue = value switch
        {
            "click" => "TargetClick",
            "swipe" => "StartName,EndName",
            "wait" => "1.0",
            "random_wait" => "1.0-3.0",
            "key" => "KEYCODE_HOME",
            "loop_start" => "-1",
            "loop_end" => "-",
            _ => ""
        };
    }

    private void OnCountdownTick(int remaining)
    {
        var h = remaining / 3600; var m = remaining % 3600 / 60; var s = remaining % 60;
        Dispatcher.UIThread.Post(() => MiniCountdownText = $"{h:00}:{m:00}:{s:00}");
    }

    private static SwipeCommand? ParseSwipe(string value)
    {
        var parts = value.Replace("->", ",").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? new SwipeCommand { Start = parts[0], End = parts[1] } : null;
    }

    private static RandomWaitCommand? ParseRandomWait(string value)
    {
        var parts = value.Split('-');
        return parts.Length == 2 && double.TryParse(parts[0], out var min) && double.TryParse(parts[1], out var max)
            ? new RandomWaitCommand { Min = min, Max = max } : null;
    }

    private void Log(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            LogLines.Add($"[{timestamp}] {message}");
            if (LogLines.Count > 1000) LogLines.RemoveAt(0);
        });
    }
}