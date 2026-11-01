using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WaydroidController.Models;

public enum KeyBindingType { Tap, Joystick }

public enum JoystickDirection { None, Up, Down, Left, Right }

public partial class KeyBinding : ObservableObject
{
    [ObservableProperty] private string _keyName = "";
    [ObservableProperty] private KeyBindingType _type = KeyBindingType.Tap;
    [ObservableProperty] private int _x, _y;
    [ObservableProperty] private bool _isPressed;

    [JsonIgnore] public string DisplayText => $"{KeyName} (Tap)";
}

public partial class JoystickGroup : ObservableObject
{
    [ObservableProperty] private int _x, _y, _radius = 70;
    [ObservableProperty] private string _upKey = "W", _leftKey = "A", _downKey = "S", _rightKey = "D";
    [ObservableProperty] private bool _isUpPressed, _isLeftPressed, _isDownPressed, _isRightPressed;

    public string PresetName { get; set; } = "WASD";
}