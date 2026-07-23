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
        // Maximum members allowed in one friend group. The solver assigns each group
        // atomically to a single workshop combo, so larger groups sharply reduce the
        // set of feasible solutions (business rule 03ad9bd4). A connected friendship
        // component bigger than this is split into subgroups below.
        const int MaxGroupSize = 3;

        var personMap = persons.ToDictionary(p => p.Id);

        // Build an UNDIRECTED friendship graph. A person's FriendId is treated as an
        // edge (person <-> person.FriendId) REGARDLESS of reciprocity: if either party
        // names the other, they belong in the same group. Grouping is then simply the
        // connected components of that graph — the intended semantics per business rule
        // 03ad9bd4 ("A->B->C becomes group [A,B,C]; circular references are valid and
        // resolved").
        //
        // ROOT-CAUSE FIX: the previous implementation required DIRECT pairwise reciprocity
        // (friend.FriendId == person.Id) and forced every "non-mutual" person to a solo
        // group. Because the data model stores a SINGLE FriendId per person, three mutual
        // friends can only be expressed as a CYCLE (P1->P2->P3->P1) in which no two people
        // reciprocate directly. The old code therefore flagged all three as non-mutual,
        // emitted each as a solo group, and the solver then placed them in different
        // workshops — splitting real friend groups apart. Treating FriendId as an
        // undirected edge makes that cycle one connected component that resolves to a
        // single group of 3, which is exactly the reported bug's correct outcome.
        var adjacency = new Dictionary<string, HashSet<string>>();
        foreach (var person in persons)
        {
            adjacency[person.Id] = new HashSet<string>();
        }

        foreach (var person in persons)
        {
            if (person.FriendId == null)
                continue;

            // Self-reference (A->A) is a no-op edge: the person simply stays solo.
            // SelfReference warnings are ExcelService's responsibility at import time,
            // not ours — emitting one here would duplicate that warning.
            if (person.FriendId == person.Id)
                continue;

            if (personMap.ContainsKey(person.FriendId))
            {
                // Undirected edge — reciprocity is NOT required.
                adjacency[person.Id].Add(person.FriendId);
                adjacency[person.FriendId].Add(person.Id);
            }
            else
            {
                // The referenced friend is not in the assignable set: either a typo, or the
                // friend was dropped earlier by preference filtering. No edge can form, so
                // the person stays solo and we surface the broken reference.
                warnings.Add(new ImportWarning(WarningCategory.InvalidFriend,
                    $"Person {person.Id} references non-existent friend {person.FriendId}"));
            }
        }

        var visited = new HashSet<string>();
        var groups = new List<List<string>>();

        // Discover connected components in stable input order so the emitted group order is
        // deterministic and preserves the run-after-preference-filtering ordering that
        // BuildAssignmentInput relies on.
        foreach (var person in persons)
        {
            if (!visited.Add(person.Id))
                continue;

            // BFS the undirected component reachable from this person.
            var component = new List<string> { person.Id };
            var queue = new Queue<string>();
            queue.Enqueue(person.Id);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbor in adjacency[current])
                {
                    if (visited.Add(neighbor))
                    {
                        component.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }

            // Deterministic, stable member ordering by person Id (ordinal). This makes the
            // subgroup membership reproducible when a component has to be split, so tests
            // and real imports get the same explainable result on every run.
            component.Sort(StringComparer.Ordinal);

            if (component.Count <= MaxGroupSize)
            {
                groups.Add(component);
            }
            else
            {
                // The social group is larger than a single group can hold. The solver places
                // each group atomically, so we cannot keep everyone together — split into
                // consecutive subgroups of up to MaxGroupSize in stable Id order (first 3
                // stay together, the remainder forms the next subgroup(s), per business rule
                // 03ad9bd4) and warn that the desired grouping had to be broken up.
                warnings.Add(new ImportWarning(WarningCategory.OversizedGroup,
                    $"Friend group with {component.Count} members [{string.Join(",", component)}] " +
                    $"exceeds max of {MaxGroupSize}; split into subgroups of up to {MaxGroupSize} " +
                    $"(ordered by person Id)"));

                for (int i = 0; i < component.Count; i += MaxGroupSize)
                {
                    groups.Add(component.GetRange(i, Math.Min(MaxGroupSize, component.Count - i)));
                }
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
