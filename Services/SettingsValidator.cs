namespace WorkshopAssignment.Services;

/// <summary>
/// Validates settings values per SETTINGS_SPEC.md constraints.
/// </summary>
public static class SettingsValidator
{
    public const int MaxSlotNameLength = 50;
    public const int MinSolverTimeSeconds = 1;
    public const int MaxSolverTimeSeconds = 59 * 60 + 59; // 59 minutes 59 seconds = 3599 seconds

    /// <summary>
    /// Validates a single slot name.
    /// </summary>
    /// <param name="name">The slot name to validate</param>
    /// <returns>True if valid (non-empty, non-whitespace, max 50 chars)</returns>
    public static bool IsValidSlotName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.Length <= MaxSlotNameLength;
    }

    /// <summary>
    /// Validates all three slot names together, including uniqueness check.
    /// </summary>
    /// <param name="slot1">Slot 1 display name</param>
    /// <param name="slot2">Slot 2 display name</param>
    /// <param name="slot3">Slot 3 display name</param>
    /// <returns>True if all names are valid and unique</returns>
    public static bool AreSlotNamesValid(string? slot1, string? slot2, string? slot3)
    {
        // Check individual validity
        if (!IsValidSlotName(slot1) || !IsValidSlotName(slot2) || !IsValidSlotName(slot3))
            return false;

        // Check for duplicates (case-sensitive per spec)
        var names = new[] { slot1!, slot2!, slot3! };
        return names.Distinct().Count() == 3;
    }

    /// <summary>
    /// Validates solver time constraint (1 second to 59:59).
    /// </summary>
    /// <param name="minutes">Minutes component (0-59)</param>
    /// <param name="seconds">Seconds component (0-59)</param>
    /// <returns>True if total time is between 1 second and 59:59</returns>
    public static bool IsValidSolverTime(int minutes, int seconds)
    {
        // Validate component ranges
        if (minutes < 0 || minutes > 59)
            return false;

        if (seconds < 0 || seconds > 59)
            return false;

        // Calculate total seconds
        var totalSeconds = minutes * 60 + seconds;

        // Must be at least 1 second, at most 59:59 (3599 seconds)
        return totalSeconds >= MinSolverTimeSeconds && totalSeconds <= MaxSolverTimeSeconds;
    }
}
