using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WorkshopAssignment.Services;

/// <summary>
/// JSON file-based settings persistence service.
/// Extracted from MainViewModel during MVVM refactoring to decouple
/// file I/O from ViewModel state management.
/// </summary>
public class SettingsService : ISettingsService
{
    private static string SettingsFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkshopAssignment",
        "settings.json");

    /// <inheritdoc />
    public SettingsData Load()
    {
        var data = new SettingsData();
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (settings != null)
                {
                    data.Slot1Name = GetStringSetting(settings, "Slot1Name", data.Slot1Name);
                    data.Slot2Name = GetStringSetting(settings, "Slot2Name", data.Slot2Name);
                    data.Slot3Name = GetStringSetting(settings, "Slot3Name", data.Slot3Name);
                    data.TimeLimitMinutes = GetIntSetting(settings, "TimeLimitMinutes", data.TimeLimitMinutes);
                    data.TimeLimitSeconds = GetIntSetting(settings, "TimeLimitSeconds", data.TimeLimitSeconds);
                    data.ExportFormat = GetStringSetting(settings, "ExportFormat", data.ExportFormat);
                    data.IsDetailedExport = GetBoolSetting(settings, "IsDetailedExport", data.IsDetailedExport);
                }
            }
        }
        catch
        {
            // If settings can't be loaded, return defaults
        }

        return data;
    }

    /// <inheritdoc />
    public void Save(string key, object value)
    {
        try
        {
            // Ensure directory exists
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Load existing settings or create new
            Dictionary<string, object> settings;
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
            }
            else
            {
                settings = new Dictionary<string, object>();
            }

            // Update and save
            settings[key] = value;
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, options));
        }
        catch
        {
            // Silently ignore if settings can't be saved
        }
    }

    private static string GetStringSetting(Dictionary<string, JsonElement> settings, string key, string defaultValue)
    {
        return settings.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? defaultValue
            : defaultValue;
    }

    private static int GetIntSetting(Dictionary<string, JsonElement> settings, string key, int defaultValue)
    {
        return settings.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : defaultValue;
    }

    private static bool GetBoolSetting(Dictionary<string, JsonElement> settings, string key, bool defaultValue)
    {
        if (settings.TryGetValue(key, out var value))
        {
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
        }
        return defaultValue;
    }
}
