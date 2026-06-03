using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkshopAssignment.Models;
using WorkshopAssignment.Services;

namespace WorkshopAssignment.ViewModels;

/// <summary>
/// Partial class containing hero overlay state and commands.
/// Split from MainViewModel for maintainability.
/// </summary>
public partial class MainViewModel
{
    // ═══════════════════════════════════════════════════════════════════════════
    // HERO OVERLAY STATE
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    private bool _isHeroExpanded;

    [ObservableProperty]
    private bool _isHeroAnimating;

    [ObservableProperty]
    private string _heroSection = "";  // "Workshops" or "People"

    [ObservableProperty]
    private string _heroTitle = "";

    [ObservableProperty]
    private string _heroCountText = "";

    [ObservableProperty]
    private ObservableCollection<HeroWorkshopDisplay> _heroWorkshops = new();

    [ObservableProperty]
    private ObservableCollection<HeroPersonDisplay> _heroPersons = new();

    // ═══════════════════════════════════════════════════════════════════════════
    // HERO OVERLAY COMMANDS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Prepares the hero overlay data for the specified section.
    /// Call this before expanding the hero overlay.
    /// </summary>
    [RelayCommand]
    private void PrepareHeroData(string section)
    {
        HeroSection = section;

        if (section == "Workshops")
        {
            HeroTitle = this["hero.workshopsHeader"];
            HeroCountText = string.Format(this["hero.workshopsCount"], TotalWorkshops);
            PrepareHeroWorkshops();
        }
        else
        {
            HeroTitle = this["hero.peopleHeader"];
            HeroCountText = string.Format(this["hero.peopleCount"], TotalPersons);
            PrepareHeroPersons();
        }
    }

    private void PrepareHeroWorkshops()
    {
        HeroWorkshops.Clear();

        // Build the shared list of type options for the ComboBox
        var typeOptions = new List<WorkshopTypeOption>
        {
            new() { Type = WorkshopType.Type1, DisplayName = GetTypeDisplayName(WorkshopType.Type1) },
            new() { Type = WorkshopType.Type2, DisplayName = GetTypeDisplayName(WorkshopType.Type2) },
            new() { Type = WorkshopType.Type3, DisplayName = GetTypeDisplayName(WorkshopType.Type3) },
            new() { Type = WorkshopType.Type4, DisplayName = GetTypeDisplayName(WorkshopType.Type4) },
        };

        // Pre-compute how many unique persons wished for each workshop (any rank)
        var wishCounts = new Dictionary<string, int>();
        foreach (var person in Persons)
        {
            foreach (var pref in person.Preferences)
            {
                if (!wishCounts.ContainsKey(pref))
                    wishCounts[pref] = 0;
                wishCounts[pref]++;
            }
        }

        var displays = Workshops
            .Select(w =>
            {
                var display = new HeroWorkshopDisplay
                {
                    Name = w.Name,
                    Type = w.Type,
                    TypeDisplay = GetTypeDisplayName(w.Type),
                    Capacity = w.Capacity,
                    MinCapacity = w.MinCapacity,
                    Id = w.Id,
                    SourceWorkshop = w,
                    AvailableTypeOptions = typeOptions,
                    TimesWished = wishCounts.TryGetValue(w.Id, out var wishCount) ? wishCount : 0,
                };
                display.SelectedTypeOption = typeOptions.First(o => o.Type == w.Type);
                return display;
            })
            .OrderBy(d => IsPauseRow(d.Name) ? 1 : 0)
            .ThenBy(d => (int)d.Type)
            .ThenBy(d => d.Name)
            .ToList();

        // Populate Assigned counts if we have results
        if (Result != null)
        {
            var workshopLookup = Workshops.ToDictionary(w => w.Id);
            var counts = new Dictionary<string, int>();

            foreach (var a in Result.Assignments)
                foreach (var wId in a.WorkshopIds)
                {
                    counts.TryGetValue(wId, out var c);
                    counts[wId] = c + 1;
                }

            foreach (var d in displays)
            {
                var ws = d.SourceWorkshop;
                if (ws == null) continue;

                var total = counts.GetValueOrDefault(ws.Id, 0);

                if (ws.Type == WorkshopType.Type4)
                {
                    // For Type4, determine slot breakdown using centralized logic
                    int slot2 = 0, slot3 = 0;
                    foreach (var a in Result.Assignments)
                    {
                        if (!a.WorkshopIds.Contains(ws.Id)) continue;
                        var slotNum = SlotPlacementHelper.GetType4SlotNumber(
                            ws.Id,
                            a.WorkshopIds,
                            id => workshopLookup.GetValueOrDefault(id));
                        if (slotNum == 2)
                            slot2++;
                        else
                            slot3++;
                    }
                    d.Assigned = $"{slot2}/{slot3}";
                    d.Slot2Count = slot2;
                    d.Slot3Count = slot3;
                }
                else
                {
                    d.Assigned = total.ToString();
                }

                // Compute utilization percentage and color-code
                if (ws.Capacity > 0)
                {
                    if (ws.Type == WorkshopType.Type4)
                    {
                        // Type4 dual-slot: show per-slot percentages independently
                        double pctSlot2 = (double)d.Slot2Count / ws.Capacity * 100;
                        double pctSlot3 = (double)d.Slot3Count / ws.Capacity * 100;
                        double pctSlot2R = Math.Round(pctSlot2, 0);
                        double pctSlot3R = Math.Round(pctSlot3, 0);

                        d.UtilizationDisplay = $"{pctSlot2R:0}%/{pctSlot3R:0}%";

                        // Use the worst (highest) slot for color coding — if one slot
                        // is over-capacity or critically low, the indicator should reflect that
                        double worstPct = Math.Max(pctSlot2, pctSlot3);
                        d.UtilizationPercent = Math.Round(worstPct, 0);
                        d.UtilizationColor = worstPct >= 100 ? "#16a34a"   // green — full or over
                                           : worstPct >= 75  ? "#65a30d"   // lime — well filled
                                           : worstPct >= 50  ? "#ca8a04"   // amber — half filled
                                           : worstPct >= 25  ? "#ea580c"   // orange — under-filled
                                           :                    "#dc2626";  // red — critically low
                    }
                    else
                    {
                        // Non-Type4: single utilization percentage
                        double pct = (double)total / ws.Capacity * 100;
                        d.UtilizationPercent = Math.Round(pct, 0);
                        d.UtilizationDisplay = $"{d.UtilizationPercent:0}%";
                        d.UtilizationColor = pct >= 100 ? "#16a34a"   // green — full or over
                                           : pct >= 75  ? "#65a30d"   // lime — well filled
                                           : pct >= 50  ? "#ca8a04"   // amber — half filled
                                           : pct >= 25  ? "#ea580c"   // orange — under-filled
                                           :               "#dc2626";  // red — critically low
                    }
                }
                else
                {
                    // Zero capacity — show 0% in grey (should not happen in practice)
                    d.UtilizationPercent = 0;
                    d.UtilizationDisplay = "0%";
                    d.UtilizationColor = "#888888";
                }
            }
        }

        foreach (var d in displays)
            HeroWorkshops.Add(d);
    }

    private void PrepareHeroPersons()
    {
        HeroPersons.Clear();

        var assignmentLookup = Result?.Assignments
            .ToDictionary(a => a.PersonId, a => a) ?? new();

        var displays = Persons
            .Select(p =>
            {
                assignmentLookup.TryGetValue(p.Id, out var assignment);
                var assignedChips = ResolveAssignmentChips(p);

                // Add "None" chips for incomplete assignments when solver has run
                if (Result != null)
                {
                    if (assignedChips.Count == 0)
                    {
                        // Fully unassigned: show two "None" chips for both missing half-day slots
                        assignedChips.Add(new WorkshopChip { Id = "None", Name = "Unassigned", IsNoneChip = true, BorderColor = "#DC2626" });
                        assignedChips.Add(new WorkshopChip { Id = "None", Name = "Unassigned", IsNoneChip = true, BorderColor = "#DC2626" });
                    }
                    else if (assignedChips.Count == 1 && assignedChips[0].Type != WorkshopType.Type1)
                    {
                        // Partially assigned: got one half-day workshop but missing the other slot
                        assignedChips.Add(new WorkshopChip { Id = "None", Name = "Unassigned", IsNoneChip = true, BorderColor = "#DC2626" });
                    }
                }

                return new HeroPersonDisplay
                {
                    Name = p.Name,
                    Info = p.Info ?? "",
                    FriendName = ResolveFriendName(p.FriendId),
                    Wish1 = ResolveWish(p, 0),
                    Wish2 = ResolveWish(p, 1),
                    Wish3 = ResolveWish(p, 2),
                    Wish4 = ResolveWish(p, 3),
                    Wish5 = ResolveWish(p, 4),
                    Wish6 = ResolveWish(p, 5),
                    Assigned = ResolveAssignment(p),
                    Wish1Chips = ResolveWishChips(p, 0),
                    Wish2Chips = ResolveWishChips(p, 1),
                    Wish3Chips = ResolveWishChips(p, 2),
                    Wish4Chips = ResolveWishChips(p, 3),
                    Wish5Chips = ResolveWishChips(p, 4),
                    Wish6Chips = ResolveWishChips(p, 5),
                    AssignedChips = assignedChips,
                    SatisfactionScore = assignment?.SatisfactionScore
                };
            })
            .ToList();

        foreach (var d in displays)
            HeroPersons.Add(d);
    }

    /// <summary>
    /// Updates a workshop's type from the hero grid.
    /// Validates type via DataAlterationValidator before applying.
    /// </summary>
    public void UpdateWorkshopType(HeroWorkshopDisplay display, WorkshopType newType)
    {
        if (display.SourceWorkshop == null) return;

        // Validate type per DATA_ALTERATION_SPEC - reject invalid enum values
        if (!_validator.IsValidType(newType)) return;

        var index = Workshops.IndexOf(display.SourceWorkshop);
        if (index < 0) return;

        // Workshop is an immutable record - replace it with a new instance
        // C# record 'with' expression preserves all fields (Location, SourceFile) while changing Type
        var updated = display.SourceWorkshop with { Type = newType };
        Workshops[index] = updated;
        display.SourceWorkshop = updated;
        display.Type = newType;
        display.TypeDisplay = GetTypeDisplayName(newType);

        UpdateGroupedDisplays();
    }

    /// <summary>
    /// Updates a workshop's editable fields (Name, Capacity, MinCapacity) from the hero grid.
    /// Called when a TextBox loses focus after inline editing. Workshop is immutable, so
    /// we create a new instance with the changed field values and replace it in the collection.
    /// Capacity values are validated and normalized via DataAlterationValidator:
    /// negative values clamped to 0, MinCapacity clamped to not exceed Capacity.
    /// </summary>
    public void UpdateWorkshopField(HeroWorkshopDisplay display)
    {
        if (display.SourceWorkshop == null) return;

        var old = display.SourceWorkshop;
        var index = Workshops.IndexOf(old);
        if (index < 0) return;

        // Resolve nullable display values BEFORE validation. The hero "Min"/"Max" cells bind
        // TwoWay to int? so a cleared cell arrives here as null (see HeroWorkshopDisplay).
        // Capacity/MinCapacity follow the SAME empty-input rule: a cleared cell REVERTS to the
        // workshop's PREVIOUS committed value, never collapses to 0.
        //   - Max empty/null  -> REVERT to old.Capacity (last committed capacity).
        //   - Min empty/null  -> REVERT to old.MinCapacity (last committed minimum).
        // Clearing a cell must NOT silently zero the value (v0.3.0, Captain's explicit rule:
        // empty input never forces 0 for either field). old is the immutable SourceWorkshop
        // record still holding the last committed values, so it is the authoritative previous
        // value. The subsequent validators leave an already-valid reverted value unchanged.
        var resolvedCapacity = display.Capacity ?? old.Capacity;
        var resolvedMinCapacity = display.MinCapacity ?? old.MinCapacity;

        // Validate and normalize capacity values per DATA_ALTERATION_SPEC
        var validatedCapacity = _validator.ValidateCapacity(resolvedCapacity);
        var validatedMinCapacity = _validator.ValidateMinCapacity(resolvedMinCapacity);
        validatedMinCapacity = _validator.AdjustMinCapacityToCapacity(validatedMinCapacity, validatedCapacity);

        // Sync normalized values back to display so UI reflects validated state.
        // Crucially this also overwrites a null (cleared) cell with the resolved concrete
        // number (0 for Min, the reverted value for Max), so the cell never renders empty
        // after commit. HeroWorkshopDisplay has no INotifyPropertyChanged, so UpdateGroupedDisplays
        // below rebuilds the bound collection to push these values to the grid (see gotchas).
        display.Capacity = validatedCapacity;
        display.MinCapacity = validatedMinCapacity;

        var updated = new Workshop(old.Id, display.Name, old.Type, validatedCapacity, validatedMinCapacity, old.Location, old.SourceFile);
        Workshops[index] = updated;
        display.SourceWorkshop = updated;
        UpdateGroupedDisplays();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HERO DATA HELPERS
    // ═══════════════════════════════════════════════════════════════════════════
    private string ResolveFriendName(string? friendId)
    {
        if (string.IsNullOrEmpty(friendId)) return "";
        return Persons.FirstOrDefault(p => p.Id == friendId)?.Name ?? friendId;
    }

    private string ResolveWish(Person person, int index)
    {
        if (person.Preferences == null || index >= person.Preferences.Count) return "";
        var workshopId = person.Preferences[index];
        var workshop = Workshops.FirstOrDefault(w => w.Id == workshopId);
        return workshop?.Name ?? workshopId;
    }

    private List<WorkshopChip> ResolveWishChips(Person person, int index)
    {
        if (person.Preferences == null || index >= person.Preferences.Count) return new();
        var workshopId = person.Preferences[index];
        var workshop = Workshops.FirstOrDefault(w => w.Id == workshopId);
        return new List<WorkshopChip>
        {
            new WorkshopChip
            {
                Id = workshopId,
                Name = workshop?.Name ?? workshopId,
                Type = workshop?.Type ?? WorkshopType.Type1
            }
        };
    }

    private string ResolveAssignment(Person person)
    {
        if (Result == null) return "-";
        var assignment = Result.Assignments.FirstOrDefault(a => a.PersonId == person.Id);
        if (assignment == null || assignment.WorkshopIds.Count == 0) return "-";
        var names = assignment.WorkshopIds
            .Select(id => Workshops.FirstOrDefault(w => w.Id == id)?.Name ?? id)
            .ToList();
        return string.Join(" / ", names);
    }

    private List<WorkshopChip> ResolveAssignmentChips(Person person)
    {
        if (Result == null) return new();
        var assignment = Result.Assignments.FirstOrDefault(a => a.PersonId == person.Id);
        if (assignment == null || assignment.WorkshopIds.Count == 0) return new();
        return assignment.WorkshopIds
            .Select(id =>
            {
                var workshop = Workshops.FirstOrDefault(w => w.Id == id);
                var workshopType = workshop?.Type ?? WorkshopType.Type1;
                return new WorkshopChip
                {
                    Id = id,
                    Name = workshop?.Name ?? id,
                    Type = workshopType,
                    // Type1 (full-day) gets black border for visual distinction from half-day (teal)
                    BorderColor = workshopType == WorkshopType.Type1 ? "#000000" : "#0d9488"
                };
            })
            .ToList();
    }

    /// <summary>
    /// Checks if a name represents a "Pause" entry that should be pinned to the bottom of sorted grids.
    /// </summary>
    public static bool IsPauseRow(string? name)
    {
        return !string.IsNullOrEmpty(name) &&
               name.Contains("Pause", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sorts the HeroWorkshops collection by the specified property.
    /// </summary>
    public void SortHeroWorkshops(string propertyName, bool ascending)
    {
        var items = HeroWorkshops.ToList();
        var pauseRows = items.Where(x => IsPauseRow(x.Name)).ToList();
        var sortableRows = items.Where(x => !IsPauseRow(x.Name)).ToList();

        IEnumerable<HeroWorkshopDisplay> sorted = propertyName switch
        {
            "Name" => ascending ? sortableRows.OrderBy(x => x.Name) : sortableRows.OrderByDescending(x => x.Name),
            "TypeDisplay" or "Type" => ascending ? sortableRows.OrderBy(x => (int)x.Type) : sortableRows.OrderByDescending(x => (int)x.Type),
            "Capacity" => ascending ? sortableRows.OrderBy(x => x.Capacity) : sortableRows.OrderByDescending(x => x.Capacity),
            "MinCapacity" => ascending ? sortableRows.OrderBy(x => x.MinCapacity) : sortableRows.OrderByDescending(x => x.MinCapacity),
            "Assigned" => ascending ? sortableRows.OrderBy(x => x.Assigned) : sortableRows.OrderByDescending(x => x.Assigned),
            _ => sortableRows
        };

        HeroWorkshops.Clear();
        foreach (var item in sorted.Concat(pauseRows))
            HeroWorkshops.Add(item);
    }

    /// <summary>
    /// Sorts the HeroPersons collection by the specified property.
    /// </summary>
    public void SortHeroPersons(string propertyName, bool ascending)
    {
        var items = HeroPersons.ToList();

        IEnumerable<HeroPersonDisplay> sorted = propertyName switch
        {
            "Name" => ascending ? items.OrderBy(x => x.Name) : items.OrderByDescending(x => x.Name),
            "Info" => ascending ? items.OrderBy(x => x.Info) : items.OrderByDescending(x => x.Info),
            "FriendName" => ascending ? items.OrderBy(x => x.FriendName) : items.OrderByDescending(x => x.FriendName),
            "Wish1" => ascending ? items.OrderBy(x => x.Wish1) : items.OrderByDescending(x => x.Wish1),
            "Wish2" => ascending ? items.OrderBy(x => x.Wish2) : items.OrderByDescending(x => x.Wish2),
            "Wish3" => ascending ? items.OrderBy(x => x.Wish3) : items.OrderByDescending(x => x.Wish3),
            "Wish4" => ascending ? items.OrderBy(x => x.Wish4) : items.OrderByDescending(x => x.Wish4),
            "Wish5" => ascending ? items.OrderBy(x => x.Wish5) : items.OrderByDescending(x => x.Wish5),
            "Wish6" => ascending ? items.OrderBy(x => x.Wish6) : items.OrderByDescending(x => x.Wish6),
            "Assigned" => ascending ? items.OrderBy(x => x.Assigned) : items.OrderByDescending(x => x.Assigned),
            "SatisfactionScore" => ascending ? items.OrderBy(x => x.SatisfactionScore) : items.OrderByDescending(x => x.SatisfactionScore),
            _ => items
        };

        HeroPersons.Clear();
        foreach (var item in sorted)
            HeroPersons.Add(item);
    }

}
