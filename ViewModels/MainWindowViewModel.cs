using System.Collections.ObjectModel;
using System.Text.Json;
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

    // --- Sorumluluk Yöneticileri (Managers) ---
    private readonly CoordinateManager _coordManager = new();
    private readonly KeyMappingManager _keyMappingManager = new();
    private readonly MacroManager _macroManager = new();
    private readonly ProfileManager _profileManager;
    private readonly AutomationManager _automationManager;

    private readonly KeyMapperEngine _keyMapperEngine;
    private readonly HashSet<string> _activePressedKeys = new();
    private KeyBinding? _waitingForKeyBinding;

    // --- UI Koleksiyon Köprüleri ---
    public ObservableCollection<Coordinate> Coordinates => _coordManager.Coordinates;
    public ObservableCollection<KeyBinding> KeyBindings => _keyMappingManager.KeyBindings;
    public ObservableCollection<JoystickGroup> JoystickGroups => _keyMappingManager.JoystickGroups;
    public ObservableCollection<MacroCommand> Commands => _macroManager.Commands;
    public ObservableCollection<string> LogLines { get; } = new();

    // --- UI State Özellikleri ---
    [ObservableProperty] private bool _isPanelOpen;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _coordName = "TargetClick";
    [ObservableProperty] private string _coordX = "1690";
    [ObservableProperty] private string _coordY = "877";
    [ObservableProperty] private string _cmdType = "click";
    [ObservableProperty] private string _cmdValue = "TargetClick";
    [ObservableProperty] private Coordinate? _selectedCoordinate;
    [ObservableProperty] private MacroCommand? _selectedCommand;
    [ObservableProperty] private KeyBinding? _selectedKeyBinding;
    [ObservableProperty] private bool _isPickingCoordinate;

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

    // --- Sağ Tık Popup Menü State Alanları ---
    [ObservableProperty] private bool _isContextMenuOpen;
    [ObservableProperty] private double _contextMenuX;
    [ObservableProperty] private double _contextMenuY;
    private double _lastRightClickX;
    private double _lastRightClickY;

    // --- Joystick ve Tap Ayarları ---
    [ObservableProperty] private List<string> _joystickPresets = new() { "WASD", "Yön Tuşları (Arrows)", "Numpad (8462)" };
    [ObservableProperty] private string _joystickPreset = "WASD";
    [ObservableProperty] private int _joystickRadius = 80;
    [ObservableProperty] private string _newTapKey = "F";

    public ResolutionDto ProfileResolution { get; } = new();
    public string[] KeyBindingTypes { get; } = { "tap", "joystick" };
    public string[] CmdTypes { get; } = { "click", "swipe", "wait", "random_wait", "key", "loop_start", "loop_end" };

    public event Action? MiniWindowRequested;

    public MainWindowViewModel()
    {
        var engine = new MacroEngine(Log);
        _keyMapperEngine = new KeyMapperEngine(Log);

        _profileManager = new ProfileManager(_coordManager, _keyMappingManager, _macroManager, JsonOptions);
        _automationManager = new AutomationManager(engine, Log);
        _automationManager.StateChanged += () =>
        {
            IsRunning = _automationManager.IsRunning;
            StartCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(MiniCountdownText));
            OnPropertyChanged(nameof(CanEditCountdown));
        };
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
        _lastRightClickX = x;
        _lastRightClickY = y;
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
            X = (int)_lastRightClickX,
            Y = (int)_lastRightClickY
        };
        KeyBindings.Add(newBinding);
        StartWaitingForKey(newBinding);
    }

    [RelayCommand]
    private void AddJoystickFromContextMenu()
    {
        IsContextMenuOpen = false;
        _keyMappingManager.AddJoystickGroup("WASD", JoystickRadius, _lastRightClickX.ToString(), _lastRightClickY.ToString(), Log);
    }

    public void StartWaitingForKey(KeyBinding binding)
    {
        _waitingForKeyBinding = binding;
        binding.KeyName = "...";
        Log($"⌨️ '{binding.Type}' için yeni tuşa basın...");
    }

    public bool HandleKeyAssignment(KeyEventArgs e)
    {
        if (_waitingForKeyBinding != null)
        {
            var keyStr = e.Key.ToString();
            if (keyStr.StartsWith("D") && keyStr.Length == 2 && char.IsDigit(keyStr[1]))
            {
                keyStr = keyStr[1].ToString();
            }

            _waitingForKeyBinding.KeyName = keyStr.ToUpper();
            Log($"✅ Yeni tuş atandı: {keyStr.ToUpper()}");
            _waitingForKeyBinding = null;
            return true;
        }
        return false;
    }

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
    private void Stop()
    {
        _automationManager.Stop();
    }
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
    public async Task OnKeyPressedAsync(string keyName)
    {
        if (_activePressedKeys.Contains(keyName)) return;
        _activePressedKeys.Add(keyName);

        var binding = KeyBindings.FirstOrDefault(b => b.KeyName.Equals(keyName, StringComparison.OrdinalIgnoreCase));
        if (binding != null) binding.IsPressed = true;

        if (binding?.Type == KeyBindingType.Joystick)
            await _keyMapperEngine.UpdateJoystickStateAsync(KeyBindings.ToList(), _activePressedKeys, ProfileResolution);
        else if (binding?.Type == KeyBindingType.Tap)
            await _keyMapperEngine.HandleKeyPressAsync(keyName, KeyBindings.ToList(), ProfileResolution);
    }

    public async Task OnKeyReleasedAsync(string keyName)
    {
        _activePressedKeys.Remove(keyName);
        var binding = KeyBindings.FirstOrDefault(b => b.KeyName.Equals(keyName, StringComparison.OrdinalIgnoreCase));
        if (binding != null) binding.IsPressed = false;

        if (binding?.Type == KeyBindingType.Joystick)
            await _keyMapperEngine.UpdateJoystickStateAsync(KeyBindings.ToList(), _activePressedKeys, ProfileResolution);
    }

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