using ClosedXML.Excel;
using WorkshopAssignment.Models;

namespace WorkshopAssignment.Services;

public class ExcelService
{
    private static readonly Dictionary<string, WorkshopType> TimeslotMapping = new()
    {
        ["a"] = WorkshopType.Type1,
        ["b"] = WorkshopType.Type2,
        ["c"] = WorkshopType.Type3,
        ["d"] = WorkshopType.Type4
    };

    public (List<Workshop> Workshops, List<ImportWarning> Warnings) LoadWorkshops(string filePath)
    {
        var debugLog = new List<string>();
        debugLog.Add($"LoadWorkshops called with: {filePath}");

        var workshops = new List<Workshop>();
        var warnings = new List<ImportWarning>();
        var sourceFile = Path.GetFileName(filePath);

        using var workbook = new XLWorkbook(filePath);
        var worksheet = FindWorksheetByName(workbook, new[] { "workshops", "workshop", "ws" })
            ?? workbook.Worksheets.First();

        debugLog.Add($"Using worksheet: '{worksheet.Name}'");

        var headerRow = worksheet.Row(1);
        var columnMap = new Dictionary<string, int>();
        for (int col = 1; col <= worksheet.ColumnsUsed().Count(); col++)
        {
            var header = headerRow.Cell(col).GetString().Trim().ToLower();
            columnMap[header] = col;
        }

        debugLog.Add($"Column count: {columnMap.Count}");
        foreach (var kvp in columnMap)
            debugLog.Add($"  columnMap[\"{kvp.Key}\"] = {kvp.Value}");

        // Find columns (flexible naming)
        int idCol = FindColumn(columnMap, "id");
        int nameCol = FindColumn(columnMap, "name", "bezeichnung");
        int capacityCol = FindColumn(columnMap, "kapazität", "kapazitat", "capacity");
        int minCapacityCol = FindColumn(columnMap, "mindestkapazität", "mindestkapazitat", "mincapacity", "min");
        int timeslotCol = FindColumn(columnMap, "zeitfenster", "slot", "type", "group");
        int locationCol = FindColumn(columnMap, "ort", "location");

        debugLog.Add($"idCol={idCol}, nameCol={nameCol}, capacityCol={capacityCol}, minCapacityCol={minCapacityCol}, timeslotCol={timeslotCol}");

        var usedIds = new HashSet<string>();

        // Check for empty sheet (no data rows)
        var dataRows = worksheet.RowsUsed().Skip(1).ToList();
        debugLog.Add($"Data rows count: {dataRows.Count}");

        if (dataRows.Count == 0)
        {
            warnings.Add(new ImportWarning(WarningCategory.EmptySheet, "Workshop file has no data rows"));

            // Write debug log to temp file
            debugLog.Add($"Workshops loaded: {workshops.Count}");
            debugLog.Add($"Warnings: {warnings.Count}");
            var earlyDebugPath = Path.Combine(Path.GetTempPath(), "workshop_import_debug.txt");
            File.WriteAllLines(earlyDebugPath, debugLog);

            return (workshops, warnings);
        }

        var rowNumber = 1; // Track row number for error messages (1-based, header is row 1)
        foreach (var row in dataRows)
        {
            rowNumber++;
            var rawId = row.Cell(idCol).GetString();
            var id = rawId.Trim();

            // Treat whitespace-only IDs as empty (skip row, warn)
            if (string.IsNullOrWhiteSpace(id))
            {
                if (!string.IsNullOrEmpty(rawId))
                {
                    warnings.Add(new ImportWarning(WarningCategory.WhitespaceId, $"Row {rowNumber}: Workshop ID contains only whitespace, skipping"));
                }
                continue;
            }

            if (usedIds.Contains(id))
            {
                warnings.Add(new ImportWarning(WarningCategory.DuplicateId, $"Duplicate workshop ID: {id}"));
                continue;
            }
            usedIds.Add(id);

            // Check for empty/missing workshop name
            var name = nameCol > 0 ? row.Cell(nameCol).GetString().Trim() : "";
            if (string.IsNullOrWhiteSpace(name))
            {
                warnings.Add(new ImportWarning(WarningCategory.EmptyWorkshopName, $"Workshop {id} has no name"));
                name = id; // Use ID as fallback name
            }

            // Parse and validate workshop type
            WorkshopType? type = null;
            if (timeslotCol > 0)
            {
                var slot = row.Cell(timeslotCol).GetString().Trim().ToLower();
                if (!string.IsNullOrEmpty(slot))
                {
                    if (TimeslotMapping.TryGetValue(slot, out var mappedType))
                    {
                        type = mappedType;
                    }
                    else
                    {
                        // Invalid workshop type - skip row with warning
                        warnings.Add(new ImportWarning(WarningCategory.WorkshopSkipped, $"Row {rowNumber}: Workshop {id} has invalid type '{slot}', skipping"));
                        continue;
                    }
                }
            }

            // If no type column or empty value, default to Type1
            type ??= WorkshopType.Type1;

            // Parse and validate capacity
            int capacity = 30; // default
            if (capacityCol > 0)
            {
                var capCell = row.Cell(capacityCol);
                if (capCell.TryGetValue<int>(out var cap))
                    capacity = cap;
            }

            // Adjust negative capacity to 0 with warning
            if (capacity < 0)
            {
                warnings.Add(new ImportWarning(WarningCategory.CapacityAdjusted, $"Workshop {id}: negative capacity ({capacity}) set to 0"));
                capacity = 0;
            }

            // Parse and validate min capacity
            int minCapacity = 0;
            if (minCapacityCol > 0)
            {
                var minCell = row.Cell(minCapacityCol);
                if (minCell.TryGetValue<int>(out var min))
                    minCapacity = min;
            }

            // Adjust negative min capacity to 0 with warning
            if (minCapacity < 0)
            {
                warnings.Add(new ImportWarning(WarningCategory.CapacityAdjusted, $"Workshop {id}: negative min capacity ({minCapacity}) set to 0"));
                minCapacity = 0;
            }

            // Adjust min capacity > capacity with warning
            if (minCapacity > capacity)
            {
                warnings.Add(new ImportWarning(WarningCategory.CapacityAdjusted, $"Workshop {id}: min capacity ({minCapacity}) exceeds capacity ({capacity}), set min = capacity"));
                minCapacity = capacity;
            }

            string? location = null;
            if (locationCol > 0)
            {
                var locValue = row.Cell(locationCol).GetString().Trim();
                if (!string.IsNullOrEmpty(locValue))
                    location = locValue;
            }

            workshops.Add(new Workshop(id, name, type.Value, capacity, minCapacity, location, sourceFile));
        }

        debugLog.Add($"Workshops loaded: {workshops.Count}");
        debugLog.Add($"Warnings: {warnings.Count}");

        // Write debug log to temp file
        var debugPath = Path.Combine(Path.GetTempPath(), "workshop_import_debug.txt");
        File.WriteAllLines(debugPath, debugLog);

        return (workshops, warnings);
    }

    public (List<Person> Persons, List<ImportWarning> Warnings) LoadPersons(string filePath, HashSet<string> validWorkshopIds)
    {
        var persons = new List<Person>();
        var warnings = new List<ImportWarning>();
        var sourceFile = Path.GetFileName(filePath);

        using var workbook = new XLWorkbook(filePath);
        var worksheet = FindWorksheetByName(workbook, new[] { "personen", "persons", "teilnehmer", "participants", "people" })
            ?? workbook.Worksheets.First();

        var headerRow = worksheet.Row(1);
        var columnMap = new Dictionary<string, int>();
        for (int col = 1; col <= worksheet.ColumnsUsed().Count(); col++)
        {
            var header = headerRow.Cell(col).GetString().Trim().ToLower();
            columnMap[header] = col;
        }

        int idCol = FindColumn(columnMap, "id");
        int firstNameCol = FindColumn(columnMap, "vorname", "firstname");
        int lastNameCol = FindColumn(columnMap, "nachname", "lastname", "name");
        int infoCol = FindColumn(columnMap, "info", "gruppe", "group");
        int friendCol = FindColumn(columnMap, "freund-id", "freundid", "friend", "friendid");

        // 6 individual preference columns (wunsch 1-6 or wish 1-6)
        var prefCols = new int[6];
        for (int i = 0; i < 6; i++)
        {
            var num = i + 1;
            prefCols[i] = FindColumn(columnMap,
                $"wunsch {num}", $"wunsch{num}", $"wish {num}", $"wish{num}");
        }

        var usedIds = new HashSet<string>();

        // Check for empty sheet (no data rows)
        var dataRows = worksheet.RowsUsed().Skip(1).ToList();
        if (dataRows.Count == 0)
        {
            warnings.Add(new ImportWarning(WarningCategory.EmptySheet, "Persons file has no data rows"));
            return (persons, warnings);
        }

        var rowNumber = 1; // Track row number for error messages (1-based, header is row 1)
        foreach (var row in dataRows)
        {
            rowNumber++;
            try
            {
                var rawId = row.Cell(idCol).GetString();
                var id = rawId.Trim();

                // Treat whitespace-only IDs as empty (skip row, warn)
                if (string.IsNullOrWhiteSpace(id))
                {
                    if (!string.IsNullOrEmpty(rawId))
                    {
                        warnings.Add(new ImportWarning(WarningCategory.WhitespaceId, $"Row {rowNumber}: Person ID contains only whitespace, skipping"));
                    }
                    continue;
                }

                if (usedIds.Contains(id))
                {
                    warnings.Add(new ImportWarning(WarningCategory.DuplicateId, $"Duplicate person ID: {id}"));
                    continue;
                }
                usedIds.Add(id);

                var firstName = firstNameCol > 0 ? row.Cell(firstNameCol).GetString().Trim() : "";
                var lastName = lastNameCol > 0 ? row.Cell(lastNameCol).GetString().Trim() : "";
                var name = $"{firstName} {lastName}".Trim();
                if (string.IsNullOrEmpty(name))
                {
                    warnings.Add(new ImportWarning(WarningCategory.EmptyPersonName, $"Person {id} has no name"));
                    name = id; // Use ID as fallback name
                }

                string? info = null;
                if (infoCol > 0)
                {
                    var infoValue = row.Cell(infoCol).GetString().Trim();
                    if (!string.IsNullOrEmpty(infoValue))
                        info = infoValue;
                }

                string? friendId = null;
                if (friendCol > 0)
                {
                    var fid = row.Cell(friendCol).GetString().Trim();
                    if (!string.IsNullOrEmpty(fid))
                    {
                        if (fid == id)
                        {
                            warnings.Add(new ImportWarning(WarningCategory.SelfReference, $"Person {id} references self as friend, ignoring"));
                        }
                        else
                        {
                            friendId = fid;
                        }
                    }
                }

                // Parse 6 individual preference columns into ranked preference list
                var preferences = new List<string>();
                var seenWorkshopIds = new HashSet<string>();

                for (int i = 0; i < 6; i++)
                {
                    if (prefCols[i] <= 0)
                        continue;

                    var cellValue = row.Cell(prefCols[i]).GetString().Trim();
                    if (string.IsNullOrEmpty(cellValue))
                        continue;

                    if (validWorkshopIds.Count > 0 && !validWorkshopIds.Contains(cellValue))
                    {
                        warnings.Add(new ImportWarning(WarningCategory.UnknownWorkshop, $"Person {id} references unknown workshop {cellValue}"));
                        continue;
                    }

                    if (seenWorkshopIds.Contains(cellValue))
                    {
                        warnings.Add(new ImportWarning(WarningCategory.DuplicateId, $"Person {id}: duplicate workshop {cellValue} in preferences, skipping"));
                        continue;
                    }

                    seenWorkshopIds.Add(cellValue);
                    preferences.Add(cellValue);
                }

                // Warn if fewer than 3 valid preferences (but at least 1)
                if (preferences.Count > 0 && preferences.Count < 3)
                {
                    warnings.Add(new ImportWarning(WarningCategory.IncompleteWish, $"Person {id} has only {preferences.Count} preferences (reduced options)"));
                }

                persons.Add(new Person(id, name, preferences, friendId, info, sourceFile));
            }
            catch (Exception ex)
            {
                warnings.Add(new ImportWarning(WarningCategory.PersonSkipped, $"Row {rowNumber}: Failed to parse person data - {ex.Message}"));
                continue;
            }
        }

        return (persons, warnings);
    }

    private static int FindColumn(Dictionary<string, int> map, params string[] names)
    {
        foreach (var name in names)
        {
            if (map.TryGetValue(name, out var col))
                return col;
        }
        return 0;
    }

    private static IXLWorksheet? FindWorksheetByName(XLWorkbook workbook, string[] names)
    {
        foreach (var ws in workbook.Worksheets)
        {
            var wsNameLower = ws.Name.ToLowerInvariant();
            if (names.Any(n => wsNameLower.Contains(n)))
                return ws;
        }
        return null;
    }

    public void WriteWorkshopLeaderReport(string filePath, AssignmentResult result, List<Workshop> workshops, List<Person> persons,
        string slot1Name, string slot2Name, string slot3Name)
    {
        using var workbook = new XLWorkbook();

        var personMap = persons.ToDictionary(p => p.Id);
        var workshopMap = workshops.ToDictionary(w => w.Id);

        // === Overview Sheet (first sheet) ===
        WriteOverviewSheet(workbook, workshops, result, persons);

        // === Per-Workshop Sheets ===
        foreach (var ws in workshops.OrderBy(w => w.Type).ThenBy(w => w.Name))
        {
            if (ws.Type == WorkshopType.Type4)
            {
                // Type4 workshops generate TWO sheets (one for Slot 2, one for Slot 3)
                WriteType4WorkshopSheet(workbook, ws, result, personMap, workshopMap, slot: 2, slot2Name);
                WriteType4WorkshopSheet(workbook, ws, result, personMap, workshopMap, slot: 3, slot3Name);
            }
            else
            {
                // Non-Type4 workshops generate a single sheet
                var slotName = ws.Type switch
                {
                    WorkshopType.Type1 => slot1Name,
                    WorkshopType.Type2 => slot2Name,
                    WorkshopType.Type3 => slot3Name,
                    _ => ""
                };
                WriteWorkshopSheet(workbook, ws, result, personMap, slotName);
            }
        }

        workbook.SaveAs(filePath);
    }

    private void WriteWorkshopSheet(XLWorkbook workbook, Workshop ws, AssignmentResult result, Dictionary<string, Person> personMap, string slotName)
    {
        var sheet = workbook.AddWorksheet(SanitizeSheetName(ws.Name));

        // Header
        sheet.Cell(1, 1).Value = "Workshop";
        sheet.Cell(1, 2).Value = ws.Name;
        sheet.Cell(2, 1).Value = "Ort";
        sheet.Cell(2, 2).Value = ws.Location ?? "";
        sheet.Cell(3, 1).Value = "Zeitfenster";
        sheet.Cell(3, 2).Value = slotName;
        sheet.Cell(4, 1).Value = "Kapazität";
        sheet.Cell(4, 2).Value = ws.Capacity;

        // Attendee table header
        sheet.Cell(6, 1).Value = "#";
        sheet.Cell(6, 2).Value = "Name";
        sheet.Cell(6, 3).Value = "Info";
        sheet.Row(6).Style.Font.Bold = true;

        var attendees = result.Assignments
            .Where(a => a.WorkshopIds.Contains(ws.Id))
            .Select(a => personMap.GetValueOrDefault(a.PersonId))
            .Where(p => p != null)
            .OrderBy(p => p!.Info ?? "")
            .ThenBy(p => p!.Name)
            .ToList();

        // Write capacity rows: filled attendees first, then empty rows for walk-ins
        for (int i = 0; i < ws.Capacity; i++)
        {
            sheet.Cell(7 + i, 1).Value = i + 1;
            if (i < attendees.Count)
            {
                sheet.Cell(7 + i, 2).Value = attendees[i]!.Name;
                sheet.Cell(7 + i, 3).Value = attendees[i]!.Info ?? "";
            }
            // else: leave name and info cells empty for unfilled spots
        }

        sheet.Columns().AdjustToContents();
    }

    private void WriteOverviewSheet(XLWorkbook workbook, List<Workshop> workshops, AssignmentResult result,
        List<Person> persons)
    {
        var sheet = workbook.AddWorksheet("Overview");

        // Header row
        sheet.Cell(1, 1).Value = "Workshop";
        sheet.Cell(1, 2).Value = "Type";
        sheet.Cell(1, 3).Value = "Min";
        sheet.Cell(1, 4).Value = "Max";
        sheet.Cell(1, 5).Value = "Wished";
        sheet.Cell(1, 6).Value = "Assigned";
        sheet.Cell(1, 7).Value = "%";
        sheet.Cell(1, 8).Value = "W1";
        sheet.Cell(1, 9).Value = "W2";
        sheet.Cell(1, 10).Value = "W3";
        sheet.Cell(1, 11).Value = "W4";
        sheet.Cell(1, 12).Value = "W5";
        sheet.Cell(1, 13).Value = "W6";
        sheet.Row(1).Style.Font.Bold = true;

        // Pre-compute how many persons wished for each workshop (across all 6 preference ranks)
        var wishedCounts = new Dictionary<string, int>();
        foreach (var person in persons)
        {
            foreach (var workshopId in person.Preferences)
            {
                if (!wishedCounts.ContainsKey(workshopId))
                    wishedCounts[workshopId] = 0;
                wishedCounts[workshopId]++;
            }
        }

        // Calculate per-workshop stats: assigned count and wish rank distribution
        var sortedWorkshops = workshops
            .OrderBy(w => w.Type)
            .ThenBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int row = 2;
        foreach (var ws in sortedWorkshops)
        {
            int assignedCount = 0;
            int wish1 = 0, wish2 = 0, wish3 = 0, wish4 = 0, wish5 = 0, wish6 = 0;

            foreach (var assignment in result.Assignments)
            {
                if (!assignment.WorkshopIds.Contains(ws.Id))
                    continue;

                assignedCount++;

                if (assignment.WishRank == 1) wish1++;
                else if (assignment.WishRank == 2) wish2++;
                else if (assignment.WishRank == 3) wish3++;
                else if (assignment.WishRank == 4) wish4++;
                else if (assignment.WishRank == 5) wish5++;
                else if (assignment.WishRank == 6) wish6++;
            }

            var filledPct = ws.Capacity > 0 ? (double)assignedCount / ws.Capacity * 100 : 0;

            sheet.Cell(row, 1).Value = ws.Name;
            sheet.Cell(row, 2).Value = ((int)ws.Type).ToString();
            sheet.Cell(row, 3).Value = ws.MinCapacity;
            sheet.Cell(row, 4).Value = ws.Capacity;
            sheet.Cell(row, 5).Value = wishedCounts.GetValueOrDefault(ws.Id, 0);
            sheet.Cell(row, 6).Value = assignedCount;
            sheet.Cell(row, 7).Value = $"{filledPct:F0}%";

            // Wish rank distribution with count and percentage
            sheet.Cell(row, 8).Value = assignedCount > 0 ? $"{wish1} ({wish1 * 100 / assignedCount}%)" : "-";
            sheet.Cell(row, 9).Value = assignedCount > 0 ? $"{wish2} ({wish2 * 100 / assignedCount}%)" : "-";
            sheet.Cell(row, 10).Value = assignedCount > 0 ? $"{wish3} ({wish3 * 100 / assignedCount}%)" : "-";
            sheet.Cell(row, 11).Value = assignedCount > 0 ? $"{wish4} ({wish4 * 100 / assignedCount}%)" : "-";
            sheet.Cell(row, 12).Value = assignedCount > 0 ? $"{wish5} ({wish5 * 100 / assignedCount}%)" : "-";
            sheet.Cell(row, 13).Value = assignedCount > 0 ? $"{wish6} ({wish6 * 100 / assignedCount}%)" : "-";

            row++;
        }

        sheet.Columns().AdjustToContents();
    }

    private void WriteType4WorkshopSheet(XLWorkbook workbook, Workshop ws, AssignmentResult result,
        Dictionary<string, Person> personMap, Dictionary<string, Workshop> workshopMap, int slot, string slotName)
    {
        var suffix = $" ({slotName})";
        var sheetName = SanitizeSheetName(ws.Name, suffix);
        var sheet = workbook.AddWorksheet(sheetName);

        // Header
        sheet.Cell(1, 1).Value = "Workshop";
        sheet.Cell(1, 2).Value = ws.Name;
        sheet.Cell(2, 1).Value = "Ort";
        sheet.Cell(2, 2).Value = ws.Location ?? "";
        sheet.Cell(3, 1).Value = "Zeitfenster";
        sheet.Cell(3, 2).Value = slotName;
        sheet.Cell(4, 1).Value = "Kapazität";
        sheet.Cell(4, 2).Value = ws.Capacity;

        // Attendee table header
        sheet.Cell(6, 1).Value = "#";
        sheet.Cell(6, 2).Value = "Name";
        sheet.Cell(6, 3).Value = "Info";
        sheet.Row(6).Style.Font.Bold = true;

        // Filter attendees assigned to this Type4 workshop in the specified slot
        var attendees = result.Assignments
            .Where(a => a.WorkshopIds.Contains(ws.Id))
            .Where(a => SlotPlacementHelper.GetType4SlotNumber(ws.Id, a.WorkshopIds, id => workshopMap.GetValueOrDefault(id)) == slot)
            .Select(a => personMap.GetValueOrDefault(a.PersonId))
            .Where(p => p != null)
            .OrderBy(p => p!.Info ?? "")
            .ThenBy(p => p!.Name)
            .ToList();

        // Write capacity rows: filled with attendees first, then empty rows
        for (int i = 0; i < ws.Capacity; i++)
        {
            sheet.Cell(7 + i, 1).Value = i + 1;
            if (i < attendees.Count)
            {
                sheet.Cell(7 + i, 2).Value = attendees[i]!.Name;
                sheet.Cell(7 + i, 3).Value = attendees[i]!.Info ?? "";
            }
            // else: leave name and info cells empty for unfilled spots
        }

        sheet.Columns().AdjustToContents();
    }

    public void WriteAttendeeReport(string filePath, AssignmentResult result, List<Workshop> workshops, List<Person> persons,
        string slot1Name, string slot2Name, string slot3Name)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("People");

        // Header row — matches import format columns plus assignment columns
        sheet.Cell(1, 1).Value = "ID";
        sheet.Cell(1, 2).Value = "Vorname";
        sheet.Cell(1, 3).Value = "Nachname";
        sheet.Cell(1, 4).Value = "Info";
        sheet.Cell(1, 5).Value = "Freund-ID";
        sheet.Cell(1, 6).Value = "Wunsch 1";
        sheet.Cell(1, 7).Value = "Wunsch 2";
        sheet.Cell(1, 8).Value = "Wunsch 3";
        sheet.Cell(1, 9).Value = "Wunsch 4";
        sheet.Cell(1, 10).Value = "Wunsch 5";
        sheet.Cell(1, 11).Value = "Wunsch 6";
        sheet.Cell(1, 12).Value = "Slot 1";
        sheet.Cell(1, 13).Value = "Slot 2";
        sheet.Row(1).Style.Font.Bold = true;

        var workshopMap = workshops.ToDictionary(w => w.Id);
        var assignmentMap = result.Assignments.ToDictionary(a => a.PersonId);

        int row = 2;
        foreach (var person in persons
            .OrderBy(p => p.Info ?? "")
            .ThenBy(p => p.Name))
        {
            sheet.Cell(row, 1).Value = person.Id;

            // Split combined Name back into Vorname/Nachname (import joins "firstName lastName")
            var nameParts = person.Name.Split(' ', 2);
            sheet.Cell(row, 2).Value = nameParts[0];
            sheet.Cell(row, 3).Value = nameParts.Length > 1 ? nameParts[1] : "";

            sheet.Cell(row, 4).Value = person.Info ?? "";
            sheet.Cell(row, 5).Value = person.FriendId ?? "";

            // Wunsch 1-6: output workshop IDs from preferences (same format as import)
            for (int i = 0; i < 6; i++)
            {
                if (i < person.Preferences.Count)
                    sheet.Cell(row, 6 + i).Value = person.Preferences[i];
            }

            // Slot 1 and Slot 2 assignment columns
            var assignment = assignmentMap.GetValueOrDefault(person.Id);
            if (assignment != null && assignment.WorkshopIds.Count > 0)
            {
                var slots = SlotPlacementHelper.ResolveSlots(
                    assignment.WorkshopIds,
                    id => workshopMap.GetValueOrDefault(id));

                if (assignment.IsFullDay)
                {
                    // Type1 (full-day): workshop name in Slot 1, "---" in Slot 2
                    sheet.Cell(row, 12).Value = slots.Slot1Workshop ?? "";
                    sheet.Cell(row, 13).Value = "---";
                }
                else
                {
                    // Half-day: Slot2 workshop -> "Slot 1" column, Slot3 workshop -> "Slot 2" column
                    sheet.Cell(row, 12).Value = slots.Slot2Workshop ?? "";
                    sheet.Cell(row, 13).Value = slots.Slot3Workshop ?? "";
                }
            }
            // else: unassigned — leave Slot 1 and Slot 2 empty

            row++;
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
    }

    private static string SanitizeSheetName(string name)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        foreach (var c in invalid)
            name = name.Replace(c, '_');
        return name.Length > 31 ? name[..31] : name;
    }

    /// <summary>
    /// Builds a sanitized Excel sheet name from a base name and a required suffix,
    /// guaranteeing the suffix is always preserved within the 31-char Excel limit.
    /// The base name is truncated (not the suffix) when the combined length exceeds 31 chars.
    /// </summary>
    private static string SanitizeSheetName(string baseName, string suffix)
    {
        var invalid = new[] { ':', '\\', '/', '?', '*', '[', ']' };
        foreach (var c in invalid)
        {
            baseName = baseName.Replace(c, '_');
            suffix = suffix.Replace(c, '_');
        }

        const int maxLength = 31;
        var maxBaseLength = maxLength - suffix.Length;
        if (maxBaseLength < 1) maxBaseLength = 1; // Safety: suffix alone shouldn't exceed 31

        if (baseName.Length > maxBaseLength)
            baseName = baseName[..maxBaseLength];

        var result = baseName + suffix;
        return result.Length > maxLength ? result[..maxLength] : result;
    }


    /// <summary>
    /// Generates a two-sheet Excel template file (Workshops + Personen) with correct German
    /// column headers matching the import parser expectations, format hints in row 2
    /// (italic gray), and 3 sample rows per sheet. Personen sheet uses 6 individual ranked
    /// preference columns (Wunsch 1-6). Used by ImportWidget's "Download Template" button.
    /// </summary>
    public void GenerateTemplate(string path)
    {
        using var wb = new XLWorkbook();

        // === Workshops Sheet ===
        var ws = wb.AddWorksheet("Workshops");

        // Headers
        ws.Cell(1, 1).Value = "ID";
        ws.Cell(1, 2).Value = "Bezeichnung";
        ws.Cell(1, 3).Value = "Kapazität";
        ws.Cell(1, 4).Value = "Mindestkapazität";
        ws.Cell(1, 5).Value = "Zeitfenster";
        ws.Cell(1, 6).Value = "Ort";

        // Format hints row
        ws.Cell(2, 1).Value = "Required, unique";
        ws.Cell(2, 2).Value = "Workshop name";
        ws.Cell(2, 3).Value = "Max participants (default: 30)";
        ws.Cell(2, 4).Value = "Min participants (default: 0)";
        ws.Cell(2, 5).Value = "a=Full day, b=Slot 2 only, c=Slot 3 only, d=Slot 2 or 3";
        ws.Cell(2, 6).Value = "Room or location";

        // Style hints row
        var hintRange = ws.Range(2, 1, 2, 6);
        hintRange.Style.Font.Italic = true;
        hintRange.Style.Font.FontColor = XLColor.FromHtml("#888888");
        hintRange.Style.Font.FontSize = 10;

        // Sample data
        ws.Cell(3, 1).Value = "W1"; ws.Cell(3, 2).Value = "Yoga"; ws.Cell(3, 3).Value = 20;
        ws.Cell(3, 4).Value = 5; ws.Cell(3, 5).Value = "a"; ws.Cell(3, 6).Value = "Room A";

        ws.Cell(4, 1).Value = "W2"; ws.Cell(4, 2).Value = "Painting"; ws.Cell(4, 3).Value = 25;
        ws.Cell(4, 4).Value = 5; ws.Cell(4, 5).Value = "b"; ws.Cell(4, 6).Value = "Art Studio";

        ws.Cell(5, 1).Value = "W3"; ws.Cell(5, 2).Value = "Cooking"; ws.Cell(5, 3).Value = 15;
        ws.Cell(5, 4).Value = 3; ws.Cell(5, 5).Value = "c"; ws.Cell(5, 6).Value = "Kitchen";

        // Style headers
        var headerRange = ws.Range(1, 1, 1, 6);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2F7");

        ws.Columns().AdjustToContents();

        // === Participants Sheet ===
        var ps = wb.AddWorksheet("Personen");

        // Headers — 6 individual ranked preference columns (no paired a/b format)
        ps.Cell(1, 1).Value = "ID";
        ps.Cell(1, 2).Value = "Vorname";
        ps.Cell(1, 3).Value = "Nachname";
        ps.Cell(1, 4).Value = "Info";
        ps.Cell(1, 5).Value = "Freund-ID";
        ps.Cell(1, 6).Value = "Wunsch 1";
        ps.Cell(1, 7).Value = "Wunsch 2";
        ps.Cell(1, 8).Value = "Wunsch 3";
        ps.Cell(1, 9).Value = "Wunsch 4";
        ps.Cell(1, 10).Value = "Wunsch 5";
        ps.Cell(1, 11).Value = "Wunsch 6";

        // Format hints row
        ps.Cell(2, 1).Value = "Required, unique";
        ps.Cell(2, 2).Value = "First name";
        ps.Cell(2, 3).Value = "Last name";
        ps.Cell(2, 4).Value = "Group/class info";
        ps.Cell(2, 5).Value = "Friend's ID (mutual)";
        ps.Cell(2, 6).Value = "W001";
        ps.Cell(2, 7).Value = "W001";
        ps.Cell(2, 8).Value = "W001";
        ps.Cell(2, 9).Value = "W001";
        ps.Cell(2, 10).Value = "W001";
        ps.Cell(2, 11).Value = "W001";

        // Style hints row
        var pHintRange = ps.Range(2, 1, 2, 11);
        pHintRange.Style.Font.Italic = true;
        pHintRange.Style.Font.FontColor = XLColor.FromHtml("#888888");
        pHintRange.Style.Font.FontSize = 10;

        // Sample data — 6 individual ranked preferences per person
        ps.Cell(3, 1).Value = "P1"; ps.Cell(3, 2).Value = "Alice"; ps.Cell(3, 3).Value = "Smith";
        ps.Cell(3, 4).Value = "Class 5a"; ps.Cell(3, 5).Value = "P2";
        ps.Cell(3, 6).Value = "W001"; ps.Cell(3, 7).Value = "W003";
        ps.Cell(3, 8).Value = "W002"; ps.Cell(3, 9).Value = "W005";
        ps.Cell(3, 10).Value = "W004"; ps.Cell(3, 11).Value = "W006";

        ps.Cell(4, 1).Value = "P2"; ps.Cell(4, 2).Value = "Bob"; ps.Cell(4, 3).Value = "Jones";
        ps.Cell(4, 4).Value = "Class 5a"; ps.Cell(4, 5).Value = "P1";
        ps.Cell(4, 6).Value = "W002"; ps.Cell(4, 7).Value = "W001";
        ps.Cell(4, 8).Value = "W003"; ps.Cell(4, 9).Value = "W004";
        ps.Cell(4, 10).Value = "W006"; ps.Cell(4, 11).Value = "W005";

        ps.Cell(5, 1).Value = "P3"; ps.Cell(5, 2).Value = "Carol"; ps.Cell(5, 3).Value = "White";
        ps.Cell(5, 4).Value = "Class 5b"; ps.Cell(5, 5).Value = "";
        ps.Cell(5, 6).Value = "W001"; ps.Cell(5, 7).Value = "W002";
        ps.Cell(5, 8).Value = "W005"; ps.Cell(5, 9).Value = "W003";
        ps.Cell(5, 10).Value = "W006"; ps.Cell(5, 11).Value = "W004";

        // Style headers
        var pHeaderRange = ps.Range(1, 1, 1, 11);
        pHeaderRange.Style.Font.Bold = true;
        pHeaderRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2F7");

        ps.Columns().AdjustToContents();

        wb.SaveAs(path);
    }
}
