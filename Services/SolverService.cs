using Google.OrTools.Sat;
using WorkshopAssignment.Models;

namespace WorkshopAssignment.Services;

public class SolverService
{
    private const int WeightAssigned = 1000; // Assignment priority dominates rank bonuses

    public AssignmentResult Solve(AssignmentInput input, int timeLimitSeconds = 60)
    {
        var model = new CpModel();
        var warnings = new List<string>();

        // --- Step 1: Setup ---
        // Build workshop lookup for type checks and capacity
        var workshopLookup = input.Workshops.ToDictionary(w => w.Id, w => w);
        var validWorkshopIds = new HashSet<string>(workshopLookup.Keys);

        // Person lookup for individual preference resolution during solution extraction
        var personLookup = input.Persons.ToDictionary(p => p.Id, p => p);

        // --- Step 2: Build preference rank lookups and generate combos per group ---
        // comboVars[g] = list of BoolVars for group g's valid combos
        // comboInfo[g] = parallel list of (workshopIds, score) for each combo
        // comboType4Slots[g] = parallel list of Type4 slot assignments per combo
        //   Each entry: dictionary of workshopId -> slot number (2 or 3) for Type4 workshops in that combo
        var comboVars = new Dictionary<int, List<BoolVar>>();
        var comboInfo = new Dictionary<int, List<(List<string> WorkshopIds, int Score)>>();
        var comboType4Slots = new Dictionary<int, List<Dictionary<string, int>>>();

        for (int g = 0; g < input.FriendGroups.Count; g++)
        {
            var group = input.FriendGroups[g];
            var preferences = group.PooledPreferences;

            // Filter preferences to valid workshop IDs, warn about phantoms
            var validPrefs = new List<string>();
            var phantomIds = new List<string>();
            foreach (var prefId in preferences)
            {
                if (validWorkshopIds.Contains(prefId))
                    validPrefs.Add(prefId);
                else
                    phantomIds.Add(prefId);
            }

            if (phantomIds.Count > 0)
            {
                var memberIds = string.Join(", ", group.MemberIds);
                warnings.Add(
                    $"Group [{memberIds}] preferences reference non-existent workshop(s): " +
                    $"{string.Join(", ", phantomIds)} — removed from consideration");
            }

            // Build preference rank: workshopId → 1-indexed rank (1 = most preferred)
            var prefRank = new Dictionary<string, int>();
            for (int i = 0; i < validPrefs.Count; i++)
            {
                prefRank[validPrefs[i]] = i + 1; // 1-indexed
            }

            // --- Step 3: Generate all valid combos ---
            var groupCombos = new List<(List<string> WorkshopIds, int Score)>();
            var groupType4Slots = new List<Dictionary<string, int>>();

            // Type1 combos: standalone full-day workshops
            foreach (var wsId in validPrefs)
            {
                if (workshopLookup[wsId].Type == WorkshopType.Type1)
                {
                    int rank = prefRank[wsId];
                    int score = (7 - rank) * 2; // Type1 score doubled
                    groupCombos.Add((new List<string> { wsId }, score));
                    groupType4Slots.Add(new Dictionary<string, int>()); // no Type4 in Type1 combo
                }
            }

            // Pair combos: two compatible half-day workshops
            for (int i = 0; i < validPrefs.Count; i++)
            {
                for (int j = i + 1; j < validPrefs.Count; j++)
                {
                    var wsI = workshopLookup[validPrefs[i]];
                    var wsJ = workshopLookup[validPrefs[j]];

                    if (IsCompatiblePair(wsI, wsJ))
                    {
                        int rankI = prefRank[validPrefs[i]];
                        int rankJ = prefRank[validPrefs[j]];
                        int score = (7 - rankI) + (7 - rankJ);
                        groupCombos.Add((new List<string> { validPrefs[i], validPrefs[j] }, score));

                        // Determine Type4 slot assignments for this combo
                        var slotMap = DetermineType4Slots(wsI, wsJ);
                        groupType4Slots.Add(slotMap);
                    }
                }
            }

            // Single half-day combos (last resort — person gets one workshop, other half-day empty)
            // Score is NOT doubled (unlike Type1 which fills the full day)
            // The solver will always prefer pairs and Type1 over half-day solos because:
            //   - Assignment weight (1000) is the same → solver still prefers assigning over not
            //   - Pair/Type1 have higher rank bonus (up to 11-12) than half-day solo (up to 6)
            //   - So the solver only picks half-day solo when no pair/Type1 is feasible
            foreach (var wsId in validPrefs)
            {
                var ws = workshopLookup[wsId];
                if (ws.Type != WorkshopType.Type1) // Type2, Type3, or Type4 alone
                {
                    int rank = prefRank[wsId];
                    int score = 7 - rank; // NOT doubled — only half a day
                    groupCombos.Add((new List<string> { wsId }, score));

                    // Type4 solo: default to Slot 2 (matching SlotPlacementHelper convention)
                    // Type2/Type3 solo: no Type4 slot entry needed
                    var soloSlotMap = new Dictionary<string, int>();
                    if (ws.Type == WorkshopType.Type4)
                    {
                        soloSlotMap[wsId] = 2; // Default slot for solo Type4
                    }
                    groupType4Slots.Add(soloSlotMap);
                }
            }

            if (groupCombos.Count == 0 && validPrefs.Count > 0)
            {
                var memberIds = string.Join(", ", group.MemberIds);
                warnings.Add(
                    $"Group [{memberIds}] has {validPrefs.Count} valid preferences but no valid assignment combos " +
                    $"(no Type1 workshops and no compatible half-day pairs found)");
            }

            // Create BoolVars for each combo
            if (groupCombos.Count > 0)
            {
                var vars = new List<BoolVar>();
                for (int c = 0; c < groupCombos.Count; c++)
                {
                    vars.Add(model.NewBoolVar($"combo_g{g}_c{c}"));
                }
                comboVars[g] = vars;
                comboInfo[g] = groupCombos;
                comboType4Slots[g] = groupType4Slots;

                // --- Step 4: At-most-one constraint per group ---
                model.Add(LinearExpr.Sum(vars) <= 1);
            }
        }

        // --- Step 5: Capacity constraints ---
        // Workshop runs tracking
        var workshopRuns = new Dictionary<string, BoolVar>();
        foreach (var ws in input.Workshops)
        {
            workshopRuns[ws.Id] = model.NewBoolVar($"runs_{ws.Id}");
        }

        // Per-slot run tracking for Type4 workshops (for per-slot min capacity)
        var workshopRunsSlot2 = new Dictionary<string, BoolVar>();
        var workshopRunsSlot3 = new Dictionary<string, BoolVar>();

        foreach (var ws in input.Workshops)
        {
            bool isType4 = ws.Type == WorkshopType.Type4;

            if (isType4)
            {
                // --- Type4: Per-slot occupancy and constraints ---
                var slot2Occupancy = new List<(BoolVar Var, int Coeff)>();
                var slot3Occupancy = new List<(BoolVar Var, int Coeff)>();

                for (int g = 0; g < input.FriendGroups.Count; g++)
                {
                    if (!comboVars.ContainsKey(g)) continue;

                    var group = input.FriendGroups[g];
                    int groupSize = group.MemberIds.Count;

                    for (int c = 0; c < comboInfo[g].Count; c++)
                    {
                        if (!comboInfo[g][c].WorkshopIds.Contains(ws.Id)) continue;

                        // Check which slot this Type4 workshop occupies in this combo
                        var slotMap = comboType4Slots[g][c];
                        if (slotMap.TryGetValue(ws.Id, out int slot))
                        {
                            if (slot == 2)
                                slot2Occupancy.Add((comboVars[g][c], groupSize));
                            else // slot == 3
                                slot3Occupancy.Add((comboVars[g][c], groupSize));
                        }
                        // else: workshop is in the combo but isn't Type4 (shouldn't happen here)
                    }
                }

                // Build per-slot linear expressions
                var hasSlot2 = slot2Occupancy.Count > 0;
                var hasSlot3 = slot3Occupancy.Count > 0;

                LinearExpr? slot2Expr = hasSlot2
                    ? LinearExpr.WeightedSum(
                        slot2Occupancy.Select(x => x.Var).ToArray(),
                        slot2Occupancy.Select(x => (long)x.Coeff).ToArray())
                    : null;

                LinearExpr? slot3Expr = hasSlot3
                    ? LinearExpr.WeightedSum(
                        slot3Occupancy.Select(x => x.Var).ToArray(),
                        slot3Occupancy.Select(x => (long)x.Coeff).ToArray())
                    : null;

                // Per-slot max capacity: each slot independently capped at ws.Capacity
                if (slot2Expr != null)
                    model.Add(slot2Expr <= ws.Capacity);
                if (slot3Expr != null)
                    model.Add(slot3Expr <= ws.Capacity);

                // Per-slot min capacity enforcement for Type4
                if (ws.MinCapacity > 0)
                {
                    // Slot 2 session
                    var runsSlot2 = model.NewBoolVar($"runs_s2_{ws.Id}");
                    workshopRunsSlot2[ws.Id] = runsSlot2;

                    if (slot2Expr != null)
                    {
                        model.Add(slot2Expr >= ws.MinCapacity).OnlyEnforceIf(runsSlot2);
                        model.Add(slot2Expr == 0).OnlyEnforceIf(runsSlot2.Not());
                    }
                    else
                    {
                        // No combos place this Type4 in Slot 2, so it can't run there
                        model.Add(runsSlot2 == 0);
                    }

                    // Slot 3 session
                    var runsSlot3 = model.NewBoolVar($"runs_s3_{ws.Id}");
                    workshopRunsSlot3[ws.Id] = runsSlot3;

                    if (slot3Expr != null)
                    {
                        model.Add(slot3Expr >= ws.MinCapacity).OnlyEnforceIf(runsSlot3);
                        model.Add(slot3Expr == 0).OnlyEnforceIf(runsSlot3.Not());
                    }
                    else
                    {
                        // No combos place this Type4 in Slot 3, so it can't run there
                        model.Add(runsSlot3 == 0);
                    }

                    // Overall: workshop runs if EITHER slot runs
                    // workshopRuns[ws.Id] = runsSlot2 OR runsSlot3
                    model.AddMaxEquality(workshopRuns[ws.Id],
                        new[] { runsSlot2, runsSlot3 });
                }
                else
                {
                    // No min capacity — workshop runs if anyone is assigned in either slot
                    var allSlotVars = slot2Occupancy.Select(x => x.Var)
                        .Concat(slot3Occupancy.Select(x => x.Var))
                        .ToArray();

                    if (allSlotVars.Length > 0)
                    {
                        model.AddMaxEquality(workshopRuns[ws.Id], allSlotVars);
                    }
                }
            }
            else
            {
                // --- Non-Type4: Original single-occupancy constraints ---
                var occupancy = new List<(BoolVar Var, int Coeff)>();

                for (int g = 0; g < input.FriendGroups.Count; g++)
                {
                    if (!comboVars.ContainsKey(g)) continue;

                    var group = input.FriendGroups[g];
                    int groupSize = group.MemberIds.Count;

                    for (int c = 0; c < comboInfo[g].Count; c++)
                    {
                        if (comboInfo[g][c].WorkshopIds.Contains(ws.Id))
                        {
                            occupancy.Add((comboVars[g][c], groupSize));
                        }
                    }
                }

                if (occupancy.Count > 0)
                {
                    var occupancyExpr = LinearExpr.WeightedSum(
                        occupancy.Select(x => x.Var).ToArray(),
                        occupancy.Select(x => (long)x.Coeff).ToArray()
                    );

                    // Max capacity
                    model.Add(occupancyExpr <= ws.Capacity);

                    // MinCapacity constraints
                    if (ws.MinCapacity > 0)
                    {
                        model.Add(occupancyExpr >= ws.MinCapacity).OnlyEnforceIf(workshopRuns[ws.Id]);
                        model.Add(occupancyExpr == 0).OnlyEnforceIf(workshopRuns[ws.Id].Not());
                    }
                    else
                    {
                        // No minimum — workshop runs if anyone is assigned
                        model.AddMaxEquality(workshopRuns[ws.Id], occupancy.Select(x => x.Var).ToArray());
                    }
                }
            }
        }

        // --- Step 7: Objective function ---
        // Maximize: sum over all groups of comboVar * (WeightAssigned * groupSize + score * groupSize)
        // WeightAssigned (1000) >> max score per person (12), so assignment always dominates
        var objectiveTerms = new List<(BoolVar Var, long Coeff)>();

        for (int g = 0; g < input.FriendGroups.Count; g++)
        {
            if (!comboVars.ContainsKey(g)) continue;

            int groupSize = input.FriendGroups[g].MemberIds.Count;

            for (int c = 0; c < comboInfo[g].Count; c++)
            {
                int comboScore = comboInfo[g][c].Score;
                long coeff = (long)(WeightAssigned * groupSize) + (long)(comboScore * groupSize);
                objectiveTerms.Add((comboVars[g][c], coeff));
            }
        }

        model.Maximize(LinearExpr.WeightedSum(
            objectiveTerms.Select(x => x.Var).ToArray(),
            objectiveTerms.Select(x => x.Coeff).ToArray()
        ));

        // --- Step 8: Solve ---
        var solver = new CpSolver();
        solver.StringParameters = $"max_time_in_seconds:{timeLimitSeconds},num_workers:8,random_seed:42";
        var status = solver.Solve(model);

        // --- Step 9: Extract solution ---
        var assignments = new List<Assignment>();
        var assignedPersons = new HashSet<string>();

        if (status == CpSolverStatus.Optimal || status == CpSolverStatus.Feasible)
        {
            for (int g = 0; g < input.FriendGroups.Count; g++)
            {
                var group = input.FriendGroups[g];
                List<string>? selectedWorkshops = null;
                int selectedScore = 0;

                // Find which combo was selected (if any)
                if (comboVars.ContainsKey(g))
                {
                    for (int c = 0; c < comboVars[g].Count; c++)
                    {
                        if (solver.Value(comboVars[g][c]) == 1)
                        {
                            selectedWorkshops = comboInfo[g][c].WorkshopIds;
                            selectedScore = comboInfo[g][c].Score;
                            break;
                        }
                    }
                }

                // Create per-person assignments with individual preference ranks
                foreach (var memberId in group.MemberIds)
                {
                    if (selectedWorkshops != null)
                    {
                        var person = personLookup[memberId];
                        bool isType1 = selectedWorkshops.Count == 1
                            && workshopLookup[selectedWorkshops[0]].Type == WorkshopType.Type1;

                        // Build per-person preference ranks for each assigned workshop
                        var preferenceRanks = new List<int>();
                        int? bestRank = null;

                        foreach (var wsId in selectedWorkshops)
                        {
                            int personRank = person.Preferences.IndexOf(wsId) + 1; // 1-indexed
                            if (personRank == 0)
                            {
                                // Workshop not in this person's preferences (came from group pooling)
                                // Use the pooled position as fallback — count from group's PooledPreferences
                                personRank = group.PooledPreferences.IndexOf(wsId) + 1;
                            }
                            preferenceRanks.Add(personRank);

                            if (!bestRank.HasValue || personRank < bestRank.Value)
                                bestRank = personRank;
                        }

                        // Satisfaction score: sum of (7 - rank) per workshop
                        // Type1 (full day) gets doubled score; half-day solos do NOT
                        int satisfactionScore;
                        if (isType1)
                        {
                            satisfactionScore = (7 - preferenceRanks[0]) * 2;
                        }
                        else
                        {
                            satisfactionScore = preferenceRanks.Sum(r => 7 - r);
                        }

                        assignments.Add(new Assignment(
                            memberId,
                            selectedWorkshops,
                            bestRank,
                            preferenceRanks,
                            satisfactionScore,
                            isType1 // IsFullDay: true only for Type1 solo assignments
                        ));
                    }
                    else
                    {
                        // Unassigned
                        assignments.Add(new Assignment(
                            memberId,
                            new List<string>(),
                            null,
                            null,
                            null
                        ));
                    }
                    assignedPersons.Add(memberId);
                }
            }
        }

        // Add persons not in any group (safety net)
        foreach (var person in input.Persons)
        {
            if (!assignedPersons.Contains(person.Id))
            {
                assignments.Add(new Assignment(person.Id, new List<string>(), null, null, null));
            }
        }

        // --- Step 10: Find cancelled workshops and per-slot cancellations ---
        var cancelledWorkshops = new List<string>();
        if (status == CpSolverStatus.Optimal || status == CpSolverStatus.Feasible)
        {
            foreach (var ws in input.Workshops)
            {
                if (workshopRuns.ContainsKey(ws.Id) && solver.Value(workshopRuns[ws.Id]) == 0)
                {
                    cancelledWorkshops.Add(ws.Id);
                }

                // Per-slot cancellation warnings for Type4 workshops
                if (ws.Type == WorkshopType.Type4 && ws.MinCapacity > 0)
                {
                    bool slot2Runs = workshopRunsSlot2.ContainsKey(ws.Id)
                        && solver.Value(workshopRunsSlot2[ws.Id]) == 1;
                    bool slot3Runs = workshopRunsSlot3.ContainsKey(ws.Id)
                        && solver.Value(workshopRunsSlot3[ws.Id]) == 1;

                    if (slot2Runs && !slot3Runs)
                    {
                        warnings.Add(
                            $"Workshop '{ws.Name}' ({ws.Id}) Slot 3 session cancelled — " +
                            $"below MinCapacity of {ws.MinCapacity}. Slot 2 session runs normally.");
                    }
                    else if (!slot2Runs && slot3Runs)
                    {
                        warnings.Add(
                            $"Workshop '{ws.Name}' ({ws.Id}) Slot 2 session cancelled — " +
                            $"below MinCapacity of {ws.MinCapacity}. Slot 3 session runs normally.");
                    }
                }
            }
        }

        return new AssignmentResult(assignments, cancelledWorkshops) { Warnings = warnings };
    }

    /// <summary>
    /// Determines which slot each Type4 workshop occupies in a given pair combo.
    /// Returns a dictionary mapping Type4 workshop IDs to their slot number (2 or 3).
    ///
    /// Slot assignment rules:
    /// - Type4 paired with Type2: Type4 goes to Slot 3 (Type2 occupies Slot 2)
    /// - Type4 paired with Type3: Type4 goes to Slot 2 (Type3 occupies Slot 3)
    /// - Type4 paired with Type4: first in pair → Slot 2, second → Slot 3
    /// </summary>
    private static Dictionary<string, int> DetermineType4Slots(Workshop a, Workshop b)
    {
        var slots = new Dictionary<string, int>();

        if (a.Type == WorkshopType.Type4 && b.Type == WorkshopType.Type4)
        {
            // Both Type4: first → Slot 2, second → Slot 3
            slots[a.Id] = 2;
            slots[b.Id] = 3;
        }
        else if (a.Type == WorkshopType.Type4)
        {
            // a is Type4, b is Type2 or Type3
            slots[a.Id] = b.Type == WorkshopType.Type2 ? 3 : 2;
        }
        else if (b.Type == WorkshopType.Type4)
        {
            // b is Type4, a is Type2 or Type3
            slots[b.Id] = a.Type == WorkshopType.Type2 ? 3 : 2;
        }
        // else: neither is Type4 — no entries needed

        return slots;
    }

    /// <summary>
    /// Determines if two workshops can be paired for a half-day assignment.
    /// Valid combinations: Type2+Type3, Type2+Type4, Type3+Type4, Type4+Type4 (different IDs).
    /// Type1 is full-day and cannot pair. Same-type pairs only valid for Type4 (flexible slot).
    /// </summary>
    private static bool IsCompatiblePair(Workshop a, Workshop b)
    {
        // Type1 cannot be part of any pair
        if (a.Type == WorkshopType.Type1 || b.Type == WorkshopType.Type1)
            return false;

        // Same workshop ID cannot pair with itself
        if (a.Id == b.Id)
            return false;

        var types = new[] { a.Type, b.Type }.OrderBy(t => t).ToArray();

        // Valid pairs: (T2,T3), (T2,T4), (T3,T4), (T4,T4)
        return types switch
        {
            [WorkshopType.Type2, WorkshopType.Type3] => true,
            [WorkshopType.Type2, WorkshopType.Type4] => true,
            [WorkshopType.Type3, WorkshopType.Type4] => true,
            [WorkshopType.Type4, WorkshopType.Type4] => true,
            _ => false,
        };
    }

}
