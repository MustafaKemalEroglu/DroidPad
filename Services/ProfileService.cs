using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using WaydroidController.Models;

namespace WaydroidController.Services;

public static class ProfileService
{
    private static void IgnoreDescriptionModifier(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object) return;
        foreach (var prop in typeInfo.Properties)
        {
            if (prop.Name == nameof(MacroCommand.Description))
                prop.ShouldSerialize = (_, _) => false;
        }
    }

    private static JsonSerializerOptions GetDefaultOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { IgnoreDescriptionModifier } }
        };
    }

    // ==========================================
    // 1. MainWindowViewModel için Metotlar
    // ==========================================

    public static async Task<(bool Success, string Message)> SaveAsync(string path, Profile profile)
    {
        try
        {
            var options = GetDefaultOptions();
            var json = JsonSerializer.Serialize(profile, options);
            await File.WriteAllTextAsync(path, json);
            return (true, "Profil başarıyla kaydedildi.");
        }
        catch (Exception ex)
        {
            return (false, $"Kaydetme hatası: {ex.Message}");
        }
    }

    public static async Task<(bool Success, string Message, Profile? Data)> LoadAsync(string path)
    {
        try
        {
            var options = GetDefaultOptions();
            var json = await File.ReadAllTextAsync(path);
            var profile = JsonSerializer.Deserialize<Profile>(json, options);
            return (true, "Profil başarıyla yüklendi.", profile);
        }
        catch (Exception ex)
        {
            return (false, $"Yükleme hatası: {ex.Message}", null);
        }
    }

    // ==========================================
    // 2. ProfileManager için Metotlar
    // ==========================================

    public static async Task SaveToFileAsync(
        string path,
        ResolutionDto resolution,
        IEnumerable coordinates,
        IEnumerable commands,
        IEnumerable keyBindings,
        IEnumerable joystickGroups,
        JsonSerializerOptions jsonOptions)
    {
        try
        {
            var coordDict = new Dictionary<string, Coordinate>();
            foreach (var c in coordinates)
            {
                if (c is Coordinate coord)
                {
                    coordDict[coord.Name] = coord;
                }
                else if (c is KeyValuePair<string, Coordinate> kvp)
                {
                    coordDict[kvp.Key] = kvp.Value;
                }
            }

            var profile = new Profile
            {
                Resolution = resolution,
                Coordinates = coordDict,
                Commands = commands.Cast<MacroCommand>().ToList(),
                // DÜZELTME: object yerine doğru türlere cast ediyoruz
                KeyBindings = keyBindings.Cast<KeyBinding>().ToList(),
                JoystickGroups = joystickGroups.Cast<JoystickGroup>().ToList()
            };

            if (jsonOptions.TypeInfoResolver == null)
            {
                jsonOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { IgnoreDescriptionModifier } };
            }

            var json = JsonSerializer.Serialize(profile, jsonOptions);
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            LogService.Error($"Profil Kaydetme Hatası: {ex.Message}");
        }
    }

    public static async Task<Profile?> LoadFromFileAsync(string path, JsonSerializerOptions jsonOptions)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path);

            if (jsonOptions.TypeInfoResolver == null)
            {
                jsonOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { IgnoreDescriptionModifier } };
            }

            return JsonSerializer.Deserialize<Profile>(json, jsonOptions);
        }
        catch (Exception ex)
        {
            LogService.Error($"Profil Yükleme Hatası: {ex.Message}");
            return null;
        }
    }
}