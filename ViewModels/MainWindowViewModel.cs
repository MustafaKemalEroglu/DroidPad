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
    private readonly KeyMapperEngine _keyMapperEngine;

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
        _keyMapperEngine = new KeyMapperEngine();

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

        int tx = (int)(windowX * _touchScaleX);
        int ty = (int)(windowY * _touchScaleY);

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
    [RelayCommand]
    private void AddTapBinding()
    {
        _keyMappingManager.AddTapBinding(NewTapKey, CoordX, CoordY);
    }

    [RelayCommand]
    private void AddMuMuJoystick()
    {
        if (!int.TryParse(JoystickRadius, out var radius)) return;
        _keyMappingManager.AddJoystickGroup(JoystickPreset, radius, CoordX, CoordY);
    }
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

    public void ProcessKeyDown(string keyName)
    {
        // Tap tuşları için anlık tıklama
        var binding = KeyBindings.FirstOrDefault(b => b.KeyName.Equals(keyName, StringComparison.OrdinalIgnoreCase));
        if (binding != null && binding.Type == KeyBindingType.Tap)
        {
            _ = AdbService.TapAsync(binding.X, binding.Y);
            LogService.Debug($"[KEY] {keyName} -> Tıklandı ({binding.X}, {binding.Y})");
        }

        // Joystick grupları için yön tuşlarını işle
        foreach (var jg in JoystickGroups)
        {
            if (jg.UpKey.Equals(keyName, StringComparison.OrdinalIgnoreCase)) jg.IsUpPressed = true;
            if (jg.DownKey.Equals(keyName, StringComparison.OrdinalIgnoreCase)) jg.IsDownPressed = true;
            if (jg.LeftKey.Equals(keyName, StringComparison.OrdinalIgnoreCase)) jg.IsLeftPressed = true;
            if (jg.RightKey.Equals(keyName, StringComparison.OrdinalIgnoreCase)) jg.IsRightPressed = true;
        }
        _ = UpdateJoysticksAsync();
    }

    public void ProcessKeyUp(string keyName)
    {
        foreach (var jg in JoystickGroups)
        {
            if (jg.UpKey.Equals(keyName, StringComparison.OrdinalIgnoreCase)) jg.IsUpPressed = false;
            if (jg.DownKey.Equals(keyName, StringComparison.OrdinalIgnoreCase)) jg.IsDownPressed = false;
            if (jg.LeftKey.Equals(keyName, StringComparison.OrdinalIgnoreCase)) jg.IsLeftPressed = false;
            if (jg.RightKey.Equals(keyName, StringComparison.OrdinalIgnoreCase)) jg.IsRightPressed = false;
        }
        _ = UpdateJoysticksAsync();
    }

    private async Task UpdateJoysticksAsync()
    {
        foreach (var jg in JoystickGroups)
        {
            await _keyMapperEngine.UpdateJoystickGroupStateAsync(jg, ProfileResolution);
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