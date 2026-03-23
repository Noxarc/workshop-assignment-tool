using ClosedXML.Excel;
using WorkshopAssignment.Models;
using WorkshopAssignment.Services;
using Xunit;

namespace WorkshopAssignment.Tests;

public class ExcelServiceTests : IDisposable
{
    private readonly ExcelService _svc = new();
    private readonly string _tempDir = TestHelpers.CreateTempDir();

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string TempPath(string filename) => Path.Combine(_tempDir, filename);

    // ──────────────────────────────────────────────
    // LoadWorkshops
    // ──────────────────────────────────────────────

    [Fact]
    public void LoadWorkshops_ValidFile_ReturnsWorkshops()
    {
        // Arrange: multiple workshops of different types
        var path = TempPath("workshops_valid.xlsx");
        TestHelpers.CreateWorkshopsExcel(path,
        [
            ("W1", "Yoga", 20, 5, "a"),
            ("W2", "Painting", 25, 3, "b"),
            ("W3", "Cooking", 15, 0, "c"),
            ("W4", "Music", 30, 10, "d"),
        ]);

        // Act
        var (workshops, warnings) = _svc.LoadWorkshops(path);

        // Assert
        Assert.Equal(4, workshops.Count);
        Assert.Empty(warnings);

        Assert.Equal("W1", workshops[0].Id);
        Assert.Equal("Yoga", workshops[0].Name);
        Assert.Equal(WorkshopType.Type1, workshops[0].Type);
        Assert.Equal(20, workshops[0].Capacity);
        Assert.Equal(5, workshops[0].MinCapacity);

        Assert.Equal("W2", workshops[1].Id);
        Assert.Equal("Painting", workshops[1].Name);
        Assert.Equal(WorkshopType.Type2, workshops[1].Type);
        Assert.Equal(25, workshops[1].Capacity);
        Assert.Equal(3, workshops[1].MinCapacity);

        Assert.Equal("W3", workshops[2].Id);
        Assert.Equal("Cooking", workshops[2].Name);
        Assert.Equal(WorkshopType.Type3, workshops[2].Type);
        Assert.Equal(15, workshops[2].Capacity);
        Assert.Equal(0, workshops[2].MinCapacity);

        Assert.Equal("W4", workshops[3].Id);
        Assert.Equal("Music", workshops[3].Name);
        Assert.Equal(WorkshopType.Type4, workshops[3].Type);
        Assert.Equal(30, workshops[3].Capacity);
        Assert.Equal(10, workshops[3].MinCapacity);
    }

    [Fact]
    public void LoadWorkshops_ColumnDiscovery_CaseInsensitive()
    {
        // Arrange: use mixed-case headers that the service should recognize
        var path = TempPath("workshops_case.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "ID";            // uppercase
            ws.Cell(1, 2).Value = "Bezeichnung";    // mixed case
            ws.Cell(1, 3).Value = "Kapazität";      // mixed case with umlaut
            ws.Cell(1, 4).Value = "Zeitfenster";     // mixed case
            ws.Cell(1, 5).Value = "Mindestkapazität";

            ws.Cell(2, 1).Value = "W1";
            ws.Cell(2, 2).Value = "TestWorkshop";
            ws.Cell(2, 3).Value = 15;
            ws.Cell(2, 4).Value = "b";
            ws.Cell(2, 5).Value = 3;

            wb.SaveAs(path);
        }

        // Act
        var (workshops, warnings) = _svc.LoadWorkshops(path);

        // Assert: columns found despite casing (headers are lowered internally)
        Assert.Single(workshops);
        Assert.Equal("W1", workshops[0].Id);
        Assert.Equal("TestWorkshop", workshops[0].Name);
        Assert.Equal(WorkshopType.Type2, workshops[0].Type);
        Assert.Equal(15, workshops[0].Capacity);
        Assert.Equal(3, workshops[0].MinCapacity);
    }

    [Fact]
    public void LoadWorkshops_TypeMapping_AllTimeslots()
    {
        // Arrange: one workshop per timeslot letter
        var path = TempPath("workshops_timeslots.xlsx");
        TestHelpers.CreateWorkshopsExcel(path,
        [
            ("W1", "A-Workshop", 10, 0, "a"),
            ("W2", "B-Workshop", 10, 0, "b"),
            ("W3", "C-Workshop", 10, 0, "c"),
            ("W4", "D-Workshop", 10, 0, "d"),
        ]);

        // Act
        var (workshops, _) = _svc.LoadWorkshops(path);

        // Assert
        Assert.Equal(WorkshopType.Type1, workshops[0].Type);
        Assert.Equal(WorkshopType.Type2, workshops[1].Type);
        Assert.Equal(WorkshopType.Type3, workshops[2].Type);
        Assert.Equal(WorkshopType.Type4, workshops[3].Type);
    }

    [Fact]
    public void LoadWorkshops_CapacityParsing_DefaultsTo30OnFailure()
    {
        // Arrange: non-numeric capacity string
        var path = TempPath("workshops_badcap.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "id";
            ws.Cell(1, 2).Value = "bezeichnung";
            ws.Cell(1, 3).Value = "kapazität";
            ws.Cell(1, 4).Value = "zeitfenster";

            ws.Cell(2, 1).Value = "W1";
            ws.Cell(2, 2).Value = "BadCap";
            ws.Cell(2, 3).Value = "not-a-number"; // non-numeric
            ws.Cell(2, 4).Value = "a";

            wb.SaveAs(path);
        }

        // Act
        var (workshops, _) = _svc.LoadWorkshops(path);

        // Assert: defaults to 30 (clamped by Math.Max(1, 30) = 30)
        Assert.Single(workshops);
        Assert.Equal(30, workshops[0].Capacity);
    }

    [Fact]
    public void LoadWorkshops_DuplicateIds_ReturnsWarning()
    {
        // Arrange: two workshops with same ID
        var path = TempPath("workshops_dup.xlsx");
        TestHelpers.CreateWorkshopsExcel(path,
        [
            ("W1", "Yoga", 20, 0, "a"),
            ("W1", "Painting", 25, 0, "b"),   // duplicate ID
            ("W2", "Cooking", 15, 0, "c"),
        ]);

        // Act
        var (workshops, warnings) = _svc.LoadWorkshops(path);

        // Assert: second W1 is skipped
        Assert.Equal(2, workshops.Count);
        Assert.Equal("Yoga", workshops[0].Name);
        Assert.Equal("Cooking", workshops[1].Name);

        Assert.Single(warnings);
        Assert.Equal(WarningCategory.DuplicateId, warnings[0].Category);
        Assert.Contains("W1", warnings[0].Message);
    }

    [Fact]
    public void LoadWorkshops_MissingRequiredColumns_Throws()
    {
        // Arrange: file without ID column — FindColumn returns 0,
        // so row.Cell(0) will throw from ClosedXML
        var path = TempPath("workshops_nocol.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "something_else";
            ws.Cell(1, 2).Value = "bezeichnung";

            ws.Cell(2, 1).Value = "val1";
            ws.Cell(2, 2).Value = "val2";

            wb.SaveAs(path);
        }

        // Act & Assert: should throw because ID column maps to col 0, and Cell(0) is invalid
        Assert.ThrowsAny<Exception>(() => _svc.LoadWorkshops(path));
    }

    [Fact]
    public void LoadWorkshops_EmptyFile_ReturnsEmpty()
    {
        // Arrange: file with only headers, no data rows
        var path = TempPath("workshops_empty.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "id";
            ws.Cell(1, 2).Value = "bezeichnung";
            ws.Cell(1, 3).Value = "kapazität";
            ws.Cell(1, 4).Value = "zeitfenster";

            wb.SaveAs(path);
        }

        // Act
        var (workshops, warnings) = _svc.LoadWorkshops(path);

        // Assert
        Assert.Empty(workshops);
        // Empty file now returns EmptySheet warning
        Assert.Single(warnings);
        Assert.Equal(WarningCategory.EmptySheet, warnings[0].Category);
    }

    [Fact]
    public void LoadWorkshops_InvalidTimeslot_ReturnsWarning()
    {
        // Arrange: timeslot value "x" — not in mapping
        var path = TempPath("workshops_badslot.xlsx");
        TestHelpers.CreateWorkshopsExcel(path,
        [
            ("W1", "BadSlot", 10, 0, "x"),
        ]);

        // Act
        var (workshops, warnings) = _svc.LoadWorkshops(path);

        // Assert: invalid timeslot now skips the row entirely
        Assert.Empty(workshops);

        Assert.Single(warnings);
        Assert.Equal(WarningCategory.WorkshopSkipped, warnings[0].Category);
        Assert.Contains("x", warnings[0].Message);
    }

    [Fact]
    public void LoadWorkshops_MinCapacityParsing_DefaultsToZero()
    {
        // Arrange: non-numeric min capacity
        var path = TempPath("workshops_badmin.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "id";
            ws.Cell(1, 2).Value = "bezeichnung";
            ws.Cell(1, 3).Value = "kapazität";
            ws.Cell(1, 4).Value = "mindestkapazität";
            ws.Cell(1, 5).Value = "zeitfenster";

            ws.Cell(2, 1).Value = "W1";
            ws.Cell(2, 2).Value = "TestWS";
            ws.Cell(2, 3).Value = 20;
            ws.Cell(2, 4).Value = "invalid";  // non-numeric
            ws.Cell(2, 5).Value = "a";

            wb.SaveAs(path);
        }

        // Act
        var (workshops, _) = _svc.LoadWorkshops(path);

        // Assert
        Assert.Single(workshops);
        Assert.Equal(0, workshops[0].MinCapacity);
    }

    [Fact]
    public void LoadWorkshops_EmptyId_SkipsRowWithWarning()
    {
        // Arrange: row with empty string for ID
        var path = TempPath("workshops_empty_id.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Workshops");
            ws.Cell(1, 1).Value = "ID";
            ws.Cell(1, 2).Value = "Bezeichnung";
            ws.Cell(1, 3).Value = "Kapazität";
            ws.Cell(1, 4).Value = "Zeitfenster";

            // Row 2: empty ID (should be skipped silently)
            ws.Cell(2, 1).Value = "";
            ws.Cell(2, 2).Value = "Ghost Workshop";
            ws.Cell(2, 3).Value = 20;
            ws.Cell(2, 4).Value = "a";

            // Row 3: valid workshop
            ws.Cell(3, 1).Value = "W1";
            ws.Cell(3, 2).Value = "Valid Workshop";
            ws.Cell(3, 3).Value = 25;
            ws.Cell(3, 4).Value = "b";

            wb.SaveAs(path);
        }

        // Act
        var (workshops, warnings) = _svc.LoadWorkshops(path);

        // Assert: empty ID row is skipped silently (no warning for truly empty)
        Assert.Single(workshops);
        Assert.Equal("W1", workshops[0].Id);
        Assert.Equal("Valid Workshop", workshops[0].Name);
        // Empty string ID doesn't produce a warning (only whitespace-only does)
        Assert.Empty(warnings);
    }

    [Fact]
    public void LoadWorkshops_WhitespaceOnlyId_SkipsRowWithWarning()
    {
        // Arrange: row with whitespace-only ID
        var path = TempPath("workshops_whitespace_id.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Workshops");
            ws.Cell(1, 1).Value = "ID";
            ws.Cell(1, 2).Value = "Bezeichnung";
            ws.Cell(1, 3).Value = "Kapazität";
            ws.Cell(1, 4).Value = "Zeitfenster";

            // Row 2: whitespace-only ID (should be skipped with warning)
            ws.Cell(2, 1).Value = "   ";
            ws.Cell(2, 2).Value = "Ghost Workshop";
            ws.Cell(2, 3).Value = 20;
            ws.Cell(2, 4).Value = "a";

            // Row 3: valid workshop
            ws.Cell(3, 1).Value = "W1";
            ws.Cell(3, 2).Value = "Valid Workshop";
            ws.Cell(3, 3).Value = 25;
            ws.Cell(3, 4).Value = "b";

            wb.SaveAs(path);
        }

        // Act
        var (workshops, warnings) = _svc.LoadWorkshops(path);

        // Assert: whitespace-only ID row is skipped with warning
        Assert.Single(workshops);
        Assert.Equal("W1", workshops[0].Id);

        Assert.Single(warnings);
        Assert.Equal(WarningCategory.WhitespaceId, warnings[0].Category);
        Assert.Contains("whitespace", warnings[0].Message.ToLower());
    }

    [Fact]
    public void LoadWorkshops_EmptyName_AcceptsWithWarning()
    {
        // Arrange: workshop with empty name
        var path = TempPath("workshops_empty_name.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Workshops");
            ws.Cell(1, 1).Value = "ID";
            ws.Cell(1, 2).Value = "Bezeichnung";
            ws.Cell(1, 3).Value = "Kapazität";
            ws.Cell(1, 4).Value = "Zeitfenster";

            // Row 2: empty name
            ws.Cell(2, 1).Value = "W1";
            ws.Cell(2, 2).Value = "";
            ws.Cell(2, 3).Value = 20;
            ws.Cell(2, 4).Value = "a";

            wb.SaveAs(path);
        }

        // Act
        var (workshops, warnings) = _svc.LoadWorkshops(path);

        // Assert: workshop is accepted with warning, ID used as fallback name
        Assert.Single(workshops);
        Assert.Equal("W1", workshops[0].Id);
        Assert.Equal("W1", workshops[0].Name); // Falls back to ID

        Assert.Single(warnings);
        Assert.Equal(WarningCategory.EmptyWorkshopName, warnings[0].Category);
        Assert.Contains("W1", warnings[0].Message);
        Assert.Contains("no name", warnings[0].Message);
    }

    [Fact]
    public void LoadWorkshops_NegativeCapacity_SetsToZeroWithWarning()
    {
        // Arrange: workshop with negative capacity
        var path = TempPath("workshops_neg_cap.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Workshops");
            ws.Cell(1, 1).Value = "ID";
            ws.Cell(1, 2).Value = "Bezeichnung";
            ws.Cell(1, 3).Value = "Kapazität";
            ws.Cell(1, 4).Value = "Zeitfenster";

            ws.Cell(2, 1).Value = "W1";
            ws.Cell(2, 2).Value = "Negative Cap Workshop";
            ws.Cell(2, 3).Value = -5;
            ws.Cell(2, 4).Value = "a";

            wb.SaveAs(path);
        }

        // Act
        var (workshops, warnings) = _svc.LoadWorkshops(path);

        // Assert: capacity set to 0 with warning
        Assert.Single(workshops);
        Assert.Equal("W1", workshops[0].Id);
        Assert.Equal(0, workshops[0].Capacity);

        Assert.Single(warnings);
        Assert.Equal(WarningCategory.CapacityAdjusted, warnings[0].Category);
        Assert.Contains("negative", warnings[0].Message.ToLower());
        Assert.Contains("-5", warnings[0].Message);
    }

    [Fact]
    public void LoadWorkshops_ZeroCapacity_AcceptsAsIs()
    {
        // Arrange: workshop with zero capacity (valid, no warning needed)
        var path = TempPath("workshops_zero_cap.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Workshops");
            ws.Cell(1, 1).Value = "ID";
            ws.Cell(1, 2).Value = "Bezeichnung";
            ws.Cell(1, 3).Value = "Kapazität";
            ws.Cell(1, 4).Value = "Zeitfenster";

            ws.Cell(2, 1).Value = "W1";
            ws.Cell(2, 2).Value = "Zero Cap Workshop";
            ws.Cell(2, 3).Value = 0;
            ws.Cell(2, 4).Value = "a";

            wb.SaveAs(path);
        }

        // Act
        var (workshops, warnings) = _svc.LoadWorkshops(path);

        // Assert: zero capacity is valid as-is, no warning
        Assert.Single(workshops);
        Assert.Equal("W1", workshops[0].Id);
        Assert.Equal(0, workshops[0].Capacity);
        Assert.Empty(warnings);
    }

    [Fact]
    public void LoadWorkshops_MinGreaterThanMax_AdjustsWithWarning()
    {
        // Arrange: workshop with min_capacity > capacity
        var path = TempPath("workshops_min_gt_max.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Workshops");
            ws.Cell(1, 1).Value = "ID";
            ws.Cell(1, 2).Value = "Bezeichnung";
            ws.Cell(1, 3).Value = "Kapazität";
            ws.Cell(1, 4).Value = "Mindestkapazität";
            ws.Cell(1, 5).Value = "Zeitfenster";

            ws.Cell(2, 1).Value = "W1";
            ws.Cell(2, 2).Value = "Min > Max Workshop";
            ws.Cell(2, 3).Value = 10;
            ws.Cell(2, 4).Value = 20; // min > capacity
            ws.Cell(2, 5).Value = "a";

            wb.SaveAs(path);
        }

        // Act
        var (workshops, warnings) = _svc.LoadWorkshops(path);

        // Assert: min_capacity adjusted to capacity with warning
        Assert.Single(workshops);
        Assert.Equal("W1", workshops[0].Id);
        Assert.Equal(10, workshops[0].Capacity);
        Assert.Equal(10, workshops[0].MinCapacity); // Adjusted to match capacity

        Assert.Single(warnings);
        Assert.Equal(WarningCategory.CapacityAdjusted, warnings[0].Category);
        Assert.Contains("min", warnings[0].Message.ToLower());
        Assert.Contains("exceeds", warnings[0].Message.ToLower());
    }

    [Fact]
    public void LoadWorkshops_NegativeMinCapacity_SetsToZeroWithWarning()
    {
        // Arrange: workshop with negative min_capacity
        var path = TempPath("workshops_neg_min.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Workshops");
            ws.Cell(1, 1).Value = "ID";
            ws.Cell(1, 2).Value = "Bezeichnung";
            ws.Cell(1, 3).Value = "Kapazität";
            ws.Cell(1, 4).Value = "Mindestkapazität";
            ws.Cell(1, 5).Value = "Zeitfenster";

            ws.Cell(2, 1).Value = "W1";
            ws.Cell(2, 2).Value = "Negative Min Workshop";
            ws.Cell(2, 3).Value = 20;
            ws.Cell(2, 4).Value = -3;
            ws.Cell(2, 5).Value = "a";

            wb.SaveAs(path);
        }

        // Act
        var (workshops, warnings) = _svc.LoadWorkshops(path);

        // Assert: min_capacity set to 0 with warning
        Assert.Single(workshops);
        Assert.Equal("W1", workshops[0].Id);
        Assert.Equal(20, workshops[0].Capacity);
        Assert.Equal(0, workshops[0].MinCapacity);

        Assert.Single(warnings);
        Assert.Equal(WarningCategory.CapacityAdjusted, warnings[0].Category);
        Assert.Contains("negative", warnings[0].Message.ToLower());
        Assert.Contains("min", warnings[0].Message.ToLower());
        Assert.Contains("-3", warnings[0].Message);
    }

    [Fact]
    public void LoadPersons_FriendReferences_Parsed()
    {
        // Arrange: use unique workshop IDs across all preferences
        var validIds = new HashSet<string> { "W1", "W2", "W3", "W4", "W5", "W6", "W7", "W8", "W9", "WA", "WB", "WC" };
        var path = TempPath("persons_friend.xlsx");
        TestHelpers.CreatePersonsExcel(path,
        [
            ("P1", "Alice", "A", null, "P2", "W1", "W2", "W3", "W4", "W5", "W6"),
            ("P2", "Bob", "B", null, "P1", "W7", "W8", "W9", "WA", "WB", "WC"),
        ]);

        // Act
        var (persons, warnings) = _svc.LoadPersons(path, validIds);

        // Assert
        Assert.Equal(2, persons.Count);
        Assert.Equal("P2", persons[0].FriendId);
        Assert.Equal("P1", persons[1].FriendId);
        // Mutual friend references (P1->P2 and P2->P1) are valid, no warnings
        Assert.Empty(warnings);
    }

    [Fact]
    public void LoadPersons_SixPreferenceFormat_ParsesCorrectly()
    {
        // Arrange: 6 individual preference columns with one workshop ID each
        var validIds = new HashSet<string> { "W1", "W2", "W3", "W4", "W5", "W6" };
        var path = TempPath("persons_6pref.xlsx");
        TestHelpers.CreatePersonsExcel(path,
        [
            ("P1", "Alice", "Smith", "GroupA", "P2", "W1", "W2", "W3", "W4", "W5", "W6"),
        ]);

        // Act
        var (persons, warnings) = _svc.LoadPersons(path, validIds);

        // Assert
        Assert.Single(persons);
        Assert.Empty(warnings);

        var p = persons[0];
        Assert.Equal("P1", p.Id);
        Assert.Equal("Alice Smith", p.Name);
        Assert.Equal("GroupA", p.Info);
        Assert.Equal("P2", p.FriendId);

        // 6 individual preferences, rank ordered by column position
        Assert.Equal(6, p.Preferences.Count);
        Assert.Equal("W1", p.Preferences[0]); // Rank 1
        Assert.Equal("W2", p.Preferences[1]); // Rank 2
        Assert.Equal("W3", p.Preferences[2]); // Rank 3
        Assert.Equal("W4", p.Preferences[3]); // Rank 4
        Assert.Equal("W5", p.Preferences[4]); // Rank 5
        Assert.Equal("W6", p.Preferences[5]); // Rank 6
    }

    [Fact]
    public void LoadPersons_UnknownWorkshopIds_ReturnsWarning()
    {
        // Arrange: preference references "W99" which is not in validIds
        var validIds = new HashSet<string> { "W1", "W2", "W3", "W4", "W5" };
        var path = TempPath("persons_unknown.xlsx");
        TestHelpers.CreatePersonsExcel(path,
        [
            ("P1", "Carol", "White", null, null, "W1", "W99", "W3", "W4", "W5", ""),
        ]);

        // Act
        var (persons, warnings) = _svc.LoadPersons(path, validIds);

        // Assert
        Assert.Single(persons);
        // Warning for unknown workshop W99
        Assert.Single(warnings);
        Assert.Equal(WarningCategory.UnknownWorkshop, warnings[0].Category);
        Assert.Contains("W99", warnings[0].Message);

        // The unknown ID is excluded from preferences, so person has 4 valid preferences
        Assert.Equal(4, persons[0].Preferences.Count);
        Assert.DoesNotContain("W99", persons[0].Preferences);
    }

    [Fact]
    public void LoadPersons_DuplicatePersonIds_ReturnsWarning()
    {
        // Arrange: use unique workshop IDs
        var validIds = new HashSet<string> { "W1", "W2", "W3", "W4", "W5", "W6", "W7", "W8", "W9" };
        var path = TempPath("persons_dup.xlsx");
        TestHelpers.CreatePersonsExcel(path,
        [
            ("P1", "Alice", "A", null, null, "W1", "W2", "W3", "W4", "W5", "W6"),
            ("P1", "Bob", "B", null, null, "W4", "W5", "W6", "W7", "W8", "W9"),  // duplicate person ID
            ("P2", "Carol", "C", null, null, "W7", "W8", "W9", "W1", "W2", "W3"),
        ]);

        // Act
        var (persons, warnings) = _svc.LoadPersons(path, validIds);

        // Assert: second P1 is skipped
        Assert.Equal(2, persons.Count);
        Assert.Equal("Alice A", persons[0].Name);
        Assert.Equal("Carol C", persons[1].Name);

        Assert.Single(warnings);
        Assert.Equal(WarningCategory.DuplicateId, warnings[0].Category);
        Assert.Contains("P1", warnings[0].Message);
    }

    [Fact]
    public void LoadPersons_SelfReferenceFriend_ReturnsWarning()
    {
        // Arrange: person's friend-id = their own id
        var validIds = new HashSet<string> { "W1", "W2", "W3", "W4", "W5", "W6" };
        var path = TempPath("persons_self.xlsx");
        TestHelpers.CreatePersonsExcel(path,
        [
            ("P1", "Alice", "A", null, "P1", "W1", "W2", "W3", "W4", "W5", "W6"), // self-reference
        ]);

        // Act
        var (persons, warnings) = _svc.LoadPersons(path, validIds);

        // Assert
        Assert.Single(persons);
        Assert.Null(persons[0].FriendId); // self-reference is ignored
        Assert.Single(warnings);
        Assert.Equal(WarningCategory.SelfReference, warnings[0].Category);
    }

    [Fact]
    public void LoadPersons_EmptyPreferences_HandledGracefully()
    {
        // Arrange: all preference cells are empty
        var validIds = new HashSet<string> { "W1", "W2" };
        var path = TempPath("persons_empty_prefs.xlsx");
        TestHelpers.CreatePersonsExcel(path,
        [
            ("P1", "Alice", "A", null, null, "", "", "", "", "", ""),
        ]);

        // Act
        var (persons, warnings) = _svc.LoadPersons(path, validIds);

        // Assert: should not crash, person has empty preferences list
        Assert.Single(persons);
        Assert.Empty(warnings);
        Assert.Empty(persons[0].Preferences);
    }

    [Fact]
    public void LoadPersons_FewerThan3Preferences_AcceptsWithWarning()
    {
        // Arrange: person has only 2 preferences (Wunsch 3-6 empty)
        var validIds = new HashSet<string> { "W1", "W2", "W3" };
        var path = TempPath("persons_fewer_prefs.xlsx");
        TestHelpers.CreatePersonsExcel(path,
        [
            ("P1", "Alice", "Smith", null, null, "W1", "W2", "", "", "", ""),
        ]);

        // Act
        var (persons, warnings) = _svc.LoadPersons(path, validIds);

        // Assert: person is accepted
        Assert.Single(persons);
        Assert.Equal("P1", persons[0].Id);

        // Only 2 preferences in the list (empty columns skipped)
        Assert.Equal(2, persons[0].Preferences.Count);
        Assert.Equal("W1", persons[0].Preferences[0]);
        Assert.Equal("W2", persons[0].Preferences[1]);

        // Should warn about reduced options (IncompleteWish category)
        Assert.Single(warnings);
        Assert.Equal(WarningCategory.IncompleteWish, warnings[0].Category);
        Assert.Contains("P1", warnings[0].Message);
        Assert.Contains("2", warnings[0].Message); // mentions only 2 preferences
    }

    [Fact]
    public void LoadPersons_DuplicatePreferenceAcrossColumns_SkipsWithWarning()
    {
        // Arrange: person has same workshop in Wunsch 1 and Wunsch 2 (duplicate across preference columns)
        var validIds = new HashSet<string> { "W1", "W2", "W3", "W4", "W5" };
        var path = TempPath("persons_dup_pref.xlsx");
        TestHelpers.CreatePersonsExcel(path,
        [
            ("P1", "Alice", "Smith", null, null, "W1", "W1", "W2", "W3", "W4", "W5"), // W1 in col 1 and 2
        ]);

        // Act
        var (persons, warnings) = _svc.LoadPersons(path, validIds);

        // Assert: person is accepted
        Assert.Single(persons);
        Assert.Equal("P1", persons[0].Id);

        // Duplicate W1 in column 2 is skipped with warning
        Assert.Single(warnings);
        Assert.Equal(WarningCategory.DuplicateId, warnings[0].Category);
        Assert.Contains("P1", warnings[0].Message);
        Assert.Contains("W1", warnings[0].Message);

        // Only 5 preferences (W1 counted once, W2-W5 from remaining columns)
        Assert.Equal(5, persons[0].Preferences.Count);
        Assert.Equal("W1", persons[0].Preferences[0]);
        Assert.Equal("W2", persons[0].Preferences[1]);
        Assert.Equal("W3", persons[0].Preferences[2]);
    }

    [Fact]
    public void LoadPersons_EmptyId_SkipsRowWithWarning()
    {
        // Arrange: row with empty string for ID
        var validIds = new HashSet<string> { "W1", "W2", "W3", "W4", "W5", "W6" };
        var path = TempPath("persons_empty_id.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Persons");
            ws.Cell(1, 1).Value = "ID";
            ws.Cell(1, 2).Value = "Vorname";
            ws.Cell(1, 3).Value = "Nachname";
            ws.Cell(1, 4).Value = "Wunsch 1";
            ws.Cell(1, 5).Value = "Wunsch 2";
            ws.Cell(1, 6).Value = "Wunsch 3";
            ws.Cell(1, 7).Value = "Wunsch 4";
            ws.Cell(1, 8).Value = "Wunsch 5";
            ws.Cell(1, 9).Value = "Wunsch 6";

            // Row 2: empty ID (should be skipped)
            ws.Cell(2, 1).Value = "";
            ws.Cell(2, 2).Value = "Ghost";
            ws.Cell(2, 3).Value = "Person";
            ws.Cell(2, 4).Value = "W1";
            ws.Cell(2, 5).Value = "W2";
            ws.Cell(2, 6).Value = "W3";

            // Row 3: valid person
            ws.Cell(3, 1).Value = "P1";
            ws.Cell(3, 2).Value = "Alice";
            ws.Cell(3, 3).Value = "Smith";
            ws.Cell(3, 4).Value = "W1";
            ws.Cell(3, 5).Value = "W2";
            ws.Cell(3, 6).Value = "W3";
            ws.Cell(3, 7).Value = "W4";
            ws.Cell(3, 8).Value = "W5";
            ws.Cell(3, 9).Value = "W6";

            wb.SaveAs(path);
        }

        // Act
        var (persons, warnings) = _svc.LoadPersons(path, validIds);

        // Assert: empty ID row is skipped silently (no warning for truly empty),
        // only valid person P1 is loaded
        Assert.Single(persons);
        Assert.Equal("P1", persons[0].Id);
        Assert.Equal("Alice Smith", persons[0].Name);
        // Empty string ID doesn't produce a warning (only whitespace-only does)
        Assert.Empty(warnings);
    }

    [Fact]
    public void LoadPersons_WhitespaceOnlyId_SkipsRowWithWarning()
    {
        // Arrange: row with whitespace-only ID
        var validIds = new HashSet<string> { "W1", "W2", "W3", "W4", "W5", "W6" };
        var path = TempPath("persons_whitespace_id.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Persons");
            ws.Cell(1, 1).Value = "ID";
            ws.Cell(1, 2).Value = "Vorname";
            ws.Cell(1, 3).Value = "Nachname";
            ws.Cell(1, 4).Value = "Wunsch 1";
            ws.Cell(1, 5).Value = "Wunsch 2";
            ws.Cell(1, 6).Value = "Wunsch 3";
            ws.Cell(1, 7).Value = "Wunsch 4";
            ws.Cell(1, 8).Value = "Wunsch 5";
            ws.Cell(1, 9).Value = "Wunsch 6";

            // Row 2: whitespace-only ID (should be skipped with warning)
            ws.Cell(2, 1).Value = "   ";
            ws.Cell(2, 2).Value = "Ghost";
            ws.Cell(2, 3).Value = "Person";
            ws.Cell(2, 4).Value = "W1";
            ws.Cell(2, 5).Value = "W2";
            ws.Cell(2, 6).Value = "W3";

            // Row 3: valid person
            ws.Cell(3, 1).Value = "P1";
            ws.Cell(3, 2).Value = "Alice";
            ws.Cell(3, 3).Value = "Smith";
            ws.Cell(3, 4).Value = "W1";
            ws.Cell(3, 5).Value = "W2";
            ws.Cell(3, 6).Value = "W3";
            ws.Cell(3, 7).Value = "W4";
            ws.Cell(3, 8).Value = "W5";
            ws.Cell(3, 9).Value = "W6";

            wb.SaveAs(path);
        }

        // Act
        var (persons, warnings) = _svc.LoadPersons(path, validIds);

        // Assert: whitespace-only ID row is skipped with warning
        Assert.Single(persons);
        Assert.Equal("P1", persons[0].Id);

        Assert.Single(warnings);
        Assert.Equal(WarningCategory.WhitespaceId, warnings[0].Category);
        Assert.Contains("whitespace", warnings[0].Message.ToLower());
    }

    [Fact]
    public void LoadPersons_EmptyName_AcceptsWithWarning()
    {
        // Arrange: person with empty first name and last name
        var validIds = new HashSet<string> { "W1", "W2", "W3", "W4", "W5", "W6" };
        var path = TempPath("persons_empty_name.xlsx");
        TestHelpers.CreatePersonsExcel(path,
        [
            ("P1", "", "", null, null, "W1", "W2", "W3", "W4", "W5", "W6"),
        ]);

        // Act
        var (persons, warnings) = _svc.LoadPersons(path, validIds);

        // Assert: person is accepted with warning about missing name
        Assert.Single(persons);
        Assert.Equal("P1", persons[0].Id);
        Assert.Equal("P1", persons[0].Name); // Falls back to ID

        Assert.Single(warnings);
        Assert.Equal(WarningCategory.EmptyPersonName, warnings[0].Category);
        Assert.Contains("P1", warnings[0].Message);
        Assert.Contains("no name", warnings[0].Message);
    }

    [Fact]
    public void LoadPersons_WhitespaceOnlyName_TreatedAsEmpty()
    {
        // Arrange: person with whitespace-only first and last name
        var validIds = new HashSet<string> { "W1", "W2", "W3", "W4", "W5", "W6" };
        var path = TempPath("persons_whitespace_name.xlsx");
        TestHelpers.CreatePersonsExcel(path,
        [
            ("P1", "   ", "   ", null, null, "W1", "W2", "W3", "W4", "W5", "W6"),
        ]);

        // Act
        var (persons, warnings) = _svc.LoadPersons(path, validIds);

        // Assert: whitespace treated as empty, person accepted with warning
        Assert.Single(persons);
        Assert.Equal("P1", persons[0].Id);
        Assert.Equal("P1", persons[0].Name); // Falls back to ID

        Assert.Single(warnings);
        Assert.Equal(WarningCategory.EmptyPersonName, warnings[0].Category);
        Assert.Contains("P1", warnings[0].Message);
    }

    [Fact]
    public void LoadPersons_DuplicateIdAcrossFiles_SkipsWithWarning()
    {
        // Arrange: two files with same person ID
        var validIds = new HashSet<string> { "W1", "W2", "W3", "W4", "W5", "W6", "W7", "W8", "W9", "WA", "WB", "WC" };

        // File A: person P001
        var pathA = TempPath("persons_fileA.xlsx");
        TestHelpers.CreatePersonsExcel(pathA,
        [
            ("P001", "Alice", "Smith", null, null, "W1", "W2", "W3", "W4", "W5", "W6"),
        ]);

        // File B: person P001 (duplicate) and P002 (new)
        var pathB = TempPath("persons_fileB.xlsx");
        TestHelpers.CreatePersonsExcel(pathB,
        [
            ("P001", "Bob", "Jones", null, null, "W4", "W5", "W6", "W7", "W8", "W9"),   // duplicate person ID
            ("P002", "Carol", "White", null, null, "W7", "W8", "W9", "WA", "WB", "WC"),
        ]);

        // Act: Load file A first
        var (personsA, warningsA) = _svc.LoadPersons(pathA, validIds);

        // Simulate MainViewModel's cross-file duplicate detection:
        var existingIds = personsA.Select(p => p.Id).ToHashSet();
        var (personsB, warningsB) = _svc.LoadPersons(pathB, validIds);

        // Detect cross-file duplicates (as MainViewModel does)
        var crossFileWarnings = new List<ImportWarning>();
        var newPersons = new List<Person>();
        foreach (var p in personsB)
        {
            if (existingIds.Contains(p.Id))
            {
                var original = personsA.First(orig => orig.Id == p.Id);
                crossFileWarnings.Add(new ImportWarning(
                    WarningCategory.DuplicateId,
                    $"Person {p.Id} already imported from {original.SourceFile}"));
            }
            else
            {
                newPersons.Add(p);
                existingIds.Add(p.Id);
            }
        }

        // Assert: File A loaded without issues
        Assert.Single(personsA);
        Assert.Equal("P001", personsA[0].Id);
        Assert.Equal("Alice Smith", personsA[0].Name);
        Assert.Empty(warningsA);

        // Assert: File B has P001 and P002, but P001 is a cross-file duplicate
        Assert.Equal(2, personsB.Count);
        Assert.Empty(warningsB);

        // Assert: Cross-file detection caught the duplicate
        Assert.Single(crossFileWarnings);
        Assert.Equal(WarningCategory.DuplicateId, crossFileWarnings[0].Category);
        Assert.Contains("P001", crossFileWarnings[0].Message);
        Assert.Contains("persons_fileA.xlsx", crossFileWarnings[0].Message);

        // Assert: Only new person P002 would be added
        Assert.Single(newPersons);
        Assert.Equal("P002", newPersons[0].Id);
    }

    // ──────────────────────────────────────────────────────
    // WriteWorkshopLeaderReport / WriteAttendeeReport
    // ──────────────────────────────────────────────────────

    [Fact]
    public void WriteWorkshopLeaderReport_CreatesValidExcel()
    {
        // Arrange
        var workshops = new List<Workshop>
        {
            new("W1", "Yoga", WorkshopType.Type1, 20, 5),
            new("W2", "Painting", WorkshopType.Type2, 25, 5),
        };

        var persons = new List<Person>
        {
            new("P1", "Alice Smith", ["W1", "W2", "W3", "W4", "W5", "W6"]),
            new("P2", "Bob Jones", ["W1", "W2", "W3", "W4", "W5", "W6"]),
        };

        // Both assigned to W1
        var result = new AssignmentResult(
        [
            new Assignment("P1", ["W1"], 1, [1], 12, IsFullDay: true),
            new Assignment("P2", ["W1"], 1, [1], 12, IsFullDay: true),
        ],
        []);

        var path = TempPath("leader_report.xlsx");

        // Act
        _svc.WriteWorkshopLeaderReport(path, result, workshops, persons, "Slot 1", "Slot 2", "Slot 3");

        // Assert: read it back
        using var wb = new XLWorkbook(path);

        // Overview sheet + one sheet per workshop, ordered by Type then Name
        Assert.Equal(3, wb.Worksheets.Count);

        // Overview is the first sheet
        var overviewSheet = wb.Worksheets.First();
        Assert.Equal("Overview", overviewSheet.Name);

        // Yoga is Type1, Painting is Type2 -> Yoga first after Overview
        var yogaSheet = wb.Worksheets.Skip(1).First();
        Assert.Equal("Yoga", yogaSheet.Name);
        Assert.Equal("Workshop", yogaSheet.Cell(1, 1).GetString());
        Assert.Equal("Yoga", yogaSheet.Cell(1, 2).GetString());
        Assert.Equal("Ort", yogaSheet.Cell(2, 1).GetString());
        Assert.Equal("Zeitfenster", yogaSheet.Cell(3, 1).GetString());
        Assert.Equal("Kapazität", yogaSheet.Cell(4, 1).GetString());
        Assert.Equal(20, yogaSheet.Cell(4, 2).GetValue<int>());

        // Attendee names present (sorted alphabetically) - data starts at row 7
        Assert.Equal("Alice Smith", yogaSheet.Cell(7, 2).GetString());
        Assert.Equal("Bob Jones", yogaSheet.Cell(8, 2).GetString());

        // Painting sheet exists but has no attendees assigned to it
        var paintingSheet = wb.Worksheets.Skip(2).First();
        Assert.Equal("Painting", paintingSheet.Name);
    }

    [Fact]
    public void WriteAttendeeReport_CreatesValidExcel()
    {
        // Arrange
        var workshops = new List<Workshop>
        {
            new("W1", "Yoga", WorkshopType.Type1, 20),
            new("W2", "Painting", WorkshopType.Type2, 25),
            new("W3", "Cooking", WorkshopType.Type3, 15),
        };

        var persons = new List<Person>
        {
            new("P1", "Alice Smith", ["W1", "W2", "W3", "W4", "W5", "W6"]),
            new("P2", "Bob Jones", ["W2", "W3", "W1", "W4", "W5", "W6"]),
        };

        var result = new AssignmentResult(
        [
            new Assignment("P1", ["W1"], 1, [1], 12, IsFullDay: true),
            new Assignment("P2", ["W2", "W3"], 1, [1, 2], 11),
        ],
        []);

        var path = TempPath("attendee_report.xlsx");

        // Act
        _svc.WriteAttendeeReport(path, result, workshops, persons,
            "Slot 1", "Slot 2", "Slot 3");

        // Assert: read it back
        using var wb = new XLWorkbook(path);
        var sheet = wb.Worksheets.First();
        Assert.Equal("People", sheet.Name);

        // Header: ID, Vorname, Nachname, Info, Freund-ID, Wunsch 1-6, Slot 1, Slot 2
        Assert.Equal("ID", sheet.Cell(1, 1).GetString());
        Assert.Equal("Vorname", sheet.Cell(1, 2).GetString());
        Assert.Equal("Nachname", sheet.Cell(1, 3).GetString());
        Assert.Equal("Info", sheet.Cell(1, 4).GetString());
        Assert.Equal("Freund-ID", sheet.Cell(1, 5).GetString());
        Assert.Equal("Wunsch 1", sheet.Cell(1, 6).GetString());
        Assert.Equal("Slot 1", sheet.Cell(1, 12).GetString());
        Assert.Equal("Slot 2", sheet.Cell(1, 13).GetString());

        // Persons sorted by Info then Name: Alice Smith, Bob Jones (both empty Info, alpha by name)
        Assert.Equal("P1", sheet.Cell(2, 1).GetString());
        Assert.Equal("Alice", sheet.Cell(2, 2).GetString());
        Assert.Equal("Smith", sheet.Cell(2, 3).GetString());
        // Alice assigned to W1 (Type1) -> Slot 1 = Yoga, Slot 2 = "---"
        Assert.Equal("Yoga", sheet.Cell(2, 12).GetString());
        Assert.Equal("---", sheet.Cell(2, 13).GetString());

        Assert.Equal("P2", sheet.Cell(3, 1).GetString());
        Assert.Equal("Bob", sheet.Cell(3, 2).GetString());
        Assert.Equal("Jones", sheet.Cell(3, 3).GetString());
        // Bob assigned to W2 (Type2) + W3 (Type3) -> Slot 1 = Painting (slot2 workshop), Slot 2 = Cooking (slot3 workshop)
        Assert.Equal("Painting", sheet.Cell(3, 12).GetString());
        Assert.Equal("Cooking", sheet.Cell(3, 13).GetString());
    }

    [Fact]
    public void WriteReports_Roundtrip_MatchesInput()
    {
        // Arrange: full pipeline — create workshops + persons, solve, export, read back
        var workshops = new List<Workshop>
        {
            new("W1", "Yoga", WorkshopType.Type1, 20, 0),
            new("W2", "Painting", WorkshopType.Type2, 25, 0),
            new("W3", "Cooking", WorkshopType.Type3, 15, 0),
            new("W4", "Music", WorkshopType.Type4, 20, 0),
        };

        var persons = new List<Person>
        {
            new("P1", "Alice Smith", ["W1", "W2", "W3", "W4"]),
            new("P2", "Bob Jones", ["W2", "W3", "W4", "W1"]),
            new("P3", "Carol White", ["W2", "W4", "W1", "W3"]),
        };

        // Use DataService + SolverService for a real solve
        var dataService = new DataService();
        var solverService = new SolverService();

        var importResult = dataService.BuildAssignmentInput(workshops, persons);
        var solveResult = solverService.Solve(importResult.InputData, timeLimitSeconds: 10);

        // Export both reports
        var leaderPath = TempPath("roundtrip_leader.xlsx");
        var attendeePath = TempPath("roundtrip_attendee.xlsx");

        _svc.WriteWorkshopLeaderReport(leaderPath, solveResult, workshops, persons, "Slot 1", "Slot 2", "Slot 3");
        _svc.WriteAttendeeReport(attendeePath, solveResult, workshops, persons,
            "Morning", "Afternoon 1", "Afternoon 2");

        // Assert leader report: Overview sheet + one sheet per workshop (Type4 creates 2 sheets)
        using (var wb = new XLWorkbook(leaderPath))
        {
            var expectedSheetCount = 1 + workshops.Count + workshops.Count(w => w.Type == WorkshopType.Type4);
            Assert.Equal(expectedSheetCount, wb.Worksheets.Count);

            // Overview is first sheet
            Assert.Equal("Overview", wb.Worksheets.First().Name);

            var sheetNames = wb.Worksheets.Select(s => s.Name).ToHashSet();
            foreach (var ws in workshops)
            {
                if (ws.Type == WorkshopType.Type4)
                    Assert.True(sheetNames.Any(n => n.StartsWith(ws.Name)), $"Expected sheet starting with {ws.Name}");
                else
                    Assert.Contains(ws.Name, sheetNames);
            }

            foreach (var assignment in solveResult.Assignments.Where(a => a.WorkshopIds.Count > 0))
            {
                var person = persons.First(p => p.Id == assignment.PersonId);
                foreach (var wsId in assignment.WorkshopIds)
                {
                    var ws = workshops.First(w => w.Id == wsId);
                    var matchingSheets = ws.Type == WorkshopType.Type4
                        ? wb.Worksheets.Where(s => s.Name.StartsWith(ws.Name)).ToList()
                        : new List<ClosedXML.Excel.IXLWorksheet> { wb.Worksheets.First(s => s.Name == ws.Name) };

                    var found = false;
                    foreach (var sheet in matchingSheets)
                    {
                        foreach (var row in sheet.RowsUsed())
                        {
                            if (row.Cell(2).GetString() == person.Name)
                            {
                                found = true;
                                break;
                            }
                        }
                        if (found) break;
                    }
                    Assert.True(found, $"Person {person.Name} not found in any sheet for workshop {ws.Name}");
                }
            }
        }

        // Assert attendee report
        using (var wb = new XLWorkbook(attendeePath))
        {
            var sheet = wb.Worksheets.First();
            Assert.Equal("People", sheet.Name);

            // New format uses fixed "Slot 1"/"Slot 2" headers at columns 12/13
            Assert.Equal("Slot 1", sheet.Cell(1, 12).GetString());
            Assert.Equal("Slot 2", sheet.Cell(1, 13).GetString());

            // All persons present (check by ID in column 1)
            var allIds = new HashSet<string>();
            for (int row = 2; row <= sheet.LastRowUsed()?.RowNumber(); row++)
            {
                var id = sheet.Cell(row, 1).GetString();
                if (!string.IsNullOrEmpty(id))
                    allIds.Add(id);
            }

            foreach (var person in persons)
            {
                Assert.Contains(person.Id, allIds);
            }
        }
    }

    // ──────────────────────────────────────────────────
    // Export Report Edge Cases (per EXPORT_SPEC.md)
    // ──────────────────────────────────────────────────

    [Fact]
    public void WriteWorkshopLeaderReport_SortedBySlotThenName()
    {
        var workshops = new List<Workshop>
        {
            new("W4", "Zebra Music", WorkshopType.Type4, 20),
            new("W2a", "Beta Painting", WorkshopType.Type2, 25),
            new("W1", "Alpha Yoga", WorkshopType.Type1, 20),
            new("W3", "Gamma Cooking", WorkshopType.Type3, 15),
            new("W2b", "Alpha Dance", WorkshopType.Type2, 30),
        };

        var persons = new List<Person>
        {
            new("P1", "Test Person", ["W1", "W2a", "W3", "W2b", "W4"]),
        };

        var result = new AssignmentResult([new Assignment("P1", ["W1"], 1, [1], 12, IsFullDay: true)], []);
        var path = TempPath("sorted_leader.xlsx");

        _svc.WriteWorkshopLeaderReport(path, result, workshops, persons, "Slot 1", "Slot 2", "Slot 3");

        using var wb = new XLWorkbook(path);
        var sheetNames = wb.Worksheets.Select(s => s.Name).ToList();

        Assert.Equal(7, sheetNames.Count);
        Assert.Equal("Overview", sheetNames[0]);
        Assert.Equal("Alpha Yoga", sheetNames[1]);
        Assert.Equal("Alpha Dance", sheetNames[2]);
        Assert.Equal("Beta Painting", sheetNames[3]);
        Assert.Equal("Gamma Cooking", sheetNames[4]);
        Assert.Equal("Zebra Music (Slot 2)", sheetNames[5]);
        Assert.Equal("Zebra Music (Slot 3)", sheetNames[6]);
    }

    [Fact]
    public void WriteWorkshopLeaderReport_CancelledWorkshopsIncluded()
    {
        var workshops = new List<Workshop>
        {
            new("W1", "Active Workshop", WorkshopType.Type1, 20, 5),
            new("W2", "Cancelled Workshop", WorkshopType.Type2, 25, 10),
        };

        var persons = new List<Person>
        {
            new("P1", "Alice", ["W1", "W2"]),
        };

        var result = new AssignmentResult([new Assignment("P1", ["W1"], 1, [1], 12, IsFullDay: true)], ["W2"]);
        var path = TempPath("cancelled_ws.xlsx");

        _svc.WriteWorkshopLeaderReport(path, result, workshops, persons, "Slot 1", "Slot 2", "Slot 3");

        using var wb = new XLWorkbook(path);
        Assert.Equal(3, wb.Worksheets.Count);
        Assert.Equal("Overview", wb.Worksheets.First().Name);

        var activeSheet = wb.Worksheets.First(s => s.Name == "Active Workshop");
        var cancelledSheet = wb.Worksheets.First(s => s.Name == "Cancelled Workshop");

        Assert.Equal("Alice", activeSheet.Cell(7, 2).GetString());
        Assert.True(string.IsNullOrEmpty(cancelledSheet.Cell(7, 2).GetString()));
    }

    [Fact]
    public void WriteWorkshopLeaderReport_Type4CreatesTwoSheets()
    {
        var workshops = new List<Workshop>
        {
            new("W4", "Flexible Music", WorkshopType.Type4, 20),
        };

        var persons = new List<Person>
        {
            new("P1", "Alice", ["W4"]),
        };

        var result = new AssignmentResult([new Assignment("P1", ["W4"], 1, [1], 12)], []);
        var path = TempPath("type4_two_sheets.xlsx");

        _svc.WriteWorkshopLeaderReport(path, result, workshops, persons, "Slot 1", "Slot 2", "Slot 3");

        using var wb = new XLWorkbook(path);
        Assert.Equal(3, wb.Worksheets.Count);
        Assert.Equal("Overview", wb.Worksheets.First().Name);

        var sheetNames = wb.Worksheets.Select(s => s.Name).ToList();
        Assert.Contains("Flexible Music (Slot 2)", sheetNames);
        Assert.Contains("Flexible Music (Slot 3)", sheetNames);

        var slot2Sheet = wb.Worksheets.First(s => s.Name.Contains("Slot 2"));
        var slot3Sheet = wb.Worksheets.First(s => s.Name.Contains("Slot 3"));

        Assert.Equal("Flexible Music", slot2Sheet.Cell(1, 2).GetString());
        Assert.Equal("Slot 3", slot3Sheet.Cell(3, 2).GetString());

        Assert.Equal("Flexible Music", slot3Sheet.Cell(1, 2).GetString());
        Assert.Equal("Slot 3", slot3Sheet.Cell(3, 2).GetString());
    }

    [Fact]
    public void WriteWorkshopLeaderReport_TableRowsEqualCapacity()
    {
        var workshops = new List<Workshop>
        {
            new("W1", "Yoga", WorkshopType.Type1, 5),
        };

        var persons = new List<Person>
        {
            new("P1", "Alice", ["W1"], Info: "Class A"),
            new("P2", "Bob", ["W1"], Info: "Class B"),
        };

        var result = new AssignmentResult(
        [
            new Assignment("P1", ["W1"], 1, [1], 12, IsFullDay: true),
            new Assignment("P2", ["W1"], 1, [1], 12, IsFullDay: true),
        ],
        []);
        var path = TempPath("capacity_rows.xlsx");

        _svc.WriteWorkshopLeaderReport(path, result, workshops, persons, "Slot 1", "Slot 2", "Slot 3");

        using var wb = new XLWorkbook(path);
        Assert.Equal("Overview", wb.Worksheets.First().Name);
        var sheet = wb.Worksheets.Skip(1).First(); // Skip Overview, get Yoga sheet

        Assert.Equal("#", sheet.Cell(6, 1).GetString());
        Assert.Equal("Name", sheet.Cell(6, 2).GetString());
        Assert.Equal("Info", sheet.Cell(6, 3).GetString());

        Assert.Equal(1, sheet.Cell(7, 1).GetValue<int>());
        Assert.Equal("Alice", sheet.Cell(7, 2).GetString());
        Assert.Equal("Class A", sheet.Cell(7, 3).GetString());

        Assert.Equal(2, sheet.Cell(8, 1).GetValue<int>());
        Assert.Equal("Bob", sheet.Cell(8, 2).GetString());
        Assert.Equal("Class B", sheet.Cell(8, 3).GetString());

        Assert.Equal(3, sheet.Cell(9, 1).GetValue<int>());
        Assert.True(string.IsNullOrEmpty(sheet.Cell(9, 2).GetString()));
        Assert.True(string.IsNullOrEmpty(sheet.Cell(9, 3).GetString()));

        Assert.Equal(4, sheet.Cell(10, 1).GetValue<int>());
        Assert.True(string.IsNullOrEmpty(sheet.Cell(10, 2).GetString()));

        Assert.Equal(5, sheet.Cell(11, 1).GetValue<int>());
        Assert.True(string.IsNullOrEmpty(sheet.Cell(11, 2).GetString()));
    }

    [Fact]
    public void WriteWorkshopLeaderReport_Type4LongNameDoesNotCrashWithDuplicateSheetName()
    {
        // Regression test: workshop name "NiPa - Wir überwinden Parkoure" (30 chars) + slot suffix
        // " (Slot 2)" (9 chars) = 39 chars, which exceeds Excel's 31-char limit.
        // Previously, SanitizeSheetName truncated AFTER combining name+suffix, so both
        // slots truncated to the same 31-char prefix, causing ArgumentException.
        var workshops = new List<Workshop>
        {
            new("W4", "NiPa - Wir überwinden Parkoure", WorkshopType.Type4, 15),
        };

        var persons = new List<Person>
        {
            new("P1", "Alice", ["W4"]),
        };

        var result = new AssignmentResult([new Assignment("P1", ["W4"], 1, [1], 12)], []);
        var path = TempPath("type4_long_name.xlsx");

        // This must not throw ArgumentException: "A worksheet with the same name has already been added"
        _svc.WriteWorkshopLeaderReport(path, result, workshops, persons, "Slot 1", "Slot 2", "Slot 3");

        using var wb = new XLWorkbook(path);
        Assert.Equal(3, wb.Worksheets.Count);
        Assert.Equal("Overview", wb.Worksheets.First().Name);

        // Skip Overview, check only the Type4 workshop sheets
        var workshopSheetNames = wb.Worksheets.Skip(1).Select(s => s.Name).ToList();
        // Both workshop sheet names must be unique (the core invariant)
        Assert.Equal(2, workshopSheetNames.Distinct().Count());

        // Both sheet names must contain their respective slot suffix
        Assert.True(workshopSheetNames[0].Contains("Slot 2") || workshopSheetNames[1].Contains("Slot 2"),
            $"No sheet contains 'Slot 2'. Sheet names: {string.Join(", ", workshopSheetNames)}");
        Assert.True(workshopSheetNames[0].Contains("Slot 3") || workshopSheetNames[1].Contains("Slot 3"),
            $"No sheet contains 'Slot 3'. Sheet names: {string.Join(", ", workshopSheetNames)}");

        // All sheet names (including Overview) must not exceed 31 chars
        var allSheetNames = wb.Worksheets.Select(s => s.Name).ToList();
        Assert.All(allSheetNames, name => Assert.True(name.Length <= 31,
            $"Sheet name '{name}' exceeds 31 chars (length: {name.Length})"));
    }

    [Fact]
    public void WriteAttendeeReport_SortedByInfoThenName()
    {
        var workshops = new List<Workshop>
        {
            new("W1", "Yoga", WorkshopType.Type1, 20),
        };

        var persons = new List<Person>
        {
            new("P1", "Person A", ["W1"], Info: "Class B"),
            new("P2", "Person B", ["W1"], Info: "Class A"),
            new("P3", "Person C", ["W1"], Info: "Class A"),
        };

        var result = new AssignmentResult(
        [
            new Assignment("P1", ["W1"], 1, [1], 12, IsFullDay: true),
            new Assignment("P2", ["W1"], 1, [1], 12, IsFullDay: true),
            new Assignment("P3", ["W1"], 1, [1], 12, IsFullDay: true),
        ],
        []);
        var path = TempPath("attendee_sorted.xlsx");

        _svc.WriteAttendeeReport(path, result, workshops, persons, "Slot 1", "Slot 2", "Slot 3");

        using var wb = new XLWorkbook(path);
        var sheet = wb.Worksheets.First();

        // Sorted by Info then Name: Class A (Person B, Person C), Class B (Person A)
        // Column 1 = ID, Column 2 = Vorname, Column 3 = Nachname
        Assert.Equal("P2", sheet.Cell(2, 1).GetString());  // Person B (Class A) first
        Assert.Equal("Person", sheet.Cell(2, 2).GetString());
        Assert.Equal("B", sheet.Cell(2, 3).GetString());

        Assert.Equal("P3", sheet.Cell(3, 1).GetString());  // Person C (Class A) second
        Assert.Equal("Person", sheet.Cell(3, 2).GetString());
        Assert.Equal("C", sheet.Cell(3, 3).GetString());

        Assert.Equal("P1", sheet.Cell(4, 1).GetString());  // Person A (Class B) last
        Assert.Equal("Person", sheet.Cell(4, 2).GetString());
        Assert.Equal("A", sheet.Cell(4, 3).GetString());
    }

    [Fact]
    public void WriteAttendeeReport_Type1FillsSlot1Only()
    {
        var workshops = new List<Workshop>
        {
            new("W1", "Full Day Yoga", WorkshopType.Type1, 20),
        };

        var persons = new List<Person>
        {
            new("P1", "Alice", ["W1"]),
        };

        var result = new AssignmentResult([new Assignment("P1", ["W1"], 1, [1], 12, IsFullDay: true)], []);
        var path = TempPath("type1_slot.xlsx");

        _svc.WriteAttendeeReport(path, result, workshops, persons, "Morning", "Afternoon 1", "Afternoon 2");

        using var wb = new XLWorkbook(path);
        var sheet = wb.Worksheets.First();

        Assert.Equal("P1", sheet.Cell(2, 1).GetString());     // ID
        Assert.Equal("Alice", sheet.Cell(2, 2).GetString());  // Vorname
        Assert.Equal("Full Day Yoga", sheet.Cell(2, 12).GetString()); // Slot 1 = workshop name
        Assert.Equal("---", sheet.Cell(2, 13).GetString());           // Slot 2 = "---" for Type1
    }

    [Fact]
    public void WriteAttendeeReport_UnassignedShowsBlank()
    {
        var workshops = new List<Workshop>
        {
            new("W1", "Yoga", WorkshopType.Type1, 20),
        };

        var persons = new List<Person>
        {
            new("P1", "Alice", ["W1"]),
            new("P2", "Bob", ["W1"]),
        };

        // P1 assigned, P2 unassigned (empty workshop list)
        var result = new AssignmentResult(
        [
            new Assignment("P1", ["W1"], 1, [1], 12, IsFullDay: true),
            new Assignment("P2", [], null),
        ],
        []);
        var path = TempPath("unassigned.xlsx");

        _svc.WriteAttendeeReport(path, result, workshops, persons, "S1", "S2", "S3");

        using var wb = new XLWorkbook(path);
        var sheet = wb.Worksheets.First();

        // Sorted by Info then Name: Alice, Bob (both empty Info)
        Assert.Equal("P1", sheet.Cell(2, 1).GetString());   // ID
        Assert.Equal("Alice", sheet.Cell(2, 2).GetString()); // Vorname
        Assert.Equal("P2", sheet.Cell(3, 1).GetString());   // ID
        Assert.Equal("Bob", sheet.Cell(3, 2).GetString());   // Vorname

        // Bob (unassigned) should have blank Slot 1 and Slot 2 columns
        Assert.True(string.IsNullOrEmpty(sheet.Cell(3, 12).GetString())); // Slot 1
        Assert.True(string.IsNullOrEmpty(sheet.Cell(3, 13).GetString())); // Slot 2
    }

    [Fact]
    public void WriteAttendeeReport_Type4SlotPlacement()
    {
        var workshops = new List<Workshop>
        {
            new("W2", "Painting", WorkshopType.Type2, 25),
            new("W3", "Cooking", WorkshopType.Type3, 15),
            new("W4", "Music Flex", WorkshopType.Type4, 20),
        };

        var persons = new List<Person>
        {
            // Alice: Type2 + Type4 -> Type4 fills Slot3
            new("P1", "Alice", ["W2", "W4", "W3"]),
            // Bob: Type3 + Type4 -> Type4 fills Slot2
            new("P2", "Bob", ["W3", "W4", "W2"]),
        };

        var result = new AssignmentResult(
        [
            new Assignment("P1", ["W2", "W4"], 1, [1, 2], 11),  // W2 in Slot2, W4 in Slot3
            new Assignment("P2", ["W3", "W4"], 1, [1, 2], 11),  // W4 in Slot2, W3 in Slot3
        ],
        []);
        var path = TempPath("type4_placement.xlsx");

        _svc.WriteAttendeeReport(path, result, workshops, persons, "Morning", "Afternoon 1", "Afternoon 2");

        using var wb = new XLWorkbook(path);
        var sheet = wb.Worksheets.First();

        // Alice: Slot2=Painting, Slot3=Music Flex -> Slot 1 col=Painting, Slot 2 col=Music Flex
        Assert.Equal("P1", sheet.Cell(2, 1).GetString());       // ID
        Assert.Equal("Alice", sheet.Cell(2, 2).GetString());    // Vorname
        Assert.Equal("Painting", sheet.Cell(2, 12).GetString());    // Slot 1 (morning/slot2 workshop)
        Assert.Equal("Music Flex", sheet.Cell(2, 13).GetString());  // Slot 2 (afternoon/slot3 workshop)

        // Bob: Slot2=Music Flex, Slot3=Cooking -> Slot 1 col=Music Flex, Slot 2 col=Cooking
        Assert.Equal("P2", sheet.Cell(3, 1).GetString());       // ID
        Assert.Equal("Bob", sheet.Cell(3, 2).GetString());      // Vorname
        Assert.Equal("Music Flex", sheet.Cell(3, 12).GetString());  // Slot 1
        Assert.Equal("Cooking", sheet.Cell(3, 13).GetString());     // Slot 2
    }
}
