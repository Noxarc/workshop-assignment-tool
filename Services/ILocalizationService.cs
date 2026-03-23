namespace WorkshopAssignment.Services;

/// <summary>
/// Service for managing localized strings with runtime language switching.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets the current language code (e.g., "de" or "en").
    /// </summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// Changes the current language and notifies all subscribers.
    /// </summary>
    /// <param name="languageCode">The language code to switch to (e.g., "de" or "en").</param>
    void SetLanguage(string languageCode);

    /// <summary>
    /// Gets a localized string by key.
    /// </summary>
    /// <param name="key">The key in dot notation (e.g., "import.pageTitle").</param>
    /// <returns>The localized string, or the key itself if not found.</returns>
    string Get(string key);

    /// <summary>
    /// Gets a localized string by key with placeholder formatting.
    /// </summary>
    /// <param name="key">The key in dot notation (e.g., "import.workshopsLoaded").</param>
    /// <param name="args">Arguments to format into placeholders ({0}, {1}, etc.).</param>
    /// <returns>The formatted localized string, or the key itself if not found.</returns>
    string Get(string key, params object[] args);

    /// <summary>
    /// Event fired when the language changes.
    /// </summary>
    event Action? LanguageChanged;
}
