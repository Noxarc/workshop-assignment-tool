using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WorkshopAssignment.Models;
using WorkshopAssignment.Services;

namespace WorkshopAssignment.ViewModels;

/// <summary>
/// Progress stages for the workflow, ordered from earliest to latest.
/// </summary>
/// <summary>
/// RuntimeWidget state machine: Idle (waiting for user) -> Running (solver executing) -> Done (solver complete).
/// Migrated from RuntimeWidget code-behind for proper View/ViewModel separation.
/// </summary>
public enum RunWidgetState { Idle, Running, Done }

/// <summary>
/// Export state determines visual appearance of export buttons.
/// Migrated from ExportWidget code-behind for proper View/ViewModel separation.
/// Disabled = no results, Attention = results available but nothing exported,
/// Partial = one export done, Complete = both exports done.
/// </summary>
public enum ExportState
{
    Disabled,   // No results - buttons greyed out
    Attention,  // Results available, nothing exported - pulsing glow
    Partial,    // One export done - completed one green, other pulsing
    Complete    // Both exports done - both green with checkmarks
}

public enum ProgressStage
{
    Import,     // Need to import data
    Settings,   // Need to configure settings
    Run,        // Ready to run optimization
    Export,     // Need to export results
    Complete    // All done
}

public partial class MainViewModel : ObservableObject
{
    private readonly ExcelService _excelService = new();
    private readonly DataService _dataService = new();
    private readonly SolverService _solverService = new();
    private readonly PdfService _pdfService = new();
    private readonly ILocalizationService _localization = new LocalizationService();
    private readonly ISettingsService _settingsService = new SettingsService();
    private readonly DataAlterationValidator _validator = new();

    public MainViewModel()
    {
        _localization.LanguageChanged += () =>
            OnPropertyChanged("Item[]");
        LoadSettings();
    }

    /// <summary>
    /// String indexer for XAML localization binding.
    /// Usage in XAML: {Binding [import.pageTitle]}
    /// Keys use dot notation matching the JSON translation files (e.g., "import.pageTitle", "settings.slot1Label").
    /// When LanguageChanged fires, OnPropertyChanged("Item[]") refreshes all indexer bindings automatically.
    /// </summary>
    public string this[string key] => _localization.Get(key);

    /// <summary>
    /// Loads persisted settings from ISettingsService on startup.
    /// Delegates all file I/O to SettingsService — ViewModel only applies values.
    /// </summary>
    private void LoadSettings()
    {
        var data = _settingsService.Load();
        Slot1Name = data.Slot1Name;
        Slot2Name = data.Slot2Name;
        Slot3Name = data.Slot3Name;
        TimeLimitMinutes = data.TimeLimitMinutes;
        TimeLimitSeconds = data.TimeLimitSeconds;
        IsPdfSelected = true; // Always default to PDF on startup
        IsDetailedExport = data.IsDetailedExport;
    }

    /// <summary>
    /// Persists a single setting via ISettingsService.
    /// Called by OnXxxChanged partial methods when observable properties change.
    /// </summary>
    private void SaveSetting(string key, object value)
    {
        _settingsService.Save(key, value);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PROGRESS STATE
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImportDone))]
    [NotifyPropertyChangedFor(nameof(IsSettingsDone))]
    [NotifyPropertyChangedFor(nameof(IsRunDone))]
    [NotifyPropertyChangedFor(nameof(IsExportDone))]
    [NotifyPropertyChangedFor(nameof(IsStageTransitionForward))]
    private ProgressStage _currentStage = ProgressStage.Import;

    [ObservableProperty]
    private ProgressStage _previousStage = ProgressStage.Import;

    public bool IsImportDone => CurrentStage > ProgressStage.Import;
    public bool IsSettingsDone => CurrentStage > ProgressStage.Settings || IsSettingsApplied;
    public bool IsRunDone => CurrentStage > ProgressStage.Run;
    public bool IsExportDone => CurrentStage > ProgressStage.Export;
    public bool IsStageTransitionForward => CurrentStage > PreviousStage;

    /// <summary>
    /// Recomputes the current progress stage from state and notifies if changed.
    /// Call this after any state change that might affect progress.
    /// </summary>
    private void UpdateProgressStage()
    {
        var newStage = ComputeProgressStage();
        if (newStage != CurrentStage)
        {
            PreviousStage = CurrentStage;
            CurrentStage = newStage;
        }
    }

    /// <summary>
    /// Computes the current progress stage from state.
    /// Evaluated top-down from most-advanced to least-advanced.
    /// Note: IsSettingsApplied persists across file deletions, so when files are
    /// re-uploaded, we skip Settings and go directly to Run stage.
    /// </summary>
    private ProgressStage ComputeProgressStage()
    {
        if (AllExported) return ProgressStage.Complete;
        if (HasResult) return ProgressStage.Export;
        if (CanRunOptimization) return ProgressStage.Run;
        // If settings were applied but data is missing, stay at Import
        // (IsSettingsDone will still show Settings as completed in the progress bar)
        if (HasData && !IsSettingsApplied) return ProgressStage.Settings;
        return ProgressStage.Import;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CORE STATE
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    private string _currentPage = "Import";

    [ObservableProperty]
    private ObservableCollection<Workshop> _workshops = new();

    [ObservableProperty]
    private ObservableCollection<Person> _persons = new();

    [ObservableProperty]
    private ObservableCollection<string> _workshopFiles = new();

    [ObservableProperty]
    private ObservableCollection<string> _personFiles = new();

    [ObservableProperty]
    private ObservableCollection<ImportWarning> _warnings = new();

    [ObservableProperty]
    private AssignmentResult? _result;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsReachableOrDone))]
    [NotifyPropertyChangedFor(nameof(IsSettingsDone))]
    private bool _isSettingsApplied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AllExported))]
    [NotifyPropertyChangedFor(nameof(CurrentExportState))]
    private bool _workshopsExported;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AllExported))]
    [NotifyPropertyChangedFor(nameof(CurrentExportState))]
    private bool _personsExported;

    [ObservableProperty]
    private string _statusMessage = "";

    // Settings
    [ObservableProperty]
    private string _slot1Name = "09:00 - 12:00";

    [ObservableProperty]
    private string _slot2Name = "09:00 - 10:30";

    [ObservableProperty]
    private string _slot3Name = "10:30 - 12:00";

    [ObservableProperty]
    private int _timeLimitMinutes = 0;

    [ObservableProperty]
    private int _timeLimitSeconds = 30;

    [ObservableProperty]
    private bool _isDetailedExport = true;

    /// <summary>
    /// Single source of truth for export format selection. True = PDF, False = Excel.
    /// ExportFormat is a computed property derived from this — never set ExportFormat
    /// directly. OnIsPdfSelectedChanged notifies ExportFormat and persists the value.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentExportState))]
    private bool _isPdfSelected = true;

    /// <summary>
    /// Read-only computed property derived from IsPdfSelected — NEVER independently settable.
    /// This eliminates the class of desync bugs where IsPdfSelected and ExportFormat
    /// could hold contradictory values. All consumers (export commands, persistence,
    /// UI bindings) read this property and always get the correct format string.
    /// </summary>
    public string ExportFormat => IsPdfSelected ? "PDF" : "Excel";

    partial void OnIsPdfSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ExportFormat));
        SaveSetting("ExportFormat", ExportFormat);
    }

    partial void OnSlot1NameChanged(string value) => SaveSetting("Slot1Name", value);
    partial void OnSlot2NameChanged(string value) => SaveSetting("Slot2Name", value);
    partial void OnSlot3NameChanged(string value) => SaveSetting("Slot3Name", value);
    partial void OnTimeLimitMinutesChanged(int value) => SaveSetting("TimeLimitMinutes", value);
    partial void OnTimeLimitSecondsChanged(int value) => SaveSetting("TimeLimitSeconds", value);
    partial void OnIsDetailedExportChanged(bool value) => SaveSetting("IsDetailedExport", value);

    public bool HasData => Workshops.Count > 0 && Persons.Count > 0;
    public bool CanRun => HasData && !IsRunning;
    public bool CanRunOptimization => HasData && IsSettingsApplied;
    public bool HasResult => Result != null;
    public bool AllExported => WorkshopsExported && PersonsExported;

    /// <summary>
    /// Computed export state based on HasResult, WorkshopsExported, and PersonsExported.
    /// Migrated from ExportWidget.GetCurrentState() for proper View/ViewModel separation.
    /// ExportWidget now reads this property to determine button visual states instead
    /// of maintaining its own duplicate boolean fields.
    /// </summary>
    public ExportState CurrentExportState
    {
        get
        {
            if (!HasResult)
                return ExportState.Disabled;
            if (WorkshopsExported && PersonsExported)
                return ExportState.Complete;
            if (WorkshopsExported || PersonsExported)
                return ExportState.Partial;
            return ExportState.Attention;
        }
    }

    /// <summary>
    /// True when the Settings step is reachable (HasData) OR already completed
    /// (IsSettingsApplied).  Used to keep the Settings progress segment visually
    /// active even after file deletions that temporarily clear data.
    /// </summary>
    public bool IsSettingsReachableOrDone => HasData || IsSettingsApplied;

    public int TotalPersons => Persons.Count;
    public int TotalWorkshops => Workshops.Count;
    public int TotalCapacity => Workshops.Sum(w => w.Capacity);

    // Grouped display collections for DataWidget
    [ObservableProperty]
    private ObservableCollection<PersonGroupDisplay> _personsByInfo = new();

    [ObservableProperty]
    private ObservableCollection<WorkshopTypeDisplay> _workshopsByType = new();

    [ObservableProperty]
    private ObservableCollection<SlotSummaryDisplay> _workshopsBySlot = new();

    // For overflow indication in Persons table
    [ObservableProperty]
    private bool _hasMorePersonGroups;

    // Computed person statistics for DataWidget summary cards — previously calculated
    // in DataWidget code-behind, moved here for proper View/ViewModel separation
    [ObservableProperty]
    private int _infoGroupCount;

    [ObservableProperty]
    private int _friendGroupCount;

    // ═══════════════════════════════════════════════════════════════════════════════
    // RUNTIME WIDGET STATE — migrated from RuntimeWidget code-behind for
    // View/ViewModel separation. View retains only DispatcherTimer, arc geometry,
    // and visual state toggling (CSS class manipulation).
    // ═══════════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunButton))]
    private RunWidgetState _runState = RunWidgetState.Idle;

    [ObservableProperty]
    private int _selectedRuntimeSeconds = 60;

    [ObservableProperty]
    private string _runtimeStatusText = "";

    // Display-friendly slot names set by SettingsWidget when user applies settings.
    // FullSlotName = all-day slot (Slot1), EarlySlotName = morning half (Slot2),
    // LateSlotName = afternoon half (Slot3). Used by DataWidget for capacity labels.
    [ObservableProperty]
    private string _fullSlotName = "Full Slot";

    [ObservableProperty]
    private string _earlySlotName = "Early Slot";

    [ObservableProperty]
    private string _lateSlotName = "Late Slot";

    /// <summary>
    /// True when Run button should be enabled: data imported, settings applied, and solver
    /// not currently executing. CanRunOptimization gates data+settings readiness,
    /// RunState gates solver execution state.
    /// </summary>
    public bool CanRunButton => CanRunOptimization && RunState != RunWidgetState.Running;

    // ═══════════════════════════════════════════════════════════════════════════════════
    // IMPORT FILE STATE — migrated from ImportWidget code-behind for View/ViewModel
    // separation. View retains only UI construction, animations, drag-drop, file pickers.
    // ═══════════════════════════════════════════════════════════════════════════════════

    // Track per-file warnings for status dots and flyouts
    private readonly Dictionary<string, List<ImportWarning>> _fileWarnings = new();

    // Track file paths for reload on deletion — split per category so the same xlsx
    // can be loaded as both workshop source and person source without false duplicate detection
    private readonly Dictionary<string, string> _workshopFilePaths = new();
    private readonly Dictionary<string, string> _personFilePaths = new();

    // Track loading state per file
    private readonly HashSet<string> _loadingFiles = new();

    /// <summary>
    /// Returns warnings for a specific file, or empty list if none.
    /// Used by ImportWidget for status dot color and warning flyout content.
    /// </summary>
    public List<ImportWarning> GetFileWarnings(string fileName)
        => _fileWarnings.TryGetValue(fileName, out var warnings) ? warnings : new List<ImportWarning>();

    /// <summary>
    /// Returns true if the specified file has any warnings.
    /// </summary>
    public bool HasFileWarnings(string fileName)
        => _fileWarnings.TryGetValue(fileName, out var warnings) && warnings.Count > 0;

    /// <summary>
    /// Returns true if the specified file is currently being loaded.
    /// </summary>
    public bool IsFileLoading(string fileName)
        => _loadingFiles.Contains(fileName);

    /// <summary>
    /// Checks if a workshop file with this name is already tracked.
    /// </summary>
    public bool HasWorkshopFilePath(string fileName)
        => _workshopFilePaths.ContainsKey(fileName);

    /// <summary>
    /// Checks if a person file with this name is already tracked.
    /// </summary>
    public bool HasPersonFilePath(string fileName)
        => _personFilePaths.ContainsKey(fileName);

    /// <summary>
    /// Sets warnings and file path for a file loaded externally (e.g., auto-load dummy data).
    /// </summary>
    public void SetFileWarnings(string fileName, List<ImportWarning> warnings)
    {
        _fileWarnings[fileName] = warnings;
    }

    /// <summary>
    /// Sets the workshop file path for a file loaded externally (e.g., auto-load dummy data).
    /// </summary>
    public void SetWorkshopFilePath(string fileName, string filePath)
    {
        _workshopFilePaths[fileName] = filePath;
    }

    /// <summary>
    /// Sets the person file path for a file loaded externally (e.g., auto-load dummy data).
    /// </summary>
    public void SetPersonFilePath(string fileName, string filePath)
    {
        _personFilePaths[fileName] = filePath;
    }

    /// <summary>
    /// Clears all tracked file warnings, paths, and loading state.
    /// Used before re-loading dummy data to prevent duplicates.
    /// </summary>
    public void ClearFileData()
    {
        _fileWarnings.Clear();
        _workshopFilePaths.Clear();
        _personFilePaths.Clear();
        _loadingFiles.Clear();
    }

    /// <summary>
    /// Returns a display-friendly name for a WorkshopType using slot names from settings.
    /// Falls back to "Type N" when settings are not applied.
    /// </summary>
    public string GetTypeDisplayName(WorkshopType type)
    {
        if (!IsSettingsApplied)
        {
            return type switch
            {
                WorkshopType.Type1 => "Type 1",
                WorkshopType.Type2 => "Type 2",
                WorkshopType.Type3 => "Type 3",
                WorkshopType.Type4 => "Type 4",
                _ => type.ToString()
            };
        }

        return type switch
        {
            WorkshopType.Type1 => Slot1Name,
            WorkshopType.Type2 => Slot2Name,
            WorkshopType.Type3 => Slot3Name,
            WorkshopType.Type4 => $"{Slot2Name} & {Slot3Name}",
            _ => type.ToString()
        };
    }

    /// <summary>
    /// Updates the grouped display collections based on current data.
    /// Call this after loading persons or workshops.
    /// </summary>
    public void UpdateGroupedDisplays()
    {
        // Update PersonsByInfo (grouped by Info field, max 3 visible)
        var personGroups = Persons
            .GroupBy(p => p.Info ?? "(No Info)")
            .Select(g => new PersonGroupDisplay(g.Key, g.Count()))
            .OrderByDescending(g => g.Number)
            .ToList();

        PersonsByInfo.Clear();
        HasMorePersonGroups = personGroups.Count > 3;

        // Show max 3 rows + "..." if more than 3
        var displayCount = HasMorePersonGroups ? 3 : personGroups.Count;
        for (int i = 0; i < displayCount; i++)
        {
            PersonsByInfo.Add(personGroups[i]);
        }

        // Add overflow indicator row if needed
        if (HasMorePersonGroups)
        {
            PersonsByInfo.Add(new PersonGroupDisplay("...", "..."));
        }

        // Compute person statistics for DataWidget summary cards
        InfoGroupCount = Persons
            .Where(p => !string.IsNullOrEmpty(p.Info))
            .Select(p => p.Info)
            .Distinct()
            .Count();

        FriendGroupCount = Persons
            .Count(p => !string.IsNullOrEmpty(p.FriendId));

        // Update WorkshopsByType (always 4 rows, one per type)
        // Type4 workshops count towards both Type2 and Type3 totals,
        // since Type4 capacity is available to either slot.
        WorkshopsByType.Clear();
        var type4Workshops = Workshops.Where(w => w.Type == WorkshopType.Type4).ToList();
        var type4Count = type4Workshops.Count;
        var type4Capacity = type4Workshops.Sum(w => w.Capacity);

        foreach (var workshopType in new[] { WorkshopType.Type1, WorkshopType.Type2, WorkshopType.Type3, WorkshopType.Type4 })
        {
            var workshopsOfType = Workshops.Where(w => w.Type == workshopType).ToList();
            var count = workshopsOfType.Count;
            var capacity = workshopsOfType.Sum(w => w.Capacity);

            // Add Type4 totals to Type2 and Type3 rows
            if (workshopType == WorkshopType.Type2 || workshopType == WorkshopType.Type3)
            {
                count += type4Count;
                capacity += type4Capacity;
            }

            var typeName = GetTypeDisplayName(workshopType);
            WorkshopsByType.Add(new WorkshopTypeDisplay(
                typeName,
                count,
                capacity
            ));
        }

        // Update WorkshopsBySlot (2 rows: Slot 2 and Slot 3)
        // Excludes Pause workshops from all counts and capacities
        WorkshopsBySlot.Clear();
        var nonPauseWorkshops = Workshops.Where(w => !IsPauseRow(w.Name)).ToList();

        var type1Workshops = nonPauseWorkshops.Where(w => w.Type == WorkshopType.Type1).ToList();
        var type2Workshops = nonPauseWorkshops.Where(w => w.Type == WorkshopType.Type2).ToList();
        var type3Workshops = nonPauseWorkshops.Where(w => w.Type == WorkshopType.Type3).ToList();
        var type4WorkshopsNonPause = nonPauseWorkshops.Where(w => w.Type == WorkshopType.Type4).ToList();

        var type1Count = type1Workshops.Count;
        var type2Count = type2Workshops.Count;
        var type3Count = type3Workshops.Count;
        var type4CountNonPause = type4WorkshopsNonPause.Count;

        var type2Capacity = type2Workshops.Sum(w => w.Capacity);
        var type3Capacity = type3Workshops.Sum(w => w.Capacity);
        var type4CapacityNonPause = type4WorkshopsNonPause.Sum(w => w.Capacity);

        // Slot 2: Single = Type2 only, Both = Type4
        WorkshopsBySlot.Add(new SlotSummaryDisplay(
            Slot2Name,
            type2Count,
            type4CountNonPause,
            type2Capacity + type4CapacityNonPause
        ));

        // Slot 3: Single = Type3 only, Both = Type4
        WorkshopsBySlot.Add(new SlotSummaryDisplay(
            Slot3Name,
            type3Count,
            type4CountNonPause,
            type3Capacity + type4CapacityNonPause
        ));

        OnPropertyChanged(nameof(TotalCapacity));
    }

    /// <summary>
    /// Import orchestration command for workshop files — encapsulates duplicate detection,
    /// loading state tracking, file path storage, and warning collection. Called by
    /// ImportWidget after user picks a file (via picker or drag-drop).
    /// Returns warnings for the View to determine status dot color.
    /// </summary>
    public async Task<List<ImportWarning>> LoadImportWorkshopFileAsync(string filePath)
    {
        var fileName = System.IO.Path.GetFileName(filePath);
        if (_workshopFilePaths.ContainsKey(fileName))
        {
            return new List<ImportWarning>(); // Duplicate — View handles dialog
        }

        _loadingFiles.Add(fileName);

        List<ImportWarning> fileWarnings;
        try
        {
            fileWarnings = await LoadWorkshopsWithWarningsAsync(filePath);
        }
        catch (Exception ex)
        {
            fileWarnings = new List<ImportWarning>
            {
                new(WarningCategory.InvalidCapacity, $"Could not read file: {ex.Message}")
            };
        }

        _fileWarnings[fileName] = fileWarnings;
        _workshopFilePaths[fileName] = filePath;
        _loadingFiles.Remove(fileName);

        return fileWarnings;
    }

    /// <summary>
    /// Import orchestration command for person files — handles duplicate detection,
    /// loading state, multi-file merge, and auto-workshop detection (if a person file
    /// also contains a workshop sheet and no workshop file is loaded yet, it tries
    /// loading workshops from the same file).
    /// Returns warnings for the View to determine status dot color.
    /// </summary>
    public async Task<List<ImportWarning>> LoadImportPersonFileAsync(string filePath)
    {
        var fileName = System.IO.Path.GetFileName(filePath);
        if (_personFilePaths.ContainsKey(fileName))
        {
            return new List<ImportWarning>(); // Duplicate — View handles dialog
        }

        _loadingFiles.Add(fileName);

        // Auto-detect workshops in same file if no workshop file loaded yet.
        // MUST run BEFORE LoadPersonsWithWarningsAsync because person wish parsing
        // builds validIds from the Workshops collection — if workshops aren't loaded
        // first, all wish references to workshop IDs are silently dropped.
        if (WorkshopFiles.Count == 0)
        {
            try
            {
                await LoadImportWorkshopFileAsync(filePath);
            }
            catch
            {
                // No workshop sheet found — that's fine, this is a people-only file
            }
        }

        List<ImportWarning> fileWarnings;
        try
        {
            fileWarnings = await LoadPersonsWithWarningsAsync(filePath);
        }
        catch (Exception ex)
        {
            fileWarnings = new List<ImportWarning>
            {
                new(WarningCategory.InvalidCapacity, $"Could not read file: {ex.Message}")
            };
            PersonFiles.Add(fileName);
        }

        _fileWarnings[fileName] = fileWarnings;
        _loadingFiles.Remove(fileName);

        _personFilePaths[fileName] = filePath;
        return fileWarnings;
    }

    /// <summary>
    /// Removes a workshop file's tracked state (warnings, path) alongside the
    /// data removal handled by RemoveWorkshopFileCommand.
    /// </summary>
    public void RemoveWorkshopFileState(string fileName)
    {
        _fileWarnings.Remove(fileName);
        _workshopFilePaths.Remove(fileName);
    }

    /// <summary>
    /// Removes a person file's tracked state (warnings, path) alongside the
    /// data removal handled by RemovePersonFileCommand.
    /// </summary>
    public void RemovePersonFileState(string fileName)
    {
        _fileWarnings.Remove(fileName);
        _personFilePaths.Remove(fileName);
    }

    /// <summary>
    /// Generates an Excel template file at the specified path using ExcelService.
    /// Wraps the service call for ViewModel-level access from the View.
    /// </summary>
    public void GenerateTemplateFile(string path)
    {
        _excelService.GenerateTemplate(path);
    }

    /// <summary>
    /// Fires PropertyChanged for all computed data properties and rebuilds grouped displays.
    /// Call after directly modifying collections outside of ViewModel commands.
    /// </summary>
    public void NotifyDataChanged()
    {
        NotifyStateChanged(persons: true, workshops: true, result: true);
        UpdateGroupedDisplays();
    }

    /// <summary>
    /// Notifies PropertyChanged for core computed properties plus optional extras.
    /// Core: HasData, IsSettingsReachableOrDone, CanRun, CanRunOptimization
    /// </summary>
    /// <param name="persons">Include TotalPersons</param>
    /// <param name="workshops">Include TotalWorkshops</param>
    /// <param name="result">Include HasResult</param>
    /// <param name="exported">Include AllExported</param>
    private void NotifyStateChanged(bool persons = false, bool workshops = false, bool result = false, bool exported = false)
    {
        // Core properties (always notified)
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(IsSettingsReachableOrDone));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(CanRunOptimization));
        OnPropertyChanged(nameof(CanRunButton));

        // Optional properties
        if (persons) OnPropertyChanged(nameof(TotalPersons));
        if (workshops) OnPropertyChanged(nameof(TotalWorkshops));
        if (result)
        {
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(CurrentExportState));
        }
        if (exported) OnPropertyChanged(nameof(AllExported));
    }

    [RelayCommand]
    private void ApplySettings()
    {
        IsSettingsApplied = true;
        OnPropertyChanged(nameof(CanRunOptimization));
        OnPropertyChanged(nameof(CanRunButton));
        OnPropertyChanged(nameof(IsSettingsDone));
        UpdateGroupedDisplays();
        UpdateProgressStage();
    }

    [RelayCommand]
    private void NavigateTo(string page)
    {
        CurrentPage = page;
    }

    [RelayCommand]
    private async Task LoadWorkshopsAsync(string filePath)
    {
        await LoadWorkshopsWithWarningsAsync(filePath);
    }

    /// <summary>
    /// Loads workshops from the specified file and returns the file-specific warnings.
    /// Use this method when you need to track warnings per file.
    /// </summary>
    public async Task<List<ImportWarning>> LoadWorkshopsWithWarningsAsync(string filePath)
    {
        var fileName = Path.GetFileName(filePath);

        // Check if file is already imported (compare by filename only)
        if (WorkshopFiles.Contains(fileName))
        {
            StatusMessage = "File already imported. Remove it first to re-import.";
            return new List<ImportWarning>
            {
                new ImportWarning(WarningCategory.DuplicateId, $"File '{fileName}' is already imported. Remove it first to re-import.")
            };
        }

        var (workshops, warnings) = await Task.Run(() => _excelService.LoadWorkshops(filePath));

        Workshops.Clear();
        foreach (var w in workshops)
            Workshops.Add(w);

        WorkshopFiles.Clear();
        WorkshopFiles.Add(fileName);

        foreach (var w in warnings)
            Warnings.Add(w);

        NotifyStateChanged(workshops: true);
        UpdateGroupedDisplays();
        UpdateProgressStage();

        return warnings;
    }

    [RelayCommand]
    private async Task LoadPersonsAsync(string filePath)
    {
        await LoadPersonsWithWarningsAsync(filePath);
    }

    /// <summary>
    /// Loads persons from the specified file and returns the file-specific warnings.
    /// Use this method when you need to track warnings per file.
    /// </summary>
    public async Task<List<ImportWarning>> LoadPersonsWithWarningsAsync(string filePath)
    {
        var fileName = Path.GetFileName(filePath);

        // Check if file is already imported (compare by filename only)
        if (PersonFiles.Contains(fileName))
        {
            StatusMessage = "File already imported. Remove it first to re-import.";
            return new List<ImportWarning>
            {
                new ImportWarning(WarningCategory.DuplicateId, $"File '{fileName}' is already imported. Remove it first to re-import.")
            };
        }

        var validIds = Workshops.Select(w => w.Id).ToHashSet();
        var (persons, warnings) = await Task.Run(() => _excelService.LoadPersons(filePath, validIds));

        var existingIds = Persons.Select(p => p.Id).ToHashSet();
        var fileWarnings = new List<ImportWarning>(warnings);

        foreach (var p in persons)
        {
            if (existingIds.Contains(p.Id))
            {
                fileWarnings.Add(new ImportWarning(WarningCategory.DuplicateId, $"Person {p.Id} already loaded, skipping duplicate"));
                continue;
            }
            Persons.Add(p);
            existingIds.Add(p.Id);
        }

        PersonFiles.Add(fileName);

        foreach (var w in fileWarnings)
            Warnings.Add(w);

        NotifyStateChanged(persons: true);
        UpdateGroupedDisplays();
        UpdateProgressStage();

        return fileWarnings;
    }

    [RelayCommand]
    private void RemoveWorkshopFile(string fileName)
    {
        WorkshopFiles.Remove(fileName);
        // Remove workshops that came from this file
        var toRemove = Workshops.Where(w => w.SourceFile == fileName).ToList();
        foreach (var w in toRemove)
            Workshops.Remove(w);
        Result = null;
        NotifyStateChanged(workshops: true, result: true);
        UpdateGroupedDisplays();
        UpdateProgressStage();
    }

    [RelayCommand]
    private void RemovePersonFile(string fileName)
    {
        PersonFiles.Remove(fileName);
        // Remove persons that came from this file
        var toRemove = Persons.Where(p => p.SourceFile == fileName).ToList();
        foreach (var p in toRemove)
            Persons.Remove(p);
        Result = null;
        NotifyStateChanged(persons: true, result: true);
        UpdateGroupedDisplays();
        UpdateProgressStage();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunOptimizationAsync()
    {
        IsRunning = true;
        StatusMessage = "Running...";
        OnPropertyChanged(nameof(CanRun));

        try
        {
            var workshopList = Workshops.ToList();
            var personList = Persons.ToList();
            var timeLimit = TimeLimitMinutes * 60 + TimeLimitSeconds;
            if (timeLimit <= 0) timeLimit = 30;

            var (result, warnings, validPersons) = await Task.Run(() =>
            {
                var importResult = _dataService.BuildAssignmentInput(workshopList, personList);
                var solverResult = _solverService.Solve(importResult.InputData, timeLimit);
                return (solverResult, importResult.Warnings, importResult.InputData.Persons);
            });

            // Add import warnings on UI thread (already on UI thread after await)
            foreach (var w in warnings)
                Warnings.Add(w);

            // Surface solver warnings (e.g., phantom workshop IDs filtered from wishes)
            foreach (var sw in result.Warnings)
                Warnings.Add(new ImportWarning(WarningCategory.UnknownWorkshop, sw));

            // Update Persons collection to exclude skipped persons
            // This ensures hero table, exports, and statistics only show valid persons
            var validPersonIds = validPersons.Select(p => p.Id).ToHashSet();
            var skippedPersons = Persons.Where(p => !validPersonIds.Contains(p.Id)).ToList();
            foreach (var skipped in skippedPersons)
                Persons.Remove(skipped);

            Result = result;
            UpdateResultDisplayProperties();
            StatusMessage = "Solution found!";
            OnPropertyChanged(nameof(HasResult));
            NotifyStateChanged(persons: true);
            UpdateGroupedDisplays();
            UpdateProgressStage();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            OnPropertyChanged(nameof(CanRun));
        }
    }

    /// <summary>
    /// Full solver lifecycle command — invoked by RuntimeWidget's Run button.
    /// Encapsulates: Done-state reset, time conversion, state transitions,
    /// RunOptimization delegation, and final Done transition.
    /// View retains only timer start/stop and elapsed display (legitimate view concerns).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunButton))]
    private async Task RunSolverAsync()
    {
        // If in Done state, clicking resets to Idle (allows re-run with new settings)
        if (RunState == RunWidgetState.Done)
        {
            RunState = RunWidgetState.Idle;
            return;
        }

        if (!CanRun) return;

        // Convert user-selected seconds into minutes/seconds for solver time limit
        TimeLimitMinutes = SelectedRuntimeSeconds / 60;
        TimeLimitSeconds = SelectedRuntimeSeconds % 60;

        // Transition to Running — RuntimeWidget observes this for visual state
        RunState = RunWidgetState.Running;
        RuntimeStatusText = "Optimizing...";

        try
        {
            await RunOptimizationCommand.ExecuteAsync(null);
            RuntimeStatusText = "Complete!";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Solver error: {ex.Message}");
            RuntimeStatusText = "Error occurred";
        }
        finally
        {
            // Always transition to Done — RuntimeWidget observes for visual cleanup
            RunState = RunWidgetState.Done;
        }
    }

    [RelayCommand]
    private async Task ExportWorkshopsAsync(string filePath)
    {
        if (Result == null) return;

        var result = Result;
        var workshopList = Workshops.ToList();
        var personList = Persons.ToList();
        var slot1 = Slot1Name;
        var slot2 = Slot2Name;
        var slot3 = Slot3Name;
        var exportFormat = ExportFormat;

        await Task.Run(() =>
        {
            if (exportFormat == "Excel")
                _excelService.WriteWorkshopLeaderReport(filePath, result, workshopList, personList, slot1, slot2, slot3);
            else
                _pdfService.WriteWorkshopLeaderReport(filePath, result, workshopList, personList, slot1, slot2, slot3);
        });
        WorkshopsExported = true;
        UpdateProgressStage();
    }

    [RelayCommand]
    private async Task ExportPersonsAsync(string filePath)
    {
        if (Result == null) return;

        var result = Result;
        var workshopList = Workshops.ToList();
        var personList = Persons.ToList();
        var slot1 = Slot1Name;
        var slot2 = Slot2Name;
        var slot3 = Slot3Name;
        var exportFormat = ExportFormat;

        await Task.Run(() =>
        {
            if (exportFormat == "Excel")
                _excelService.WriteAttendeeReport(filePath, result, workshopList, personList, slot1, slot2, slot3);
            else
                _pdfService.WriteAttendeeReport(filePath, result, workshopList, personList, slot1, slot2, slot3);
        });
        PersonsExported = true;
        UpdateProgressStage();
    }

    [RelayCommand]
    private void ClearAll()
    {
        Workshops.Clear();
        Persons.Clear();
        WorkshopFiles.Clear();
        PersonFiles.Clear();
        Warnings.Clear();
        Result = null;
        UpdateResultDisplayProperties();
        IsSettingsApplied = false;
        WorkshopsExported = false;
        PersonsExported = false;
        StatusMessage = "";

        NotifyStateChanged(persons: true, workshops: true, result: true, exported: true);
        UpdateGroupedDisplays();
        UpdateProgressStage();
    }

    [RelayCommand]
    private void ResetSettings()
    {
        // Reset slot names to defaults
        Slot1Name = "09:00 - 12:00";
        Slot2Name = "09:00 - 10:30";
        Slot3Name = "10:30 - 12:00";
        
        // Reset time limit to defaults
        TimeLimitMinutes = 0;
        TimeLimitSeconds = 30;
        
        // Reset export settings
        IsPdfSelected = true;
        IsDetailedExport = true;
        
        // Clear settings applied flag so user must re-confirm
        IsSettingsApplied = false;
        
        OnPropertyChanged(nameof(CanRunOptimization));
        OnPropertyChanged(nameof(CanRunButton));
        OnPropertyChanged(nameof(IsSettingsDone));
        UpdateGroupedDisplays();
        UpdateProgressStage();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // LANGUAGE TOGGLE
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets the current language code from the localization service.
    /// Used by SettingsWidget to highlight the active language in the toggle.
    /// </summary>
    public string CurrentLanguage => _localization.CurrentLanguage;

    /// <summary>
    /// Toggles between English and German. Called by the language toggle in SettingsWidget.
    /// Delegates to ILocalizationService.SetLanguage which fires LanguageChanged,
    /// which in turn triggers OnPropertyChanged("Item[]") to refresh all localized bindings.
    /// Also raises PropertyChanged for CurrentLanguage so the toggle UI updates.
    /// </summary>
    public void SwitchLanguage(string languageCode)
    {
        _localization.SetLanguage(languageCode);
        OnPropertyChanged(nameof(CurrentLanguage));
        // Explicitly fire Item[] to refresh all indexer bindings in case the
        // LanguageChanged event subscription did not propagate through nested
        // binding paths (e.g. {Binding ViewModel[key]} in UserControls where
        // DataContext is MainWindow, not MainViewModel directly).
        OnPropertyChanged("Item[]");
    }


    // ═══════════════════════════════════════════════════════════════════════════════
    // RESULT DISPLAY STATE — migrated from ResultsWidget code-behind for
    // View/ViewModel separation. View retains only bar chart proportions,
    // legend text rendering, and chip UI construction.
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Number of slot entries assigned to rank 1 (most preferred).
    /// Computed from Result.SlotRankDistribution when Result changes.
    /// Type1 workshops count their rank twice (fill full day = 2 slots).
    /// </summary>
    [ObservableProperty]
    private int _wish1Count;

    /// <summary>
    /// Number of slot entries assigned to rank 2.
    /// </summary>
    [ObservableProperty]
    private int _wish2Count;

    /// <summary>
    /// Number of slot entries assigned to rank 3.
    /// </summary>
    [ObservableProperty]
    private int _wish3Count;

    /// <summary>
    /// Number of slot entries assigned to rank 4.
    /// </summary>
    [ObservableProperty]
    private int _wish4Count;

    /// <summary>
    /// Number of slot entries assigned to rank 5.
    /// </summary>
    [ObservableProperty]
    private int _wish5Count;

    /// <summary>
    /// Number of slot entries assigned to rank 6 (least preferred).
    /// </summary>
    [ObservableProperty]
    private int _wish6Count;

    /// <summary>
    /// Slot-weighted count for unassigned persons in the bar chart.
    /// Each unassigned person represents 2 empty slots (matching the slot-based
    /// counting where every person contributes exactly 2 slot entries).
    /// Equals Result.UnassignedCount * 2. Used for bar segment proportions only.
    /// For actual person count in legend text, use UnassignedPersonCount.
    /// </summary>
    [ObservableProperty]
    private int _unassignedCount;

    /// <summary>
    /// Actual number of unassigned persons (NOT slot-weighted).
    /// Used for legend text display: "Unassigned: 30" shows real person count,
    /// while UnassignedCount (60) drives the bar chart segment width.
    /// </summary>
    [ObservableProperty]
    private int _unassignedPersonCount;

    /// <summary>
    /// Actual number of persons assigned to any workshop by the solver.
    /// Uses Result.AssignedCount (real person count), NOT slot-weighted sums.
    /// Displayed in the summary text as "X out of Y assigned".
    /// </summary>
    [ObservableProperty]
    private int _totalAssigned;

    /// <summary>
    /// Resolved names of cancelled workshops (excluding "Pause" workshops).
    /// ResultsWidget reads this to render cancelled workshop chips instead of
    /// resolving workshop IDs to names itself.
    /// </summary>
    [ObservableProperty]
    private List<string> _cancelledWorkshopNames = new();

    /// <summary>
    /// Total number of persons (assigned + unassigned) for the results summary display.
    /// </summary>
    [ObservableProperty]
    private int _resultTotalPersons;

    /// <summary>
    /// Recomputes all result display properties from the current Result.
    /// Called when Result changes (after solver completes or on clear).
    /// Uses SlotRankDistribution which counts per-slot (Type1 double-counted)
    /// to give accurate bar chart segments where every assigned person
    /// contributes exactly 2 slot entries.
    /// </summary>
    public void UpdateResultDisplayProperties()
    {
        if (Result == null)
        {
            Wish1Count = 0;
            Wish2Count = 0;
            Wish3Count = 0;
            Wish4Count = 0;
            Wish5Count = 0;
            Wish6Count = 0;
            UnassignedCount = 0;
            UnassignedPersonCount = 0;
            TotalAssigned = 0;
            ResultTotalPersons = 0;
            CancelledWorkshopNames = new List<string>();
            return;
        }

        var slotDist = Result.SlotRankDistribution;
        Wish1Count = slotDist.TryGetValue(1, out var v1) ? v1 : 0;
        Wish2Count = slotDist.TryGetValue(2, out var v2) ? v2 : 0;
        Wish3Count = slotDist.TryGetValue(3, out var v3) ? v3 : 0;
        Wish4Count = slotDist.TryGetValue(4, out var v4) ? v4 : 0;
        Wish5Count = slotDist.TryGetValue(5, out var v5) ? v5 : 0;
        Wish6Count = slotDist.TryGetValue(6, out var v6) ? v6 : 0;
        // TotalAssigned = sum of filled slots from bar segments.
        TotalAssigned = Wish1Count + Wish2Count + Wish3Count + Wish4Count + Wish5Count + Wish6Count;
        // Unfilled slots = total possible slots - filled slots.
        // Covers fully unassigned (2 open) AND half-day solos (1 open).
        UnassignedCount = (Result.TotalPersons * 2) - TotalAssigned;
        UnassignedPersonCount = Result.UnassignedCount;
        // ResultTotalPersons = always TotalPersons × 2 (every person has 2 slots).
        ResultTotalPersons = Result.TotalPersons * 2;

        CancelledWorkshopNames = Result.CancelledWorkshopIds
            .Select(id => Workshops.FirstOrDefault(w => w.Id == id))
            .Where(w => w != null && !w!.Name.Contains("Pause", StringComparison.OrdinalIgnoreCase))
            .Select(w => w!.Name)
            .ToList();
    }

}
