namespace WorkshopAssignment.Services;

/// <summary>
/// Abstraction for application settings persistence.
/// Decouples ViewModel from file I/O concerns, enabling testability
/// and future migration to other storage backends.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Loads all persisted settings and returns them as a typed dictionary.
    /// Returns null if no settings file exists or loading fails.
    /// </summary>
    SettingsData Load();

    /// <summary>
    /// Persists a single setting by key. Read-modify-write on the backing store.
    /// Silently ignores write failures to prioritize app stability.
    /// </summary>
    void Save(string key, object value);
}

/// <summary>
/// Strongly-typed settings data returned by ISettingsService.Load().
/// Replaces the raw Dictionary&lt;string, JsonElement&gt; that was previously
/// deserialized inline in MainViewModel.LoadSettings().
/// </summary>
public class SettingsData
{
    public string Slot1Name { get; set; } = "09:00 - 12:00";
    public string Slot2Name { get; set; } = "09:00 - 10:30";
    public string Slot3Name { get; set; } = "10:30 - 12:00";
    public int TimeLimitMinutes { get; set; } = 0;
    public int TimeLimitSeconds { get; set; } = 30;
    public string ExportFormat { get; set; } = "PDF";
    public bool IsDetailedExport { get; set; } = true;
}
