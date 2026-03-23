using WorkshopAssignment.Models;
using WorkshopAssignment.Services;
using Xunit;

namespace WorkshopAssignment.Tests;

public class SolverServiceTests
{
    private readonly SolverService _svc = new();

    // ── Helpers ────────────────────────────────────────────────

    /// <summary>
    /// Creates a solo FriendGroup for a single person (no friends).
    /// PooledPreferences mirrors the person's own Preferences.
    /// </summary>
    private static FriendGroup Solo(Person p) =>
        new(new List<string> { p.Id }, p.Preferences);

    /// <summary>
    /// Creates a FriendGroup for two friends with pooled preferences.
    /// Union of both members' preference lists, deduplicated keeping
    /// best (lowest index) rank across both lists.
    /// </summary>
    private static FriendGroup Pair(Person a, Person b)
    {
        var rankLookup = new Dictionary<string, int>();
        foreach (var pref in new[] { a.Preferences, b.Preferences })
        {
            for (int i = 0; i < pref.Count; i++)
            {
                int rank = i + 1;
                if (!rankLookup.ContainsKey(pref[i]) || rank < rankLookup[pref[i]])
                    rankLookup[pref[i]] = rank;
            }
        }
        var pooled = rankLookup.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
        return new FriendGroup(new List<string> { a.Id, b.Id }, pooled);
    }

    /// <summary>
    /// Creates a FriendGroup for three friends with pooled preferences.
    /// Same deduplication logic as Pair but across three members.
    /// </summary>
    private static FriendGroup Trio(Person a, Person b, Person c)
    {
        var rankLookup = new Dictionary<string, int>();
        foreach (var pref in new[] { a.Preferences, b.Preferences, c.Preferences })
        {
            for (int i = 0; i < pref.Count; i++)
            {
                int rank = i + 1;
                if (!rankLookup.ContainsKey(pref[i]) || rank < rankLookup[pref[i]])
                    rankLookup[pref[i]] = rank;
            }
        }
        var pooled = rankLookup.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
        return new FriendGroup(new List<string> { a.Id, b.Id, c.Id }, pooled);
    }

    /// <summary>
    /// Shorthand for creating a ranked preference list from workshop IDs.
    /// Index 0 = rank 1 (most wanted), index 5 = rank 6 (least wanted).
    /// </summary>
    private static List<string> Prefs(params string[] ids) => ids.ToList();

    // ── 1. Small Input — All Assigned ──────────────────────────

    [Fact]
    public void Solve_SmallInput_AllAssigned()
    {
        // 5 persons, 6 workshops with ample capacity → everyone assigned
        var workshops = new List<Workshop>
        {
            new("W1", "Yoga", WorkshopType.Type1, 20),
            new("W2", "Painting", WorkshopType.Type2, 20),
            new("W3", "Cooking", WorkshopType.Type3, 20),
            new("W4", "Music", WorkshopType.Type4, 20),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        var persons = new List<Person>
        {
            new("P1", "Alice", Prefs("W2", "W3", "W5", "W6", "W4", "W1")),
            new("P2", "Bob",   Prefs("W5", "W6", "W2", "W3", "W4", "W1")),
            new("P3", "Carol", Prefs("W5", "W3", "W2", "W6", "W1", "W4")),
            new("P4", "Dave",  Prefs("W2", "W4", "W5", "W3", "W6", "W1")),
            new("P5", "Eve",   Prefs("W1", "W2", "W3", "W5", "W6", "W4")),
        };

        var groups = persons.Select(Solo).ToList();

        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        Assert.Equal(5, result.TotalPersons);
        Assert.Equal(5, result.AssignedCount);
        Assert.Equal(0, result.UnassignedCount);
        Assert.Equal(100, result.AssignmentRate);

        // Verify new model properties exist on assigned persons
        foreach (var a in result.Assignments.Where(a => a.WorkshopIds.Count > 0))
        {
            Assert.NotNull(a.PreferenceRanks);
            Assert.NotNull(a.SatisfactionScore);
            Assert.True(a.SatisfactionScore > 0, "Assigned person should have positive satisfaction");
            Assert.True(a.WishRank >= 1 && a.WishRank <= 6, $"WishRank should be 1-6, got {a.WishRank}");
        }
    }

    // ── 2. Capacity Constraint Holds ───────────────────────────

    [Fact]
    public void Solve_CapacityConstraint_Holds()
    {
        // Workshop W2 has capacity=2, 5 persons list it as top preference → at most 2 assigned to W2
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 2),  // tight capacity
            new("W3", "Cooking", WorkshopType.Type3, 20),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        var persons = new List<Person>();
        for (int i = 1; i <= 5; i++)
        {
            persons.Add(new($"P{i}", $"Person{i}",
                Prefs("W2", "W3", "W5", "W6", "W2", "W5")));
            // Rank 1=W2, fallbacks: W5+W6 pair, W5+W3 pair
        }

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // Count how many persons are assigned to W2
        int w2Count = result.Assignments
            .Count(a => a.WorkshopIds.Contains("W2"));

        Assert.True(w2Count <= 2, $"W2 capacity is 2 but {w2Count} persons assigned");
    }

    // ── 3. Friend Group Assigned Together ──────────────────────

    [Fact]
    public void Solve_FriendGroupAssignedTogether()
    {
        // P1 and P2 are friends → must get same assignment (same workshop set)
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20),
            new("W3", "Cooking", WorkshopType.Type3, 20),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        var p1 = new Person("P1", "Alice", Prefs("W2", "W3", "W5", "W6", "W2", "W6"), FriendId: "P2");
        var p2 = new Person("P2", "Bob",   Prefs("W2", "W3", "W5", "W6", "W5", "W3"), FriendId: "P1");
        var p3 = new Person("P3", "Carol", Prefs("W5", "W6", "W2", "W3", "W5", "W3"));

        var groups = new List<FriendGroup>
        {
            Pair(p1, p2),
            Solo(p3),
        };

        var input = new AssignmentInput(workshops, new List<Person> { p1, p2, p3 }, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        var a1 = result.Assignments.First(a => a.PersonId == "P1");
        var a2 = result.Assignments.First(a => a.PersonId == "P2");

        // Both must have the same workshop assignments (same combo from pooled preferences)
        Assert.Equal(a1.WorkshopIds, a2.WorkshopIds);
        // Both should be assigned (plenty of capacity)
        Assert.True(a1.WorkshopIds.Count > 0, "P1 should be assigned");
        Assert.True(a2.WorkshopIds.Count > 0, "P2 should be assigned");
    }

    // ── 4. Wish Ranking Prefers First ──────────────────────────

    [Fact]
    public void Solve_WishRanking_PrefersFirst()
    {
        // With ample capacity, solver should prefer top-ranked preferences
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 50),
            new("W3", "Cooking", WorkshopType.Type3, 50),
            new("W5", "Dance", WorkshopType.Type2, 50),
            new("W6", "Drama", WorkshopType.Type3, 50),
        };

        var persons = new List<Person>();
        for (int i = 1; i <= 8; i++)
        {
            // Everyone's top 2 preferences form the best combo (W2+W3, rank 1+2, score=6+5=11)
            persons.Add(new($"P{i}", $"Person{i}",
                Prefs("W2", "W3", "W5", "W6", "W5", "W3")));
        }

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // With ample capacity, most/all should get a combo using their rank 1 preference
        int highSatisfaction = result.Assignments
            .Count(a => a.SatisfactionScore.HasValue && a.SatisfactionScore >= 9);
        Assert.True(highSatisfaction >= 6,
            $"Expected most persons to get high satisfaction (>=9), but only {highSatisfaction}/8 did. " +
            $"Scores: {string.Join(", ", result.Assignments.Select(a => $"{a.PersonId}={a.SatisfactionScore}"))}");
    }

    // ── 5. Min Capacity Cancellation ───────────────────────────

    [Fact]
    public void Solve_MinCapacityCancellation()
    {
        // W2 has minCapacity=10 but only 2 persons want it → should be cancelled
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20, MinCapacity: 10),
            new("W3", "Cooking", WorkshopType.Type3, 20),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        // Only 2 persons, both have W2 as top preference
        var persons = new List<Person>
        {
            new("P1", "Alice", Prefs("W2", "W3", "W5", "W6", "W5", "W3")),
            new("P2", "Bob",   Prefs("W2", "W3", "W5", "W6", "W5", "W3")),
        };

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // W2 needs 10 people minimum but only 2 exist → W2 must be cancelled
        Assert.Contains("W2", result.CancelledWorkshopIds);

        // No person should be assigned to W2
        Assert.True(result.Assignments.All(a => !a.WorkshopIds.Contains("W2")),
            "No person should be assigned to a cancelled workshop");
    }

    // ── 6. Timeout Behavior ────────────────────────────────────

    [Fact]
    public void Solve_TimeoutBehavior()
    {
        // Short timeout should still return a result
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20),
            new("W3", "Cooking", WorkshopType.Type3, 20),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        var persons = new List<Person>
        {
            new("P1", "Alice", Prefs("W2", "W3", "W5", "W6", "W5", "W3")),
            new("P2", "Bob",   Prefs("W5", "W6", "W2", "W3", "W5", "W3")),
            new("P3", "Carol", Prefs("W2", "W6", "W5", "W3", "W2", "W3")),
        };

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);

        var result = _svc.Solve(input, timeLimitSeconds: 1);

        // Should return a result (not null, not exception)
        Assert.NotNull(result);
        Assert.NotNull(result.Assignments);
        Assert.Equal(3, result.TotalPersons);
    }

    // ── 7. Empty Input — No Workshops ──────────────────────────────────

    [Fact]
    public void Solve_EmptyInput_NoWorkshops()
    {
        var input = new AssignmentInput(
            new List<Workshop>(),
            new List<Person>(),
            new List<FriendGroup>()
        );

        var result = _svc.Solve(input, timeLimitSeconds: 5);

        Assert.NotNull(result);
        Assert.Empty(result.Assignments);
        Assert.Empty(result.CancelledWorkshopIds);
    }

    // ── 8. Empty Input — No Persons ────────────────────────────────────

    [Fact]
    public void Solve_EmptyInput_NoPersons()
    {
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20),
            new("W3", "Cooking", WorkshopType.Type3, 20),
        };

        var input = new AssignmentInput(
            workshops,
            new List<Person>(),
            new List<FriendGroup>()
        );

        var result = _svc.Solve(input, timeLimitSeconds: 5);

        Assert.NotNull(result);
        Assert.Empty(result.Assignments);
    }

    // ── 9. Phantom Workshop Validation — All Unassigned ────────

    [Fact]
    public void Solve_PhantomWorkshopIds_AllUnassignedWithWarnings()
    {
        // Persons have preferences referencing workshops not in the input list.
        // The solver must filter these phantom preferences and leave persons unassigned.
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20),
            new("W3", "Cooking", WorkshopType.Type3, 20),
        };

        // All preferences reference W99/W98/W97 which don't exist in workshops
        var persons = new List<Person>
        {
            new("P1", "Alice", Prefs("W99", "W98", "W97", "W99", "W98", "W97")),
            new("P2", "Bob",   Prefs("W99", "W98", "W97", "W99", "W98", "W97")),
        };

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        Assert.NotNull(result);
        Assert.Equal(2, result.TotalPersons);

        // With phantom workshop validation, all preferences are filtered out because they
        // reference non-existent workshop IDs. Both persons become unassigned.
        Assert.Equal(0, result.AssignedCount);
        Assert.Equal(2, result.UnassignedCount);

        // Solver must surface warnings about the phantom workshop IDs
        Assert.NotEmpty(result.Warnings);
        Assert.All(result.Warnings, w => Assert.Contains("non-existent workshop", w));
    }

    // ── 9b. Mixed Phantom and Valid Preferences ────────────────

    [Fact]
    public void Solve_MixedPhantomAndValidPreferences_AssignsValidOnly()
    {
        // P1 has some valid preferences (W2, W3) and some phantom (W99, W98, W97).
        // P2 has only phantom preferences — should be unassigned.
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20),
            new("W3", "Cooking", WorkshopType.Type3, 20),
        };

        var persons = new List<Person>
        {
            new("P1", "Alice", Prefs("W2", "W3", "W99", "W98", "W97", "W99")),
            new("P2", "Bob",   Prefs("W99", "W98", "W97", "W99", "W98", "W97")),
        };

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        Assert.NotNull(result);
        Assert.Equal(2, result.TotalPersons);

        // P1 has valid preferences (W2+W3 form a Type2+Type3 combo) and should be assigned
        Assert.Equal(1, result.AssignedCount);
        var p1Assignment = result.Assignments.First(a => a.PersonId == "P1");
        Assert.Contains("W2", p1Assignment.WorkshopIds);
        Assert.Contains("W3", p1Assignment.WorkshopIds);

        // P2 has only phantom preferences — should be unassigned
        var p2Assignment = result.Assignments.First(a => a.PersonId == "P2");
        Assert.Empty(p2Assignment.WorkshopIds);

        // Warnings generated for phantom preferences
        Assert.NotEmpty(result.Warnings);
        Assert.All(result.Warnings, w => Assert.Contains("non-existent workshop", w));
    }

    // ── 10. Type4 Flexible Slot ────────────────────────────────────────

    [Fact]
    public void Solve_Type4FlexibleSlot()
    {
        // Type4 workshop can fill either Slot 2 or Slot 3 (flexible pairing)
        // Verify Type4 can pair with Type2 or Type3 in the new combo model
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20),
            new("W3", "Cooking", WorkshopType.Type3, 20),
            new("W4", "Music", WorkshopType.Type4, 20),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        // P1 prefers W2+W4 (Type2+Type4) as top combo, P2 prefers W3+W4 (Type3+Type4)
        var persons = new List<Person>
        {
            new("P1", "Alice", Prefs("W2", "W4", "W5", "W3", "W6", "W5")),
            new("P2", "Bob",   Prefs("W3", "W4", "W2", "W6", "W5", "W6")),
            new("P3", "Carol", Prefs("W5", "W6", "W2", "W3", "W2", "W4")),
        };

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // All should be assigned — plenty of capacity
        Assert.Equal(3, result.AssignedCount);

        // P1's first wish includes W4 (Type4) — verify solver can assign it
        var a1 = result.Assignments.First(a => a.PersonId == "P1");
        Assert.True(a1.WorkshopIds.Count > 0, "P1 should be assigned");

        // P2's first wish includes W4 (Type4) — verify solver can assign it
        var a2 = result.Assignments.First(a => a.PersonId == "P2");
        Assert.True(a2.WorkshopIds.Count > 0, "P2 should be assigned");

        // Verify W4 can appear in assignments (Type4 fills flexible slot)
        bool anyHasW4 = result.Assignments.Any(a => a.WorkshopIds.Contains("W4"));
        Assert.True(anyHasW4, "At least one person should be assigned to Type4 workshop W4");
    }

    // ── 11. All Workshops Cancelled — All Unassigned ──────────────────────

    [Fact]
    public void Solve_AllWorkshopsCancelled_ReturnsUnassigned()
    {
        // All workshops have min_capacity that cannot be met
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20, MinCapacity: 100),
            new("W3", "Cooking", WorkshopType.Type3, 20, MinCapacity: 100),
            new("W5", "Dance", WorkshopType.Type2, 20, MinCapacity: 100),
            new("W6", "Drama", WorkshopType.Type3, 20, MinCapacity: 100),
        };

        var persons = new List<Person>
        {
            new("P1", "Alice", Prefs("W2", "W3", "W5", "W6", "W5", "W3")),
            new("P2", "Bob",   Prefs("W5", "W6", "W2", "W3", "W5", "W3")),
        };

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // All workshops should be cancelled (min capacity = 100, only 2 persons)
        Assert.Equal(4, result.CancelledWorkshopIds.Count);
        Assert.Contains("W2", result.CancelledWorkshopIds);
        Assert.Contains("W3", result.CancelledWorkshopIds);
        Assert.Contains("W5", result.CancelledWorkshopIds);
        Assert.Contains("W6", result.CancelledWorkshopIds);

        // All persons should be unassigned (no valid workshops left)
        Assert.Equal(2, result.UnassignedCount);
        Assert.True(result.Assignments.All(a => a.WorkshopIds.Count == 0),
            "All persons should be unassigned when all workshops are cancelled");
    }

    // ── 12. CanRunSolver — No Workshops ───────────────────────

    [Fact]
    public void CanRunSolver_NoWorkshops_ReturnsFalse()
    {
        var persons = new List<Person>
        {
            new("P1", "Alice", Prefs("W2", "W3", "W5", "W6", "W5", "W3")),
        };
        var groups = persons.Select(Solo).ToList();

        var input = new AssignmentInput(
            new List<Workshop>(),
            persons,
            groups
        );

        var result = new ImportResult(input, new List<ImportWarning>());

        Assert.False(result.CanRunSolver, "CanRunSolver should be false when no workshops");
    }

    // ── 13. CanRunSolver — No Persons ─────────────────────────────────────

    [Fact]
    public void CanRunSolver_NoPersons_ReturnsFalse()
    {
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20),
            new("W3", "Cooking", WorkshopType.Type3, 20),
        };

        var input = new AssignmentInput(
            workshops,
            new List<Person>(),
            new List<FriendGroup>()
        );

        var result = new ImportResult(input, new List<ImportWarning>());

        Assert.False(result.CanRunSolver, "CanRunSolver should be false when no persons");
    }

    // ── 14. CanRunSolver — Valid Data ─────────────────────────

    [Fact]
    public void CanRunSolver_ValidData_ReturnsTrue()
    {
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20),
            new("W3", "Cooking", WorkshopType.Type3, 20),
        };

        var persons = new List<Person>
        {
            new("P1", "Alice", Prefs("W2", "W3", "W2", "W3", "W2", "W3")),
        };

        var groups = persons.Select(Solo).ToList();

        var input = new AssignmentInput(workshops, persons, groups);
        var result = new ImportResult(input, new List<ImportWarning>());

        Assert.True(result.CanRunSolver, "CanRunSolver should be true with valid workshops, persons, and groups");
    }


    // ── 15. Type4+Type4 Placed In Different Slots ───────────

    [Fact]
    public void Solve_Type4Type4_PlacedInDifferentSlots()
    {
        // Two Type4 (flexible) workshops assigned to the same person must occupy
        // different slots. The solver cannot put both in the same slot.
        var workshops = new List<Workshop>
        {
            new("W4a", "Music A", WorkshopType.Type4, 20),
            new("W4b", "Music B", WorkshopType.Type4, 20),
            new("W4c", "Music C", WorkshopType.Type4, 20),
            new("W4d", "Music D", WorkshopType.Type4, 20),
        };

        var persons = new List<Person>
        {
            new("P1", "Alice", Prefs("W4a", "W4b", "W4c", "W4d", "W4a", "W4d")),
            new("P2", "Bob",   Prefs("W4c", "W4d", "W4a", "W4b", "W4b", "W4c")),
            new("P3", "Carol", Prefs("W4a", "W4d", "W4b", "W4c", "W4a", "W4c")),
        };

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // All should be assigned — plenty of T4 capacity
        Assert.Equal(3, result.AssignedCount);

        // Each assigned person should have exactly 2 workshops (both Type4)
        foreach (var assignment in result.Assignments.Where(a => a.WorkshopIds.Count > 0))
        {
            Assert.Equal(2, assignment.WorkshopIds.Count);

            // The two assigned workshops must be DIFFERENT IDs
            Assert.NotEqual(assignment.WorkshopIds[0], assignment.WorkshopIds[1]);
        }

        // Verify no workshop exceeds its capacity
        foreach (var ws in workshops)
        {
            int count = result.Assignments.Count(a => a.WorkshopIds.Contains(ws.Id));
            Assert.True(count <= ws.Capacity,
                $"Workshop {ws.Id} has capacity {ws.Capacity} but {count} persons assigned");
        }
    }


    // ── 16. MinCapacity Cancellation → Redistributes ────────

    [Fact]
    public void Solve_MinCapacityCancellation_RedistributesToLowerRankedPreferences()
    {
        // Workshop W2 has MinCapacity=5 but only 3 persons want it.
        // W2 must be cancelled, and those 3 persons should fall through
        // to combos using their lower-ranked preferences.
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20, MinCapacity: 5),
            new("W3", "Cooking", WorkshopType.Type3, 20),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        // 3 persons all have W2 as top preference, but MinCapacity=5 means W2 is cancelled
        var persons = new List<Person>
        {
            new("P1", "Alice", Prefs("W2", "W3", "W5", "W6", "W5", "W3")),
            new("P2", "Bob",   Prefs("W2", "W3", "W5", "W6", "W5", "W3")),
            new("P3", "Carol", Prefs("W2", "W3", "W5", "W6", "W5", "W3")),
        };

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // W2 should be cancelled (MinCapacity=5, only 3 interested)
        Assert.Contains("W2", result.CancelledWorkshopIds);

        // No person should be assigned to cancelled W2
        Assert.True(result.Assignments.All(a => !a.WorkshopIds.Contains("W2")),
            "No person should be assigned to cancelled workshop W2");

        // All 3 should still be assigned (via fallback combos not involving W2)
        Assert.Equal(3, result.AssignedCount);
    }


    // ── 17. Oversubscribed Workshop — Capacity Enforced ─────

    [Fact]
    public void Solve_OversubscribedWorkshop_CapacityEnforced()
    {
        // 10 persons want workshop W2 (capacity=5) as their top preference.
        // The solver must enforce the capacity: at most 5 assigned to W2.
        // The remaining 5 should fall through to lower-ranked combos.
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 5),   // tight capacity!
            new("W3", "Cooking", WorkshopType.Type3, 20),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        var persons = new List<Person>();
        for (int i = 1; i <= 10; i++)
        {
            persons.Add(new($"P{i}", $"Person{i}",
                Prefs("W2", "W3", "W5", "W6", "W5", "W3")));
        }

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // Count persons assigned to W2 — must not exceed capacity of 5
        int w2Count = result.Assignments.Count(a => a.WorkshopIds.Contains("W2"));
        Assert.True(w2Count <= 5,
            $"W2 capacity is 5 but {w2Count} persons assigned — capacity constraint violated!");

        // All 10 persons should still be assigned (overflow goes to lower-ranked combos)
        Assert.Equal(10, result.AssignedCount);

        // At least some persons should get lower-ranked combos (can't all get W2)
        int nonW2 = result.Assignments.Count(a =>
            a.WorkshopIds.Count > 0 && !a.WorkshopIds.Contains("W2"));
        Assert.True(nonW2 >= 5,
            $"Expected at least 5 persons on non-W2 combos, but only {nonW2}.");
    }


    // ── 18. Friend Group of 3 — Assigned Atomically ─────────

    [Fact]
    public void Solve_FriendGroup3_AssignedAtomically()
    {
        // A 3-person friend group must be assigned atomically: all members
        // get the SAME workshop assignment. The solver treats the group
        // as a single unit consuming 3 capacity slots.
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20),
            new("W3", "Cooking", WorkshopType.Type3, 20),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        var p1 = new Person("P1", "Alice", Prefs("W2", "W3", "W5", "W6", "W2", "W6"), FriendId: "P2");
        var p2 = new Person("P2", "Bob",   Prefs("W2", "W3", "W5", "W6", "W5", "W3"), FriendId: "P1");
        var p3 = new Person("P3", "Carol", Prefs("W2", "W3", "W5", "W6", "W2", "W6"));
        var p4 = new Person("P4", "Dave",  Prefs("W5", "W6", "W2", "W3", "W5", "W3"));

        var groups = new List<FriendGroup>
        {
            Trio(p1, p2, p3),
            Solo(p4),
        };

        var input = new AssignmentInput(workshops, new List<Person> { p1, p2, p3, p4 }, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // All 4 should be assigned (plenty of capacity)
        Assert.Equal(4, result.AssignedCount);

        // The trio must have identical assignments
        var a1 = result.Assignments.First(a => a.PersonId == "P1");
        var a2 = result.Assignments.First(a => a.PersonId == "P2");
        var a3 = result.Assignments.First(a => a.PersonId == "P3");

        // All three must get the same workshops
        Assert.Equal(a1.WorkshopIds, a2.WorkshopIds);
        Assert.Equal(a2.WorkshopIds, a3.WorkshopIds);

        // All three should actually be assigned (not empty)
        Assert.True(a1.WorkshopIds.Count > 0, "P1 in trio should be assigned");
    }


    // ── 19. All Preferences Phantom — Person Unassigned ──────

    [Fact]
    public void Solve_AllPreferencesPhantom_PersonUnassignedWithWarning()
    {
        // P1's preferences all reference non-existent workshop IDs.
        // P2 has valid preferences → should be assigned normally.
        // Tests selective unassignment — one assigned, one not.
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20),
            new("W3", "Cooking", WorkshopType.Type3, 20),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        // P1 has only phantom workshop references (W90, W91, W92 don't exist)
        // P2 has valid preferences → should be assigned normally
        var persons = new List<Person>
        {
            new("P1", "Ghost", Prefs("W90", "W91", "W92", "W90", "W91", "W92")),
            new("P2", "Valid", Prefs("W2", "W3", "W5", "W6", "W5", "W3")),
        };

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        Assert.Equal(2, result.TotalPersons);

        // P2 should be assigned (valid preferences)
        var p2a = result.Assignments.First(a => a.PersonId == "P2");
        Assert.True(p2a.WorkshopIds.Count > 0, "P2 with valid preferences should be assigned");

        // P1 should be unassigned (all phantom preferences)
        var p1a = result.Assignments.First(a => a.PersonId == "P1");
        Assert.Empty(p1a.WorkshopIds);

        // P1's phantom workshops should generate warnings
        Assert.NotEmpty(result.Warnings);
        // Warnings should reference the phantom workshop IDs
        var warningText = string.Join(" ", result.Warnings);
        Assert.True(
            warningText.Contains("W90") || warningText.Contains("W91") || warningText.Contains("W92"),
            $"Expected warnings about phantom workshop IDs (W90/W91/W92), got: {warningText}");
    }


    // == 20. Type4 Per-Slot MinCapacity — Partial Slot Cancellation ==

    [Fact]
    public void Solve_Type4PerSlotMinCapacity_PartialSlotCancellation()
    {
        // W4 (Type4, MinCapacity=5) can go to Slot 2 or Slot 3.
        // 6 persons pair W4 with W3 (Type3) → W4 goes to Slot 2 (6 people, meets min)
        // 2 persons pair W4 with W2 (Type2) → W4 goes to Slot 3 (2 people, below min=5)
        // Expected: Slot 2 runs (6 >= 5), Slot 3 session cancelled (2 < 5)
        // The workshop overall should NOT be cancelled (one slot still runs)
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20),
            new("W3", "Cooking", WorkshopType.Type3, 20),
            new("W4", "Music", WorkshopType.Type4, 20, MinCapacity: 5),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        var persons = new List<Person>();
        // 6 persons prefer W3+W4 (Type3+Type4 → W4 goes to Slot 2)
        for (int i = 1; i <= 6; i++)
        {
            persons.Add(new($"PA{i}", $"SlotA{i}",
                Prefs("W3", "W4", "W2", "W5", "W6", "W5")));
        }
        // 2 persons prefer W2+W4 (Type2+Type4 → W4 goes to Slot 3)
        for (int i = 1; i <= 2; i++)
        {
            persons.Add(new($"PB{i}", $"SlotB{i}",
                Prefs("W2", "W4", "W3", "W5", "W6", "W5")));
        }

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // W4 should NOT be fully cancelled — Slot 2 has enough people
        Assert.DoesNotContain("W4", result.CancelledWorkshopIds);

        // All persons should be assigned
        Assert.Equal(8, result.AssignedCount);

        // A per-slot cancellation warning should be generated for W4's under-filled slot
        Assert.True(result.Warnings.Any(w =>
            w.Contains("W4") && w.Contains("cancelled")),
            $"Expected per-slot cancellation warning for W4, got: [{string.Join("; ", result.Warnings)}]");
    }


    // == 21. Type4 Per-Slot MinCapacity — Both Slots Cancelled ==

    [Fact]
    public void Solve_Type4PerSlotMinCapacity_BothSlotsCancelled()
    {
        // W4 (Type4, MinCapacity=10) — only 3 total people want it
        // Neither slot can meet MinCapacity=10 → workshop fully cancelled
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20),
            new("W3", "Cooking", WorkshopType.Type3, 20),
            new("W4", "Music", WorkshopType.Type4, 20, MinCapacity: 10),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        var persons = new List<Person>
        {
            // 2 prefer W3+W4 (W4→Slot2, 2 < 10)
            new("P1", "Alice", Prefs("W3", "W4", "W2", "W5", "W6", "W5")),
            new("P2", "Bob",   Prefs("W3", "W4", "W2", "W5", "W6", "W5")),
            // 1 prefers W2+W4 (W4→Slot3, 1 < 10)
            new("P3", "Carol", Prefs("W2", "W4", "W3", "W5", "W6", "W5")),
        };

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // W4 should be fully cancelled — neither slot meets MinCapacity=10
        Assert.Contains("W4", result.CancelledWorkshopIds);

        // No person should be assigned to W4
        Assert.True(result.Assignments.All(a => !a.WorkshopIds.Contains("W4")),
            "No person should be assigned to fully cancelled Type4 workshop W4");

        // All persons should still be assigned via fallback combos
        Assert.Equal(3, result.AssignedCount);
    }


    // == 22. Type4 Per-Slot Max Capacity ==

    [Fact]
    public void Solve_Type4PerSlotMaxCapacity_EnforcedPerSlot()
    {
        // W4 (Type4, Capacity=3) — each slot independently capped at 3
        // 5 persons pair with Type3 (W4→Slot2), 5 pair with Type2 (W4→Slot3)
        // At most 3 in Slot 2, at most 3 in Slot 3 → max 6 total for W4
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20),
            new("W3", "Cooking", WorkshopType.Type3, 20),
            new("W4", "Music", WorkshopType.Type4, 3),   // tight per-slot capacity!
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        var persons = new List<Person>();
        // 5 persons prefer W3+W4 (W4→Slot2, capacity 3 per slot)
        for (int i = 1; i <= 5; i++)
        {
            persons.Add(new($"PA{i}", $"SlotA{i}",
                Prefs("W3", "W4", "W2", "W5", "W6", "W5")));
        }
        // 5 persons prefer W2+W4 (W4→Slot3, capacity 3 per slot)
        for (int i = 1; i <= 5; i++)
        {
            persons.Add(new($"PB{i}", $"SlotB{i}",
                Prefs("W2", "W4", "W3", "W5", "W6", "W5")));
        }

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // Count W4 assignments by slot direction
        int slot2Count = 0; // paired with W3 → W4 in Slot 2
        int slot3Count = 0; // paired with W2 → W4 in Slot 3
        foreach (var a in result.Assignments.Where(a => a.WorkshopIds.Contains("W4")))
        {
            if (a.WorkshopIds.Contains("W3"))
                slot2Count++;
            else if (a.WorkshopIds.Contains("W2"))
                slot3Count++;
        }

        Assert.True(slot2Count <= 3,
            $"W4 Slot 2 capacity is 3 but {slot2Count} persons assigned");
        Assert.True(slot3Count <= 3,
            $"W4 Slot 3 capacity is 3 but {slot3Count} persons assigned");

        // All 10 should still be assigned (overflow goes to W5+W6 fallback)
        Assert.Equal(10, result.AssignedCount);
    }


    // == 23. Type4+Type4 Pair — Per-Slot MinCapacity ==

    [Fact]
    public void Solve_Type4Type4Pair_PerSlotMinCapacity()
    {
        // Two Type4 workshops paired together: W4a→Slot2, W4b→Slot3 (convention)
        // Both have MinCapacity=3 — with 5 people wanting this combo,
        // both slots get 5 people each (exceeds min=3), both should run.
        var workshops = new List<Workshop>
        {
            new("W4a", "Music A", WorkshopType.Type4, 20, MinCapacity: 3),
            new("W4b", "Music B", WorkshopType.Type4, 20, MinCapacity: 3),
            new("W2", "Painting", WorkshopType.Type2, 20),
            new("W3", "Cooking", WorkshopType.Type3, 20),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        var persons = new List<Person>();
        // 5 persons prefer W4a+W4b pair
        for (int i = 1; i <= 5; i++)
        {
            persons.Add(new($"P{i}", $"Person{i}",
                Prefs("W4a", "W4b", "W2", "W3", "W5", "W6")));
        }

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // Both W4a and W4b should run (5 >= 3 in each slot)
        Assert.DoesNotContain("W4a", result.CancelledWorkshopIds);
        Assert.DoesNotContain("W4b", result.CancelledWorkshopIds);

        // All 5 persons should be assigned
        Assert.Equal(5, result.AssignedCount);

        // Verify the W4a+W4b combo was actually chosen for at least some persons
        int pairCount = result.Assignments.Count(a =>
            a.WorkshopIds.Contains("W4a") && a.WorkshopIds.Contains("W4b"));
        Assert.True(pairCount >= 3,
            $"Expected at least 3 persons with W4a+W4b combo, got {pairCount}");
    }


    // == 24. Type4 Without MinCapacity — No Per-Slot Enforcement ==

    [Fact]
    public void Solve_Type4NoMinCapacity_NoPerSlotEnforcement()
    {
        // W4 (Type4, NO MinCapacity) — should run even with 1 person in one slot
        // and 0 in the other. No per-slot cancellation warnings.
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 20),
            new("W3", "Cooking", WorkshopType.Type3, 20),
            new("W4", "Music", WorkshopType.Type4, 20),  // no MinCapacity!
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        // Only 1 person pairs W4 with W3 (W4→Slot2), no one pairs W4→Slot3
        var persons = new List<Person>
        {
            new("P1", "Alice", Prefs("W3", "W4", "W2", "W5", "W6", "W5")),
            new("P2", "Bob",   Prefs("W5", "W6", "W2", "W3", "W4", "W2")),
        };

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // W4 should NOT be cancelled — even with just 1 person (no MinCapacity)
        Assert.DoesNotContain("W4", result.CancelledWorkshopIds);

        // No per-slot cancellation warnings should be present
        Assert.True(!result.Warnings.Any(w => w.Contains("W4") && w.Contains("cancelled")),
            $"No per-slot cancellation warning expected for W4 without MinCapacity, got: [{string.Join("; ", result.Warnings)}]");

        // Both persons should be assigned
        Assert.Equal(2, result.AssignedCount);
    }


    // == 25. Slot Exclusivity — No Same-Slot Double Booking ==

    [Fact]
    public void Solve_SlotExclusivity_NeverAssignsTwoWorkshopsInSameSlot()
    {
        // Create 4 workshops: two morning (Type2), two afternoon (Type3)
        // Persons prefer W2a+W2b (both Type2 = same slot) as top choices
        // Solver must never assign two Type2 or two Type3 workshops to one person
        var workshops = new List<Workshop>
        {
            new("W2a", "Paint A", WorkshopType.Type2, 20),
            new("W2b", "Paint B", WorkshopType.Type2, 20),
            new("W3a", "Cook A",  WorkshopType.Type3, 20),
            new("W3b", "Cook B",  WorkshopType.Type3, 20),
        };

        var persons = new List<Person>
        {
            // Top choices are W2a+W2b (both Type2, can't pair) — must fall to valid combo
            new("P1", "Alice", Prefs("W2a", "W2b", "W3a", "W3b", "W2a", "W3b")),
            new("P2", "Bob",   Prefs("W2a", "W2b", "W3a", "W3b", "W2b", "W3a")),
            new("P3", "Carol", Prefs("W3a", "W3b", "W2a", "W2b", "W3a", "W2b")),
        };

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        foreach (var assignment in result.Assignments.Where(a => a.WorkshopIds.Count > 0))
        {
            var assignedTypes = assignment.WorkshopIds
                .Select(id => workshops.First(w => w.Id == id).Type)
                .ToList();

            // Never two Type2 in same assignment
            Assert.True(assignedTypes.Count(t => t == WorkshopType.Type2) <= 1,
                $"Person {assignment.PersonId} has two Type2 workshops — same-slot double booking!");

            // Never two Type3 in same assignment
            Assert.True(assignedTypes.Count(t => t == WorkshopType.Type3) <= 1,
                $"Person {assignment.PersonId} has two Type3 workshops — same-slot double booking!");

            // Must be either 1 Type1 workshop, or exactly 2 workshops in compatible slots
            if (assignment.WorkshopIds.Count == 2)
            {
                var sortedTypes = assignedTypes.OrderBy(t => t).ToList();
                var validPairs = new[]
                {
                    new[] { WorkshopType.Type2, WorkshopType.Type3 },
                    new[] { WorkshopType.Type2, WorkshopType.Type4 },
                    new[] { WorkshopType.Type3, WorkshopType.Type4 },
                    new[] { WorkshopType.Type4, WorkshopType.Type4 },
                };
                Assert.Contains(validPairs, pair =>
                    pair[0] == sortedTypes[0] && pair[1] == sortedTypes[1]);
            }
        }
    }


    // == 26. Type1 Excludes Half-Day — Never Mixed ==

    [Fact]
    public void Solve_Type1ExcludesHalfDay_NeverMixed()
    {
        var workshops = new List<Workshop>
        {
            new("W1", "Yoga",    WorkshopType.Type1, 30),
            new("W2", "Paint",   WorkshopType.Type2, 30),
            new("W3", "Cook",    WorkshopType.Type3, 30),
        };

        var person = new Person("P1", "Alice", Prefs("W1", "W2", "W3", "W1", "W2", "W3"));
        var groups = new List<FriendGroup> { Solo(person) };
        var input = new AssignmentInput(workshops, new List<Person> { person }, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        var assignment = result.Assignments.First(a => a.PersonId == "P1");
        Assert.True(assignment.WorkshopIds.Count > 0, "P1 should be assigned");

        if (assignment.WorkshopIds.Contains("W1"))
        {
            Assert.Single(assignment.WorkshopIds);
            Assert.Equal("W1", assignment.WorkshopIds[0]);
        }
        else
        {
            Assert.Equal(2, assignment.WorkshopIds.Count);
            Assert.DoesNotContain("W1", assignment.WorkshopIds);
            Assert.Contains("W2", assignment.WorkshopIds);
            Assert.Contains("W3", assignment.WorkshopIds);
        }
    }


    // == 27. Friend Group Atomic Unassignment Under Pressure ==

    [Fact]
    public void Solve_FriendGroup_UnassignedTogetherWhenCapacityInsufficient()
    {
        // Pair (P1+P2) needs 2 slots per workshop but cap=1 → can't fit
        // Solo P3 needs 1 slot → fits. Pair must be ATOMICALLY unassigned.
        var workshops = new List<Workshop>
        {
            new("W2", "Paint", WorkshopType.Type2, 1),
            new("W4", "Cook",  WorkshopType.Type3, 1),
        };

        var p1 = new Person("P1", "Alice", Prefs("W2", "W4", "W2", "W4", "W2", "W4"), FriendId: "P2");
        var p2 = new Person("P2", "Bob",   Prefs("W2", "W4", "W2", "W4", "W2", "W4"), FriendId: "P1");
        var p3 = new Person("P3", "Carol", Prefs("W2", "W4", "W2", "W4", "W2", "W4"));

        var groups = new List<FriendGroup>
        {
            Pair(p1, p2),
            Solo(p3),
        };

        var input = new AssignmentInput(workshops, new List<Person> { p1, p2, p3 }, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        var a1 = result.Assignments.First(a => a.PersonId == "P1");
        var a2 = result.Assignments.First(a => a.PersonId == "P2");
        var a3 = result.Assignments.First(a => a.PersonId == "P3");

        // Pair MUST be unassigned together — atomic constraint
        Assert.Empty(a1.WorkshopIds);
        Assert.Empty(a2.WorkshopIds);

        // Solo P3 fits in capacity
        Assert.True(a3.WorkshopIds.Count > 0, "Solo P3 should be assigned");
    }


    // == 28. Assignment Structure Validation — Universal Invariant Check ==

    [Fact]
    public void Solve_AllAssignments_HaveValidStructure()
    {
        var workshops = new List<Workshop>
        {
            new("W1", "Yoga",    WorkshopType.Type1, 20),
            new("W2", "Paint",   WorkshopType.Type2, 25),
            new("W3", "Cook",    WorkshopType.Type3, 15),
            new("W4", "Music",   WorkshopType.Type4, 20),
            new("W5", "Dance",   WorkshopType.Type2, 30),
            new("W6", "Drama",   WorkshopType.Type3, 20),
        };
        var wsLookup = workshops.ToDictionary(w => w.Id);

        var persons = new List<Person>
        {
            new("P1",  "A", Prefs("W1", "W2", "W3", "W4", "W5", "W6")),
            new("P2",  "B", Prefs("W2", "W3", "W5", "W6", "W4", "W1")),
            new("P3",  "C", Prefs("W5", "W6", "W2", "W3", "W1", "W4")),
            new("P4",  "D", Prefs("W4", "W2", "W3", "W5", "W6", "W1")),
            new("P5",  "E", Prefs("W1", "W4", "W2", "W3", "W5", "W6")),
            new("P6",  "F", Prefs("W2", "W6", "W5", "W3", "W4", "W1")),
            new("P7",  "G", Prefs("W3", "W4", "W2", "W5", "W6", "W1")),
            new("P8",  "H", Prefs("W5", "W3", "W2", "W6", "W1", "W4")),
            new("P9",  "I", Prefs("W4", "W5", "W6", "W2", "W3", "W1")),
            new("P10", "J", Prefs("W6", "W2", "W3", "W5", "W4", "W1")),
        };

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        Assert.Equal(10, result.TotalPersons);

        foreach (var a in result.Assignments)
        {
            // No duplicate workshop IDs
            Assert.Equal(a.WorkshopIds.Count, a.WorkshopIds.Distinct().Count());

            if (a.WorkshopIds.Count == 0)
            {
                Assert.Null(a.WishRank);
                Assert.Null(a.PreferenceRanks);
                Assert.Null(a.SatisfactionScore);
            }
            else if (a.WorkshopIds.Count == 1)
            {
                Assert.Equal(WorkshopType.Type1, wsLookup[a.WorkshopIds[0]].Type);
                Assert.NotNull(a.WishRank);
                Assert.NotNull(a.PreferenceRanks);
                Assert.Single(a.PreferenceRanks!);
                Assert.NotNull(a.SatisfactionScore);
            }
            else if (a.WorkshopIds.Count == 2)
            {
                var types = a.WorkshopIds.Select(id => wsLookup[id].Type).OrderBy(t => t).ToArray();
                var isValidPair =
                    (types[0] == WorkshopType.Type2 && types[1] == WorkshopType.Type3) ||
                    (types[0] == WorkshopType.Type2 && types[1] == WorkshopType.Type4) ||
                    (types[0] == WorkshopType.Type3 && types[1] == WorkshopType.Type4) ||
                    (types[0] == WorkshopType.Type4 && types[1] == WorkshopType.Type4);
                Assert.True(isValidPair, $"{a.PersonId} invalid pair: {types[0]}+{types[1]}");
                Assert.NotNull(a.WishRank);
                Assert.NotNull(a.PreferenceRanks);
                Assert.Equal(2, a.PreferenceRanks!.Count);
                Assert.NotNull(a.SatisfactionScore);
            }
            else
            {
                Assert.Fail($"{a.PersonId} has {a.WorkshopIds.Count} workshops — max is 2!");
            }
        }
    }


    // == 29. Type1 Scoring Doubled ==

    [Fact]
    public void Solve_Type1Scoring_SatisfactionScoreDoubled()
    {
        // Type1 score: (7 - rank) * 2. W1 at rank 1 → 12, W8 at rank 3 → 8
        var workshops = new List<Workshop>
        {
            new("W1", "Yoga",  WorkshopType.Type1, 30),
            new("W8", "Zen",   WorkshopType.Type1, 30),
            new("W2", "Paint", WorkshopType.Type2, 30),
            new("W3", "Cook",  WorkshopType.Type3, 30),
        };

        // P1: W1 at rank 1 → Type1 score = (7-1)*2 = 12
        var p1 = new Person("P1", "Alice", Prefs("W1", "W2", "W3", "W8", "W2", "W3"));
        // P2: W8 at rank 3, W2+W3 at ranks 1+2 → solver picks W2+W3 (score 11 > 8)
        var p2 = new Person("P2", "Bob", Prefs("W2", "W3", "W8", "W1", "W2", "W3"));

        var groups = new List<FriendGroup> { Solo(p1), Solo(p2) };
        var input = new AssignmentInput(workshops, new List<Person> { p1, p2 }, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        var a1 = result.Assignments.First(a => a.PersonId == "P1");
        Assert.Single(a1.WorkshopIds);
        Assert.Equal("W1", a1.WorkshopIds[0]);
        Assert.Equal(12, a1.SatisfactionScore); // (7-1)*2 = 12

        var a2 = result.Assignments.First(a => a.PersonId == "P2");
        Assert.True(a2.WorkshopIds.Count > 0, "P2 should be assigned");
        if (a2.WorkshopIds.Count == 1 && a2.WorkshopIds[0] == "W8")
        {
            Assert.Equal(8, a2.SatisfactionScore); // (7-3)*2 = 8
        }
        else if (a2.WorkshopIds.Count == 2)
        {
            // Got W2+W3 pair (score 11) — verify pair scoring works
            Assert.NotNull(a2.SatisfactionScore);
            Assert.True(a2.SatisfactionScore > 0);
        }
    }


    // == 30. Sum-of-Ranks Scoring Arithmetic ==

    [Fact]
    public void Solve_SumOfRanks_ScoreCalculatedCorrectly()
    {
        // Force a specific combo: only W2 (Type2) and W4 (Type3) available
        // Person has W2 at rank 1, W4 at rank 4 → score = (7-1)+(7-4) = 6+3 = 9
        var workshops = new List<Workshop>
        {
            new("W2", "Paint", WorkshopType.Type2, 30),
            new("W4", "Cook",  WorkshopType.Type3, 30),
        };

        // Only valid combo is W2+W4. W2 is rank 1, W4 is rank 4.
        var person = new Person("P1", "Alice", Prefs("W2", "W2", "W2", "W4", "W2", "W4"));

        var groups = new List<FriendGroup> { Solo(person) };
        var input = new AssignmentInput(workshops, new List<Person> { person }, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        var a = result.Assignments.First(a => a.PersonId == "P1");
        Assert.Equal(2, a.WorkshopIds.Count);
        Assert.Contains("W2", a.WorkshopIds);
        Assert.Contains("W4", a.WorkshopIds);

        // Score = (7 - rank_W2) + (7 - rank_W4) = (7-1) + (7-4) = 6 + 3 = 9
        Assert.Equal(9, a.SatisfactionScore);

        // PreferenceRanks should contain the individual ranks
        Assert.NotNull(a.PreferenceRanks);
        Assert.Equal(2, a.PreferenceRanks!.Count);
        Assert.Contains(1, a.PreferenceRanks); // W2 at rank 1
        Assert.Contains(4, a.PreferenceRanks); // W4 at rank 4
    }


    // == 31. Assignment Dominates Scoring — Never Sacrifices Assignment ==

    [Fact]
    public void Solve_AssignmentDominatesScoring_NeverSacrificesAssignment()
    {
        // WeightAssigned (1000) >> max rank bonus (12), so the solver should
        // ALWAYS prefer assigning someone over leaving them out for better scores.
        // P1 can only get a terrible combo (rank 5+6), but should still be assigned.
        var workshops = new List<Workshop>
        {
            new("W2", "Paint", WorkshopType.Type2, 20),
            new("W3", "Cook",  WorkshopType.Type3, 20),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        // P1: only valid combos use rank 5+6 preferences (worst possible)
        var p1 = new Person("P1", "Bad Luck",
            Prefs("W2", "W2", "W2", "W2", "W5", "W6"));
        // P2-P5: get great combos (rank 1+2)
        var p2 = new Person("P2", "Lucky2", Prefs("W2", "W3", "W5", "W6", "W2", "W3"));
        var p3 = new Person("P3", "Lucky3", Prefs("W2", "W3", "W5", "W6", "W2", "W3"));
        var p4 = new Person("P4", "Lucky4", Prefs("W5", "W6", "W2", "W3", "W5", "W6"));
        var p5 = new Person("P5", "Lucky5", Prefs("W5", "W6", "W2", "W3", "W5", "W6"));

        var persons = new List<Person> { p1, p2, p3, p4, p5 };
        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // P1 MUST be assigned despite terrible score
        var a1 = result.Assignments.First(a => a.PersonId == "P1");
        Assert.True(a1.WorkshopIds.Count > 0,
            "P1 must be assigned — WeightAssigned (1000) dominates scoring (max 12)");

        // All 5 persons should be assigned
        Assert.Equal(5, result.AssignedCount);
    }


    // == 32. Person With Only Type1 Preferences ==

    [Fact]
    public void Solve_OnlyType1Preferences_AssignedToOneType1()
    {
        // Person lists only Type1 workshops — should get exactly 1 Type1 workshop
        var workshops = new List<Workshop>
        {
            new("W1", "Yoga",  WorkshopType.Type1, 20),
            new("W8", "Zen",   WorkshopType.Type1, 20),
            new("W2", "Paint", WorkshopType.Type2, 20),
            new("W3", "Cook",  WorkshopType.Type3, 20),
        };

        // Only Type1 preferences — no half-day combos possible
        var person = new Person("P1", "Alice", Prefs("W1", "W8", "W1", "W8", "W1", "W8"));
        var groups = new List<FriendGroup> { Solo(person) };
        var input = new AssignmentInput(workshops, new List<Person> { person }, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        var a = result.Assignments.First(a => a.PersonId == "P1");
        Assert.Single(a.WorkshopIds); // exactly 1 workshop
        var assignedWs = workshops.First(w => w.Id == a.WorkshopIds[0]);
        Assert.Equal(WorkshopType.Type1, assignedWs.Type); // must be Type1

        // Satisfaction score must be doubled (Type1 formula)
        Assert.NotNull(a.SatisfactionScore);
        Assert.True(a.SatisfactionScore!.Value % 2 == 0,
            $"Type1 score must be even (doubled formula), got {a.SatisfactionScore}");
    }


    // == 33. Type4+Type4 Slot Separation Verified ==

    [Fact]
    public void Solve_Type4Type4Pair_WorkshopsInDifferentSlots()
    {
        // Two Type4 workshops paired together must occupy different slots
        // Use SlotPlacementHelper to verify slot placement
        var workshops = new List<Workshop>
        {
            new("W4a", "Music A", WorkshopType.Type4, 20),
            new("W4b", "Music B", WorkshopType.Type4, 20),
            new("W2",  "Paint",   WorkshopType.Type2, 20),
            new("W3",  "Cook",    WorkshopType.Type3, 20),
        };
        var wsLookup = workshops.ToDictionary(w => w.Id, w => w);

        // Person prefers W4a+W4b pair
        var person = new Person("P1", "Alice", Prefs("W4a", "W4b", "W2", "W3", "W4a", "W3"));
        var groups = new List<FriendGroup> { Solo(person) };
        var input = new AssignmentInput(workshops, new List<Person> { person }, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        var a = result.Assignments.First(a => a.PersonId == "P1");
        Assert.True(a.WorkshopIds.Count > 0, "P1 should be assigned");

        // If they got the W4a+W4b pair, verify slot separation
        if (a.WorkshopIds.Contains("W4a") && a.WorkshopIds.Contains("W4b"))
        {
            Assert.Equal(2, a.WorkshopIds.Count);

            // Use SlotPlacementHelper to check slot assignments
            int slotA = SlotPlacementHelper.GetType4SlotNumber(
                "W4a", a.WorkshopIds, id => wsLookup.GetValueOrDefault(id));
            int slotB = SlotPlacementHelper.GetType4SlotNumber(
                "W4b", a.WorkshopIds, id => wsLookup.GetValueOrDefault(id));

            Assert.NotEqual(slotA, slotB); // different slots
            Assert.Contains(slotA, new[] { 2, 3 });
            Assert.Contains(slotB, new[] { 2, 3 });

            // Convention: first in list → Slot 2, second → Slot 3
            Assert.Equal(2, slotA); // W4a (first) → Slot 2
            Assert.Equal(3, slotB); // W4b (second) → Slot 3
        }
    }


    // == 34. Multi-Level Cascading Cancellation ==

    [Fact]
    public void Solve_CascadingCancellation_MultiLevel()
    {
        // W2(Type2, cap=5, min=4) and W4(Type3, cap=5, min=4) — paired
        // W3(Type2, cap=20) and W5(Type3, cap=20) — fallback pair
        // Only 3 persons want W2+W4 → below min=4 → W2 cancelled
        // Those 3 must redistribute to W3+W5 or other valid combos
        var workshops = new List<Workshop>
        {
            new("W2", "Paint A", WorkshopType.Type2, 5, MinCapacity: 4),
            new("W4", "Cook A",  WorkshopType.Type3, 5, MinCapacity: 4),
            new("W3", "Paint B", WorkshopType.Type2, 20),
            new("W5", "Cook B",  WorkshopType.Type3, 20),
        };

        // 3 persons prefer W2+W4 (rank 1+2) with W3+W5 as fallback
        var persons = new List<Person>
        {
            new("P1", "Alice", Prefs("W2", "W4", "W3", "W5", "W3", "W5")),
            new("P2", "Bob",   Prefs("W2", "W4", "W3", "W5", "W3", "W5")),
            new("P3", "Carol", Prefs("W2", "W4", "W3", "W5", "W3", "W5")),
        };

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // W2 or W4 should be cancelled (3 < min=4)
        bool w2Cancelled = result.CancelledWorkshopIds.Contains("W2");
        bool w4Cancelled = result.CancelledWorkshopIds.Contains("W4");
        Assert.True(w2Cancelled || w4Cancelled,
            "With only 3 persons, at least one workshop with min=4 should be cancelled");

        // No person should be assigned to a cancelled workshop
        foreach (var cancelledId in result.CancelledWorkshopIds)
        {
            Assert.True(result.Assignments.All(a => !a.WorkshopIds.Contains(cancelledId)),
                $"No person should be assigned to cancelled workshop {cancelledId}");
        }

        // All 3 persons must still be assigned (redistributed to fallbacks)
        Assert.Equal(3, result.AssignedCount);
    }


    // == 35. PreferenceRanks Per-Person (Not Per-Group) ==

    [Fact]
    public void Solve_PreferenceRanks_ReflectIndividualNotGroupPreferences()
    {
        // P1 has W2 at rank 1, P2 has W2 at rank 4 — same group, same workshops
        // Their PreferenceRanks should differ based on PERSONAL preferences
        var workshops = new List<Workshop>
        {
            new("W2", "Paint", WorkshopType.Type2, 20),
            new("W3", "Cook",  WorkshopType.Type3, 20),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        // P1: W2 at rank 1, W3 at rank 2
        var p1 = new Person("P1", "Alice",
            Prefs("W2", "W3", "W5", "W6", "W5", "W6"), FriendId: "P2");
        // P2: W2 at rank 4, W3 at rank 1
        var p2 = new Person("P2", "Bob",
            Prefs("W3", "W5", "W6", "W2", "W5", "W6"), FriendId: "P1");

        var groups = new List<FriendGroup> { Pair(p1, p2) };
        var input = new AssignmentInput(workshops, new List<Person> { p1, p2 }, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        var a1 = result.Assignments.First(a => a.PersonId == "P1");
        var a2 = result.Assignments.First(a => a.PersonId == "P2");

        // Both must get the same workshops (same group)
        Assert.Equal(a1.WorkshopIds, a2.WorkshopIds);
        Assert.True(a1.WorkshopIds.Count > 0, "Group should be assigned");

        // But PreferenceRanks must reflect INDIVIDUAL preferences
        Assert.NotNull(a1.PreferenceRanks);
        Assert.NotNull(a2.PreferenceRanks);

        // If they got W2+W3:
        if (a1.WorkshopIds.Contains("W2") && a1.WorkshopIds.Contains("W3"))
        {
            // P1: W2=rank1, W3=rank2 → ranks contain 1 and 2
            int p1W2Rank = a1.PreferenceRanks![a1.WorkshopIds.IndexOf("W2")];
            int p1W3Rank = a1.PreferenceRanks![a1.WorkshopIds.IndexOf("W3")];
            Assert.Equal(1, p1W2Rank); // W2 is rank 1 for P1
            Assert.Equal(2, p1W3Rank); // W3 is rank 2 for P1

            // P2: W2=rank4, W3=rank1 → ranks contain 4 and 1
            int p2W2Rank = a2.PreferenceRanks![a2.WorkshopIds.IndexOf("W2")];
            int p2W3Rank = a2.PreferenceRanks![a2.WorkshopIds.IndexOf("W3")];
            Assert.Equal(4, p2W2Rank); // W2 is rank 4 for P2
            Assert.Equal(1, p2W3Rank); // W3 is rank 1 for P2

            // Ranks must be different because personal preferences differ
            Assert.NotEqual(a1.PreferenceRanks, a2.PreferenceRanks);
        }
    }


    // == 36. WishRank Is Best (Lowest) Among Assigned Workshops ==

    [Fact]
    public void Solve_WishRank_IsBestRankAmongAssignedWorkshops()
    {
        // Force person to get workshops at known ranks, verify WishRank = best rank
        var workshops = new List<Workshop>
        {
            new("W2", "Paint", WorkshopType.Type2, 30),
            new("W3", "Cook",  WorkshopType.Type3, 30),
        };

        // W2 at rank 2, W3 at rank 5 → WishRank should be 2 (the better one)
        var person = new Person("P1", "Alice",
            Prefs("W2", "W2", "W2", "W2", "W3", "W3"));
        // After dedup in solver: validPrefs = [W2, W3], W2=rank1, W3=rank5
        // Actually the solver uses index-based ranking from the original list.
        // Let's be explicit: position 0=W2(rank1?), but W2 appears at idx 0.
        // prefRank maps first occurrence: W2→1, W3→5

        var groups = new List<FriendGroup> { Solo(person) };
        var input = new AssignmentInput(workshops, new List<Person> { person }, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        var a = result.Assignments.First(a => a.PersonId == "P1");
        Assert.Equal(2, a.WorkshopIds.Count);

        // WishRank is the BEST (lowest) rank among assigned workshops
        Assert.NotNull(a.WishRank);
        Assert.NotNull(a.PreferenceRanks);
        Assert.Equal(a.PreferenceRanks!.Min(), a.WishRank);

        // WishRank must be between 1 and 6
        Assert.InRange(a.WishRank!.Value, 1, 6);
    }


    // == 37. Capacity Exactly at Max — Boundary ==

    [Fact]
    public void Solve_CapacityExactlyAtMax_Accepted()
    {
        // W2(Type2, cap=3) and W4(Type3, cap=3) — exactly 3 persons want this combo
        // All 3 should fit (boundary: count == capacity)
        var workshops = new List<Workshop>
        {
            new("W2", "Paint", WorkshopType.Type2, 3),
            new("W4", "Cook",  WorkshopType.Type3, 3),
        };

        var persons = new List<Person>
        {
            new("P1", "Alice", Prefs("W2", "W4", "W2", "W4", "W2", "W4")),
            new("P2", "Bob",   Prefs("W2", "W4", "W2", "W4", "W2", "W4")),
            new("P3", "Carol", Prefs("W2", "W4", "W2", "W4", "W2", "W4")),
        };

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // All 3 should be assigned to W2+W4 (exactly fills capacity)
        Assert.Equal(3, result.AssignedCount);

        foreach (var a in result.Assignments)
        {
            Assert.Contains("W2", a.WorkshopIds);
            Assert.Contains("W4", a.WorkshopIds);
        }

        // Verify exact capacity: W2 has 3 persons, W4 has 3 persons
        Assert.Equal(3, result.Assignments.Count(a => a.WorkshopIds.Contains("W2")));
        Assert.Equal(3, result.Assignments.Count(a => a.WorkshopIds.Contains("W4")));
    }


    // == 38. MinCapacity Exactly Met — Boundary ==

    [Fact]
    public void Solve_MinCapacityExactlyMet_WorkshopRuns()
    {
        // W2(Type2, cap=20, min=3) — exactly 3 persons want W2
        // MinCapacity boundary: 3 == 3 → workshop should run (not cancelled)
        var workshops = new List<Workshop>
        {
            new("W2", "Paint", WorkshopType.Type2, 20, MinCapacity: 3),
            new("W3", "Cook",  WorkshopType.Type3, 20),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        // Exactly 3 persons prefer W2+W3 combo
        var persons = new List<Person>
        {
            new("P1", "Alice", Prefs("W2", "W3", "W5", "W6", "W5", "W6")),
            new("P2", "Bob",   Prefs("W2", "W3", "W5", "W6", "W5", "W6")),
            new("P3", "Carol", Prefs("W2", "W3", "W5", "W6", "W5", "W6")),
        };

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // W2 should NOT be cancelled (3 >= min=3)
        Assert.DoesNotContain("W2", result.CancelledWorkshopIds);

        // All 3 should be assigned (preferably to W2+W3)
        Assert.Equal(3, result.AssignedCount);

        // At least the 3 who wanted W2 should have it (meets min exactly)
        int w2Count = result.Assignments.Count(a => a.WorkshopIds.Contains("W2"));
        Assert.True(w2Count >= 3,
            $"W2 MinCapacity=3, exactly 3 want it — should have at least 3 assigned, got {w2Count}");
    }


    // == 39. Person With Exactly 3 Preferences (Minimum) ==

    [Fact]
    public void Solve_MinimumPreferences_ThreePreferencesWork()
    {
        // Person has only 3 preferences (the minimum accepted by the system)
        // Should still be assignable if a valid combo exists from those 3
        var workshops = new List<Workshop>
        {
            new("W2", "Paint", WorkshopType.Type2, 20),
            new("W3", "Cook",  WorkshopType.Type3, 20),
            new("W5", "Dance", WorkshopType.Type2, 20),
            new("W6", "Drama", WorkshopType.Type3, 20),
        };

        // Only 3 unique preferences: W2 (Type2), W3 (Type3), W5 (Type2)
        // Valid combos: W2+W3 (T2+T3), W5+W3 (T2+T3)
        var person = new Person("P1", "Alice", Prefs("W2", "W3", "W5", "W2", "W3", "W5"));

        var groups = new List<FriendGroup> { Solo(person) };
        var input = new AssignmentInput(workshops, new List<Person> { person }, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        var a = result.Assignments.First(a => a.PersonId == "P1");
        Assert.True(a.WorkshopIds.Count > 0,
            "Person with 3 valid preferences forming a valid combo should be assigned");
        Assert.Equal(2, a.WorkshopIds.Count); // half-day pair
    }


    // == 40. Unassigned Person Has Correct Null Fields ==

    [Fact]
    public void Solve_UnassignedPerson_HasCorrectNullFields()
    {
        // Force a person to be unassignable — all phantom preferences
        // Verify all result fields are correctly null/empty
        var workshops = new List<Workshop>
        {
            new("W2", "Paint", WorkshopType.Type2, 20),
            new("W3", "Cook",  WorkshopType.Type3, 20),
        };

        // All preferences reference non-existent workshops → unassignable
        var person = new Person("P1", "Ghost",
            Prefs("W90", "W91", "W92", "W93", "W94", "W95"));

        var groups = new List<FriendGroup> { Solo(person) };
        var input = new AssignmentInput(workshops, new List<Person> { person }, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        var a = result.Assignments.First(a => a.PersonId == "P1");

        // Unassigned person must have all fields correctly null/empty
        Assert.Empty(a.WorkshopIds);
        Assert.Null(a.WishRank);
        Assert.Null(a.PreferenceRanks);
        Assert.Null(a.SatisfactionScore);
    }


    // ══════════════════════════════════════════════════════════════════════
    // POST-ASSIGNMENT RECONCILIATION VALIDATION
    // Like accounting reconciliation: cross-checks that all numbers
    // in the assignment result are internally consistent.
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Comprehensive post-solve reconciliation validator. Verifies internal consistency
    /// of assignment results across 7 dimensions: person coverage, workshop occupancy,
    /// slot balance, assignment structure, scoring correctness, cross-totals, and
    /// cancelled workshop handling. Every assertion includes a descriptive message
    /// pinpointing exactly which reconciliation rule failed and for which entity.
    /// </summary>
    private static void AssertAssignmentReconciliation(
        AssignmentInput input,
        AssignmentResult result,
        List<Workshop> workshops)
    {
        var wsLookup = workshops.ToDictionary(w => w.Id);

        // ── 1. Person-Level Checks ──────────────────────────────────────

        // Every input person appears exactly once in result
        var resultPersonIds = result.Assignments.Select(a => a.PersonId).ToList();
        Assert.Equal(
            input.Persons.Count,
            resultPersonIds.Count);

        foreach (var person in input.Persons)
        {
            int occurrences = resultPersonIds.Count(id => id == person.Id);
            Assert.True(occurrences == 1,
                $"RECONCILIATION: Person '{person.Id}' appears {occurrences} times in result (expected exactly 1)");
        }

        // No phantom persons (not in input)
        var inputPersonIds = new HashSet<string>(input.Persons.Select(p => p.Id));
        foreach (var a in result.Assignments)
        {
            Assert.True(inputPersonIds.Contains(a.PersonId),
                $"RECONCILIATION: Phantom person '{a.PersonId}' in result — not present in input");
        }

        // AssignedCount + UnassignedCount == TotalPersons
        Assert.Equal(
            result.TotalPersons,
            result.AssignedCount + result.UnassignedCount);

        // AssignmentRate is mathematically correct
        double expectedRate = result.TotalPersons > 0
            ? (double)result.AssignedCount / result.TotalPersons * 100
            : 0;
        Assert.Equal(expectedRate, result.AssignmentRate);

        // ── 2. Workshop Occupancy Reconciliation ────────────────────────

        foreach (var ws in workshops)
        {
            int assignedCount = result.Assignments
                .Count(a => a.WorkshopIds.Contains(ws.Id));

            if (ws.Type == WorkshopType.Type4)
            {
                // Type4: per-slot counts must independently respect Capacity and MinCapacity
                int slot2Count = 0;
                int slot3Count = 0;

                foreach (var a in result.Assignments.Where(a => a.WorkshopIds.Contains(ws.Id)))
                {
                    int slot = SlotPlacementHelper.GetType4SlotNumber(
                        ws.Id, a.WorkshopIds, id => wsLookup.GetValueOrDefault(id));
                    if (slot == 2) slot2Count++;
                    else slot3Count++;
                }

                Assert.True(slot2Count <= ws.Capacity,
                    $"RECONCILIATION: Workshop '{ws.Id}' ({ws.Name}) Type4 Slot2 has {slot2Count} persons, exceeds Capacity {ws.Capacity}");
                Assert.True(slot3Count <= ws.Capacity,
                    $"RECONCILIATION: Workshop '{ws.Id}' ({ws.Name}) Type4 Slot3 has {slot3Count} persons, exceeds Capacity {ws.Capacity}");

                if (ws.MinCapacity > 0)
                {
                    Assert.True(slot2Count >= ws.MinCapacity || slot2Count == 0,
                        $"RECONCILIATION: Workshop '{ws.Id}' ({ws.Name}) Type4 Slot2 has {slot2Count} persons — below MinCapacity {ws.MinCapacity} but not cancelled (should be 0 or >= MinCapacity)");
                    Assert.True(slot3Count >= ws.MinCapacity || slot3Count == 0,
                        $"RECONCILIATION: Workshop '{ws.Id}' ({ws.Name}) Type4 Slot3 has {slot3Count} persons — below MinCapacity {ws.MinCapacity} but not cancelled (should be 0 or >= MinCapacity)");
                }
            }
            else
            {
                // Non-Type4: simple capacity check
                Assert.True(assignedCount <= ws.Capacity,
                    $"RECONCILIATION: Workshop '{ws.Id}' ({ws.Name}) has {assignedCount} persons, exceeds Capacity {ws.Capacity}");

                if (ws.MinCapacity > 0)
                {
                    Assert.True(assignedCount >= ws.MinCapacity || assignedCount == 0,
                        $"RECONCILIATION: Workshop '{ws.Id}' ({ws.Name}) has {assignedCount} persons — below MinCapacity {ws.MinCapacity} but not cancelled (should be 0 or >= MinCapacity)");
                }
            }
        }

        // ── 3. Slot Balance Check ───────────────────────────────────────

        foreach (var a in result.Assignments.Where(a => a.WorkshopIds.Count > 0))
        {
            if (a.WorkshopIds.Count == 1)
            {
                var ws = wsLookup[a.WorkshopIds[0]];
                if (ws.Type == WorkshopType.Type1)
                {
                    // Type1: single workshop must be Type1 and IsFullDay must be true
                    Assert.True(a.IsFullDay,
                        $"RECONCILIATION: Person '{a.PersonId}' has Type1 workshop but IsFullDay is false");
                }
                else
                {
                    // Half-day solo: single non-Type1 workshop (last resort assignment)
                    // IsFullDay must be false
                    Assert.False(a.IsFullDay,
                        $"RECONCILIATION: Person '{a.PersonId}' has half-day solo ({ws.Type}) but IsFullDay is true");
                    // Workshop type must be Type2, Type3, or Type4
                    Assert.True(
                        ws.Type == WorkshopType.Type2 || ws.Type == WorkshopType.Type3 || ws.Type == WorkshopType.Type4,
                        $"RECONCILIATION: Person '{a.PersonId}' has unexpected single workshop type: {ws.Type}");
                }
            }
            else if (a.WorkshopIds.Count == 2)
            {
                // Pair: IsFullDay must be false
                Assert.False(a.IsFullDay,
                    $"RECONCILIATION: Person '{a.PersonId}' has pair but IsFullDay is true");

                // Pair: one workshop in Slot 2, the other in Slot 3 (no two in same slot)
                var slots = a.WorkshopIds.Select(wid =>
                {
                    var ws = wsLookup[wid];
                    return ws.Type switch
                    {
                        WorkshopType.Type2 => 2,
                        WorkshopType.Type3 => 3,
                        WorkshopType.Type4 => SlotPlacementHelper.GetType4SlotNumber(
                            wid, a.WorkshopIds, id => wsLookup.GetValueOrDefault(id)),
                        _ => -1
                    };
                }).ToList();

                Assert.True(slots[0] != slots[1],
                    $"RECONCILIATION: Person '{a.PersonId}' has both workshops in same slot {slots[0]} — slots should differ (got {string.Join(",", a.WorkshopIds)})");
                Assert.True(
                    (slots.Contains(2) && slots.Contains(3)),
                    $"RECONCILIATION: Person '{a.PersonId}' pair should occupy Slot 2 and Slot 3, got slots {slots[0]} and {slots[1]}");
            }
        }

        // ── 4. Assignment Structure Check ───────────────────────────────

        foreach (var a in result.Assignments)
        {
            // Valid workshop count: 0, 1, or 2
            Assert.True(a.WorkshopIds.Count >= 0 && a.WorkshopIds.Count <= 2,
                $"RECONCILIATION: Person '{a.PersonId}' has {a.WorkshopIds.Count} workshops — must be 0, 1, or 2");

            // No duplicate workshop IDs
            Assert.Equal(a.WorkshopIds.Count, a.WorkshopIds.Distinct().Count());

            if (a.WorkshopIds.Count == 2)
            {
                // Valid type combination
                var types = a.WorkshopIds
                    .Select(id => wsLookup[id].Type)
                    .OrderBy(t => t)
                    .ToArray();
                var isValidPair =
                    (types[0] == WorkshopType.Type2 && types[1] == WorkshopType.Type3) ||
                    (types[0] == WorkshopType.Type2 && types[1] == WorkshopType.Type4) ||
                    (types[0] == WorkshopType.Type3 && types[1] == WorkshopType.Type4) ||
                    (types[0] == WorkshopType.Type4 && types[1] == WorkshopType.Type4);
                Assert.True(isValidPair,
                    $"RECONCILIATION: Person '{a.PersonId}' invalid type pair: {types[0]}+{types[1]}");
            }
        }

        // ── 5. Scoring Check ────────────────────────────────────────────

        var personLookup = input.Persons.ToDictionary(p => p.Id);

        foreach (var a in result.Assignments)
        {
            if (a.WorkshopIds.Count > 0)
            {
                // Assigned: all scoring fields must be non-null
                Assert.True(a.WishRank != null,
                    $"RECONCILIATION: Assigned person '{a.PersonId}' has null WishRank");
                Assert.True(a.PreferenceRanks != null,
                    $"RECONCILIATION: Assigned person '{a.PersonId}' has null PreferenceRanks");
                Assert.True(a.SatisfactionScore != null,
                    $"RECONCILIATION: Assigned person '{a.PersonId}' has null SatisfactionScore");

                // WishRank == min(PreferenceRanks)
                Assert.Equal(a.PreferenceRanks!.Min(), a.WishRank!.Value);

                // SatisfactionScore verification
                // Type1 (IsFullDay=true): doubled. Pairs and half-day solos: sum of (7-rank)
                int expectedScore;
                if (a.IsFullDay)
                {
                    expectedScore = (7 - a.PreferenceRanks![0]) * 2;
                }
                else
                {
                    expectedScore = a.PreferenceRanks!.Sum(r => 7 - r);
                }
                Assert.Equal(expectedScore, a.SatisfactionScore!.Value);
            }
            else
            {
                // Unassigned: all scoring fields must be null
                Assert.True(a.WishRank == null,
                    $"RECONCILIATION: Unassigned person '{a.PersonId}' has non-null WishRank={a.WishRank}");
                Assert.True(a.PreferenceRanks == null,
                    $"RECONCILIATION: Unassigned person '{a.PersonId}' has non-null PreferenceRanks");
                Assert.True(a.SatisfactionScore == null,
                    $"RECONCILIATION: Unassigned person '{a.PersonId}' has non-null SatisfactionScore={a.SatisfactionScore}");
            }
        }

        // ── 6. Cross-Total Check (The Accounting Rule) ──────────────────

        // Sum of per-workshop attendee counts == sum of per-person workshop assignments
        int sumWorkshopSide = workshops.Sum(ws =>
            result.Assignments.Count(a => a.WorkshopIds.Contains(ws.Id)));
        int sumPersonSide = result.Assignments.Sum(a => a.WorkshopIds.Count);

        Assert.Equal(sumPersonSide, sumWorkshopSide);

        // SlotRankDistribution total entries:
        //   pairs × 2 entries + Type1 × 2 entries + half-day solos × 1 entry
        int personsWithPairs = result.Assignments.Count(a => a.WorkshopIds.Count == 2);
        int personsWithType1 = result.Assignments.Count(a => a.IsFullDay);
        int personsWithHalfDaySolo = result.Assignments.Count(a =>
            a.WorkshopIds.Count == 1 && !a.IsFullDay);
        int expectedSlotEntries = (personsWithPairs * 2) + (personsWithType1 * 2) + (personsWithHalfDaySolo * 1);
        int actualSlotEntries = result.SlotRankDistribution.Values.Sum();

        Assert.Equal(expectedSlotEntries, actualSlotEntries);

        // ── 7. Cancelled Workshop Check ─────────────────────────────────

        // Every cancelled workshop has 0 persons assigned
        foreach (var cancelledId in result.CancelledWorkshopIds)
        {
            int assignedToCancelled = result.Assignments
                .Count(a => a.WorkshopIds.Contains(cancelledId));
            Assert.True(assignedToCancelled == 0,
                $"RECONCILIATION: Cancelled workshop '{cancelledId}' still has {assignedToCancelled} persons assigned");
        }

        // Every workshop with 0 persons AND MinCapacity > 0 should be in CancelledWorkshopIds
        foreach (var ws in workshops.Where(w => w.MinCapacity > 0))
        {
            int assignedToWs = result.Assignments
                .Count(a => a.WorkshopIds.Contains(ws.Id));

            if (assignedToWs == 0)
            {
                Assert.True(result.CancelledWorkshopIds.Contains(ws.Id),
                    $"RECONCILIATION: Workshop '{ws.Id}' ({ws.Name}) has 0 persons and MinCapacity={ws.MinCapacity} but is NOT in CancelledWorkshopIds");
            }
        }
    }


    // == 41. Half-Day Solo Assignment — Last Resort ==

    [Fact]
    public void Solve_HalfDaySolo_AssignedWhenNoPairAvailable()
    {
        // Person prefers only Type2 workshops — no compatible pair exists (T2+T2 invalid)
        // The solver should assign a single half-day workshop rather than leaving unassigned
        var workshops = new List<Workshop>
        {
            new("W2a", "Painting", WorkshopType.Type2, 20),
            new("W2b", "Drawing",  WorkshopType.Type2, 20),
            new("W2c", "Sketch",   WorkshopType.Type2, 20),
        };

        var person = new Person("P1", "Alice",
            Prefs("W2a", "W2b", "W2c", "W2a", "W2b", "W2c"));

        var groups = new List<FriendGroup> { Solo(person) };
        var input = new AssignmentInput(workshops, new List<Person> { person }, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // Person should be assigned (not left unassigned)
        Assert.Equal(1, result.AssignedCount);
        Assert.Equal(0, result.UnassignedCount);

        var assignment = result.Assignments.First(a => a.PersonId == "P1");

        // Should have exactly 1 workshop (half-day solo, not a pair)
        Assert.Single(assignment.WorkshopIds);
        Assert.False(assignment.IsFullDay, "Half-day solo should have IsFullDay=false");

        // The workshop should be their top preference
        Assert.Equal("W2a", assignment.WorkshopIds[0]);

        // Scoring: NOT doubled (only half a day)
        // Rank 1 → score = 7 - 1 = 6 (not 12)
        Assert.Equal(1, assignment.WishRank);
        Assert.Equal(new List<int> { 1 }, assignment.PreferenceRanks);
        Assert.Equal(6, assignment.SatisfactionScore); // 7-1=6, NOT doubled

        // SlotRankDistribution should have 1 entry (not 2 like Type1)
        var slotDist = result.SlotRankDistribution;
        Assert.Equal(1, slotDist.Values.Sum()); // 1 slot entry, not 2

        // Run full reconciliation
        AssertAssignmentReconciliation(input, result, workshops);
    }

    [Fact]
    public void Solve_HalfDaySolo_PrefersPairOverSolo()
    {
        // Person prefers Type2 workshops (no valid pair among them) BUT also has
        // a Type3 in their preferences that CAN form a valid pair with a Type2.
        // The solver should prefer the pair over a solo assignment.
        var workshops = new List<Workshop>
        {
            new("W2a", "Painting", WorkshopType.Type2, 20),
            new("W2b", "Drawing",  WorkshopType.Type2, 20),
            new("W3",  "Cooking",  WorkshopType.Type3, 20),
        };

        var person = new Person("P1", "Alice",
            Prefs("W2a", "W2b", "W3", "W2a", "W2b", "W3"));

        var groups = new List<FriendGroup> { Solo(person) };
        var input = new AssignmentInput(workshops, new List<Person> { person }, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        var assignment = result.Assignments.First(a => a.PersonId == "P1");

        // Should get a pair (W2a+W3 or W2b+W3), NOT a half-day solo
        Assert.Equal(2, assignment.WorkshopIds.Count);
        Assert.False(assignment.IsFullDay);

        // The pair should include one Type2 and one Type3
        Assert.Contains("W3", assignment.WorkshopIds);
        Assert.True(
            assignment.WorkshopIds.Contains("W2a") || assignment.WorkshopIds.Contains("W2b"),
            "Pair should include a Type2 workshop");

        // Run full reconciliation
        AssertAssignmentReconciliation(input, result, workshops);
    }

    [Fact]
    public void Solve_HalfDaySolo_Type4SoloTrackedInSlot2()
    {
        // Person prefers only Type4 workshops with different IDs but capacity prevents pairing
        // (capacity=1 means each Type4 can only hold 1 person per slot)
        // With 2 persons and capacity=1 per Type4, one person gets a pair,
        // the other may get a solo Type4
        var workshops = new List<Workshop>
        {
            new("W4a", "Flex1", WorkshopType.Type4, 1), // capacity 1
            new("W4b", "Flex2", WorkshopType.Type4, 1), // capacity 1
        };

        // P1 and P2 both want W4a+W4b pair, but capacity=1 means only 1 can get the pair
        var p1 = new Person("P1", "Alice", Prefs("W4a", "W4b", "W4a", "W4b", "W4a", "W4b"));
        var p2 = new Person("P2", "Bob",   Prefs("W4a", "W4b", "W4a", "W4b", "W4a", "W4b"));

        var groups = new List<FriendGroup> { Solo(p1), Solo(p2) };
        var input = new AssignmentInput(workshops, new List<Person> { p1, p2 }, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // Both should be assigned (either pair or solo)
        Assert.Equal(2, result.AssignedCount);
        Assert.Equal(0, result.UnassignedCount);

        // At least one should have a half-day solo OR both get different solos
        // The key point: nobody is left unassigned due to capacity
        foreach (var a in result.Assignments)
        {
            Assert.True(a.WorkshopIds.Count >= 1,
                $"Person '{a.PersonId}' should be assigned at least 1 workshop");
        }

        // Run full reconciliation
        AssertAssignmentReconciliation(input, result, workshops);
    }

    [Fact]
    public void Solve_HalfDaySolo_ScoringNotDoubled()
    {
        // Verifies that half-day solo satisfaction score is NOT doubled
        // Compare: Type1 at rank 2 gets (7-2)*2=10, half-day solo at rank 2 gets (7-2)=5
        var workshops = new List<Workshop>
        {
            new("W3", "Cooking", WorkshopType.Type3, 20),
        };

        // Person's only option is a single Type3 (no pairs possible, no Type1)
        var person = new Person("P1", "Alice",
            Prefs("W3", "W3", "W3", "W3", "W3", "W3"));

        var groups = new List<FriendGroup> { Solo(person) };
        var input = new AssignmentInput(workshops, new List<Person> { person }, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        var assignment = result.Assignments.First(a => a.PersonId == "P1");

        Assert.Single(assignment.WorkshopIds);
        Assert.Equal("W3", assignment.WorkshopIds[0]);
        Assert.False(assignment.IsFullDay);
        Assert.Equal(1, assignment.WishRank);
        Assert.Equal(6, assignment.SatisfactionScore); // 7-1=6, NOT 12

        // Run full reconciliation
        AssertAssignmentReconciliation(input, result, workshops);
    }


    // == 41. Reconciliation — Small Dataset ==

    [Fact]
    public void Solve_Reconciliation_SmallDataset()
    {
        // Uses StandardWorkshops (6 workshops, all 4 types, with MinCapacity)
        // and StandardPersons (5 persons, friends P1-P2, mixed preferences)
        // Validates the reconciliation cross-check works on known good data
        var workshops = TestHelpers.StandardWorkshops();
        var persons = TestHelpers.StandardPersons();

        // Build friend groups: P1-P2 are mutual friends, rest are solo
        var p1 = persons.First(p => p.Id == "P1");
        var p2 = persons.First(p => p.Id == "P2");
        var groups = new List<FriendGroup>
        {
            Pair(p1, p2),
            Solo(persons.First(p => p.Id == "P3")),
            Solo(persons.First(p => p.Id == "P4")),
            Solo(persons.First(p => p.Id == "P5")),
        };

        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // Basic sanity — solver should assign everyone with ample capacity
        Assert.Equal(5, result.TotalPersons);
        Assert.True(result.AssignedCount > 0, "At least some persons should be assigned");

        // Run the full reconciliation cross-check
        AssertAssignmentReconciliation(input, result, workshops);
    }


    // == 42. Reconciliation — Stress Test (20 workshops, 50 persons) ==

    [Fact]
    public void Solve_Reconciliation_StressTest()
    {
        // 20 workshops (mix of all 4 types, varied capacities 5-30, some with MinCapacity)
        // 50 persons with seeded-random but valid preferences (6 each)
        // Some friend pairs
        // Validates reconciliation under realistic load
        var workshops = new List<Workshop>
        {
            new("S01", "FullDay-A",    WorkshopType.Type1, 15, 3),
            new("S02", "FullDay-B",    WorkshopType.Type1, 10),
            new("S03", "Morning-A",    WorkshopType.Type2, 20, 5),
            new("S04", "Morning-B",    WorkshopType.Type2, 25),
            new("S05", "Morning-C",    WorkshopType.Type2, 8, 2),
            new("S06", "Afternoon-A",  WorkshopType.Type3, 15, 4),
            new("S07", "Afternoon-B",  WorkshopType.Type3, 30),
            new("S08", "Afternoon-C",  WorkshopType.Type3, 12, 3),
            new("S09", "Afternoon-D",  WorkshopType.Type3, 20),
            new("S10", "Flexible-A",   WorkshopType.Type4, 18, 5),
            new("S11", "Flexible-B",   WorkshopType.Type4, 25),
            new("S12", "Flexible-C",   WorkshopType.Type4, 10, 3),
            new("S13", "Morning-D",    WorkshopType.Type2, 15),
            new("S14", "Morning-E",    WorkshopType.Type2, 5, 2),
            new("S15", "Afternoon-E",  WorkshopType.Type3, 22),
            new("S16", "Flexible-D",   WorkshopType.Type4, 30),
            new("S17", "FullDay-C",    WorkshopType.Type1, 20, 5),
            new("S18", "Morning-F",    WorkshopType.Type2, 12),
            new("S19", "Afternoon-F",  WorkshopType.Type3, 8, 2),
            new("S20", "Flexible-E",   WorkshopType.Type4, 15),
        };

        var wsIds = workshops.Select(w => w.Id).ToList();
        var rng = new Random(42); // Seeded for reproducibility

        var persons = new List<Person>();
        for (int i = 1; i <= 50; i++)
        {
            // Each person gets 6 random but unique preferences
            var shuffled = wsIds.OrderBy(_ => rng.Next()).Take(6).ToList();
            string? friendId = null;

            // Create some friend pairs: person 1&2, 5&6, 9&10, etc.
            if (i % 4 == 1 && i + 1 <= 50) friendId = $"SP{i + 1}";
            else if (i % 4 == 2) friendId = $"SP{i - 1}";

            persons.Add(new Person($"SP{i}", $"StressPerson{i}", shuffled, FriendId: friendId));
        }

        // Build friend groups
        var groups = new List<FriendGroup>();
        var grouped = new HashSet<string>();
        foreach (var p in persons)
        {
            if (grouped.Contains(p.Id)) continue;

            if (p.FriendId != null && !grouped.Contains(p.FriendId))
            {
                var friend = persons.FirstOrDefault(f => f.Id == p.FriendId);
                if (friend != null && friend.FriendId == p.Id)
                {
                    groups.Add(Pair(p, friend));
                    grouped.Add(p.Id);
                    grouped.Add(friend.Id);
                    continue;
                }
            }
            groups.Add(Solo(p));
            grouped.Add(p.Id);
        }

        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // Basic sanity
        Assert.Equal(50, result.TotalPersons);

        // Run the full reconciliation cross-check
        AssertAssignmentReconciliation(input, result, workshops);
    }


    // == 43. Reconciliation — High Contention ==

    [Fact]
    public void Solve_Reconciliation_HighContention()
    {
        // 5 workshops with small capacity (3-5 each)
        // 30 persons all wanting the same popular workshops
        // Heavy contention forces fallbacks and possibly cancellations
        // Validates numbers still add up under maximum stress
        var workshops = new List<Workshop>
        {
            new("HC1", "HotTopic",     WorkshopType.Type2, 3, 2),
            new("HC2", "PopularPM",    WorkshopType.Type3, 4, 2),
            new("HC3", "TrendyFlex",   WorkshopType.Type4, 5, 2),
            new("HC4", "NicheMorning", WorkshopType.Type2, 4),
            new("HC5", "NicheAfternoon", WorkshopType.Type3, 3, 2),
        };

        var persons = new List<Person>();
        for (int i = 1; i <= 30; i++)
        {
            // Everyone wants the same popular workshops first, with minor variations
            // Preferences are rotated slightly so some people get different fallbacks
            var basePrefs = new[] { "HC1", "HC2", "HC3", "HC4", "HC5" };
            var offset = (i - 1) % 5;
            var rotated = basePrefs.Skip(offset).Concat(basePrefs.Take(offset)).ToList();
            // Ensure exactly 6 preferences (repeat first as 6th to fill the slot)
            rotated.Add(rotated[0]);

            persons.Add(new Person($"HCP{i}", $"ContentionPerson{i}", rotated));
        }

        var groups = persons.Select(Solo).ToList();
        var input = new AssignmentInput(workshops, persons, groups);
        var result = _svc.Solve(input, timeLimitSeconds: 10);

        // Basic sanity — many will be unassigned due to extreme contention
        Assert.Equal(30, result.TotalPersons);

        // Total capacity: HC1(3) + HC2(4) + HC3(5+5 per slot) + HC4(4) + HC5(3) = max ~19 persons
        // (Type4 HC3 can serve different persons in each slot)
        // So most of 30 should be assigned but some might not be
        Assert.True(result.AssignedCount + result.UnassignedCount == 30,
            "Assigned + Unassigned must equal total persons");

        // Run the full reconciliation cross-check
        AssertAssignmentReconciliation(input, result, workshops);
    }

}
