using ClosedXML.Excel;
using WorkshopAssignment.Models;

namespace WorkshopAssignment.Tests;

/// <summary>
/// Shared test utilities for creating test data.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Creates a minimal workshops Excel file at the given path.
    /// Columns: ID, Bezeichnung, Kapazitat, Mindestkapazitat, Zeitfenster
    /// </summary>
    public static void CreateWorkshopsExcel(string path, List<(string Id, string Name, int Capacity, int MinCapacity, string Timeslot)> workshops)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Workshops");
        ws.Cell(1, 1).Value = "ID";
        ws.Cell(1, 2).Value = "Bezeichnung";
        ws.Cell(1, 3).Value = "Kapazität";
        ws.Cell(1, 4).Value = "Mindestkapazität";
        ws.Cell(1, 5).Value = "Zeitfenster";

        for (int i = 0; i < workshops.Count; i++)
        {
            var w = workshops[i];
            ws.Cell(i + 2, 1).Value = w.Id;
            ws.Cell(i + 2, 2).Value = w.Name;
            ws.Cell(i + 2, 3).Value = w.Capacity;
            ws.Cell(i + 2, 4).Value = w.MinCapacity;
            ws.Cell(i + 2, 5).Value = w.Timeslot;
        }

        wb.SaveAs(path);
    }

    /// <summary>
    /// Creates a persons Excel file with 6 individual preference columns (Wunsch 1-6).
    /// Each column holds a single workshop ID. Pass empty string for unused preference slots.
    /// Columns: ID, Vorname, Nachname, Info, Freund-ID, Wunsch 1, Wunsch 2, Wunsch 3, Wunsch 4, Wunsch 5, Wunsch 6
    /// </summary>
    public static void CreatePersonsExcel(string path, List<(string Id, string FirstName, string LastName, string? Info, string? FriendId, string Pref1, string Pref2, string Pref3, string Pref4, string Pref5, string Pref6)> persons)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Persons");
        ws.Cell(1, 1).Value = "ID";
        ws.Cell(1, 2).Value = "Vorname";
        ws.Cell(1, 3).Value = "Nachname";
        ws.Cell(1, 4).Value = "Info";
        ws.Cell(1, 5).Value = "Freund-ID";
        ws.Cell(1, 6).Value = "Wunsch 1";
        ws.Cell(1, 7).Value = "Wunsch 2";
        ws.Cell(1, 8).Value = "Wunsch 3";
        ws.Cell(1, 9).Value = "Wunsch 4";
        ws.Cell(1, 10).Value = "Wunsch 5";
        ws.Cell(1, 11).Value = "Wunsch 6";

        for (int i = 0; i < persons.Count; i++)
        {
            var p = persons[i];
            ws.Cell(i + 2, 1).Value = p.Id;
            ws.Cell(i + 2, 2).Value = p.FirstName;
            ws.Cell(i + 2, 3).Value = p.LastName;
            ws.Cell(i + 2, 4).Value = p.Info ?? "";
            ws.Cell(i + 2, 5).Value = p.FriendId ?? "";
            ws.Cell(i + 2, 6).Value = p.Pref1;
            ws.Cell(i + 2, 7).Value = p.Pref2;
            ws.Cell(i + 2, 8).Value = p.Pref3;
            ws.Cell(i + 2, 9).Value = p.Pref4;
            ws.Cell(i + 2, 10).Value = p.Pref5;
            ws.Cell(i + 2, 11).Value = p.Pref6;
        }

        wb.SaveAs(path);
    }

    /// <summary>
    /// Creates a standard set of workshops for testing.
    /// </summary>
    public static List<Workshop> StandardWorkshops(string? sourceFile = null) =>
    [
        new("W1", "Yoga", WorkshopType.Type1, 20, 5, null, sourceFile),
        new("W2", "Painting", WorkshopType.Type2, 25, 5, null, sourceFile),
        new("W3", "Cooking", WorkshopType.Type3, 15, 3, null, sourceFile),
        new("W4", "Music", WorkshopType.Type4, 20, 5, null, sourceFile),
        new("W5", "Dance", WorkshopType.Type2, 30, 5, null, sourceFile),
        new("W6", "Drama", WorkshopType.Type3, 20, 5, null, sourceFile),
    ];

    /// <summary>
    /// Creates a standard set of persons with 6 ranked preferences for testing.
    /// P1 and P2 are mutual friends. Each person has 6 individual workshop preferences.
    /// </summary>
    public static List<Person> StandardPersons(string? sourceFile = null) =>
    [
        new("P1", "Alice Smith", ["W2", "W3", "W5", "W6", "W4", "W1"], FriendId: "P2", Info: "GroupA", SourceFile: sourceFile),
        new("P2", "Bob Jones", ["W2", "W3", "W5", "W4", "W6", "W1"], FriendId: "P1", Info: "GroupA", SourceFile: sourceFile),
        new("P3", "Carol White", ["W5", "W6", "W2", "W3", "W4", "W1"], Info: "GroupB", SourceFile: sourceFile),
        new("P4", "Dave Brown", ["W2", "W4", "W5", "W3", "W6", "W1"], Info: "GroupB", SourceFile: sourceFile),
        new("P5", "Eve Green", ["W1", "W2", "W3", "W5", "W6", "W4"], Info: "GroupA", SourceFile: sourceFile),
    ];

    /// <summary>
    /// Creates a temp directory for test files and returns the path.
    /// Call in constructor, clean up in Dispose.
    /// </summary>
    public static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wa-tests", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
