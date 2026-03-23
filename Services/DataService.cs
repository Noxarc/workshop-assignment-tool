using WorkshopAssignment.Models;

namespace WorkshopAssignment.Services;

public class DataService
{
    public ImportResult BuildAssignmentInput(List<Workshop> workshops, List<Person> persons)
    {
        var warnings = new List<ImportWarning>();
        var validWorkshopIds = new HashSet<string>(workshops.Select(w => w.Id));

        // First pass: filter each person's preferences to valid workshop IDs,
        // skip persons with fewer than 3 valid preferences
        var filteredPersons = new List<Person>();

        foreach (var person in persons)
        {
            var validPreferences = new List<string>();
            foreach (var prefId in person.Preferences)
            {
                if (validWorkshopIds.Contains(prefId))
                {
                    validPreferences.Add(prefId);
                }
                else
                {
                    warnings.Add(new ImportWarning(WarningCategory.UnknownWorkshop,
                        $"Person {person.Id} ({person.Name}): preference '{prefId}' references unknown workshop (removed)"));
                }
            }

            if (validPreferences.Count < 3)
            {
                warnings.Add(new ImportWarning(WarningCategory.PersonSkipped,
                    $"Person {person.Id} ({person.Name}) skipped: only {validPreferences.Count} valid preferences (minimum 3 required)"));
                continue;
            }

            // Replace person's preferences with the filtered list (preserving rank order)
            filteredPersons.Add(person with { Preferences = validPreferences });
        }

        // Resolve friend chains into groups (using only valid persons)
        var friendGroups = ResolveFriendGroups(filteredPersons, warnings);

        // Pool preferences for each friend group:
        // Collect all preferences from all members, deduplicate by workshop ID,
        // keep the best (lowest) rank for each workshop, sort by rank
        var groupsWithPreferences = friendGroups.Select(group =>
        {
            // Collect (workshopId, rank) pairs from all group members
            // rank = index position in member's Preferences list (0 = best)
            var bestRankByWorkshop = new Dictionary<string, int>();

            foreach (var memberId in group)
            {
                var member = filteredPersons.First(p => p.Id == memberId);
                for (int rank = 0; rank < member.Preferences.Count; rank++)
                {
                    var workshopId = member.Preferences[rank];
                    if (!bestRankByWorkshop.TryGetValue(workshopId, out var existingRank) || rank < existingRank)
                    {
                        bestRankByWorkshop[workshopId] = rank;
                    }
                }
            }

            // Sort by best rank (ascending = best first), then by workshop ID for stable ordering
            var pooledPreferences = bestRankByWorkshop
                .OrderBy(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key)
                .Select(kvp => kvp.Key)
                .ToList();

            return new FriendGroup(group, pooledPreferences);
        }).ToList();

        var input = new AssignmentInput(workshops, filteredPersons, groupsWithPreferences);
        return new ImportResult(input, warnings);
    }

    private List<List<string>> ResolveFriendGroups(List<Person> persons, List<ImportWarning> warnings)
    {
        var personMap = persons.ToDictionary(p => p.Id);
        var visited = new HashSet<string>();
        var groups = new List<List<string>>();

        // First, detect non-mutual friend references and collect persons involved
        var nonMutualPersons = new HashSet<string>();
        foreach (var person in persons)
        {
            if (person.FriendId != null && personMap.TryGetValue(person.FriendId, out var friend))
            {
                // Check if the friend references back
                if (friend.FriendId != person.Id)
                {
                    // Non-mutual: A references B but B doesn't reference A (or references someone else)
                    // Warning mentions both persons for transparency, but only the initiator goes solo
                    warnings.Add(new ImportWarning(WarningCategory.NonMutualFriend,
                        $"Non-mutual friend reference: {person.Id} references {person.FriendId}, but not reciprocated"));
                    nonMutualPersons.Add(person.Id);
                }
            }
            else if (person.FriendId != null && !personMap.ContainsKey(person.FriendId))
            {
                // Friend doesn't exist
                warnings.Add(new ImportWarning(WarningCategory.InvalidFriend,
                    $"Person {person.Id} references non-existent friend {person.FriendId}"));
            }
        }

        foreach (var person in persons)
        {
            if (visited.Contains(person.Id))
                continue;

            // If person is involved in non-mutual reference, make them solo
            if (nonMutualPersons.Contains(person.Id))
            {
                visited.Add(person.Id);
                groups.Add(new List<string> { person.Id });
                continue;
            }

            // BFS to find connected component (only following mutual references)
            var group = new List<string>();
            var queue = new Queue<string>();
            queue.Enqueue(person.Id);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (visited.Contains(current))
                    continue;

                visited.Add(current);
                group.Add(current);

                if (personMap.TryGetValue(current, out var p) && p.FriendId != null)
                {
                    // Only follow if friend exists and is not in non-mutual set
                    if (!visited.Contains(p.FriendId) &&
                        personMap.ContainsKey(p.FriendId) &&
                        !nonMutualPersons.Contains(p.FriendId))
                    {
                        queue.Enqueue(p.FriendId);
                    }
                }

                // Also check if anyone references this person as friend (only mutual)
                foreach (var other in persons)
                {
                    if (other.FriendId == current &&
                        !visited.Contains(other.Id) &&
                        !nonMutualPersons.Contains(other.Id))
                    {
                        queue.Enqueue(other.Id);
                    }
                }
            }

            // Check group size
            if (group.Count > 3)
            {
                warnings.Add(new ImportWarning(WarningCategory.OversizedGroup,
                    $"Friend group with {group.Count} members [{string.Join(",", group)}] exceeds max of 3, splitting into individuals"));

                // Split into individual groups
                foreach (var memberId in group)
                {
                    groups.Add(new List<string> { memberId });
                }
            }
            else
            {
                groups.Add(group);
            }
        }

        return groups;
    }

    /// <summary>
    /// Validates workshops from a persons file against the main workshops file (source of truth).
    /// Per IMPORT_WORKFLOW_SPEC.md cross-file validation rules:
    /// - Matching workshops: no warning
    /// - Different workshop data: warn, use main file data
    /// - Missing workshops in persons file: warn
    /// - Extra workshops in persons file: warn, ignore
    /// </summary>
    /// <param name="mainWorkshops">Workshops from the dedicated workshops file (source of truth)</param>
    /// <param name="personsFileWorkshops">Workshops embedded in a persons file</param>
    /// <returns>List of warnings about cross-file workshop discrepancies</returns>
    public List<ImportWarning> ValidateCrossFileWorkshops(List<Workshop> mainWorkshops, List<Workshop> personsFileWorkshops)
    {
        var warnings = new List<ImportWarning>();

        if (mainWorkshops == null || mainWorkshops.Count == 0 || personsFileWorkshops == null || personsFileWorkshops.Count == 0)
        {
            return warnings;
        }

        var mainWorkshopMap = mainWorkshops.ToDictionary(w => w.Id);
        var personsWorkshopMap = personsFileWorkshops.ToDictionary(w => w.Id);

        // Check for different workshop data (same ID but different properties)
        foreach (var personsWs in personsFileWorkshops)
        {
            if (mainWorkshopMap.TryGetValue(personsWs.Id, out var mainWs))
            {
                // Compare relevant properties (Name, Type, Capacity, MinCapacity)
                if (mainWs.Name != personsWs.Name ||
                    mainWs.Type != personsWs.Type ||
                    mainWs.Capacity != personsWs.Capacity ||
                    mainWs.MinCapacity != personsWs.MinCapacity)
                {
                    warnings.Add(new ImportWarning(
                        WarningCategory.WorkshopDataMismatch,
                        $"Workshop {personsWs.Id} in {personsWs.SourceFile} differs from main workshops file"));
                }
            }
        }

        // Check for extra workshops in persons file (not in main)
        var extraWorkshopIds = personsFileWorkshops
            .Where(w => !mainWorkshopMap.ContainsKey(w.Id))
            .Select(w => w.Id)
            .ToList();

        if (extraWorkshopIds.Count > 0)
        {
            var sourceFile = personsFileWorkshops.FirstOrDefault()?.SourceFile ?? "persons file";
            warnings.Add(new ImportWarning(
                WarningCategory.ExtraWorkshopIgnored,
                $"Workshops not in main file (ignored): {string.Join(", ", extraWorkshopIds)}"));
        }

        // Check for missing workshops in persons file
        var missingWorkshopIds = mainWorkshops
            .Where(w => !personsWorkshopMap.ContainsKey(w.Id))
            .Select(w => w.Id)
            .ToList();

        if (missingWorkshopIds.Count > 0)
        {
            var sourceFile = personsFileWorkshops.FirstOrDefault()?.SourceFile ?? "persons file";
            warnings.Add(new ImportWarning(
                WarningCategory.MissingWorkshops,
                $"File {sourceFile} is missing workshops: {string.Join(", ", missingWorkshopIds)}"));
        }

        return warnings;
    }
}
