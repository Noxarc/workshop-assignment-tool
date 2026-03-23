namespace WorkshopAssignment.Models;

public record Assignment(
    string PersonId,
    List<string> WorkshopIds,
    int? WishRank,
    List<int>? PreferenceRanks = null,
    int? SatisfactionScore = null,
    bool IsFullDay = false
);

public record AssignmentResult(
    List<Assignment> Assignments,
    List<string> CancelledWorkshopIds
)
{
    /// <summary>
    /// Solver-generated warnings (e.g., phantom workshop IDs filtered from wishes).
    /// Init-only with default empty list — backward compatible with all existing constructors.
    /// </summary>
    public List<string> Warnings { get; init; } = new();

    public IEnumerable<Assignment> Assigned => Assignments.Where(a => a.WorkshopIds.Count > 0);
    public IEnumerable<Assignment> Unassigned => Assignments.Where(a => a.WorkshopIds.Count == 0);

    public int TotalPersons => Assignments.Count;
    public int AssignedCount => Assigned.Count();
    public int UnassignedCount => Unassigned.Count();

    public double AssignmentRate => TotalPersons > 0
        ? (double)AssignedCount / TotalPersons * 100
        : 0;

    public Dictionary<int, int> WishDistribution => Assignments
        .Where(a => a.WishRank.HasValue)
        .GroupBy(a => a.WishRank!.Value)
        .ToDictionary(g => g.Key, g => g.Count());

    /// <summary>
    /// Counts each workshop slot separately for the rank distribution bar chart.
    /// - Type1 (IsFullDay=true): rank counted TWICE (fills full day = 2 slots)
    /// - Pairs (2 workshops): each workshop's rank counted once (2 entries total)
    /// - Half-day solos (1 workshop, IsFullDay=false): rank counted ONCE (only 1 slot filled)
    /// Type1 and pairs contribute 2 slot entries; half-day solos contribute 1.
    /// </summary>
    public Dictionary<int, int> SlotRankDistribution => Assignments
        .Where(a => a.PreferenceRanks != null)
        .SelectMany(a => {
            if (a.WorkshopIds.Count == 1 && a.PreferenceRanks!.Count == 1 && a.IsFullDay)
                // Type1: count the rank twice (fills full day = 2 slots)
                return new[] { a.PreferenceRanks![0], a.PreferenceRanks![0] };
            else
                // Pairs: 2 entries. Half-day solos: 1 entry.
                return a.PreferenceRanks!.AsEnumerable();
        })
        .GroupBy(r => r)
        .ToDictionary(g => g.Key, g => g.Count());
}
