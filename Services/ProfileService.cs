using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using WaydroidController.Models;

namespace WaydroidController.Services;

public static class ProfileService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "WaydroidController");

    public static string DefaultPath => Path.Combine(ConfigDir, "default.json");

    public static async Task<(bool Success, string Message)> SaveAsync(string path, Profile profile)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var tmp = path + ".tmp"; // yarım kalan yazım mevcut profili bozmasın
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(profile, Options));
            File.Move(tmp, path, overwrite: true);
            return (true, $"Profil kaydedildi: {Path.GetFileName(path)}");
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
            var json = await File.ReadAllTextAsync(path);
            var profile = JsonSerializer.Deserialize<Profile>(json, Options);
            return profile is null
                ? (false, "Profil boş.", null)
                : (true, $"Profil yüklendi: {Path.GetFileName(path)}", profile);
        }
        catch (Exception ex)
        {
            return (false, $"Yükleme hatası: {ex.Message}", null);
        }
    }
}
