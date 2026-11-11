using System.Text.Json.Serialization;

namespace WaydroidController.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ClickCommand), "click")]
[JsonDerivedType(typeof(SwipeCommand), "swipe")]
[JsonDerivedType(typeof(WaitCommand), "wait")]
[JsonDerivedType(typeof(RandomWaitCommand), "random_wait")]
[JsonDerivedType(typeof(KeyCommand), "key")]
[JsonDerivedType(typeof(LoopStartCommand), "loop_start")]
[JsonDerivedType(typeof(LoopEndCommand), "loop_end")]
public abstract class MacroCommand
{
    [JsonIgnore] public abstract string Description { get; }
}

public sealed class ClickCommand : MacroCommand
{
    public string Target { get; set; } = "";
    [JsonIgnore] public override string Description => $"CLICK → {Target}";
}

public sealed class SwipeCommand : MacroCommand
{
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
    [JsonIgnore] public override string Description => $"SWIPE → {Start} ➔ {End}";
}

public sealed class WaitCommand : MacroCommand
{
    public double Seconds { get; set; }
    [JsonIgnore] public override string Description => $"WAIT → {Seconds:0.##} sn";
}

public sealed class RandomWaitCommand : MacroCommand
{
    public double Min { get; set; }
    public double Max { get; set; }
    [JsonIgnore] public override string Description => $"RANDOM WAIT → {Min:0.##} - {Max:0.##} sn";
}

public sealed class KeyCommand : MacroCommand
{
    public string Keycode { get; set; } = "";
    [JsonIgnore] public override string Description => $"KEY → {Keycode}";
}

public sealed class LoopStartCommand : MacroCommand
{
    public int Count { get; set; }
    [JsonIgnore] public override string Description => $"LOOP START → {(Count == -1 ? "∞" : Count)}";
}

public sealed class LoopEndCommand : MacroCommand
{
    [JsonIgnore] public override string Description => "LOOP END";
}