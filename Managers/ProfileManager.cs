using System.Threading.Tasks;
using WaydroidController.Models;
using WaydroidController.Services;

namespace WaydroidController.Managers;

public class ProfileManager
{
    private readonly CoordinateManager _coordinateManager;
    private readonly KeyMappingManager _keyMappingManager;
    private readonly MacroManager _macroManager;
    private readonly System.Text.Json.JsonSerializerOptions _jsonOptions;

    public ProfileManager(
        CoordinateManager coordinateManager,
        KeyMappingManager keyMappingManager,
        MacroManager macroManager,
        System.Text.Json.JsonSerializerOptions jsonOptions)
    {
        _coordinateManager = coordinateManager;
        _keyMappingManager = keyMappingManager;
        _macroManager = macroManager;
        _jsonOptions = jsonOptions;
    }

    public async Task SaveAsync(string path, ResolutionDto resolution)
    {
        await ProfileService.SaveToFileAsync(
            path,
            resolution,
            _coordinateManager.Coordinates,
            _macroManager.Commands,
            _keyMappingManager.KeyBindings,
            _keyMappingManager.JoystickGroups,
            _jsonOptions
        );
    }

    public async Task LoadAsync(string path)
    {
        var profile = await ProfileService.LoadFromFileAsync(path, _jsonOptions);
        if (profile == null) return;

        _coordinateManager.Coordinates.Clear();
        if (profile.Coordinates != null)
        {
            foreach (var kv in profile.Coordinates)
            {
                kv.Value.Name = kv.Key;
                _coordinateManager.Coordinates.Add(kv.Value);
            }
        }

        _macroManager.Commands.Clear();
        if (profile.Commands != null)
        {
            foreach (var c in profile.Commands) _macroManager.Commands.Add(c);
        }

        _keyMappingManager.KeyBindings.Clear();
        if (profile.KeyBindings != null)
        {
            foreach (var kb in profile.KeyBindings) _keyMappingManager.KeyBindings.Add(kb);
        }

        _keyMappingManager.JoystickGroups.Clear();
        if (profile.JoystickGroups != null)
        {
            foreach (var jg in profile.JoystickGroups) _keyMappingManager.JoystickGroups.Add(jg);
        }
    }
}