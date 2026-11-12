using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DroidPad.Models;
using DroidPad.Services;

namespace DroidPad.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly AutomationService _automation = new();
    private readonly InputController _input = new();

    public ObservableCollection<Coordinate> Coordinates { get; } = new();
    public ObservableCollection<Macro> Macros { get; } = new();
    private static readonly ObservableCollection<MacroCommand> NoCommands = new();
    /// <summary>Seçili makronun komutları.</summary>
    public ObservableCollection<MacroCommand> Commands => SelectedMacro?.Commands ?? NoCommands;
    public ObservableCollection<KeyBinding> KeyBindings { get; } = new();
    public ObservableCollection<JoystickGroup> JoystickGroups { get; } = new();
    public ObservableCollection<LogEntry> Log => LogService.Entries;
    public string[] CmdTypes { get; } = { "click", "swipe", "wait", "random_wait", "key", "long_press", "text", "loop_start", "loop_end" };

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
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Commands), nameof(HasMacro))] private Macro? _selectedMacro;
    public bool HasMacro => SelectedMacro is not null;

    // Seçili tuş öğesi
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsTapSelected), nameof(HasSelection))] private KeyBinding? _selectedTap;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsJoystickSelected), nameof(HasSelection))] private JoystickGroup? _selectedJoystick;

    // Oyun modu ayarları (profille birlikte saklanır)
    [ObservableProperty] private bool _rightClickBack = true;
    [ObservableProperty] private bool _wheelScroll = true;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EffectiveOpacity))] private double _keyOpacity = 0.5;
    public double EffectiveOpacity => State.IsEditMode ? 1 : KeyOpacity;

    // Geri sayım (süre sınırı)
    [ObservableProperty] private bool _enableCountdown;
    [ObservableProperty] private bool _keepCountdownAfterMacro;
    [ObservableProperty] private decimal? _cdHours = 0;
    [ObservableProperty] private decimal? _cdMinutes = 30;
    [ObservableProperty] private decimal? _cdSeconds = 0;

    public bool IsRunning => _automation.IsRunning;
    public string RemainingText => _automation.RemainingText;
    public bool IsCountdownPaused => _automation.IsCountdownPaused;
    public bool IsTapSelected => SelectedTap is not null;
    public bool IsJoystickSelected => SelectedJoystick is not null;
    public bool HasSelection => IsTapSelected || IsJoystickSelected;
    public string ConnectionText => IsAdbConnected ? "Waydroid bağlı" : "Bağlı değil";

    // Uygulama başına profiller
    public ObservableCollection<string> ProfileNames { get; } = new();
    [ObservableProperty] private string? _selectedProfileName;
    [ObservableProperty] private bool _autoSwitchProfiles = true;
    [ObservableProperty] private string _activeProfileName = ProfileService.DefaultName;
    [ObservableProperty] private string? _foregroundPackage;
    private bool _switching;

    private string? _rebindSlot;
    private readonly Stopwatch _recClock = new();
    private double _recLast;
    private int _recCounter;

    public MainWindowViewModel()
    {
        _automation.PropertyChanged += OnAutomationChanged;
        EnsureMacro();
        State.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorState.ActivePanel)) OnPropertyChanged(nameof(EffectiveOpacity));
        };
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
        else if (e.PropertyName == nameof(AutomationService.IsCountdownPaused))
        {
            OnPropertyChanged(nameof(IsCountdownPaused));
        }
        else if (e.PropertyName == nameof(AutomationService.RemainingText))
        {
            OnPropertyChanged(nameof(RemainingText));
        }
    }

    private async Task InitializeAsync()
    {
        await ConnectAsync();
        if (ProfileService.Exists(ProfileService.DefaultName))
            await LoadProfileAsync(ProfileService.DefaultPath, silent: true);
        RefreshProfileNames();
        _ = WatchKeyboardAsync();
        await WatchForegroundAsync();
    }

    // ================= Uygulama başına profil =================

    private void RefreshProfileNames()
    {
        var keep = SelectedProfileName;
        ProfileNames.Clear();
        foreach (var n in ProfileService.List()) ProfileNames.Add(n);
        // Clear() seçimi sıfırlar; önceki (yoksa aktif) profili yeniden seç
        SelectedProfileName = keep is not null && ProfileNames.Contains(keep) ? keep
            : ProfileNames.Contains(ActiveProfileName) ? ActiveProfileName : null;
    }

    /// <summary>Waydroid'de ön plandaki uygulamayı izler; o uygulamaya ait profil varsa otomatik yükler.</summary>
    private async Task WatchForegroundAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync())
        {
            if (!IsAdbConnected || !WindowActive || _switching || State.IsEditMode || State.IsRecording || IsRunning) continue;

            var pkg = await Task.Run(AdbService.GetForegroundPackageAsync);
            if (pkg is null || pkg == ForegroundPackage) continue;
            // Sistem arayüzü / başlatıcı görünümleri profil değişimini tetiklemesin
            if (pkg.StartsWith("com.android.", StringComparison.Ordinal) || pkg.StartsWith("org.lineageos.", StringComparison.Ordinal)) continue;

            ForegroundPackage = pkg;
            if (!AutoSwitchProfiles) continue;

            if (ProfileService.Exists(pkg)) await ActivateProfileAsync(pkg);
            else if (ActiveProfileName != ProfileService.DefaultName) await ActivateProfileAsync(ProfileService.DefaultName);
        }
    }

    /// <summary>Mevcut profili kaydedip adı verilen profile geçer.</summary>
    private async Task ActivateProfileAsync(string name)
    {
        _switching = true;
        try
        {
            ReleaseAllKeys();
            await SaveProfileAsync(ProfileService.PathFor(ActiveProfileName), silent: true);
            if (!ProfileService.Exists(name)) return;
            await LoadProfileAsync(ProfileService.PathFor(name), silent: true);
            ActiveProfileName = name;
            LogService.Info($"Profil: {name}");
        }
        finally { _switching = false; }
    }

    [RelayCommand]
    private async Task SaveForCurrentAppAsync()
    {
        if (ForegroundPackage is not { } pkg)
        {
            LogService.Warning("Ön plandaki uygulama algılanamadı.");
            return;
        }
        await SaveProfileAsync(ProfileService.PathFor(ActiveProfileName), silent: true);
        await SaveProfileAsync(ProfileService.PathFor(pkg));
        ActiveProfileName = pkg;
        SelectedProfileName = pkg;
        RefreshProfileNames();
    }

    [RelayCommand]
    private async Task LoadSelectedProfileAsync()
    {
        if (SelectedProfileName is { } name) await ActivateProfileAsync(name);
    }

    [RelayCommand]
    private async Task DeleteSelectedProfileAsync()
    {
        if (SelectedProfileName is not { } name || name == ProfileService.DefaultName) return;
        var wasActive = ActiveProfileName == name;
        ProfileService.Delete(name);
        if (wasActive)
        {
            // Bellekte silinen profilin içeriği kalmasın; aksi halde default.json'un üzerine yazılır
            ActiveProfileName = ProfileService.DefaultName;
            if (ProfileService.Exists(ProfileService.DefaultName))
                await LoadProfileAsync(ProfileService.DefaultPath, silent: true);
        }
        RefreshProfileNames();
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
        foreach (var k in KeyBindings) { k.X *= sx; k.Y *= sy; k.EndX *= sx; k.EndY *= sy; }
        foreach (var j in JoystickGroups) { j.X *= sx; j.Y *= sy; j.Radius *= (sx + sy) / 2; }
        var selectedName = SelectedCoordinate?.Name;
        for (var i = 0; i < Coordinates.Count; i++)
        {
            var c = Coordinates[i];
            Coordinates[i] = new Coordinate { Name = c.Name, X = (int)Math.Round(c.X * sx), Y = (int)Math.Round(c.Y * sy) };
        }
        if (selectedName is not null) SelectedCoordinate = Coordinates.FirstOrDefault(c => c.Name == selectedName);
    }

    // ================= Paneller & kısayollar =================

    [RelayCommand]
    private void ShowPanel(PanelKind kind)
    {
        var next = State.ActivePanel == kind ? PanelKind.None : kind;
        if (State.ActivePanel == PanelKind.Keys && next != PanelKind.Keys) LeaveEditMode();
        State.IsContextMenuOpen = false;
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
        _ = SaveActiveAsync();
    }

    /// <summary>Klavye girdisi. true dönerse olay işlenmiş sayılır.</summary>
    public bool HandleKeyDown(string key, bool typing, string? symbol = null, bool ctrl = false, bool shift = false, string? physical = null)
    {
        switch (key)
        {
            case "F12": ShowPanel(PanelKind.Keys); return true;
            case "F11": ShowPanel(PanelKind.Macro); return true;
            case "F10": ShowPanel(PanelKind.Log); return true;
            case "F9": ToggleRun(); return true;
            case "F8": State.ShowKeys = !State.ShowKeys; return true;
            case "F7": ToggleTyping(); return true;
        }
        if (typing) return false;

        if (State.IsTyping) { HandleTypingKey(key, symbol, ctrl, shift, physical); return true; }

        if (State.IsWaitingForKey) { CompleteRebind(key); return true; }

        if (key == "Escape")
        {
            if (State.IsContextMenuOpen) State.IsContextMenuOpen = false;
            else if (State.IsPickingEnd) State.IsPickingEnd = false;
            else if (State.PendingTool is not null) State.PendingTool = null;
            else if (State.IsPicking) State.IsPicking = false;
            else if (State.IsRecording) StopRecording();
            else if (State.IsAnyPanel) ShowPanel(State.ActivePanel);
            else { _input.KeyDown(key, KeyBindings, JoystickGroups); return true; } // oyunda Esc bağlaması
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

    /// <summary>Yazma modu: oyun tuşları askıya alınır, basılan tuşlar cihazdaki metin kutusuna gider.</summary>
    private bool _manualTyping, _imeShown, _imeIgnored;

    /// <summary>Pencere aktif değilken yoklama yapılmaz (gereksiz adb/dumpsys yükü).</summary>
    public bool WindowActive { get; set; } = true;

    private void ToggleTyping() => SetManualTyping(!State.IsTyping);

    private void SetManualTyping(bool on)
    {
        _manualTyping = on;
        // IME yanlış algılansa bile kullanıcı çıkabilsin: klavye kapanana kadar yoksay
        if (!on && _imeShown) _imeIgnored = true;
        RefreshTyping();
    }

    private void RefreshTyping()
    {
        var on = _manualTyping || (_imeShown && !_imeIgnored);
        if (on == State.IsTyping) return;
        if (on) ReleaseAllKeys(); // takılı kalan WASD vb. bırakılsın
        State.IsTyping = on;
    }

    /// <summary>Android ekran klavyesi açıkken (metin kutusu odakta) oyun tuşlarını geçici olarak askıya alır.</summary>
    private async Task WatchKeyboardAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000));
        while (await timer.WaitForNextTickAsync())
        {
            if (!IsAdbConnected) { _imeShown = _imeIgnored = false; RefreshTyping(); continue; }
            if (!WindowActive) continue;
            var shown = await Task.Run(AdbService.IsKeyboardShownAsync); // süreç başlatma UI iş parçacığında olmasın
            if (shown is null) continue;
            _imeShown = shown.Value;
            if (!_imeShown) _imeIgnored = false;
            RefreshTyping();
        }
    }

    private void HandleTypingKey(string key, string? symbol, bool ctrl, bool shift, string? physical)
    {
        if (key is "LeftShift" or "RightShift" or "LeftCtrl" or "RightCtrl" or "LeftAlt" or "RightAlt" or "LWin" or "RWin") return;
        _symbolSent = false;
        switch (key)
        {
            case "Escape":
                if (_imeShown) AdbService.KeyEvent("KEYCODE_BACK"); // klavyeyi kapat
                SetManualTyping(false);
                return;
            case "Return" or "Enter":
                AdbService.KeyEvent("KEYCODE_ENTER", background: false);
                SetManualTyping(false);
                return;
        }

        // Gezinme/silme tuşları: Ctrl ve Shift ile birleşince tek kombinasyon olarak gider
        // (Ctrl+←/→ kelime atlama, Ctrl+Shift+←/→ kelime seçme, Shift+←/→ seçme, Ctrl+Backspace kelime silme...).
        var nav = key switch
        {
            "Back" => "KEYCODE_DEL",
            "Delete" => "KEYCODE_FORWARD_DEL",
            "Left" => "KEYCODE_DPAD_LEFT",
            "Right" => "KEYCODE_DPAD_RIGHT",
            "Up" => "KEYCODE_DPAD_UP",
            "Down" => "KEYCODE_DPAD_DOWN",
            "Home" => "KEYCODE_MOVE_HOME",
            "End" => "KEYCODE_MOVE_END",
            "Prior" or "PageUp" => "KEYCODE_PAGE_UP",
            "Next" or "PageDown" => "KEYCODE_PAGE_DOWN",
            _ => null
        };
        if (nav is not null)
        {
            if (!ctrl && !shift) { AdbService.KeyEvent(nav, background: false); return; }
            var keys = new List<string>();
            if (ctrl) keys.Add("KEYCODE_CTRL_LEFT");
            if (shift) keys.Add("KEYCODE_SHIFT_LEFT");
            keys.Add(nav);
            AdbService.KeyCombination(keys.ToArray());
            return;
        }

        // Ctrl+A/C/V/X/Z/Y (Ctrl+Shift+Z yinele): tümünü seç, kopyala, yapıştır...
        if (ctrl)
        {
            if (key.Length == 1 && key[0] is >= 'A' and <= 'Z' && "ACVXZY".Contains(key[0]))
                AdbService.KeyCombination(shift
                    ? new[] { "KEYCODE_CTRL_LEFT", "KEYCODE_SHIFT_LEFT", $"KEYCODE_{key}" }
                    : new[] { "KEYCODE_CTRL_LEFT", $"KEYCODE_{key}" });
            return;
        }
        if (!string.IsNullOrEmpty(symbol) && !char.IsControl(symbol[0])) { AdbService.Text(symbol); _symbolSent = true; return; }

        // Shift'li karakter KeySymbol ile gelmediyse önce TextInput beklenir (IME düzene uygun karakteri verir);
        // gelmezse fiziksel tuş + etkin düzenden çıkarılır.
        if (shift && physical is not null)
        {
            _awaitingText = true;
            DispatcherTimer.RunOnce(() =>
            {
                if (!_awaitingText) return;
                _awaitingText = false;
                if (KeyboardLayout.Shifted(physical) is { } ch) AdbService.Text(ch);
                else LogService.Warning($"Shift+{key} için karakter alınamadı.");
            }, TimeSpan.FromMilliseconds(60));
        }
    }

    private bool _symbolSent, _awaitingText;

    /// <summary>Platformdan gelen metin girdisi (IME/düzen uyumlu gerçek karakter). Yazma modunda cihaza gönderilir.</summary>
    public void OnTextInput(string? text)
    {
        if (!State.IsTyping || string.IsNullOrEmpty(text) || char.IsControl(text[0])) return;
        if (_symbolSent) { _symbolSent = false; return; } // KeySymbol ile zaten gönderildi
        _awaitingText = false;
        AdbService.Text(text);
    }

    public void HandleKeyUp(string key) => _input.KeyUp(key, KeyBindings, JoystickGroups);
    public void ReleaseAllKeys() => _input.ReleaseAll(KeyBindings, JoystickGroups);

    // ================= Fare (oyun modu) — koordinatlar cihaz uzayında =================

    private TimeSpan _downAt;

    public void TouchDown(double x, double y)
    {
        _downAt = _recClock.Elapsed;
        _input.MouseDown((int)x, (int)y);
    }

    /// <summary>Android "Geri" (oyun modunda sağ tık).</summary>
    public void NavBack() => AdbService.KeyEvent("KEYCODE_BACK");

    private long _lastScrollTicks;

    /// <summary>Fare tekerleği: imlecin olduğu yerde kısa bir kaydırma yapar.</summary>
    public void Scroll(double x, double y, double wheelDelta)
    {
        var now = Environment.TickCount64;
        if (wheelDelta == 0 || now - _lastScrollTicks < 90) return;
        _lastScrollTicks = now;
        var dy = (int)(DeviceHeight * 0.18) * Math.Sign(wheelDelta); // tekerlek yukarı → içerik yukarı kayar
        var y2 = (int)Math.Clamp(y - dy, 0, DeviceHeight - 1);
        AdbService.Swipe((int)x, (int)y, (int)x, y2, 160);
    }

    // ================= Araç çubuğu =================

    [RelayCommand] private void NavKey(string keycode) => AdbService.KeyEvent(keycode);

    [RelayCommand]
    private async Task ScreenshotAsync()
    {
        var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "DroidPad");
        var path = await AdbService.ScreenshotAsync(dir);
        if (path is null) LogService.Error("Ekran görüntüsü alınamadı.");
        else LogService.Success($"Ekran görüntüsü kaydedildi: {path}");
    }

    public async Task InstallApkAsync(string path)
    {
        LogService.Info($"APK kuruluyor: {System.IO.Path.GetFileName(path)} …");
        if (await AdbService.InstallApkAsync(path)) LogService.Success("APK kuruldu.");
    }
    public void TouchMove(double x, double y) => _input.MouseMove((int)x, (int)y);

    public void TouchUp(double x, double y, double startX, double startY)
    {
        _input.MouseUp((int)x, (int)y);
        if (State.IsRecording) RecordGesture((int)startX, (int)startY, (int)x, (int)y, (_recClock.Elapsed - _downAt).TotalSeconds);
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
            var tap = new KeyBinding { KeyName = "?", X = x, Y = y, EndX = x, EndY = y };
            KeyBindings.Add(tap);
            Select(tap);
            BeginRebindForSelected(); // MuMu gibi: yerleştirince hemen tuşa bas
        }
        else
        {
            var joy = JoystickGroup.FromPreset(tool, x, y, Math.Max(60, DeviceHeight * 0.1));
            JoystickGroups.Add(joy);
            Select(joy);
            BeginRebindForSelected();
        }
    }

    [RelayCommand]
    private void AddFromContextMenu(string tool) => PlaceTool(tool, State.ContextDeviceX, State.ContextDeviceY);

    [RelayCommand]
    private void SetKind(string kind)
    {
        if (SelectedTap is not { } t) return;
        t.Kind = kind;
        if (kind == BindingKind.Swipe && Math.Abs(t.EndX - t.X) + Math.Abs(t.EndY - t.Y) < 1)
        {
            t.EndX = t.X;
            t.EndY = Math.Clamp(t.Y - DeviceHeight * 0.2, 0, DeviceHeight);
        }
        State.IsContextMenuOpen = false;
        if (kind == BindingKind.Swipe) State.IsPickingEnd = true;
    }

    [RelayCommand]
    private void BeginEndPick()
    {
        if (SelectedTap is null) return;
        State.IsContextMenuOpen = false;
        State.IsPickingEnd = true;
    }

    public void CompleteEndPick(double x, double y)
    {
        State.IsPickingEnd = false;
        if (SelectedTap is not { } t) return;
        t.EndX = x;
        t.EndY = y;
    }

    [RelayCommand]
    private void AdjustTurbo(string delta)
    {
        if (SelectedTap is { } t && int.TryParse(delta, out var d))
            t.IntervalMs = Math.Clamp(t.IntervalMs + d, KeyBinding.MinTurboMs, 1000);
    }

    [RelayCommand]
    private void ResizeJoystick(string delta)
    {
        if (SelectedJoystick is { } j && double.TryParse(delta, out var d))
            j.Radius = Math.Clamp(j.Radius + d, 40, 260);
    }

    [RelayCommand]
    private void BeginRebind(string slot)
    {
        if (!HasSelection) return;
        State.IsContextMenuOpen = false;
        _rebindQueue.Clear();
        StartSlot(slot);
    }

    /// <summary>Seçili öğe için atama başlatır: tuşta tek tuş, joystickte sırayla ↑ ← ↓ →.</summary>
    public void BeginRebindForSelected()
    {
        if (SelectedTap is not null) BeginRebind("key");
        else if (SelectedJoystick is not null)
        {
            State.IsContextMenuOpen = false;
            _rebindQueue.Clear();
            foreach (var n in new[] { "left", "down", "right" }) _rebindQueue.Enqueue(n);
            StartSlot("up");
        }
    }

    private readonly System.Collections.Generic.Queue<string> _rebindQueue = new();

    private static string SlotName(string slot) => slot switch
    {
        "up" => "↑ Yukarı", "left" => "← Sol", "down" => "↓ Aşağı", "right" => "→ Sağ", _ => "tuş"
    };

    private void StartSlot(string slot)
    {
        _rebindSlot = slot;
        State.RebindHint = $"{SlotName(slot)} için bir tuşa basın  (Esc de atanabilir · iptal: ekrana tıklayın)";
    }

    private void CompleteRebind(string key)
    {
        var slot = _rebindSlot;
        var rest = _rebindQueue.ToArray();
        CancelRebind();
        if (slot is null) return;
        WarnIfKeyInUse(slot, key);
        ApplySlot(slot, key);
        if (rest.Length > 0)
        {
            foreach (var r in rest[1..]) _rebindQueue.Enqueue(r);
            StartSlot(rest[0]);
        }
    }

    /// <summary>Aynı tuş başka bir öğede de kullanılıyorsa uyarır (ikisi de tetiklenir).</summary>
    private void WarnIfKeyInUse(string slot, string key)
    {
        var self = SelectedTap;
        var inTaps = KeyBindings.Any(k => !ReferenceEquals(k, self) && KeyNames.Same(k.KeyName, key));
        var inSticks = JoystickGroups.Any(j => !ReferenceEquals(j, SelectedJoystick) &&
            (KeyNames.Same(j.UpKey, key) || KeyNames.Same(j.DownKey, key) || KeyNames.Same(j.LeftKey, key) || KeyNames.Same(j.RightKey, key)));
        var inSelf = SelectedJoystick is { } sj && slot != "up" && KeyNames.Same(sj.UpKey, key)
                     || SelectedJoystick is { } s2 && (slot != "left" && KeyNames.Same(s2.LeftKey, key) || slot != "down" && KeyNames.Same(s2.DownKey, key) || slot != "right" && KeyNames.Same(s2.RightKey, key));
        if (inTaps || inSticks || inSelf)
            LogService.Warning($"'{KeyNames.Display(key)}' tuşu birden fazla öğeye atanmış; hepsi aynı anda tetiklenir.");
    }

    private void ApplySlot(string slot, string key)
    {

        switch (slot)
        {
            case "key" when SelectedTap is not null: SelectedTap.KeyName = key; break;
            case "up" when SelectedJoystick is not null: SelectedJoystick.UpKey = key; break;
            case "left" when SelectedJoystick is not null: SelectedJoystick.LeftKey = key; break;
            case "down" when SelectedJoystick is not null: SelectedJoystick.DownKey = key; break;
            case "right" when SelectedJoystick is not null: SelectedJoystick.RightKey = key; break;
        }
    }

    /// <summary>Tuş beklerken ekrana tıklanırsa atamayı iptal eder (Esc artık atanabilir bir tuş).</summary>
    public bool CancelRebindIfWaiting()
    {
        if (!State.IsWaitingForKey) return false;
        CancelRebind();
        return true;
    }

    private void CancelRebind()
    {
        _rebindQueue.Clear();
        _rebindSlot = null;
        State.RebindHint = null;
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        State.IsContextMenuOpen = false;
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

    // ================= Makro listesi =================

    private void EnsureMacro()
    {
        if (Macros.Count == 0) Macros.Add(new Macro { Name = "Makro 1" });
        SelectedMacro ??= Macros[0];
    }

    private string UniqueMacroName(string baseName, Macro? except = null)
    {
        var name = baseName;
        for (var n = 2; Macros.Any(m => m != except && m.Name == name); n++) name = $"{baseName} {n}";
        return name;
    }

    partial void OnSelectedMacroChanged(Macro? value)
    {
        StopRecording();
        SelectedCommand = null;
        MacroNameEdit = value?.Name ?? "";
    }

    [ObservableProperty] private string _macroNameEdit = "";

    [RelayCommand]
    private void AddMacro()
    {
        var m = new Macro { Name = UniqueMacroName($"Makro {Macros.Count + 1}") };
        Macros.Add(m);
        SelectedMacro = m;
    }

    [RelayCommand]
    private void RenameMacro()
    {
        var name = MacroNameEdit.Trim();
        if (SelectedMacro is null || name.Length == 0) return;
        SelectedMacro.Name = UniqueMacroName(name, SelectedMacro);
        MacroNameEdit = SelectedMacro.Name;
    }

    [RelayCommand]
    private void DuplicateMacro()
    {
        if (SelectedMacro is null) return;
        var copy = new Macro { Name = UniqueMacroName(SelectedMacro.Name + " kopya") };
        foreach (var c in SelectedMacro.Commands) copy.Commands.Add(CloneCommand(c));
        Macros.Add(copy);
        SelectedMacro = copy;
    }

    private static MacroCommand CloneCommand(MacroCommand c) =>
        System.Text.Json.JsonSerializer.Deserialize<MacroCommand>(System.Text.Json.JsonSerializer.Serialize(c))!;

    [RelayCommand]
    private void RemoveMacro()
    {
        if (SelectedMacro is null || IsRunning) return;
        var idx = Macros.IndexOf(SelectedMacro);
        Macros.RemoveAt(idx);
        if (Macros.Count == 0) Macros.Add(new Macro { Name = "Makro 1" });
        SelectedMacro = Macros[Math.Min(idx, Macros.Count - 1)];
    }

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
            "long_press" => ParseLongPress(v),
            "text" when CmdValue.Length > 0 => new TextCommand { Text = CmdValue },
            "loop_start" => new LoopStartCommand { Count = int.TryParse(v, out var c) ? c : -1 },
            "loop_end" => new LoopEndCommand(),
            _ => null
        };

        if (cmd is null) { LogService.Warning($"'{CmdType}' için geçersiz değer: '{v}'"); return; }
        // Seçili komutun ardına ekle; seçim yoksa sona
        var at = SelectedCommand is null ? -1 : Commands.IndexOf(SelectedCommand);
        if (at >= 0) Commands.Insert(at + 1, cmd); else Commands.Add(cmd);
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

    private void RecordGesture(int x1, int y1, int x2, int y2, double held)
    {
        var started = _recClock.Elapsed.TotalSeconds - held;
        var gap = started - _recLast;
        if (Commands.Count > 0 && gap >= 0.1)
            Commands.Add(new WaitCommand { Seconds = Math.Round(gap, 1) });

        var dist = Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
        if (dist < 15 && held >= 0.6)
            Commands.Add(new LongPressCommand { Target = PointName(x1, y1), Seconds = Math.Round(held, 1) });
        else if (dist < 15)
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
        if (SelectedMacro is { } macro) LogService.Info($"Makro: {macro.Name}");
        await _automation.StartAsync(Commands.ToList(), coords, seconds, KeepCountdownAfterMacro);
    }

    [RelayCommand] private void ToggleMoreTools() => State.ShowMoreTools = !State.ShowMoreTools;
    [RelayCommand] private void Stop() => _automation.Stop();
    [RelayCommand] private void CancelCountdown() => _automation.CancelCountdown();

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
        Macros = Macros.ToList(),
        SelectedMacro = SelectedMacro?.Name,
        KeyBindings = KeyBindings.ToList(),
        JoystickGroups = JoystickGroups.ToList(),
        RightClickBack = RightClickBack,
        WheelScroll = WheelScroll,
        KeyOpacity = KeyOpacity
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
        Macros.Clear();
        SelectedMacro = null;
        foreach (var m in profile.Macros) Macros.Add(m);
        if (profile.Commands is { Count: > 0 } legacy) // eski tek makrolu profil
        {
            var m = new Macro { Name = UniqueMacroName("Makro 1") };
            foreach (var c in legacy) m.Commands.Add(c);
            Macros.Insert(0, m);
        }
        SelectedMacro = Macros.FirstOrDefault(m => m.Name == profile.SelectedMacro);
        EnsureMacro();
        KeyBindings.Clear();
        foreach (var k in profile.KeyBindings) KeyBindings.Add(k);
        JoystickGroups.Clear();
        foreach (var j in profile.JoystickGroups) JoystickGroups.Add(j);
        RightClickBack = profile.RightClickBack;
        WheelScroll = profile.WheelScroll;
        KeyOpacity = Math.Clamp(profile.KeyOpacity, 0.1, 1);

        // Profil başka bir çözünürlükte kaydedilmişse mevcut ekrana ölçekle
        var pw = profile.Resolution?.Width > 0 ? profile.Resolution.Width : DeviceWidth;
        var ph = profile.Resolution?.Height > 0 ? profile.Resolution.Height : DeviceHeight;
        Rescale(DeviceWidth / (double)pw, DeviceHeight / (double)ph);
    }

    private async Task SaveActiveAsync()
    {
        await SaveProfileAsync(ProfileService.PathFor(ActiveProfileName), silent: true);
        RefreshProfileNames();
    }

    public void Shutdown()
    {
        _automation.Stop();
        ReleaseAllKeys();
        ProfileService.Save(ProfileService.PathFor(ActiveProfileName), BuildProfile());
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
        "long_press" => (Coordinates.FirstOrDefault()?.Name ?? "Nokta1") + ",1.0",
        "text" => "merhaba",
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

    private static LongPressCommand? ParseLongPress(string value)
    {
        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var secs = 1.0;
        if (parts.Length > 1 && !TryDouble(parts[1], out secs)) return null;
        return new LongPressCommand { Target = parts[0], Seconds = secs };
    }

    private static RandomWaitCommand? ParseRandomWait(string value)
    {
        var parts = value.Split('-', StringSplitOptions.TrimEntries);
        return parts.Length == 2 && TryDouble(parts[0], out var min) && TryDouble(parts[1], out var max)
            ? new RandomWaitCommand { Min = min, Max = max } : null;
    }
}
