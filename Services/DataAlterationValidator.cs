using WorkshopAssignment.Models;

namespace WorkshopAssignment.Services;

/// <summary>
/// Validates and normalizes workshop data alterations per DATA_ALTERATION_SPEC.md.
/// All validation rules apply to both hero expand table and inline UI edits.
/// </summary>
public class DataAlterationValidator
{
    /// <summary>
    /// Validates and normalizes workshop capacity.
    /// Per spec: 0 = valid (disabled), negative -> 0.
    /// </summary>
    /// <param name="capacity">The input capacity value.</param>
    /// <returns>Normalized capacity (>= 0).</returns>
    public int ValidateCapacity(int capacity)
    {
        return Math.Max(0, capacity);
    }

    /// <summary>
    /// Validates and normalizes workshop min capacity.
    /// Per spec: negative -> 0.
    /// </summary>
    /// <param name="minCapacity">The input min capacity value.</param>
    /// <returns>Normalized min capacity (>= 0).</returns>
    public int ValidateMinCapacity(int minCapacity)
    {
        return Math.Max(0, minCapacity);
    }

    /// <summary>
    /// Adjusts min capacity if it exceeds capacity.
    /// Per spec: if min > capacity after edit, min should become = capacity.
    /// </summary>
    /// <param name="minCapacity">The min capacity value.</param>
    /// <param name="capacity">The capacity value to constrain against.</param>
    /// <returns>Adjusted min capacity (<= capacity).</returns>
    public int AdjustMinCapacityToCapacity(int minCapacity, int capacity)
    {
        // Capacity 0 is a special case - workshop is disabled, min is effectively irrelevant
        // but we still clamp it to not exceed capacity
        return Math.Min(minCapacity, capacity);
    }

    /// <summary>
    /// Validates that the workshop type is a valid enum value.
    /// Per spec: invalid type should not be possible (UI shows dropdown).
    /// This method is for programmatic validation.
    /// </summary>
    /// <param name="type">The workshop type to validate.</param>
    /// <returns>True if the type is valid.</returns>
    public bool IsValidType(WorkshopType type)
    {
        return type is WorkshopType.Type1 or WorkshopType.Type2 or WorkshopType.Type3 or WorkshopType.Type4;
    }

    /// <summary>
    /// Validates that location is valid (any string including empty is valid).
    /// Per spec: location is optional, no constraints.
    /// </summary>
    /// <param name="location">The location value.</param>
    /// <returns>Always true (location has no constraints).</returns>
    public bool IsValidLocation(string? location)
    {
        // Per spec: empty/missing is fine, any string value is fine
        return true;
    }

    /// <summary>
    /// Applies all validation rules to create a normalized workshop from edit values.
    /// </summary>
    /// <param name="original">The original workshop being edited.</param>
    /// <param name="newCapacity">The new capacity value.</param>
    /// <param name="newMinCapacity">The new min capacity value.</param>
    /// <param name="newType">The new workshop type.</param>
    /// <param name="newLocation">The new location value.</param>
    /// <returns>A new Workshop record with validated/normalized values.</returns>
    public Workshop ApplyEdits(
        Workshop original,
        int newCapacity,
        int newMinCapacity,
        WorkshopType newType,
        string? newLocation)
    {
        // Validate and normalize capacity (negative -> 0)
        var validatedCapacity = ValidateCapacity(newCapacity);

        // Validate and normalize min capacity (negative -> 0)
        var validatedMinCapacity = ValidateMinCapacity(newMinCapacity);

        // Adjust min capacity if it exceeds capacity
        validatedMinCapacity = AdjustMinCapacityToCapacity(validatedMinCapacity, validatedCapacity);

        // Create new workshop with validated values (preserving source file for tracking)
        return original with
        {
            Capacity = validatedCapacity,
            MinCapacity = validatedMinCapacity,
            Type = newType,
            Location = newLocation
        };
    }
}

/// <summary>
/// Represents an edit operation on a workshop, tracking the source file.
/// </summary>
public record WorkshopEdit(
    string WorkshopId,
    int? Capacity = null,
    int? MinCapacity = null,
    WorkshopType? Type = null,
    string? Location = null,
    string? SourceFile = null
);

/// <summary>
/// Manages workshop edits and their relationship to source files.
/// Per spec: edits are tracked separately from source files, but removed when source file is removed.
/// </summary>
public class WorkshopEditTracker
{
    private readonly Dictionary<string, WorkshopEdit> _edits = new();

    /// <summary>
    /// Records an edit for a workshop.
    /// </summary>
    /// <param name="edit">The edit to record.</param>
    public void RecordEdit(WorkshopEdit edit)
    {
        _edits[edit.WorkshopId] = edit;
    }

    /// <summary>
    /// Gets the edit for a workshop, if any.
    /// </summary>
    /// <param name="workshopId">The workshop ID.</param>
    /// <returns>The edit, or null if no edit exists.</returns>
    public WorkshopEdit? GetEdit(string workshopId)
    {
        return _edits.TryGetValue(workshopId, out var edit) ? edit : null;
    }

    /// <summary>
    /// Gets all recorded edits.
    /// </summary>
    /// <returns>All edits.</returns>
    public IReadOnlyDictionary<string, WorkshopEdit> GetAllEdits() => _edits;

    /// <summary>
    /// Removes all edits for workshops that came from the specified source file.
    /// Per spec: when file X is removed, workshops from file X lose their edits.
    /// </summary>
    /// <param name="sourceFile">The source file being removed.</param>
    /// <returns>List of workshop IDs whose edits were removed.</returns>
    public List<string> RemoveEditsForSourceFile(string sourceFile)
    {
        var editsToRemove = _edits
            .Where(kvp => kvp.Value.SourceFile == sourceFile)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var workshopId in editsToRemove)
        {
            _edits.Remove(workshopId);
        }

        return editsToRemove;
    }

    /// <summary>
    /// Clears all edits.
    /// </summary>
    public void Clear()
    {
        _edits.Clear();
    }
}
