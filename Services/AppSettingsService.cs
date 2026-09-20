using System.IO;
using System.Text.Json;

namespace NODR.Services;

public sealed class AppSettings
{
    public string Language { get; set; } = "en";
    public string Theme { get; set; } = "System";
}

public static class AppSettingsService
{
    private static readonly object Gate = new();
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NODR");
    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
    private static AppSettings _current = new();

    public static AppSettings Current => _current;

    public static void Load()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    _current = new AppSettings();
                    return;
                }

                var json = File.ReadAllText(SettingsPath);
                _current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                _current = new AppSettings();
            }
        }
    }

    public static void SetLanguage(string code)
    {
        lock (Gate)
        {
            _current.Language = code;
            SaveLocked();
        }
    }

    public static void SetTheme(string mode)
    {
        lock (Gate)
        {
            _current.Theme = mode;
            SaveLocked();
        }
    }

    private static void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Preferences are non-critical. Never block NODR because settings could not be persisted.
        }
    }
}
