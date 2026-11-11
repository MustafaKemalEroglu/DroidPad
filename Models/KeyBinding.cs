using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WaydroidController.Models;

public partial class KeyBinding : ObservableObject
{
    public const double Size = 46;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(KeyLabel))] private string _keyName = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Left))] private double _x;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Top))] private double _y;

    [ObservableProperty] [property: JsonIgnore] private bool _isPressed;
    [ObservableProperty] [property: JsonIgnore] private bool _isSelected;

    [JsonIgnore] public string KeyLabel => KeyNames.Display(KeyName);
    [JsonIgnore] public double Left => X - Size / 2;
    [JsonIgnore] public double Top => Y - Size / 2;
}

public partial class JoystickGroup : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Left))] private double _x;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Top))] private double _y;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Left), nameof(Top), nameof(Diameter), nameof(ThumbDx), nameof(ThumbDy))]
    private double _radius = 90;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(UpLabel))] private string _upKey = "W";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(LeftLabel))] private string _leftKey = "A";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DownLabel))] private string _downKey = "S";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RightLabel))] private string _rightKey = "D";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ThumbDx), nameof(ThumbDy))] [property: JsonIgnore] private bool _isUpPressed;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ThumbDx), nameof(ThumbDy))] [property: JsonIgnore] private bool _isLeftPressed;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ThumbDx), nameof(ThumbDy))] [property: JsonIgnore] private bool _isDownPressed;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ThumbDx), nameof(ThumbDy))] [property: JsonIgnore] private bool _isRightPressed;
    [ObservableProperty] [property: JsonIgnore] private bool _isSelected;

    [JsonIgnore] public double Left => X - Radius;
    [JsonIgnore] public double Top => Y - Radius;
    [JsonIgnore] public double Diameter => Radius * 2;
    [JsonIgnore] public double ThumbDx => GetOffset().dx;
    [JsonIgnore] public double ThumbDy => GetOffset().dy;
    [JsonIgnore] public string UpLabel => KeyNames.Display(UpKey);
    [JsonIgnore] public string LeftLabel => KeyNames.Display(LeftKey);
    [JsonIgnore] public string DownLabel => KeyNames.Display(DownKey);
    [JsonIgnore] public string RightLabel => KeyNames.Display(RightKey);

    /// <summary>Basılı yönlerin bileşkesi; çapraz basışta vektör yarıçapa normalize edilir.</summary>
    public (double dx, double dy) GetOffset()
    {
        double x = (IsRightPressed ? 1 : 0) - (IsLeftPressed ? 1 : 0);
        double y = (IsDownPressed ? 1 : 0) - (IsUpPressed ? 1 : 0);
        if (x == 0 && y == 0) return (0, 0);
        var len = Math.Sqrt(x * x + y * y);
        return (x / len * Radius, y / len * Radius);
    }

    public bool IsActive => IsUpPressed || IsDownPressed || IsLeftPressed || IsRightPressed;

    /// <summary>Tuş bu joystick'e aitse durumu günceller.</summary>
    public bool SetKey(string key, bool pressed)
    {
        var matched = false;
        if (KeyNames.Same(UpKey, key)) { IsUpPressed = pressed; matched = true; }
        if (KeyNames.Same(DownKey, key)) { IsDownPressed = pressed; matched = true; }
        if (KeyNames.Same(LeftKey, key)) { IsLeftPressed = pressed; matched = true; }
        if (KeyNames.Same(RightKey, key)) { IsRightPressed = pressed; matched = true; }
        return matched;
    }

    public void ReleaseAll() => IsUpPressed = IsDownPressed = IsLeftPressed = IsRightPressed = false;

    public static JoystickGroup FromPreset(string preset, double x, double y, double radius = 90)
    {
        var (u, l, d, r) = preset switch
        {
            "arrows" => ("Up", "Left", "Down", "Right"),
            "numpad" => ("NumPad8", "NumPad4", "NumPad2", "NumPad6"),
            _ => ("W", "A", "S", "D")
        };
        return new JoystickGroup { X = x, Y = y, Radius = radius, UpKey = u, LeftKey = l, DownKey = d, RightKey = r };
    }
}
