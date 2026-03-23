using System.Text.Json;

namespace WorkshopAssignment.Services;

/// <summary>
/// Localization service that loads strings from JSON files and supports runtime language switching.
/// </summary>
public class LocalizationService : ILocalizationService
{
    private const string DefaultLanguage = "en";
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _strings = new();
    private string _currentLanguage = DefaultLanguage;

    /// <inheritdoc />
    public string CurrentLanguage => _currentLanguage;

    /// <inheritdoc />
    public event Action? LanguageChanged;

    public LocalizationService()
    {
        LoadLanguage("de");
        LoadLanguage("en");
    }

    /// <inheritdoc />
    public void SetLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return;

        var normalizedCode = languageCode.ToLowerInvariant();

        // Only switch if the language is different and loaded
        if (normalizedCode == _currentLanguage)
            return;

        if (!_strings.ContainsKey(normalizedCode))
        {
            // Try to load the requested language
            LoadLanguage(normalizedCode);
        }

        // Fall back to English (default) if language still not available
        if (!_strings.ContainsKey(normalizedCode))
        {
            normalizedCode = DefaultLanguage;
        }

        if (normalizedCode != _currentLanguage)
        {
            _currentLanguage = normalizedCode;
            LanguageChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public string Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return key ?? string.Empty;

        var value = GetStringFromLanguage(key, _currentLanguage);
        if (value != null)
            return value;

        // Fall back to English (default) if not found in current language
        if (_currentLanguage != DefaultLanguage)
        {
            value = GetStringFromLanguage(key, DefaultLanguage);
            if (value != null)
                return value;
        }

        // Return the key itself if not found
        return key;
    }

    /// <inheritdoc />
    public string Get(string key, params object[] args)
    {
        var template = Get(key);

        if (args == null || args.Length == 0)
            return template;

        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            // If formatting fails (e.g., placeholder count mismatch), return template as-is
            return template;
        }
    }

    private string? GetStringFromLanguage(string key, string languageCode)
    {
        if (!_strings.TryGetValue(languageCode, out var categories))
            return null;

        // Parse the key: "category.key" format
        var parts = key.Split('.', 2);
        if (parts.Length != 2)
            return null;

        var category = parts[0];
        var stringKey = parts[1];

        if (!categories.TryGetValue(category, out var strings))
            return null;

        return strings.GetValueOrDefault(stringKey);
    }

    private void LoadLanguage(string languageCode)
    {
        try
        {
            var jsonContent = LoadJsonFromResources(languageCode);
            if (string.IsNullOrEmpty(jsonContent))
                return;

            var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                jsonContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed != null)
            {
                _strings[languageCode] = parsed;
            }
        }
        catch (JsonException)
        {
            // Log or handle JSON parsing errors silently
            // Language will simply not be available
        }
        catch (Exception)
        {
            // Handle file loading errors silently
        }
    }

    private static string? LoadJsonFromResources(string languageCode)
    {
        // Get the base directory of the application
        var baseDir = AppContext.BaseDirectory;

        // Try multiple possible locations for the JSON file
        var possiblePaths = new[]
        {
            Path.Combine(baseDir, "Resources", $"Strings.{languageCode}.json"),
            Path.Combine(baseDir, $"Strings.{languageCode}.json"),
            // For development: check relative to source
            Path.Combine(Directory.GetCurrentDirectory(), "Resources", $"Strings.{languageCode}.json"),
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }

        return null;
    }

}
