using CommunityToolkit.Mvvm.ComponentModel;

namespace WaydroidController.Models;

public enum KeyBindingType
{
    Tap,
    Joystick
}

public enum JoystickDirection
{
    None,
    Up,
    Down,
    Left,
    Right
}

public partial class KeyBinding : ObservableObject
{
    [ObservableProperty]
    private string _keyName = "F";

    [ObservableProperty]
    private KeyBindingType _type = KeyBindingType.Tap;

    [ObservableProperty]
    private int _x, _y, _radius;

    [ObservableProperty]
    private JoystickDirection _direction;

    [ObservableProperty]
    private bool _isPressed;
}

public partial class JoystickGroup : ObservableObject
{
    [ObservableProperty] private int _x; // Merkez X
    [ObservableProperty] private int _y; // Merkez Y
    [ObservableProperty] private int _radius = 70;

    [ObservableProperty] private string _upKey = "W";
    [ObservableProperty] private string _leftKey = "A";
    [ObservableProperty] private string _downKey = "S";
    [ObservableProperty] private string _rightKey = "D";

    [ObservableProperty] private bool _isUpPressed;
    [ObservableProperty] private bool _isLeftPressed;
    [ObservableProperty] private bool _isDownPressed;
    [ObservableProperty] private bool _isRightPressed;

    public string PresetName { get; set; } = "WASD";
}