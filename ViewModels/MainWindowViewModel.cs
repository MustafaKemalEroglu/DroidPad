using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WaydroidController.Models;
using WaydroidController.Services;

namespace WaydroidController.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly AutomationService _automation = new();
    private readonly InputController _input = new();

    public ObservableCollection<Coordinate> Coordinates { get; } = new();
    public ObservableCollection<MacroCommand> Commands { get; } = new();
    public ObservableCollection<KeyBinding> KeyBindings { get; } = new();
    public ObservableCollection<JoystickGroup> JoystickGroups { get; } = new();
    public ObservableCollection<LogEntry> Log => LogService.Entries;
    public string[] CmdTypes { get; } = { "click", "swipe", "wait", "random_wait", "key", "loop_start", "loop_end" };

    public EditorState State { get; } = new();

    // Android ekran çözünürlüğü; tüm koordinatlar bu uzayda tutulur.
    [ObservableProperty] private int _deviceWidth = 1920;
    [ObservableProperty] private int _deviceHeight = 1080;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ConnectionText))] private bool _isAdbConnected;

    // Koordinat / komut formu
    [ObservableProperty] private string _coordName = "Nokta1";
    [ObservableProperty] private string _coordX = "960";
    [ObservableProperty] private string _coordY = "540";
    [ObservableProperty] private string _cmdType = "click";
    [ObservableProperty] private string _cmdValue = "Nokta1";
    [ObservableProperty] private Coordinate? _selectedCoordinate;
    [ObservableProperty] private MacroCommand? _selectedCommand;

    // Seçili tuş öğesi
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsTapSelected), nameof(HasSelection))] private KeyBinding? _selectedTap;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsJoystickSelected), nameof(HasSelection))] private JoystickGroup? _selectedJoystick;

    // Geri sayım (süre sınırı)
    [ObservableProperty] private bool _enableCountdown;
    [ObservableProperty] private decimal? _cdHours = 0;
    [ObservableProperty] private decimal? _cdMinutes = 30;
    [ObservableProperty] private decimal? _cdSeconds = 0;

    public bool IsRunning => _automation.IsRunning;
    public string RemainingText => _automation.RemainingText;
    public bool IsTapSelected => SelectedTap is not null;
    public bool IsJoystickSelected => SelectedJoystick is not null;
    public bool HasSelection => IsTapSelected || IsJoystickSelected;
    public string ConnectionText => IsAdbConnected ? "Waydroid bağlı" : "Bağlı değil";

    private string? _rebindSlot;
    private readonly Stopwatch _recClock = new();
    private double _recLast;
    private int _recCounter;

    public MainWindowViewModel()
    {
        _automation.PropertyChanged += OnAutomationChanged;
        AdbService.ConnectionChanged += connected =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => IsAdbConnected = connected);

        _ = InitializeAsync();
    }

    private void OnAutomationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AutomationService.IsRunning))
        {
            State.IsRunning = _automation.IsRunning;
            OnPropertyChanged(nameof(IsRunning));
        }
        else if (e.PropertyName == nameof(AutomationService.RemainingText))
        {
            OnPropertyChanged(nameof(RemainingText));
        }
    }

    private async Task InitializeAsync()
    {
        await ConnectAsync();
        if (File.Exists(ProfileService.DefaultPath))
            await LoadProfileAsync(ProfileService.DefaultPath, silent: true);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        IsAdbConnected = await AdbService.EnsureConnectedAsync();
        if (!IsAdbConnected) return;

        var res = await AdbService.GetResolutionAsync(force: true);
        if (res is not null) ApplyDeviceResolution(res.Width, res.Height);
    }

    /// <summary>Çözünürlük değiştiyse (ör. farklı Waydroid ekranı) tüm koordinatları orantılı ölçekler.</summary>
    private void ApplyDeviceResolution(int w, int h)
    {
        if (w <= 0 || h <= 0 || (w == DeviceWidth && h == DeviceHeight)) return;
        Rescale(w / (double)DeviceWidth, h / (double)DeviceHeight);
        DeviceWidth = w;
        DeviceHeight = h;
        LogService.Info($"Ekran çözünürlüğü: {w}x{h}");
    }

    private void Rescale(double sx, double sy)
    {
        if (Math.Abs(sx - 1) < 1e-6 && Math.Abs(sy - 1) < 1e-6) return;
        foreach (var k in KeyBindings) { k.X *= sx; k.Y *= sy; }
        foreach (var j in JoystickGroups) { j.X *= sx; j.Y *= sy; j.Radius *= (sx + sy) / 2; }
        for (var i = 0; i < Coordinates.Count; i++)
        {
            var c = Coordinates[i];
            Coordinates[i] = new Coordinate { Name = c.Name, X = (int)Math.Round(c.X * sx), Y = (int)Math.Round(c.Y * sy) };
        }
    }

    // ================= Paneller & kısayollar =================

    [RelayCommand]
    private void ShowPanel(PanelKind kind)
    {
        var next = State.ActivePanel == kind ? PanelKind.None : kind;
        if (State.ActivePanel == PanelKind.Keys && next != PanelKind.Keys) LeaveEditMode();
        State.ActivePanel = next;
        if (next != PanelKind.Macro) { State.IsPicking = false; StopRecording(); }
        ReleaseAllKeys();
    }

    private void LeaveEditMode()
    {
        CancelRebind();
        State.PendingTool = null;
        State.IsContextMenuOpen = false;
        Select(null);
        _ = SaveProfileAsync(ProfileService.DefaultPath, silent: true);
    }

    /// <summary>Klavye girdisi. true dönerse olay işlenmiş sayılır.</summary>
    public bool HandleKeyDown(string key, bool typing)
    {
        switch (key)
        {
            case "F12": ShowPanel(PanelKind.Keys); return true;
            case "F11": ShowPanel(PanelKind.Macro); return true;
            case "F10": ShowPanel(PanelKind.Log); return true;
            case "F9": ToggleRun(); return true;
        }
        if (typing) return false;

        if (State.IsWaitingForKey) { CompleteRebind(key); return true; }

        if (key == "Escape")
        {
            if (State.IsContextMenuOpen) State.IsContextMenuOpen = false;
            else if (State.PendingTool is not null) State.PendingTool = null;
            else if (State.IsPicking) State.IsPicking = false;
            else if (State.IsRecording) StopRecording();
            else if (State.IsAnyPanel) ShowPanel(State.ActivePanel);
            return true;
        }

        if (State.IsEditMode)
        {
            if (key == "Delete") DeleteSelected();
            return true; // düzenlerken oyun tuşları cihaza gitmez
        }

        _input.KeyDown(key, KeyBindings, JoystickGroups);
        return true;
    }

    public void HandleKeyUp(string key) => _input.KeyUp(key, KeyBindings, JoystickGroups);
    public void ReleaseAllKeys() => _input.ReleaseAll(KeyBindings, JoystickGroups);

    // ================= Fare (oyun modu) — koordinatlar cihaz uzayında =================

    public void TouchDown(double x, double y) => _input.MouseDown((int)x, (int)y);
    public void TouchMove(double x, double y) => _input.MouseMove((int)x, (int)y);

    public void TouchUp(double x, double y, double startX, double startY)
    {
        _input.MouseUp((int)x, (int)y);
        if (State.IsRecording) RecordGesture((int)startX, (int)startY, (int)x, (int)y);
    }

    public void CompletePick(double x, double y)
    {
        CoordX = ((int)x).ToString();
        CoordY = ((int)y).ToString();
        State.IsPicking = false;
        LogService.Info($"Koordinat seçildi: ({CoordX}, {CoordY})");
    }

    // ================= Tuş eşleme düzenleme =================

    public void Select(object? item)
    {
        if (SelectedTap is not null) SelectedTap.IsSelected = false;
        if (SelectedJoystick is not null) SelectedJoystick.IsSelected = false;
        SelectedTap = item as KeyBinding;
        SelectedJoystick = item as JoystickGroup;
        if (SelectedTap is not null) SelectedTap.IsSelected = true;
        if (SelectedJoystick is not null) SelectedJoystick.IsSelected = true;
        CancelRebind();
    }

    [RelayCommand]
    private void SelectTool(string tool)
    {
        State.PendingTool = State.PendingTool == tool ? null : tool;
        State.IsContextMenuOpen = false;
    }

    public void PlaceTool(string tool, double x, double y)
    {
        x = Math.Clamp(x, 0, DeviceWidth);
        y = Math.Clamp(y, 0, DeviceHeight);
        State.PendingTool = null;
        State.IsContextMenuOpen = false;

        if (tool == "tap")
        {
            var tap = new KeyBinding { KeyName = "?", X = x, Y = y };
            KeyBindings.Add(tap);
            Select(tap);
            BeginRebind("key"); // MuMu gibi: yerleştirince hemen tuşa bas
        }
        else
        {
            var joy = JoystickGroup.FromPreset(tool, x, y, Math.Max(60, DeviceHeight * 0.1));
            JoystickGroups.Add(joy);
            Select(joy);
        }
    }

    [RelayCommand]
    private void AddFromContextMenu(string tool) => PlaceTool(tool, State.ContextDeviceX, State.ContextDeviceY);

    [RelayCommand]
    private void BeginRebind(string slot)
    {
        if (!HasSelection) return;
        _rebindSlot = slot;
        State.RebindHint = "Atamak için bir tuşa basın  (Esc: iptal)";
    }

    private void CompleteRebind(string key)
    {
        var slot = _rebindSlot;
        CancelRebind();
        if (key == "Escape" || slot is null) return;

        switch (slot)
        {
            case "key" when SelectedTap is not null: SelectedTap.KeyName = key; break;
            case "up" when SelectedJoystick is not null: SelectedJoystick.UpKey = key; break;
            case "left" when SelectedJoystick is not null: SelectedJoystick.LeftKey = key; break;
            case "down" when SelectedJoystick is not null: SelectedJoystick.DownKey = key; break;
            case "right" when SelectedJoystick is not null: SelectedJoystick.RightKey = key; break;
        }
    }

    private void CancelRebind()
    {
        _rebindSlot = null;
        State.RebindHint = null;
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedTap is not null) KeyBindings.Remove(SelectedTap);
        else if (SelectedJoystick is not null) JoystickGroups.Remove(SelectedJoystick);
        Select(null);
    }

    [RelayCommand]
    private void ClearMappings()
    {
        Select(null);
        KeyBindings.Clear();
        JoystickGroups.Clear();
    }

    // ================= Koordinatlar =================

    [RelayCommand]
    private void StartPicking()
    {
        State.IsPicking = true;
        LogService.Info("Ekranda bir noktaya tıklayın (Esc: iptal)...");
    }

    [RelayCommand]
    private void AddCoordinate()
    {
        if (!int.TryParse(CoordX, out var x) || !int.TryParse(CoordY, out var y) || string.IsNullOrWhiteSpace(CoordName))
        {
            LogService.Warning("Geçersiz koordinat adı veya değeri.");
            return;
        }
        UpsertCoordinate(CoordName.Trim(), x, y);
    }

    private void UpsertCoordinate(string name, int x, int y)
    {
        var item = new Coordinate { Name = name, X = x, Y = y };
        var idx = Coordinates.ToList().FindIndex(c => c.Name == name);
        if (idx >= 0) Coordinates[idx] = item; else Coordinates.Add(item);
    }

    [RelayCommand] private void RemoveCoordinate() { if (SelectedCoordinate is not null) Coordinates.Remove(SelectedCoordinate); }

    // ================= Makro komutları =================

    [RelayCommand] private void RemoveCommand() { if (SelectedCommand is not null) Commands.Remove(SelectedCommand); }
    [RelayCommand] private void ClearCommands() => Commands.Clear();

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
        if (idx >= 0 && idx < Commands.Count - 1) Commands.Move(idx, idx + 1);
    }

    [RelayCommand]
    private void AddCommand()
    {
        var v = CmdValue.Trim();
        MacroCommand? cmd = CmdType switch
        {
            "click" when v.Length > 0 => new ClickCommand { Target = v },
            "swipe" => ParseSwipe(v),
            "wait" => TryDouble(v, out var s) ? new WaitCommand { Seconds = s } : null,
            "random_wait" => ParseRandomWait(v),
            "key" when v.Length > 0 => new KeyCommand { Keycode = v },
            "loop_start" => new LoopStartCommand { Count = int.TryParse(v, out var c) ? c : -1 },
            "loop_end" => new LoopEndCommand(),
            _ => null
        };

        if (cmd is null) { LogService.Warning($"'{CmdType}' için geçersiz değer: '{v}'"); return; }
        Commands.Add(cmd);
        SelectedCommand = cmd;
    }

    // ================= Kayıt (MuMu tarzı makro kaydı) =================

    [RelayCommand]
    private void ToggleRecording()
    {
        if (State.IsRecording) { StopRecording(); return; }
        if (IsRunning) return;
        State.IsRecording = true;
        _recCounter = 0;
        _recLast = 0;
        _recClock.Restart();
        LogService.Info("Kayıt başladı: ekrandaki tıklama/kaydırmalar makroya eklenir.");
    }

    private void StopRecording()
    {
        if (!State.IsRecording) return;
        State.IsRecording = false;
        _recClock.Stop();
        LogService.Info("Kayıt durdu.");
    }

    private void RecordGesture(int x1, int y1, int x2, int y2)
    {
        var started = _recClock.Elapsed.TotalSeconds;
        var gap = started - _recLast;
        if (Commands.Count > 0 && gap >= 0.1)
            Commands.Add(new WaitCommand { Seconds = Math.Round(gap, 1) });

        var dist = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
        if (dist < 15)
            Commands.Add(new ClickCommand { Target = PointName(x1, y1) });
        else
            Commands.Add(new SwipeCommand { Start = PointName(x1, y1), End = PointName(x2, y2) });

        _recLast = _recClock.Elapsed.TotalSeconds;
    }

    /// <summary>Aynı noktayı yeniden kullanır, yoksa "R1, R2…" adıyla yeni koordinat ekler.</summary>
    private string PointName(int x, int y)
    {
        var existing = Coordinates.FirstOrDefault(c => c.X == x && c.Y == y);
        if (existing is not null) return existing.Name;

        string name;
        do name = $"R{++_recCounter}"; while (Coordinates.Any(c => c.Name == name));
        Coordinates.Add(new Coordinate { Name = name, X = x, Y = y });
        return name;
    }

    // ================= Çalıştırma =================

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsRunning) return;
        StopRecording();
        State.IsPicking = false;

        var seconds = EnableCountdown
            ? (int)((CdHours ?? 0) * 3600 + (CdMinutes ?? 0) * 60 + (CdSeconds ?? 0))
            : 0;
        if (EnableCountdown && seconds <= 0)
        {
            LogService.Warning("Geri sayım süresi 0'dan büyük olmalı.");
            return;
        }

        var coords = Coordinates.ToDictionary(c => c.Name);
        await _automation.StartAsync(Commands.ToList(), coords, seconds);
    }

    [RelayCommand] private void Stop() => _automation.Stop();

    private void ToggleRun()
    {
        if (IsRunning) _automation.Stop();
        else _ = StartAsync();
    }

    // ================= Profil =================

    public Profile BuildProfile() => new()
    {
        Resolution = new ResolutionDto { Width = DeviceWidth, Height = DeviceHeight },
        Coordinates = Coordinates.ToDictionary(c => c.Name),
        Commands = Commands.ToList(),
        KeyBindings = KeyBindings.ToList(),
        JoystickGroups = JoystickGroups.ToList()
    };

    public async Task SaveProfileAsync(string path, bool silent = false)
    {
        var (ok, msg) = await ProfileService.SaveAsync(path, BuildProfile());
        if (!silent || !ok) LogService.Write(msg, ok);
    }

    public async Task LoadProfileAsync(string path, bool silent = false)
    {
        var (ok, msg, profile) = await ProfileService.LoadAsync(path);
        if (!silent || !ok) LogService.Write(msg, ok);
        if (!ok || profile is null) return;

        Select(null);
        Coordinates.Clear();
        foreach (var (name, c) in profile.Coordinates) Coordinates.Add(new Coordinate { Name = name, X = c.X, Y = c.Y });
        Commands.Clear();
        foreach (var c in profile.Commands) Commands.Add(c);
        KeyBindings.Clear();
        foreach (var k in profile.KeyBindings) KeyBindings.Add(k);
        JoystickGroups.Clear();
        foreach (var j in profile.JoystickGroups) JoystickGroups.Add(j);

        // Profil başka bir çözünürlükte kaydedilmişse mevcut ekrana ölçekle
        var pw = profile.Resolution?.Width > 0 ? profile.Resolution.Width : DeviceWidth;
        var ph = profile.Resolution?.Height > 0 ? profile.Resolution.Height : DeviceHeight;
        Rescale(DeviceWidth / (double)pw, DeviceHeight / (double)ph);
    }

    public void Shutdown()
    {
        _automation.Stop();
        ReleaseAllKeys();
        ProfileService.SaveAsync(ProfileService.DefaultPath, BuildProfile()).GetAwaiter().GetResult();
        AdbService.Disconnect();
    }

    // ================= Yardımcılar =================

    partial void OnCmdTypeChanged(string value) => CmdValue = value switch
    {
        "click" => Coordinates.FirstOrDefault()?.Name ?? "Nokta1",
        "swipe" => "Baslangic,Bitis",
        "wait" => "1.0",
        "random_wait" => "1.0-3.0",
        "key" => "KEYCODE_BACK",
        "loop_start" => "-1",
        _ => ""
    };

    private static bool TryDouble(string s, out double v) =>
        double.TryParse(s.Replace(',', '.'), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out v);

    private static SwipeCommand? ParseSwipe(string value)
    {
        var parts = value.Replace("->", ",").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 ? new SwipeCommand { Start = parts[0], End = parts[1] } : null;
    }

    private static RandomWaitCommand? ParseRandomWait(string value)
    {
        var parts = value.Split('-', StringSplitOptions.TrimEntries);
        return parts.Length == 2 && TryDouble(parts[0], out var min) && TryDouble(parts[1], out var max)
            ? new RandomWaitCommand { Min = min, Max = max } : null;
    }
}
