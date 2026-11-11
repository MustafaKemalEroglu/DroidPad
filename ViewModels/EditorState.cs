using CommunityToolkit.Mvvm.ComponentModel;

namespace DroidPad.ViewModels;

public enum PanelKind { None, Keys, Macro, Log }

/// <summary>Yalnızca arayüz durumu (hangi panel açık, bağlam menüsü, kayıt/seçim modları).</summary>
public partial class EditorState : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditMode), nameof(IsKeysPanel), nameof(IsMacroPanel), nameof(IsLogPanel), nameof(IsAnyPanel), nameof(IsSidePanel), nameof(ItemsVisible))]
    private PanelKind _activePanel;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ItemsVisible))] private bool _showKeys = true;

    [ObservableProperty] private bool _isContextMenuOpen;
    [ObservableProperty] private double _contextMenuX;
    [ObservableProperty] private double _contextMenuY;
    [ObservableProperty] private double _contextDeviceX;
    [ObservableProperty] private double _contextDeviceY;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWaitingForKey))] private string? _rebindHint;
    [ObservableProperty] private string? _pendingTool;
    [ObservableProperty] private bool _isPickingEnd;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasBadge))] private bool _isPicking;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasBadge))] private bool _isRecording;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasBadge))] private bool _isRunning;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasBadge))] private bool _isTyping;

    public bool IsEditMode => ActivePanel == PanelKind.Keys;
    public bool IsKeysPanel => ActivePanel == PanelKind.Keys;
    public bool IsMacroPanel => ActivePanel == PanelKind.Macro;
    public bool IsLogPanel => ActivePanel == PanelKind.Log;
    public bool IsAnyPanel => ActivePanel != PanelKind.None;
    public bool IsSidePanel => ActivePanel is PanelKind.Macro or PanelKind.Log;
    public bool IsWaitingForKey => RebindHint is not null;
    public bool ItemsVisible => IsEditMode || ShowKeys;
    public bool HasBadge => IsPicking || IsRecording || IsRunning || IsTyping;
}
