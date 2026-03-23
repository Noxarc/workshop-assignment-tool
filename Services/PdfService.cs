using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WorkshopAssignment.Models;

namespace WorkshopAssignment.Services;

public class PdfService
{
    static PdfService()
    {
        // QuestPDF Community license (free for revenue < $1M)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public void WriteWorkshopLeaderReport(string filePath, AssignmentResult result, List<Workshop> workshops, List<Person> persons,
        string slot1Name, string slot2Name, string slot3Name)
    {
        var personMap = persons.ToDictionary(p => p.Id);
        var workshopMap = workshops.ToDictionary(w => w.Id);

        // Pre-calculate statistics for overview page
        var workshopStats = CalculateWorkshopStats(workshops, result, personMap, workshopMap);

        Document.Create(container =>
        {
            // Page 1: Overview page
            WriteOverviewPage(container, workshopStats, slot1Name, slot2Name, slot3Name);

            // Subsequent pages: Individual workshop pages
            // Sorted by timeslot (Type) then name alphabetically
            var sortedWorkshops = workshops
                .OrderBy(w => w.Type)
                .ThenBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var ws in sortedWorkshops)
            {
                if (ws.Type == WorkshopType.Type4)
                {
                    // Type4 workshops generate TWO pages (one for Slot 2, one for Slot 3)
                    WriteType4WorkshopPage(container, ws, result, personMap, workshopMap, slot: 2, slot2Name);
                    WriteType4WorkshopPage(container, ws, result, personMap, workshopMap, slot: 3, slot3Name);
                }
                else
                {
                    // Non-Type4 workshops generate a single page
                    var slotName = ws.Type switch
                    {
                        WorkshopType.Type1 => slot1Name,
                        WorkshopType.Type2 => slot2Name,
                        WorkshopType.Type3 => slot3Name,
                        _ => ""
                    };
                    WriteWorkshopPage(container, ws, result, personMap, slotName);
                }
            }
        }).GeneratePdf(filePath);
    }

    private record WorkshopStat(
        Workshop Workshop,
        int AssignedCount,
        int Wish1Count,
        int Wish2Count,
        int Wish3Count,
        int Wish4Count,
        int Wish5Count,
        int Wish6Count
    );

    private List<WorkshopStat> CalculateWorkshopStats(
        List<Workshop> workshops,
        AssignmentResult result,
        Dictionary<string, Person> personMap,
        Dictionary<string, Workshop> workshopMap)
    {
        var stats = new List<WorkshopStat>();

        foreach (var ws in workshops)
        {
            int assignedCount = 0;
            int wish1 = 0, wish2 = 0, wish3 = 0, wish4 = 0, wish5 = 0, wish6 = 0;

            foreach (var assignment in result.Assignments)
            {
                if (!assignment.WorkshopIds.Contains(ws.Id))
                    continue;

                // For Type4 workshops, count appearances in both slots
                // The same workshop ID may appear with different slot placements
                assignedCount++;

                if (assignment.WishRank == 1) wish1++;
                else if (assignment.WishRank == 2) wish2++;
                else if (assignment.WishRank == 3) wish3++;
                else if (assignment.WishRank == 4) wish4++;
                else if (assignment.WishRank == 5) wish5++;
                else if (assignment.WishRank == 6) wish6++;
            }

            stats.Add(new WorkshopStat(ws, assignedCount, wish1, wish2, wish3, wish4, wish5, wish6));
        }

        return stats;
    }

    private void WriteOverviewPage(IDocumentContainer container, List<WorkshopStat> stats,
        string slot1Name, string slot2Name, string slot3Name)
    {
        // Sort by timeslot (Type) then workshop name alphabetically
        var sortedStats = stats
            .OrderBy(s => s.Workshop.Type)
            .ThenBy(s => s.Workshop.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(1.5f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(8));

            page.Header().Text("Workshop-Übersicht").Bold().FontSize(16);

            page.Content().PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3);   // Workshop name
                    cols.ConstantColumn(30);  // Type
                    cols.ConstantColumn(30);  // Min capacity
                    cols.ConstantColumn(30);  // Capacity
                    cols.ConstantColumn(50);  // Capacity filled %
                    cols.ConstantColumn(40);  // # assigned
                    cols.RelativeColumn(1);   // W1
                    cols.RelativeColumn(1);   // W2
                    cols.RelativeColumn(1);   // W3
                    cols.RelativeColumn(1);   // W4
                    cols.RelativeColumn(1);   // W5
                    cols.RelativeColumn(1);   // W6
                });

                table.Header(header =>
                {
                    header.Cell().Element(CellStyle).Text("Workshop").Bold();
                    header.Cell().Element(CellStyle).Text("Typ").Bold();
                    header.Cell().Element(CellStyle).Text("Min").Bold();
                    header.Cell().Element(CellStyle).Text("Max").Bold();
                    header.Cell().Element(CellStyle).Text("Auslastung").Bold();
                    header.Cell().Element(CellStyle).Text("Zugew.").Bold();
                    header.Cell().Element(CellStyle).Text("W1").Bold();
                    header.Cell().Element(CellStyle).Text("W2").Bold();
                    header.Cell().Element(CellStyle).Text("W3").Bold();
                    header.Cell().Element(CellStyle).Text("W4").Bold();
                    header.Cell().Element(CellStyle).Text("W5").Bold();
                    header.Cell().Element(CellStyle).Text("W6").Bold();
                });

                foreach (var stat in sortedStats)
                {
                    var ws = stat.Workshop;
                    var filledPct = ws.Capacity > 0 ? (double)stat.AssignedCount / ws.Capacity * 100 : 0;

                    table.Cell().Element(CellStyle).Text(ws.Name);
                    table.Cell().Element(CellStyle).Text(((int)ws.Type).ToString());
                    table.Cell().Element(CellStyle).Text(ws.MinCapacity.ToString());
                    table.Cell().Element(CellStyle).Text(ws.Capacity.ToString());
                    table.Cell().Element(CellStyle).Text($"{filledPct:F0}%");
                    table.Cell().Element(CellStyle).Text(stat.AssignedCount.ToString());

                    // Wish counts with percentages for ranks 1-6
                    table.Cell().Element(CellStyle).Text(stat.AssignedCount > 0 ? $"{stat.Wish1Count} ({stat.Wish1Count * 100 / stat.AssignedCount}%)" : "-");
                    table.Cell().Element(CellStyle).Text(stat.AssignedCount > 0 ? $"{stat.Wish2Count} ({stat.Wish2Count * 100 / stat.AssignedCount}%)" : "-");
                    table.Cell().Element(CellStyle).Text(stat.AssignedCount > 0 ? $"{stat.Wish3Count} ({stat.Wish3Count * 100 / stat.AssignedCount}%)" : "-");
                    table.Cell().Element(CellStyle).Text(stat.AssignedCount > 0 ? $"{stat.Wish4Count} ({stat.Wish4Count * 100 / stat.AssignedCount}%)" : "-");
                    table.Cell().Element(CellStyle).Text(stat.AssignedCount > 0 ? $"{stat.Wish5Count} ({stat.Wish5Count * 100 / stat.AssignedCount}%)" : "-");
                    table.Cell().Element(CellStyle).Text(stat.AssignedCount > 0 ? $"{stat.Wish6Count} ({stat.Wish6Count * 100 / stat.AssignedCount}%)" : "-");
                }
            });

            page.Footer().AlignCenter().Text(x =>
            {
                x.Span("Seite ");
                x.CurrentPageNumber();
                x.Span(" von ");
                x.TotalPages();
            });
        });
    }

    private void WriteWorkshopPage(IDocumentContainer container, Workshop ws, AssignmentResult result,
        Dictionary<string, Person> personMap, string slotName)
    {
        var attendees = result.Assignments
            .Where(a => a.WorkshopIds.Contains(ws.Id))
            .Select(a => personMap.GetValueOrDefault(a.PersonId))
            .Where(p => p != null)
            .OrderBy(p => p!.Info ?? "")
            .ThenBy(p => p!.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(11));

            page.Header().Column(col =>
            {
                col.Item().Text(ws.Name).Bold().FontSize(18);
                col.Item().PaddingTop(5);

                // Location: show value or blank line for handwriting
                col.Item().Row(row =>
                {
                    row.AutoItem().Text("Ort:").FontSize(12);
                    if (!string.IsNullOrWhiteSpace(ws.Location))
                    {
                        row.AutoItem().PaddingLeft(4).Text(ws.Location).FontSize(12);
                    }
                    else
                    {
                        row.RelativeItem().PaddingLeft(5).BorderBottom(1).BorderColor(Colors.Grey.Medium).Height(14);
                    }
                });

                col.Item().Text($"Zeitfenster: {slotName}").FontSize(12);
                col.Item().PaddingBottom(10);
            });

            page.Content().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(40);  // #
                    cols.RelativeColumn(2);   // Name
                    cols.RelativeColumn();    // Info
                });

                table.Header(header =>
                {
                    header.Cell().Element(CellStyle).Text("#").Bold();
                    header.Cell().Element(CellStyle).Text("Name").Bold();
                    header.Cell().Element(CellStyle).Text("Info").Bold();
                });

                // Write capacity rows: filled with attendees first, then empty rows
                for (int i = 0; i < ws.Capacity; i++)
                {
                    table.Cell().Element(CellStyle).Text((i + 1).ToString());
                    if (i < attendees.Count)
                    {
                        table.Cell().Element(CellStyle).Text(attendees[i]!.Name);
                        table.Cell().Element(CellStyle).Text(attendees[i]!.Info ?? "");
                    }
                    else
                    {
                        // Empty row for unfilled spot
                        table.Cell().Element(CellStyle).Text("");
                        table.Cell().Element(CellStyle).Text("");
                    }
                }
            });

            page.Footer().AlignCenter().Text(x =>
            {
                x.Span("Seite ");
                x.CurrentPageNumber();
                x.Span(" von ");
                x.TotalPages();
            });
        });
    }

    private void WriteType4WorkshopPage(IDocumentContainer container, Workshop ws, AssignmentResult result,
        Dictionary<string, Person> personMap, Dictionary<string, Workshop> workshopMap, int slot, string slotName)
    {
        // Filter attendees to only those assigned to this Type4 workshop in the specified slot
        var attendees = result.Assignments
            .Where(a => a.WorkshopIds.Contains(ws.Id))
            .Where(a => SlotPlacementHelper.GetType4SlotNumber(ws.Id, a.WorkshopIds, id => workshopMap.GetValueOrDefault(id)) == slot)
            .Select(a => personMap.GetValueOrDefault(a.PersonId))
            .Where(p => p != null)
            .OrderBy(p => p!.Info ?? "")
            .ThenBy(p => p!.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(11));

            page.Header().Column(col =>
            {
                col.Item().Text(ws.Name).Bold().FontSize(18);
                col.Item().PaddingTop(5);

                // Location: show value or blank line for handwriting
                col.Item().Row(row =>
                {
                    row.AutoItem().Text("Ort:").FontSize(12);
                    if (!string.IsNullOrWhiteSpace(ws.Location))
                    {
                        row.AutoItem().PaddingLeft(4).Text(ws.Location).FontSize(12);
                    }
                    else
                    {
                        row.RelativeItem().PaddingLeft(5).BorderBottom(1).BorderColor(Colors.Grey.Medium).Height(14);
                    }
                });

                col.Item().Text($"Zeitfenster: {slotName}").FontSize(12);
                col.Item().PaddingBottom(10);
            });

            page.Content().Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(40);  // #
                    cols.RelativeColumn(2);   // Name
                    cols.RelativeColumn();    // Info
                });

                table.Header(header =>
                {
                    header.Cell().Element(CellStyle).Text("#").Bold();
                    header.Cell().Element(CellStyle).Text("Name").Bold();
                    header.Cell().Element(CellStyle).Text("Info").Bold();
                });

                // Write capacity rows: filled with attendees first, then empty rows
                for (int i = 0; i < ws.Capacity; i++)
                {
                    table.Cell().Element(CellStyle).Text((i + 1).ToString());
                    if (i < attendees.Count)
                    {
                        table.Cell().Element(CellStyle).Text(attendees[i]!.Name);
                        table.Cell().Element(CellStyle).Text(attendees[i]!.Info ?? "");
                    }
                    else
                    {
                        // Empty row for unfilled spot
                        table.Cell().Element(CellStyle).Text("");
                        table.Cell().Element(CellStyle).Text("");
                    }
                }
            });

            page.Footer().AlignCenter().Text(x =>
            {
                x.Span("Seite ");
                x.CurrentPageNumber();
                x.Span(" von ");
                x.TotalPages();
            });
        });
    }

    // Helper records for attendee report
    private record OverviewRow(string Name, string Info, string Assignment);
    private record PersonAssignment(Assignment Assignment, Person Person);

    public void WriteAttendeeReport(string filePath, AssignmentResult result, List<Workshop> workshops, List<Person> persons,
        string slot1Name, string slot2Name, string slot3Name)
    {
        var workshopMap = workshops.ToDictionary(w => w.Id);
        var personMap = persons.ToDictionary(p => p.Id);

        // Prepare overview data sorted by info then name
        var overviewRows = result.Assignments
            .OrderBy(a => personMap.GetValueOrDefault(a.PersonId)?.Info ?? "")
            .ThenBy(a => personMap.GetValueOrDefault(a.PersonId)?.Name ?? a.PersonId, StringComparer.OrdinalIgnoreCase)
            .Select(a =>
            {
                var person = personMap.GetValueOrDefault(a.PersonId);
                var workshopNames = a.WorkshopIds
                    .Select(id => workshopMap.GetValueOrDefault(id)?.Name ?? "")
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                return new OverviewRow(
                    person?.Name ?? a.PersonId,
                    person?.Info ?? "",
                    string.Join(", ", workshopNames)
                );
            })
            .ToList();

        // Group persons by Info field for per-group pages
        var infoGroups = result.Assignments
            .Select(a => new { Assignment = a, Person = personMap.GetValueOrDefault(a.PersonId) })
            .Where(x => x.Person != null)
            .Select(x => new PersonAssignment(x.Assignment, x.Person!))
            .GroupBy(x => x.Person.Info ?? "")
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Document.Create(container =>
        {
            // Overview pages (can span multiple pages)
            WriteAttendeeOverviewPages(container, overviewRows);

            // Per-info-group pages (each starts on new page)
            foreach (var group in infoGroups)
            {
                WriteInfoGroupPage(container, group.Key, group.ToList(), workshopMap, slot1Name, slot2Name, slot3Name);
            }
        }).GeneratePdf(filePath);
    }

    private void WriteAttendeeOverviewPages(IDocumentContainer container, List<OverviewRow> overviewRows)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(1.5f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(9));

            page.Header().Text("Teilnehmer-Übersicht").Bold().FontSize(16);

            page.Content().PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(2);  // Name
                    cols.RelativeColumn(1);  // Info
                    cols.RelativeColumn(3);  // Assignment
                });

                table.Header(header =>
                {
                    header.Cell().Element(CellStyle).Text("Name").Bold();
                    header.Cell().Element(CellStyle).Text("Info").Bold();
                    header.Cell().Element(CellStyle).Text("Zuweisung").Bold();
                });

                foreach (var row in overviewRows)
                {
                    table.Cell().Element(CellStyle).Text(row.Name);
                    table.Cell().Element(CellStyle).Text(row.Info);
                    table.Cell().Element(CellStyle).Text(row.Assignment);
                }
            });

            page.Footer().AlignCenter().Text(x =>
            {
                x.Span("Seite ");
                x.CurrentPageNumber();
                x.Span(" von ");
                x.TotalPages();
            });
        });
    }

    private void WriteInfoGroupPage(IDocumentContainer container, string infoValue,
        List<PersonAssignment> groupMembers, Dictionary<string, Workshop> workshopMap,
        string slot1Name, string slot2Name, string slot3Name)
    {
        // Sort members by name
        var sortedMembers = groupMembers
            .OrderBy(m => m.Person.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(11));

            // Header: Info value (large)
            page.Header().Column(col =>
            {
                col.Item().Text(string.IsNullOrEmpty(infoValue) ? "(Keine Gruppe)" : infoValue).Bold().FontSize(18);
                col.Item().PaddingBottom(10);
            });

            page.Content().Table(table =>
            {
                // Use slot2Name and slot3Name as column headers (Type1 shows arrow in second column)
                table.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(2);  // Name
                    cols.RelativeColumn(2);  // Timeslot 1 (slot2Name - or Type1 workshop name)
                    cols.RelativeColumn(2);  // Timeslot 2 (slot3Name - or "<---" for Type1)
                });

                table.Header(header =>
                {
                    header.Cell().Element(CellStyle).Text("Name").Bold();
                    header.Cell().Element(CellStyle).Text(slot2Name).Bold();
                    header.Cell().Element(CellStyle).Text(slot3Name).Bold();
                });

                foreach (var member in sortedMembers)
                {
                    var slots = SlotPlacementHelper.ResolveSlots(
                        member.Assignment.WorkshopIds,
                        id => workshopMap.GetValueOrDefault(id));

                    table.Cell().Element(CellStyle).Text(member.Person.Name);

                    // Check if this is a Type1 assignment (has Slot1 workshop)
                    if (!string.IsNullOrEmpty(slots.Slot1Workshop))
                    {
                        // Type1: workshop name in first slot column, arrow pointing back in second
                        table.Cell().Element(CellStyle).Text(slots.Slot1Workshop);
                        table.Cell().Element(CellStyle).Text("<---");
                    }
                    else
                    {
                        // Normal: separate columns for slot 2 and slot 3
                        table.Cell().Element(CellStyle).Text(slots.Slot2Workshop ?? "");
                        table.Cell().Element(CellStyle).Text(slots.Slot3Workshop ?? "");
                    }
                }
            });

            page.Footer().AlignCenter().Text(x =>
            {
                x.Span("Seite ");
                x.CurrentPageNumber();
                x.Span(" von ");
                x.TotalPages();
            });
        });
    }

    private static IContainer CellStyle(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).PaddingHorizontal(3);

}
