using System.Collections.Generic;

namespace DroidPad.Models;

public sealed class Profile
{
    /// <summary>Koordinatların kaydedildiği cihaz çözünürlüğü (yüklerken ölçeklemek için).</summary>
    public ResolutionDto Resolution { get; set; } = new();
    public Dictionary<string, Coordinate> Coordinates { get; set; } = new();
    public List<Macro> Macros { get; set; } = new();
    /// <summary>Çalıştırılmak üzere seçili makronun adı.</summary>
    public string? SelectedMacro { get; set; }
    /// <summary>Yalnızca eski (tek makrolu) profilleri okumak için; yazılmaz.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<MacroCommand>? Commands { get; set; }
    public List<KeyBinding> KeyBindings { get; set; } = new();
    public List<JoystickGroup> JoystickGroups { get; set; } = new();

    /// <summary>Oyun modunda sağ tık = Android "Geri".</summary>
    public bool RightClickBack { get; set; } = true;
    /// <summary>Oyun modunda fare tekerleği = kaydırma.</summary>
    public bool WheelScroll { get; set; } = true;
    /// <summary>Oyun modunda tuş katmanının saydamlığı (0.1–1).</summary>
    public double KeyOpacity { get; set; } = 0.5;
}
