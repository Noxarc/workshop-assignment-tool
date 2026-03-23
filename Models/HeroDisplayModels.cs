using System.Collections.Generic;

namespace WorkshopAssignment.Models;

/// <summary>
/// Represents a selectable workshop type option for the Type ComboBox.
/// </summary>
public class WorkshopTypeOption
{
    public WorkshopType Type { get; set; }
    public string DisplayName { get; set; } = "";

    public override string ToString() => DisplayName;
}

/// <summary>
/// Represents a workshop chip for display in the hero persons table.
/// Contains the workshop ID (displayed as text) and name (shown in tooltip).
/// BorderColor controls the chip's outer ring color for visual categorization:
/// teal (#0d9488) = half-day assigned, black (#000000) = full-day Type1 assigned,
/// red (#DC2626) = unassigned "None" placeholder chip.
/// </summary>
public class WorkshopChip
{
    // Predefined color palette for chips - muted, visually distinct colors
    private static readonly string[] ChipColors = new[]
    {
        "#E3F2FD", // Light Blue
        "#F3E5F5", // Light Purple
        "#E8F5E9", // Light Green
        "#FFF3E0", // Light Orange
        "#FCE4EC", // Light Pink
        "#E0F7FA", // Light Cyan
        "#FFF8E1", // Light Amber
        "#F1F8E9", // Light Lime
        "#EDE7F6", // Light Deep Purple
        "#E8EAF6", // Light Indigo
        "#FFEBEE", // Light Red
        "#E0F2F1", // Light Teal
    };

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public WorkshopType Type { get; set; }

    /// <summary>
    /// Border color for the chip when displayed in the Assigned column.
    /// Teal (#0d9488) for half-day assignments, black (#000000) for full-day Type1.
    /// Red (#DC2626) for "None" placeholder chips indicating missing half-day slots.
    /// Default is teal to match existing assigned chip style.
    /// </summary>
    public string BorderColor { get; set; } = "#0d9488";

    /// <summary>
    /// True for placeholder "None" chips representing unassigned half-day slots.
    /// Used to distinguish real assignment chips from gap indicators.
    /// None chips render with red border and a light red background (#FEE2E2).
    /// </summary>
    public bool IsNoneChip { get; set; } = false;

    /// <summary>
    /// Background color for the chip. For normal chips, computed from the workshop ID hash.
    /// For "None" chips, returns a light red background (#FEE2E2) for immediate visibility.
    /// Each unique workshop ID gets a consistent color from the palette.
    /// </summary>
    public string ChipColor => IsNoneChip ? "#FEE2E2" : GetColorForId(Id);

    private static string GetColorForId(string id)
    {
        if (string.IsNullOrEmpty(id)) return ChipColors[0];
        // Use a simple hash to get a consistent color index
        int hash = 0;
        foreach (char c in id)
            hash = (hash * 31 + c) & 0x7FFFFFFF;
        return ChipColors[hash % ChipColors.Length];
    }
}

/// <summary>
/// Mutable display model for the hero workshops expanded table.
/// Workshop is an immutable record, so we need a mutable class for DataGrid editing.
/// </summary>
public class HeroWorkshopDisplay
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public WorkshopType Type { get; set; }
    public string TypeDisplay { get; set; } = "";
    public int Capacity { get; set; }
    public int MinCapacity { get; set; }

    /// <summary>
    /// Reference to the original Workshop record for propagating changes.
    /// </summary>
    public Workshop? SourceWorkshop { get; set; }

    /// <summary>
    /// The currently selected type option, used for ComboBox binding.
    /// </summary>
    public WorkshopTypeOption? SelectedTypeOption { get; set; }

    /// <summary>
    /// Available type options for the ComboBox. Set to the same shared list for all rows.
    /// Instance property so WinUI data binding can resolve it from the DataContext.
    /// </summary>
    public List<WorkshopTypeOption> AvailableTypeOptions { get; set; } = new();

    /// <summary>
    /// Number of unique persons who listed this workshop in their Preferences (any rank).
    /// Helps organizers identify overloaded workshops that could benefit from increased capacity.
    /// </summary>
    public int TimesWished { get; set; }

    /// <summary>
    /// Number of assigned people. For Type4: "x/y" (slot2/slot3). Otherwise just the count. "-" if solver hasn't run.
    /// </summary>
    public string Assigned { get; set; } = "-";

    /// <summary>
    /// For Type4 dual-slot workshops: number of persons assigned to Slot 2.
    /// Used to compute per-slot utilization. Zero for non-Type4 workshops.
    /// </summary>
    public int Slot2Count { get; set; }

    /// <summary>
    /// For Type4 dual-slot workshops: number of persons assigned to Slot 3.
    /// Used to compute per-slot utilization. Zero for non-Type4 workshops.
    /// </summary>
    public int Slot3Count { get; set; }

    /// <summary>
    /// Utilization percentage: (assigned / capacity) * 100, rounded to 0 decimals.
    /// Range 0.0 to 100.0+ (can exceed 100 if over-capacity, though solver prevents this).
    /// -1.0 means "not computed yet" (solver hasn't run). Display as "-" in this case.
    /// For Type4, this holds the WORST (highest) of the two slot percentages for color coding.
    /// </summary>
    public double UtilizationPercent { get; set; } = -1.0;

    /// <summary>
    /// Hex color for the utilization percentage text, color-coded by fill level:
    /// green (#16a34a) >= 100%, lime (#65a30d) >= 75%, amber (#ca8a04) >= 50%,
    /// orange (#ea580c) >= 25%, red (#dc2626) < 25%. Grey (#888888) when no data.
    /// For Type4, color is based on the worst (most-filled) slot to flag potential issues.
    /// </summary>
    public string UtilizationColor { get; set; } = "#888888";

    /// <summary>
    /// Formatted display string for utilization percentage.
    /// Non-Type4: "85%" single value. Type4: "75%/83%" showing Slot2%/Slot3% independently.
    /// "-" when solver hasn't run (UtilizationPercent == -1).
    /// Set explicitly by PrepareHeroWorkshops rather than computed, because Type4 needs
    /// dual-slot formatting that depends on Slot2Count/Slot3Count and Capacity.
    /// </summary>
    public string UtilizationDisplay { get; set; } = "-";

    /// <summary>
    /// The source file this workshop was loaded from.
    /// </summary>
    public string? SourceFile { get; set; }
}

/// <summary>
/// Mutable display model for the hero persons expanded table.
/// Person is an immutable record, so we need a mutable class for DataGrid editing.
/// Wish1-Wish6 correspond to the person's 6 ranked preferences (3-6 populated).
/// SatisfactionScore shows the computed score from the solver (null if unassigned).
/// </summary>
public class HeroPersonDisplay
{
    public string Name { get; set; } = "";
    public string Info { get; set; } = "";
    public string FriendName { get; set; } = "";
    public string Wish1 { get; set; } = "";
    public string Wish2 { get; set; } = "";
    public string Wish3 { get; set; } = "";
    public string Wish4 { get; set; } = "";
    public string Wish5 { get; set; } = "";
    public string Wish6 { get; set; } = "";
    public string Assigned { get; set; } = "-";

    /// <summary>
    /// Reference to the original Person record for propagating changes.
    /// </summary>
    public Person? SourcePerson { get; set; }

    // Workshop chips for styled display (Id shown, Name in tooltip)
    public List<WorkshopChip> Wish1Chips { get; set; } = new();
    public List<WorkshopChip> Wish2Chips { get; set; } = new();
    public List<WorkshopChip> Wish3Chips { get; set; } = new();
    public List<WorkshopChip> Wish4Chips { get; set; } = new();
    public List<WorkshopChip> Wish5Chips { get; set; } = new();
    public List<WorkshopChip> Wish6Chips { get; set; } = new();
    public List<WorkshopChip> AssignedChips { get; set; } = new();

    /// <summary>
    /// Solver-computed satisfaction score for this person's assignment.
    /// Score = sum of (7 - rank) per assigned workshop, Type1 doubled.
    /// Null when person is unassigned or solver hasn't run.
    /// </summary>
    public int? SatisfactionScore { get; set; }

    /// <summary>
    /// True if there are assigned workshops (chips to display).
    /// Used for visibility binding to show chips vs fallback text.
    /// </summary>
    public bool HasAssignedChips => AssignedChips.Count > 0;

    /// <summary>
    /// The source file this person was loaded from.
    /// </summary>
    public string? SourceFile { get; set; }
}
