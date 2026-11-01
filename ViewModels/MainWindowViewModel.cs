using System.Collections.ObjectModel;
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
    private readonly KeyMappingManager _keyMappingManager;

    // --- Koleksiyonlar ---
    public ObservableCollection<Coordinate> Coordinates => _workspace.Coordinates;
    public ObservableCollection<MacroCommand> Commands => _workspace.Commands;
    public ResolutionDto ProfileResolution { get; } = new();
    public string[] CmdTypes { get; } = { "click", "swipe", "wait", "random_wait", "key", "loop_start", "loop_end" };

    // ControlPanel ve OverlayWindow için gerekli koleksiyonlar
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

    // Dokunma Ölçekleme Değişkenleri
    private double _touchScaleX = 1.0;
    private double _touchScaleY = 1.0;

    public MainWindowViewModel()
    {
        _workspace = new WorkspaceManager();
        _keyMappingManager = new KeyMappingManager();
        _automation = new AutomationService();

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
        _ = AdbService.EnsureConnectedAsync();
    }

    // --- OverlayWindow için Kesintisiz Dokunma Köprüleri ---
    public async Task StartTouchAsync(double windowX, double windowY, double windowWidth, double windowHeight, double scaling)
    {
        if (windowWidth <= 0 || windowHeight <= 0) return;

        var resolution = await AdbService.GetResolutionAsync();
        _touchScaleX = (double)resolution.Width / windowWidth;
        _touchScaleY = (double)resolution.Height / windowHeight;

        // Scrcpy Soket bağlantısını başlat (Waydroid boyutlarını gönderiyoruz)
        await ScrcpyService.ConnectAsync(resolution.Width, resolution.Height);

        int tx = (int)(windowX * _touchScaleX);
        int ty = (int)(windowY * _touchScaleY);

        ScrcpyService.SendTouchDown(tx, ty);
    }

    public void MoveTouch(double windowX, double windowY, double scaling)
    {
        int tx = (int)(windowX * _touchScaleX);
        int ty = (int)(windowY * _touchScaleY);

        ScrcpyService.SendTouchMove(tx, ty);
    }

    public void EndTouch(double windowX, double windowY, double scaling)
    {
        int tx = (int)(windowX * _touchScaleX);
        int ty = (int)(windowY * _touchScaleY);

        ScrcpyService.SendTouchUp(tx, ty);
    }

    // --- OverlayWindow için Eski Metotlar ---

    public void StartWaitingForKey()
    {
        State.IsWaitingForKey = true;
        LogService.Info("Tuş ataması bekleniyor...");
    }

    public void HandleKeyAssignment(string keyName)
    {
        if (!State.IsWaitingForKey) return;
        State.IsWaitingForKey = false;
        LogService.Info($"Atanan tuş: {keyName}");
    }

    public async Task OnKeyPressedAsync(string keyName) => await Task.CompletedTask;
    public async Task OnKeyReleasedAsync(string keyName) => await Task.CompletedTask;
    public async Task ExecuteInstantSwipe(double x1, double y1, double x2, double y2, double windowWidth, double windowHeight) => await AdbService.SwipeAsync((int)x1, (int)y1, (int)x2, (int)y2, 200);
    public async Task ExecuteInstantTap(double x, double y, double windowWidth, double windowHeight) => await AdbService.TapAsync((int)x, (int)y);

    // --- Komutlar (UI Butonları) ---

    [RelayCommand] private void StartPickingCoordinate() => LogService.Info("Koordinat seçimi başlatıldı...");
    [RelayCommand] private void AddTapBinding() => LogService.Info($"Tap tuşu eklendi: {NewTapKey}");
    [RelayCommand] private void AddMuMuJoystick() => LogService.Info($"Joystick eklendi: {JoystickPreset}, Yarıçap: {JoystickRadius}");
    [RelayCommand] private void RemoveKeyBinding() { if (SelectedKeyBinding != null) KeyBindings.Remove(SelectedKeyBinding); }
    [RelayCommand] private void AddTapFromContextMenu() => LogService.Info("Sağ tık menüsünden Tap eklendi.");
    [RelayCommand] private void AddJoystickFromContextMenu() => LogService.Info("Sağ tık menüsünden Joystick eklendi.");

    [RelayCommand]
    private void AddCoordinate()
    {
        if (!int.TryParse(CoordX, out var x) || !int.TryParse(CoordY, out var y)) return;
        if (string.IsNullOrWhiteSpace(CoordName)) return;
        _workspace.AddOrUpdateCoordinate(CoordName, x, y);
        LogService.Info($"Koordinat eklendi: {CoordName} ({x}, {y})");
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
        LogService.Info(msg);
    }

    public async Task LoadProfileAsync(string path)
    {
        var (success, msg, profile) = await ProfileService.LoadAsync(path);
        LogService.Info(msg);
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
}