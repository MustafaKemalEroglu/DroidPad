using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using DroidPad.Models;

namespace DroidPad.Services;

public static class ProfileService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "DroidPad");

    public const string DefaultName = "default";

    public static string ProfilesDir => Path.Combine(ConfigDir, "profiles");

    /// <summary>"default" → default.json; diğer adlar (genelde paket adı) profiles/ altında saklanır.</summary>
    public static string PathFor(string name) => name == DefaultName
        ? Path.Combine(ConfigDir, "default.json")
        : Path.Combine(ProfilesDir, SafeFileName(name) + ".json");

    public static string DefaultPath => PathFor(DefaultName);

    public static bool Exists(string name) => File.Exists(PathFor(name));

    public static IReadOnlyList<string> List()
    {
        var names = new List<string>();
        if (File.Exists(DefaultPath)) names.Add(DefaultName);
        if (Directory.Exists(ProfilesDir))
            names.AddRange(Directory.EnumerateFiles(ProfilesDir, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f)).Order());
        return names;
    }

    public static void Delete(string name)
    {
        try { File.Delete(PathFor(name)); } catch { }
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_'));

    /// <summary>Eski sürümün (WaydroidController) yapılandırmasını bir kez yeni klasöre taşır.</summary>
    public static void MigrateLegacy()
    {
        try
        {
            var old = Path.Combine(Path.GetDirectoryName(ConfigDir)!, "WaydroidController");
            if (Directory.Exists(old) && !Directory.Exists(ConfigDir)) Directory.Move(old, ConfigDir);
        }
        catch { /* taşıma başarısızsa sıfırdan başlanır */ }
    }

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
