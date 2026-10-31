using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using WaydroidController.Models;

namespace WaydroidController.Models;

public partial class EditorState : ObservableObject
{
    // --- Panel ve Mod Durumları ---
    [ObservableProperty] private bool _isPanelOpen;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isPickingCoordinate;

    // --- Form Girdileri ---
    [ObservableProperty] private string _coordName = "TargetClick";
    [ObservableProperty] private string _coordX = "1690";
    [ObservableProperty] private string _coordY = "877";
    [ObservableProperty] private string _cmdType = "click";
    [ObservableProperty] private string _cmdValue = "TargetClick";

    // --- Seçim State'leri ---
    [ObservableProperty] private Coordinate? _selectedCoordinate;
    [ObservableProperty] private MacroCommand? _selectedCommand;
    [ObservableProperty] private KeyBinding? _selectedKeyBinding;

    // --- Sağ Tık Popup Menü Durumu ---
    [ObservableProperty] private bool _isContextMenuOpen;
    [ObservableProperty] private double _contextMenuX;
    [ObservableProperty] private double _contextMenuY;

    // --- Joystick ve Tuş Ayarları ---
    [ObservableProperty] private List<string> _joystickPresets = new() { "WASD", "Yön Tuşları (Arrows)", "Numpad (8462)" };
    [ObservableProperty] private string _joystickPreset = "WASD";
    [ObservableProperty] private int _joystickRadius = 80;
    [ObservableProperty] private string _newTapKey = "F";
}