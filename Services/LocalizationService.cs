using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace NODR.Services;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _languages = new(StringComparer.OrdinalIgnoreCase);
    private string _currentLanguage = "en";

    private LocalizationService()
    {
        LoadLanguage("uk");
        LoadLanguage("en");
        LoadLanguage("ru");
    }

    public static LocalizationService Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public string CurrentLanguage => _currentLanguage;

    public string this[string key] => Get(key);

    public void Initialize(string? languageCode)
    {
        var resolved = IsSupported(languageCode) ? languageCode!.ToLowerInvariant() : "en";
        _currentLanguage = resolved;
        ApplyCulture(_currentLanguage);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
    }

    public void SetLanguage(string languageCode, bool persist = true)
    {
        if (!IsSupported(languageCode))
            languageCode = "en";

        if (string.Equals(_currentLanguage, languageCode, StringComparison.OrdinalIgnoreCase))
            return;

        _currentLanguage = languageCode.ToLowerInvariant();
        ApplyCulture(_currentLanguage);
        if (persist)
            AppSettingsService.SetLanguage(_currentLanguage);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Get(string key)
    {
        if (_languages.TryGetValue(_currentLanguage, out var current) && current.TryGetValue(key, out var value))
            return value;
        if (_languages.TryGetValue("en", out var english) && english.TryGetValue(key, out value))
            return value;
        return key;
    }

    public string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    private bool IsSupported(string? code) => !string.IsNullOrWhiteSpace(code) && _languages.ContainsKey(code);

    private void LoadLanguage(string code)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"NODR.Localization.{code}.json";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
                throw new FileNotFoundException($"Missing localization resource: {resourceName}");
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>();
            _languages[code] = dictionary;
        }
        catch
        {
            _languages[code] = new Dictionary<string, string>();
        }
    }

    private static void ApplyCulture(string languageCode)
    {
        var cultureName = languageCode switch
        {
            "uk" => "uk-UA",
            "ru" => "ru-RU",
            _ => "en-US"
        };
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
