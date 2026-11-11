using System.Text.Json.Serialization;

namespace DroidPad.Models;

public sealed class Coordinate
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }

    [JsonIgnore]
    public string DisplayText => $"{Name} -> x:{X}, y:{Y}";
}