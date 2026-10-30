using System.Collections.Generic;

namespace WaydroidController.Models;

public sealed class Profile
{
    public ResolutionDto Resolution { get; set; } = new();
    public Dictionary<string, Coordinate> Coordinates { get; set; } = new();
    public List<MacroCommand> Commands { get; set; } = new();
    public List<KeyBinding> KeyBindings { get; set; } = new();
    public List<JoystickGroup> JoystickGroups { get; set; } = new();
}