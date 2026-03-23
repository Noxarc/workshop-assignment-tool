namespace WorkshopAssignment.Models;

/// <summary>
/// Display model for "Persons by Info" grouped table.
/// Groups persons by their Info field and shows count.
/// </summary>
public record PersonGroupDisplay
{
    public string Info { get; init; }      // The grouping value (e.g., "Class A", "Department X")
    public string NumberDisplay { get; init; }  // Display string for count (number or "...")

    /// <summary>
    /// Creates a PersonGroupDisplay with a numeric count.
    /// </summary>
    public PersonGroupDisplay(string info, int number)
    {
        Info = info;
        NumberDisplay = number.ToString();
    }

    /// <summary>
    /// Creates a PersonGroupDisplay with a string display value (for overflow indicator).
    /// </summary>
    public PersonGroupDisplay(string info, string numberDisplay)
    {
        Info = info;
        NumberDisplay = numberDisplay;
    }

    /// <summary>
    /// For backward compatibility - tries to parse NumberDisplay as int.
    /// Returns 0 if not a valid number.
    /// </summary>
    public int Number => int.TryParse(NumberDisplay, out var n) ? n : 0;
}

/// <summary>
/// Display model for "Workshops by Type" grouped table.
/// Groups workshops by WorkshopType and shows count + total capacity.
/// </summary>
public record WorkshopTypeDisplay(
    string Type,     // The type (e.g., "Type 1", "Type 2")
    int Number,      // Count of workshops of this type
    int Capacity     // Total capacity across all workshops of this type
);

/// <summary>
/// Display model for slot-based workshop summary in DataWidget.
/// Shows Single (slot-dedicated) and Both (flexible Type4) workshop counts.
/// </summary>
public record SlotSummaryDisplay(
    string SlotName,   // Custom slot name from settings (e.g., "09:00 - 10:30")
    int SingleCount,   // Workshops dedicated to this slot only (Type2 for Slot2, Type3 for Slot3)
    int BothCount,     // Type4 flexible workshops (can be in either slot)
    int Capacity       // Total capacity for this slot (single + both capacities combined)
);
