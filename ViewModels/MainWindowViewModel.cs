using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaydroidController.Models;
using WaydroidController.Services;
using System.Text.Json.Serialization.Metadata;

namespace WaydroidController.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { IgnoreDescriptionModifier }
        }
    };

    private static void IgnoreDescriptionModifier(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object) return;

        foreach (var prop in typeInfo.Properties)
            if (prop.Name == nameof(MacroCommand.Description))
                prop.ShouldSerialize = (_, _) => false;
    }

    private readonly MacroEngine _engine;
    private readonly KeyMapperEngine _keyMapperEngine;
    private CancellationTokenSource? _runCts;
    private CountdownService? _countdown;
    public bool CanEditCountdown => EnableCountdown && !IsRunning;

    // --- UI State ---
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private string _coordName = "TargetClick";
    [ObservableProperty] private string _coordX = "1690";
    [ObservableProperty] private string _coordY = "877";
    [ObservableProperty] private string _cmdType = "click";
    [ObservableProperty] private string _cmdValue = "TargetClick";
    [ObservableProperty] private Coordinate? _selectedCoordinate;
    [ObservableProperty] private MacroCommand? _selectedCommand;
    [ObservableProperty] private bool _enableCountdown;
    [ObservableProperty] private bool _stopCountdownOnStop;
    [ObservableProperty] private int _cdHours = 1;
    [ObservableProperty] private int _cdMinutes;
    [ObservableProperty] private int _cdSeconds;
    [ObservableProperty] private string _miniCountdownText = "01:00:00";

    // --- UI State (Yeni eklenenler) ---
    [ObservableProperty] private string _mapButtonKey = "W";
    [ObservableProperty] private string _mapButtonType = "tap"; // "tap" veya "joystick"
    [ObservableProperty] private string _mapTargetCoord = "TargetClick"; // Hangi koordinata bağlanacağı
    [ObservableProperty] private KeyBinding? _selectedKeyBinding;

    public ObservableCollection<KeyBinding> KeyBindings { get; } = new();
    public string[] KeyBindingTypes { get; } = { "tap", "joystick" };
    public ObservableCollection<Coordinate> Coordinates { get; } = new();
    public ObservableCollection<MacroCommand> Commands { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();

    public ResolutionDto ProfileResolution { get; } = new();

    public string[] CmdTypes { get; } = { "click", "swipe", "wait", "random_wait", "key", "loop_start", "loop_end" };

    public event Action? MiniWindowRequested;

    public MainWindowViewModel()
    {
        _engine = new MacroEngine(Log);
        _keyMapperEngine = new KeyMapperEngine(Log);
    }

    partial void OnIsRunningChanged(bool value)
    {
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEditCountdown));

        // Makro durduğunda ve seçenek işaretliyse geri sayımı durdur
        if (!value && StopCountdownOnStop && _countdown?.IsRunning == true)
            _countdown.Stop();
    }

    partial void OnStopCountdownOnStopChanged(bool value)
    {
        // Seçenek işaretlendiyse ve makro o an çalışmıyorsa geri sayımı hemen durdur
        if (value && !IsRunning && _countdown?.IsRunning == true)
            _countdown.Stop();
    }

    partial void OnEnableCountdownChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditCountdown));
        if (!value && _countdown?.IsRunning == true)
            _countdown.Stop();
    }

    partial void OnCmdTypeChanged(string value)
    {
        CmdValue = value switch
        {
            "click" => "TargetClick",
            "swipe" => "StartName,EndName",
            "wait" => "1,0",
            "random_wait" => "1,0-3,0",
            "key" => "KEYCODE_HOME",
            "loop_start" => "-1",
            "loop_end" => "-",
            _ => ""
        };
    }

    // --- Commands ---
    [RelayCommand]
    private void AddCoordinate()
    {
        if (!int.TryParse(CoordX, out var x) || !int.TryParse(CoordY, out var y)) return;
        if (string.IsNullOrWhiteSpace(CoordName)) return;

        // Aynı isimde koordinat varsa güncelle, yoksa yeni ekle
        var existing = Coordinates.FirstOrDefault(c => c.Name == CoordName);
        if (existing != null)
        {
            existing.X = x;
            existing.Y = y;
            // ListBox'ın güncellenmesi için koleksiyonu tetikle
            var idx = Coordinates.IndexOf(existing);
            Coordinates[idx] = new Coordinate { Name = CoordName, X = x, Y = y };
        }
        else
        {
            Coordinates.Add(new Coordinate { Name = CoordName, X = x, Y = y });
        }

        Log($"Koordinat eklendi/güncellendi: {CoordName} ({x}, {y})");
    }

    [RelayCommand]
    private void RemoveCoordinate()
    {
        if (SelectedCoordinate is null) return;
        Coordinates.Remove(SelectedCoordinate);
        Log("Koordinat silindi.");
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
        if (cmd is null) return;
        Commands.Add(cmd);
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

    [RelayCommand]
    private void RemoveCommand()
    {
        if (SelectedCommand is null) return;
        Commands.Remove(SelectedCommand);
    }

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
            _countdown?.Stop(); // Eski sayacı durdur
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

    [RelayCommand]
    private void AddKeyBinding()
    {
        if (string.IsNullOrWhiteSpace(MapButtonKey)) return;

        var type = MapButtonType.ToLower() == "joystick" ? KeyBindingType.Joystick : KeyBindingType.Tap;

        // Eğer koordinat adından X ve Y değerini çözmek istiyorsak veya doğrudan koordinat dict'ten bulabiliriz:
        int x = 0, y = 0;
        if (Coordinates.FirstOrDefault(c => c.Name == MapTargetCoord) is { } targetCoord)
        {
            x = targetCoord.X;
            y = targetCoord.Y;
        }

        var existing = KeyBindings.FirstOrDefault(k => k.KeyName.Equals(MapButtonKey, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Type = type;
            existing.X = x;
            existing.Y = y;
            // ListBox tetiklenmesi için yenileme
            var idx = KeyBindings.IndexOf(existing);
            KeyBindings[idx] = new KeyBinding { KeyName = MapButtonKey, Type = type, X = x, Y = y };
        }
        else
        {
            KeyBindings.Add(new KeyBinding { KeyName = MapButtonKey, Type = type, X = x, Y = y });
        }

        Log($"Tuş ataması eklendi/güncellendi: [{MapButtonKey}] -> {MapTargetCoord} ({x}, {y})");
    }

    [RelayCommand]
    private void RemoveKeyBinding()
    {
        if (SelectedKeyBinding is null) return;
        KeyBindings.Remove(SelectedKeyBinding);
        Log("Tuş ataması silindi.");
    }

    // --- Profil ---
    public async Task SaveProfileAsync(string path)
    {
        try
        {
            var profile = new Profile
            {
                Resolution = ProfileResolution,
                Coordinates = Coordinates.ToDictionary(c => c.Name, c => c),
                Commands = Commands.ToList(),
                KeyBindings = KeyBindings.ToList() // Tuş atamaları kaydediliyor
            };
            var json = JsonSerializer.Serialize(profile, JsonOptions);
            await File.WriteAllTextAsync(path, json);
            Log($"Profil kaydedildi: {path}");
        }
        catch (Exception ex) { Log($"Kaydetme hatası: {ex.Message}"); }
    }

    public async Task LoadProfileAsync(string path)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var profile = JsonSerializer.Deserialize<Profile>(json, JsonOptions);
            if (profile is null) return;

            Coordinates.Clear();
            if (profile.Coordinates != null)
            {
                foreach (var kv in profile.Coordinates)
                {
                    kv.Value.Name = kv.Key;
                    Coordinates.Add(kv.Value);
                }
            }

            Commands.Clear();
            if (profile.Commands != null)
            {
                foreach (var c in profile.Commands) Commands.Add(c);
            }

            KeyBindings.Clear();
            if (profile.KeyBindings != null)
            {
                foreach (var kb in profile.KeyBindings) KeyBindings.Add(kb); // Tuş atamaları yükleniyor
            }

            Log($"Profil yüklendi: {path}");
        }
        catch (Exception ex) { Log($"Yükleme hatası: {ex.Message}"); }
    }

    // --- Overlay Penceresi Tetikleyicisi ---
    public event Action? OverlayWindowRequested;

    [RelayCommand]
    private void OpenOverlayWindow()
    {
        OverlayWindowRequested?.Invoke();
    }

    public async Task OnKeyPressedAsync(string keyName)
    {
        // İlgili tuş rozetini bul ve görsel olarak tetikle
        var binding = KeyBindings.FirstOrDefault(b => b.KeyName.Equals(keyName, StringComparison.OrdinalIgnoreCase));
        if (binding != null)
        {
            binding.IsPressed = true;
            _ = Task.Run(async () =>
            {
                await Task.Delay(120); // 120ms görsel yanıp sönme süresi
                binding.IsPressed = false;
            });
        }

        await _keyMapperEngine.HandleKeyPressAsync(keyName, KeyBindings, ProfileResolution);
    }

    // --- Koordinat Seçme Modu State'leri ---
    [ObservableProperty] private bool _isPickingCoordinate;

    [RelayCommand]
    private void StartPickingCoordinate()
    {
        IsPickingCoordinate = true;
        Log("🎯 Koordinat seçme modu aktif: Şeffaf ekranda hedef noktaya sol tıklayın.");
    }

    public void SetPickedCoordinates(int x, int y)
    {
        CoordX = x.ToString();
        CoordY = y.ToString();
        IsPickingCoordinate = false;
        Log($"✅ Koordinat başarıyla seçildi: X={x}, Y={y}");
    }

    // --- Yardımcılar ---
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

    private void Log(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            if (LogLines.Count > 1000) LogLines.RemoveAt(0);
        });
    }
}