using CommunityToolkit.Mvvm.ComponentModel;

namespace WaydroidController.Models;

public partial class KeyBinding : ObservableObject
{
    public string KeyName { get; set; } = string.Empty;
    public KeyBindingType Type { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Radius { get; set; } = 80;

    [ObservableProperty]
    private bool _isPressed;
}

public enum KeyBindingType
{
    Tap,
    Joystick
}