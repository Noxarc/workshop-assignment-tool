using WorkshopAssignment.Models;

namespace WorkshopAssignment.Services;

/// <summary>
/// Centralized logic for determining which slot(s) workshops fill based on their types.
///
/// Slot rules:
/// - Type1: Slot 1 only (standalone, fills entire assignment)
/// - Type2: Slot 2 only
/// - Type3: Slot 3 only
/// - Type4: Flexible - fills Slot 2 OR Slot 3, whichever is empty
/// </summary>
public static class SlotPlacementHelper
{
    /// <summary>
    /// Result of slot resolution for a person's workshop assignments.
    /// </summary>
    public record SlotAssignment(
        string? Slot1Workshop,  // Type1 workshop name (if any)
        string? Slot2Workshop,  // Type2 or Type4 workshop name
        string? Slot3Workshop   // Type3 or Type4 workshop name
    );

    /// <summary>
    /// Determines which slot each assigned workshop fills.
    ///
    /// Algorithm:
    /// 1. First pass: Place non-flexible workshops (Type1 -> Slot1, Type2 -> Slot2, Type3 -> Slot3)
    /// 2. Second pass: Place Type4 workshops into remaining slots (prefer Slot2, then Slot3)
    /// </summary>
    /// <param name="assignedWorkshopIds">List of workshop IDs assigned to a person</param>
    /// <param name="workshopLookup">Function to resolve workshop ID to Workshop object</param>
    /// <returns>SlotAssignment with workshop names in their respective slots</returns>
    public static SlotAssignment ResolveSlots(
        IEnumerable<string> assignedWorkshopIds,
        Func<string, Workshop?> workshopLookup)
    {
        var assignedWorkshops = assignedWorkshopIds
            .Select(workshopLookup)
            .Where(w => w != null)
            .ToList();

        // Track which slots are filled
        string? slot1 = null;
        string? slot2 = null;
        string? slot3 = null;
        var type4Workshops = new List<Workshop>();

        // First pass: place non-flexible workshops
        foreach (var ws in assignedWorkshops)
        {
            switch (ws!.Type)
            {
                case WorkshopType.Type1:
                    slot1 = ws.Name;
                    break;
                case WorkshopType.Type2:
                    slot2 = ws.Name;
                    break;
                case WorkshopType.Type3:
                    slot3 = ws.Name;
                    break;
                case WorkshopType.Type4:
                    type4Workshops.Add(ws);
                    break;
            }
        }

        // Second pass: place Type4 workshops into remaining slots
        // Prefer Slot2, then Slot3
        foreach (var ws in type4Workshops)
        {
            if (slot2 == null)
                slot2 = ws.Name;
            else if (slot3 == null)
                slot3 = ws.Name;
        }

        return new SlotAssignment(slot1, slot2, slot3);
    }

    /// <summary>
    /// Determines which slot a Type4 workshop occupies for a specific person's assignment.
    /// Used for calculating Type4 slot breakdown statistics.
    ///
    /// Rules mirror DetermineType4Slots in SolverService exactly:
    /// - Type4 paired with Type2 -> Type4 goes to Slot 3 (Type2 owns Slot 2)
    /// - Type4 paired with Type3 -> Type4 goes to Slot 2 (Type3 owns Slot 3)
    /// - Type4 paired with Type4 -> positional tiebreaker from Assignment.WorkshopIds:
    ///   first ID in list -> Slot 2, second -> Slot 3 (matches solver's combo order i&lt;j)
    /// - Type4 alone (no partner) -> defaults to Slot 2
    /// </summary>
    /// <param name="type4WorkshopId">ID of the Type4 workshop to check</param>
    /// <param name="allAssignedWorkshopIds">All workshop IDs assigned to the person (order matters for Type4+Type4 tiebreaker)</param>
    /// <param name="workshopLookup">Function to resolve workshop ID to Workshop object</param>
    /// <returns>2 for Slot2, 3 for Slot3</returns>
    public static int GetType4SlotNumber(
        string type4WorkshopId,
        IEnumerable<string> allAssignedWorkshopIds,
        Func<string, Workshop?> workshopLookup)
    {
        // Materialize to preserve positional order — this order matches the solver's combo generation
        var idList = allAssignedWorkshopIds as IList<string> ?? allAssignedWorkshopIds.ToList();

        var otherWorkshops = idList
            .Where(id => id != type4WorkshopId)
            .Select(id => (Id: id, Workshop: workshopLookup(id)))
            .Where(x => x.Workshop != null)
            .ToList();

        // If no other workshops, default to Slot2
        if (otherWorkshops.Count == 0)
            return 2;

        var partner = otherWorkshops[0];

        // Type4 paired with Type3: Type4 fills Slot 2 (Type3 owns Slot 3)
        if (partner.Workshop!.Type == WorkshopType.Type3)
            return 2;

        // Type4 paired with Type2: Type4 fills Slot 3 (Type2 owns Slot 2)
        if (partner.Workshop!.Type == WorkshopType.Type2)
            return 3;

        // Type4 paired with Type4: use positional tiebreaker from the assignment's workshop list.
        // The solver's DetermineType4Slots assigns first workshop (param a) -> Slot 2, second (param b) -> Slot 3.
        // In the combo generation loop (i<j), workshop at index i is first, j is second.
        // Assignment.WorkshopIds preserves this combo order, so index 0 -> Slot 2, index 1 -> Slot 3.
        if (partner.Workshop!.Type == WorkshopType.Type4)
        {
            var indexOfThis = IndexOf(idList, type4WorkshopId);
            var indexOfPartner = IndexOf(idList, partner.Id);

            // Earlier position in the assignment list -> Slot 2, later -> Slot 3
            return indexOfThis < indexOfPartner ? 2 : 3;
        }

        // Fallback for unexpected types (Type1 shouldn't pair, but be safe)
        return 3;
    }

    /// <summary>
    /// Index lookup that works on both IList and materialized List without LINQ overhead.
    /// </summary>
    private static int IndexOf(IList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
                return i;
        }
        return -1;
    }
}
