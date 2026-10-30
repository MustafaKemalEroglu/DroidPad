using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaydroidController.Managers;
using WaydroidController.Models;
using WaydroidController.Services;

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

    private readonly MacroEngine _engine;
    private readonly KeyMapperEngine _keyMapperEngine;
    private readonly HashSet<string> _activePressedKeys = new();

    private CancellationTokenSource? _runCts;
    private CountdownService? _countdown;
    public bool CanEditCountdown => EnableCountdown && !IsRunning;

    // --- UI Koleksiyon Köprüleri ---
    public ObservableCollection<Coordinate> Coordinates => _coordManager.Coordinates;
    public ObservableCollection<KeyBinding> KeyBindings => _keyMappingManager.KeyBindings;
    public ObservableCollection<JoystickGroup> JoystickGroups => _keyMappingManager.JoystickGroups; // EKLENDİ
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
    [ObservableProperty] private bool _enableCountdown;
    [ObservableProperty] private bool _stopCountdownOnStop;
    [ObservableProperty] private int _cdHours = 1;
    [ObservableProperty] private int _cdMinutes;
    [ObservableProperty] private int _cdSeconds;
    [ObservableProperty] private string _miniCountdownText = "01:00:00";

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
        _engine = new MacroEngine(Log);
        _keyMapperEngine = new KeyMapperEngine(Log);

        _profileManager = new ProfileManager(_coordManager, _keyMappingManager, _macroManager, JsonOptions);
    }

    partial void OnIsRunningChanged(bool value)
    {
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditCountdown));

        if (!value && StopCountdownOnStop && _countdown?.IsRunning == true)
            _countdown.Stop();
    }

    partial void OnStopCountdownOnStopChanged(bool value)
    {
        if (value && !IsRunning && _countdown?.IsRunning == true)
            _countdown.Stop();
    }

    partial void OnEnableCountdownChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditCountdown));
        if (!value && _countdown?.IsRunning == true)
            _countdown.Stop();
    }

    // --- Panel & Mod Kontrolleri ---
    [RelayCommand]
    private void TogglePanel() => IsPanelOpen = !IsPanelOpen;

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

    // --- Koordinat İşlemleri ---
    [RelayCommand] private void AddCoordinate() => _coordManager.AddOrUpdate(CoordName, CoordX, CoordY, Log);
    [RelayCommand] private void RemoveCoordinate() => _coordManager.Remove(SelectedCoordinate, Log);

    // --- Tuş Atama & Joystick İşlemleri ---
    [RelayCommand] private void AddTapBinding() => _keyMappingManager.AddTapBinding(NewTapKey, CoordX, CoordY, Log);
    [RelayCommand] private void AddTapAtPosition(Point coord) => _keyMappingManager.AddTapAtPosition(coord, IsPanelOpen, Log);
    [RelayCommand] private void AddMuMuJoystick() => _keyMappingManager.AddJoystickGroup(JoystickPreset, JoystickRadius, CoordX, CoordY, Log);
    [RelayCommand] private void RemoveKeyBinding() => _keyMappingManager.RemoveBinding(SelectedKeyBinding, Log);

    // --- Komut & Makro İşlemleri ---
    [RelayCommand] private void AddCommand() => _macroManager.AddCommand(CmdType, CmdValue);
    [RelayCommand] private void RemoveCommand() => _macroManager.Remove(SelectedCommand);
    [RelayCommand] private void MoveCommandUp() => _macroManager.MoveUp(SelectedCommand);
    [RelayCommand] private void MoveCommandDown() => _macroManager.MoveDown(SelectedCommand);

    // --- Makro Çalıştırma ve Durdurma Komutları ---
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (Commands.Count == 0) { Log("Hata: Çalıştırılacak komut yok!"); return; }

        if (EnableCountdown)
        {
            var total = CdHours * 3600 + CdMinutes * 60 + CdSeconds;
            if (total <= 0)
            {
                Log("Hata: Geri sayım süresi 0'dan büyük olmalıdır!");
                return;
            }
            _countdown?.Stop();
            _countdown = new CountdownService(OnCountdownTick, () => Dispatcher.UIThread.Post(Stop));
            _countdown.Start(total);
        }

        IsRunning = true;
        _runCts = new CancellationTokenSource();
        try
        {
            var coordsDict = Coordinates.ToDictionary(c => c.Name, c => c);
            await _engine.RunAsync(Commands, coordsDict, ProfileResolution, _runCts.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        if (!IsRunning) return;
        _runCts?.Cancel();
        Log("Otomasyon durduruldu.");
    }

    private bool CanStop() => IsRunning;

    [RelayCommand]
    private void OpenMiniWindow() => MiniWindowRequested?.Invoke();

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
            await _keyMapperEngine.UpdateJoystickStateAsync(KeyBindings, _activePressedKeys, ProfileResolution);
        else if (binding?.Type == KeyBindingType.Tap)
            await _keyMapperEngine.HandleKeyPressAsync(keyName, KeyBindings, ProfileResolution);
    }

    public async Task OnKeyReleasedAsync(string keyName)
    {
        _activePressedKeys.Remove(keyName);
        var binding = KeyBindings.FirstOrDefault(b => b.KeyName.Equals(keyName, StringComparison.OrdinalIgnoreCase));
        if (binding != null) binding.IsPressed = false;

        if (binding?.Type == KeyBindingType.Joystick)
            await _keyMapperEngine.UpdateJoystickStateAsync(KeyBindings, _activePressedKeys, ProfileResolution);
    }

    private void OnCountdownTick(int remaining)
    {
        var h = remaining / 3600;
        var m = remaining % 3600 / 60;
        var s = remaining % 60;
        Dispatcher.UIThread.Post(() =>
        {
            MiniCountdownText = $"{h:00}:{m:00}:{s:00}";
            CdHours = h; CdMinutes = m; CdSeconds = s;
        });
    }

    // --- Sağ Tık Popup Menü State Alanları ---
    [ObservableProperty] private bool _isContextMenuOpen;
    [ObservableProperty] private double _contextMenuX;
    [ObservableProperty] private double _contextMenuY;
    private double _lastRightClickX;
    private double _lastRightClickY;

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
        // Joystick için merkez tuşa (örn: W veya Up) dinleme başlatılabilir veya varsayılan eklenebilir
        _keyMappingManager.AddJoystickGroup("WASD", JoystickRadius, _lastRightClickX.ToString(), _lastRightClickY.ToString(), Log);
    }

    // --- Tuş Atama / Dinleme State Alanları ---
    private KeyBinding? _waitingForKeyBinding;

    public void StartWaitingForKey(KeyBinding binding)
    {
        _waitingForKeyBinding = binding;
        binding.KeyName = "...";
        Log($"⌨️ '{binding.Type}' için yeni tuşa basın (Klavyeden bir tuşa dokunun)...");
    }

    public bool HandleKeyAssignment(Avalonia.Input.KeyEventArgs e)
    {
        if (_waitingForKeyBinding != null)
        {
            var keyStr = e.Key.ToString();

            // Sayısal tuşları temizleme (örn: D1 -> 1)
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