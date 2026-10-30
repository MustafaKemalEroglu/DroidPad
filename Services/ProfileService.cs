using System.IO;
using System.Text.Json;
using WaydroidController.Models;

namespace WaydroidController.Services;

public static class ProfileService
{
    public static async Task SaveToFileAsync(
        string path,
        ResolutionDto resolution,
        IEnumerable<Coordinate> coords,
        IEnumerable<MacroCommand> cmds,
        IEnumerable<KeyBinding> bindings,
        IEnumerable<JoystickGroup> joystickGroups,
        JsonSerializerOptions options)
    {
        var profile = new Profile
        {
            Resolution = resolution,
            Coordinates = coords.ToDictionary(c => c.Name, c => c),
            Commands = cmds.ToList(),
            KeyBindings = bindings.ToList(),
            JoystickGroups = joystickGroups.ToList()
        };

        var json = JsonSerializer.Serialize(profile, options);
        await File.WriteAllTextAsync(path, json);
    }

    public static async Task<Profile?> LoadFromFileAsync(string path, JsonSerializerOptions options)
    {
        if (!File.Exists(path)) return null;

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<Profile>(json, options);
    }
}