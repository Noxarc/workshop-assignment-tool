using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using System.Text.RegularExpressions;
using WorkshopAssignment.Models;
using WorkshopAssignment.ViewModels;

namespace WorkshopAssignment.Views.Controls;

public partial class ImportWidget : UserControl
{
    private MainViewModel? _viewModel;
    public MainViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel != null)
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = value;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                UpdateDisplay();
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Workshops) ||
            e.PropertyName == nameof(MainViewModel.Persons))
        {
            UpdateDisplay();
        }
    }

    // File state (warnings, paths, loading) now lives in MainViewModel.
    // View queries ViewModel via GetFileWarnings, HasFileWarnings, IsFileLoading, etc.

    // Track dynamically created person file UI elements
    private readonly Dictionary<string, Border> _personFileItems = new();

    // Spinner animation timers
    private readonly Dictionary<string, DispatcherTimer> _spinnerTimers = new();

    /// <summary>
    /// Raised when files are loaded or removed, so parent can refresh other widgets.
    /// </summary>
    public event EventHandler? FilesChanged;

    /// <summary>
    /// Clears all tracked file warnings and paths. Used before re-loading dummy data to prevent duplicates.
    /// </summary>
    public void ClearFileData()
    {
        // Delegate state cleanup to ViewModel
        _viewModel?.ClearFileData();
        
        // Clean up View-owned resources (spinner timers, dynamic UI elements)
        foreach (var timer in _spinnerTimers.Values)
            timer.Stop();
        _spinnerTimers.Clear();
        
        // Remove dynamic person file items
        foreach (var item in _personFileItems.Values)
        {
            if (PersonPanel.Children.Contains(item))
                PersonPanel.Children.Remove(item);
        }
        _personFileItems.Clear();
    }

    /// <summary>
    /// Sets warnings for a file loaded externally (e.g., auto-load dummy data).
    /// </summary>
    public void SetFileWarnings(string fileName, List<ImportWarning> warnings)
    {
        _viewModel?.SetFileWarnings(fileName, warnings);
    }

    /// <summary>
    /// Sets the workshop file path for a file loaded externally (e.g., auto-load dummy data).
    /// </summary>
    public void SetWorkshopFilePath(string fileName, string filePath)
    {
        _viewModel?.SetWorkshopFilePath(fileName, filePath);
    }

    /// <summary>
    /// Sets the person file path for a file loaded externally (e.g., auto-load dummy data).
    /// </summary>
    public void SetPersonFilePath(string fileName, string filePath)
    {
        _viewModel?.SetPersonFilePath(fileName, filePath);
    }

    public ImportWidget()
    {
        InitializeComponent();
        
        // Set up Avalonia DragDrop handlers
        AddHandler(DragDrop.DragOverEvent, DropZone_DragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }


    #region DragDrop Handlers

    private void DropZone_DragOver(object? sender, DragEventArgs e)
    {
        // Accept only file drops
        e.DragEffects = e.Data.Contains(DataFormats.Files) 
            ? DragDropEffects.Copy 
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files)) return;
        
        var files = e.Data.GetFiles();
        if (files == null) return;

        // Determine which drop zone received the drop based on position
        var workshopPoint = e.GetPosition(WorkshopDropZone);
        var personPoint = e.GetPosition(PersonDropZone);
        var addMorePoint = e.GetPosition(AddMoreBtn);
        
        bool isWorkshopZone = WorkshopDropZone.IsVisible &&
                              workshopPoint.X >= 0 && workshopPoint.X <= WorkshopDropZone.Bounds.Width &&
                              workshopPoint.Y >= 0 && workshopPoint.Y <= WorkshopDropZone.Bounds.Height;
        bool isPersonZone = (PersonDropZone.IsVisible &&
                             personPoint.X >= 0 && personPoint.X <= PersonDropZone.Bounds.Width &&
                             personPoint.Y >= 0 && personPoint.Y <= PersonDropZone.Bounds.Height) ||
                            (AddMoreBtn.IsVisible &&
                             addMorePoint.X >= 0 && addMorePoint.X <= AddMoreBtn.Bounds.Width &&
                             addMorePoint.Y >= 0 && addMorePoint.Y <= AddMoreBtn.Bounds.Height);

        foreach (var item in files)
        {
            if (item is IStorageFile file && IsExcelFile(file.Name))
            {
                var path = file.Path.LocalPath;
                if (isWorkshopZone && ViewModel?.WorkshopFiles.Count == 0)
                {
                    await LoadWorkshopFile(path);
                }
                else if (isPersonZone)
                {
                    await LoadPersonFile(path);
                }
                else if (!isWorkshopZone && !isPersonZone)
                {
                    // Fallback: position-based detection missed both zones.
                    // This happens when the pointer lands outside WorkshopDropZone
                    // and PersonDropZone bounds (e.g., on the gap between zones,
                    // the widget border, or on overlapping UI elements).
                    // Route the file using current application state instead:
                    //   - No workshop loaded yet → treat as workshop drop
                    //   - Workshop loaded, accepting more files → treat as person drop
                    if (ViewModel?.WorkshopFiles.Count == 0)
                    {
                        await LoadWorkshopFile(path);
                    }
                    else
                    {
                        await LoadPersonFile(path);
                    }
                }
            }
        }
    }

    #endregion

    #region Click Handlers

    private async void WorkshopDropZone_Click(object? sender, PointerPressedEventArgs e)
    {
        // Block import if a workshop file is already loaded (single file only)
        if (ViewModel?.WorkshopFiles.Count > 0) return;

        var file = await PickExcelFileAsync();
        if (file != null && ViewModel != null)
        {
            await LoadWorkshopFile(file.Path.LocalPath);
        }
    }

    private async void PersonDropZone_Click(object? sender, PointerPressedEventArgs e)
    {
        var file = await PickExcelFileAsync();
        if (file != null && ViewModel != null)
        {
            await LoadPersonFile(file.Path.LocalPath);
        }
    }

    private async void AddMoreBtn_Click(object? sender, PointerPressedEventArgs e)
    {
        var file = await PickExcelFileAsync();
        if (file != null && ViewModel != null)
        {
            await LoadPersonFile(file.Path.LocalPath);
        }
    }

    private void WorkshopDeleteBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.WorkshopFiles.Count > 0)
        {
            var fileName = ViewModel.WorkshopFiles[0];
            ViewModel.RemoveWorkshopFileState(fileName);
            ViewModel.RemoveWorkshopFileCommand.Execute(fileName);
            UpdateDisplay();
            FilesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void WorkshopWarningBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.WorkshopFiles.Count > 0)
        {
            var fileName = ViewModel.WorkshopFiles[0];
            var warnings = ViewModel.GetFileWarnings(fileName);
            if (warnings.Count > 0)
            {
                ShowWarningsDialog(fileName, warnings, sender as Control);
            }
        }
    }

    private void PersonDeleteBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string fileName)
        {
            ViewModel?.RemovePersonFileState(fileName);
            
            // Remove UI element
            if (_personFileItems.TryGetValue(fileName, out var item))
            {
                PersonPanel.Children.Remove(item);
                _personFileItems.Remove(fileName);
            }
            
            ViewModel?.RemovePersonFileCommand.Execute(fileName);
            UpdateDisplay();
            FilesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void PersonWarningBtn_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string fileName && ViewModel != null)
        {
            var warnings = ViewModel.GetFileWarnings(fileName);
            if (warnings.Count > 0)
            {
                ShowWarningsDialog(fileName, warnings, btn);
            }
        }
    }

    private async void TemplateBtn_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = ViewModel?["import.saveTemplate"] ?? "Save Template",
                    SuggestedFileName = "workshop_template",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Excel Files")
                        {
                            Patterns = new[] { "*.xlsx" }
                        }
                    },
                    DefaultExtension = "xlsx"
                });

            if (file == null) return;

            var path = file.Path.LocalPath;
            ViewModel?.GenerateTemplateFile(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Template export failed: {ex.Message}");
        }
    }

    // GenerateTemplateFile moved to ExcelService.GenerateTemplate — called via ViewModel.GenerateTemplateFile

    #endregion


    #region File Loading

    public async Task<List<ImportWarning>> LoadWorkshopFile(string filePath)
    {
        if (ViewModel == null) return new List<ImportWarning>();

        var fileName = System.IO.Path.GetFileName(filePath);
        if (ViewModel.HasWorkshopFilePath(fileName))
        {
            await ShowDuplicateFileDialog(fileName);
            return new List<ImportWarning>();
        }

        // Show loading state (View concern — spinner animation)
        ShowWorkshopLoading(fileName);

        // Delegate orchestration to ViewModel
        var fileWarnings = await ViewModel.LoadImportWorkshopFileAsync(filePath);
        
        // Update display and play completion animation (View concern)
        UpdateWorkshopDisplay(fileName, fileWarnings.Count > 0);
        PlayRippleAnimation(WorkshopRipple, fileWarnings.Count > 0);
        
        FilesChanged?.Invoke(this, EventArgs.Empty);
        return fileWarnings;
    }

    public async Task<List<ImportWarning>> LoadPersonFile(string filePath)
    {
        if (ViewModel == null) return new List<ImportWarning>();

        var fileName = System.IO.Path.GetFileName(filePath);
        if (ViewModel.HasPersonFilePath(fileName))
        {
            await ShowDuplicateFileDialog(fileName);
            return new List<ImportWarning>();
        }

        // Create and show loading state (View concern — UI construction and spinner)
        var fileItem = CreatePersonFileItem(fileName, true, false);
        var insertIndex = PersonPanel.Children.Count - 2;
        if (insertIndex < 0) insertIndex = 0;
        PersonPanel.Children.Insert(insertIndex, fileItem);
        _personFileItems[fileName] = fileItem;

        if (fileItem.Tag is PersonFileItemData itemData)
        {
            StartSpinnerAnimation(fileName, itemData.Spinner);
        }

        UpdatePersonPanelVisibility();

        // Delegate orchestration to ViewModel (handles loading, error wrapping, state tracking,
        // auto-workshop detection, and file path registration)
        var fileWarnings = await ViewModel.LoadImportPersonFileAsync(filePath);

        // Update View after load completes
        StopSpinnerAnimation(fileName);
        UpdatePersonFileItem(fileName, fileWarnings.Count > 0);

        // If auto-workshop detection found workshops, update workshop display
        if (ViewModel.WorkshopFiles.Count > 0 && !WorkshopFileItem.IsVisible)
        {
            var wsFileName = ViewModel.WorkshopFiles[0];
            UpdateWorkshopDisplay(wsFileName, ViewModel.HasFileWarnings(wsFileName));
        }

        FilesChanged?.Invoke(this, EventArgs.Empty);
        return fileWarnings;
    }

    #endregion


    #region Display Updates

    public void UpdateDisplay()
    {
        if (ViewModel == null) return;

        // Workshop section
        if (ViewModel.WorkshopFiles.Count > 0)
        {
            var fileName = ViewModel.WorkshopFiles[0];
            var hasWarnings = ViewModel.HasFileWarnings(fileName);
            UpdateWorkshopDisplay(fileName, hasWarnings);
        }
        else
        {
            // Show drop zone
            WorkshopDropZone.IsVisible = true;
            WorkshopFileItem.IsVisible = false;
        }

        // People section - rebuild dynamic items
        RebuildPersonFileItems();
        UpdatePersonPanelVisibility();
    }

    private void ShowWorkshopLoading(string fileName)
    {
        WorkshopDropZone.IsVisible = false;
        WorkshopFileItem.IsVisible = true;
        WorkshopFileName.Text = fileName;
        WorkshopFileName.Classes.Add("loading");
        WorkshopSpinner.IsVisible = true;
        WorkshopStatusDot.IsVisible = false;
        WorkshopWarningBtn.IsVisible = false;
        WorkshopDeleteBtn.IsVisible = false;
        WorkshopFileItem.Classes.Remove("error");
        
        // Start spinner animation
        StartSpinnerAnimation("workshop", WorkshopSpinnerArc);
    }

    private void UpdateWorkshopDisplay(string fileName, bool hasWarnings)
    {
        WorkshopDropZone.IsVisible = false;
        WorkshopFileItem.IsVisible = true;
        WorkshopFileName.Text = fileName;
        WorkshopFileName.Classes.Remove("loading");
        
        // Stop spinner
        StopSpinnerAnimation("workshop");
        WorkshopSpinner.IsVisible = false;
        WorkshopStatusDot.IsVisible = true;
        WorkshopDeleteBtn.IsVisible = true;
        
        if (hasWarnings)
        {
            WorkshopStatusDot.Classes.Remove("success");
            WorkshopStatusDot.Classes.Add("error");
            WorkshopRipple.Classes.Remove("success");
            WorkshopRipple.Classes.Add("error");
            WorkshopFileItem.Classes.Add("error");
            WorkshopWarningBtn.IsVisible = true;
        }
        else
        {
            WorkshopStatusDot.Classes.Remove("error");
            WorkshopStatusDot.Classes.Add("success");
            WorkshopRipple.Classes.Remove("error");
            WorkshopRipple.Classes.Add("success");
            WorkshopFileItem.Classes.Remove("error");
            WorkshopWarningBtn.IsVisible = false;
        }
    }

    private void RebuildPersonFileItems()
    {
        if (ViewModel == null) return;

        // Get current files
        var currentFiles = ViewModel.PersonFiles.ToHashSet();
        
        // Remove items for files that no longer exist
        var toRemove = _personFileItems.Keys.Where(f => !currentFiles.Contains(f)).ToList();
        foreach (var fileName in toRemove)
        {
            if (_personFileItems.TryGetValue(fileName, out var item))
            {
                PersonPanel.Children.Remove(item);
                _personFileItems.Remove(fileName);
            }
        }
        
        // Add items for new files
        foreach (var fileName in ViewModel.PersonFiles)
        {
            if (!_personFileItems.ContainsKey(fileName))
            {
                var hasWarnings = ViewModel.HasFileWarnings(fileName);
                var isLoading = ViewModel.IsFileLoading(fileName);
                var fileItem = CreatePersonFileItem(fileName, isLoading, hasWarnings);
                
                var insertIndex = PersonPanel.Children.Count - 2;
                if (insertIndex < 0) insertIndex = 0;
                PersonPanel.Children.Insert(insertIndex, fileItem);
                _personFileItems[fileName] = fileItem;
            }
        }
    }

    private void UpdatePersonPanelVisibility()
    {
        var hasFiles = _personFileItems.Count > 0;
        PersonDropZone.IsVisible = !hasFiles;
        AddMoreBtn.IsVisible = hasFiles;
    }


    private Border CreatePersonFileItem(string fileName, bool isLoading, bool hasWarnings)
    {
        var border = new Border
        {
            Height = 32,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 0),
            Background = hasWarnings ? new SolidColorBrush(Color.Parse("#fee2e2")) : Brushes.Transparent
        };
        border.Classes.Add("file-item");
        if (hasWarnings) border.Classes.Add("error");

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto")
        };

        // Status indicator panel
        var statusPanel = new Panel
        {
            Width = 16,
            Height = 16,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        // Spinner
        var spinnerContainer = new Border
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            IsVisible = isLoading
        };
        spinnerContainer.Classes.Add("spinner-container");
        
        var spinner = new Arc
        {
            Width = 8,
            Height = 8,
            StrokeThickness = 1.5,
            Stroke = new SolidColorBrush(Color.Parse("#55acbf")),
            StartAngle = 0,
            SweepAngle = 270,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative)
        };
        spinner.Classes.Add("spinner");
        spinnerContainer.Child = spinner;

        // Status dot
        var statusDot = new Ellipse
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Fill = hasWarnings 
                ? new SolidColorBrush(Color.Parse("#ef4444")) 
                : new SolidColorBrush(Color.Parse("#22c55e")),
            IsVisible = !isLoading
        };
        statusDot.Classes.Add("status-dot");
        statusDot.Classes.Add(hasWarnings ? "error" : "success");

        // Ripple
        var ripple = new Ellipse
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Fill = hasWarnings 
                ? new SolidColorBrush(Color.Parse("#ef4444")) 
                : new SolidColorBrush(Color.Parse("#22c55e")),
            Opacity = 0,
            RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RenderTransform = new ScaleTransform(1, 1)
        };
        ripple.Classes.Add("ripple");
        ripple.Classes.Add(hasWarnings ? "error" : "success");

        statusPanel.Children.Add(spinnerContainer);
        statusPanel.Children.Add(statusDot);
        statusPanel.Children.Add(ripple);
        Grid.SetColumn(statusPanel, 0);

        // File name
        var fileNameText = new TextBlock
        {
            Text = fileName,
            FontSize = 12,
            Foreground = isLoading 
                ? new SolidColorBrush(Color.Parse("#6b7280")) 
                : new SolidColorBrush(Color.Parse("#1a1a2e")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0)
        };
        fileNameText.Classes.Add("file-name");
        if (isLoading) fileNameText.Classes.Add("loading");
        Grid.SetColumn(fileNameText, 1);

        // Warning button
        var warningBtn = new Button
        {
            Content = ViewModel?["import.warningButton"] ?? "Warning",
            FontSize = 11,
            Padding = new Thickness(8, 2),
            CornerRadius = new CornerRadius(3),
            BorderThickness = new Thickness(0),
            MinHeight = 20,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
            IsVisible = hasWarnings && !isLoading,
            Background = new SolidColorBrush(Color.Parse("#26F59E0B")),
            Foreground = new SolidColorBrush(Color.Parse("#b45309")),
            Tag = fileName
        };
        warningBtn.Classes.Add("warning-btn");
        warningBtn.Click += PersonWarningBtn_Click;
        Grid.SetColumn(warningBtn, 2);

        // Delete button
        var deleteBtn = new Button
        {
            Content = "\u00D7", // multiplication sign
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Padding = new Thickness(4, 0),
            BorderThickness = new Thickness(0),
            MinWidth = 24,
            MinHeight = 24,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#999999")),
            IsVisible = !isLoading,
            Tag = fileName
        };
        deleteBtn.Classes.Add("delete-btn");
        deleteBtn.Click += PersonDeleteBtn_Click;
        Grid.SetColumn(deleteBtn, 3);

        grid.Children.Add(statusPanel);
        grid.Children.Add(fileNameText);
        grid.Children.Add(warningBtn);
        grid.Children.Add(deleteBtn);
        border.Child = grid;

        // Store references for animation
        border.Tag = new PersonFileItemData
        {
            SpinnerContainer = spinnerContainer,
            Spinner = spinner,
            StatusDot = statusDot,
            Ripple = ripple,
            FileNameText = fileNameText,
            WarningBtn = warningBtn,
            DeleteBtn = deleteBtn
        };

        return border;
    }

    private class PersonFileItemData
    {
        public Border SpinnerContainer { get; set; } = null!;
        public Arc Spinner { get; set; } = null!;
        public Ellipse StatusDot { get; set; } = null!;
        public Ellipse Ripple { get; set; } = null!;
        public TextBlock FileNameText { get; set; } = null!;
        public Button WarningBtn { get; set; } = null!;
        public Button DeleteBtn { get; set; } = null!;
    }

    #endregion


    #region Animations

    private void StartSpinnerAnimation(string key, object spinner)
    {
        if (_spinnerTimers.ContainsKey(key)) return;

        var angle = 0.0;
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60fps
        };
        
        timer.Tick += (s, e) =>
        {
            angle = (angle + 8) % 360; // 8 degrees per frame = ~0.8s per rotation
            
            if (spinner is Arc arc)
            {
                arc.RenderTransform = new RotateTransform(angle);
            }
        };
        
        _spinnerTimers[key] = timer;
        timer.Start();
    }

    private void StopSpinnerAnimation(string key)
    {
        if (_spinnerTimers.TryGetValue(key, out var timer))
        {
            timer.Stop();
            _spinnerTimers.Remove(key);
        }
    }

    private async void PlayRippleAnimation(Ellipse ripple, bool isError)
    {
        // Set initial state
        ripple.Opacity = 0.6;
        ripple.RenderTransform = new ScaleTransform(1, 1);
        
        // Animation parameters
        var duration = TimeSpan.FromMilliseconds(500);
        var startTime = DateTime.Now;
        var endScale = 3.5;
        
        // Use a timer for smooth animation
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        
        timer.Tick += (s, e) =>
        {
            var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            var progress = Math.Min(elapsed / duration.TotalMilliseconds, 1.0);
            
            // Ease out cubic
            var eased = 1 - Math.Pow(1 - progress, 3);
            
            // Interpolate scale and opacity
            var scale = 1 + (endScale - 1) * eased;
            var opacity = 0.6 * (1 - eased);
            
            ripple.RenderTransform = new ScaleTransform(scale, scale);
            ripple.Opacity = opacity;
            
            if (progress >= 1.0)
            {
                timer.Stop();
                ripple.Opacity = 0;
                ripple.RenderTransform = new ScaleTransform(1, 1);
            }
        };
        
        timer.Start();
    }

    #endregion

    #region Utilities

    private static bool IsExcelFile(string fileName)
    {
        var ext = System.IO.Path.GetExtension(fileName).ToLowerInvariant();
        return ext == ".xlsx" || ext == ".xls";
    }

    private async Task ShowDuplicateFileDialog(string fileName)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;
        // Simple duplicate detection - could show dialog in future
    }

    private void ShowWarningsDialog(string fileName, List<ImportWarning> warnings, Control? target = null)
    {
        // Build the flyout content: header with file name, scrollable list of categorized warnings
        var contentPanel = new StackPanel
        {
            Spacing = 8,
            MinWidth = 280,
            MaxWidth = 380
        };

        // Header: file name with warning count
        var headerPanel = new StackPanel { Spacing = 2 };
        headerPanel.Children.Add(new TextBlock
        {
            Text = fileName,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#1a1a2e")),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        headerPanel.Children.Add(new TextBlock
        {
            Text = $"{warnings.Count} warning{(warnings.Count != 1 ? "s" : "")}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#b45309"))
        });
        contentPanel.Children.Add(headerPanel);

        // Divider line between header and warnings list
        contentPanel.Children.Add(new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.Parse("#e0e0e0")),
            Margin = new Thickness(0, 2)
        });

        // Scrollable warnings list
        var warningsList = new StackPanel { Spacing = 6 };

        // Group warnings by category for a cleaner presentation
        var groupedWarnings = warnings
            .GroupBy(w => w.Category)
            .OrderBy(g => g.Key.ToString());

        foreach (var group in groupedWarnings)
        {
            // Category label
            var categoryName = FormatWarningCategory(group.Key);
            warningsList.Children.Add(new TextBlock
            {
                Text = categoryName,
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse("#555555")),
                Margin = new Thickness(0, 2, 0, 0)
            });

            foreach (var warning in group)
            {
                // Each warning as a compact row with a tinted background
                var warningRow = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#08F59E0B")),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(8, 4)
                };

                var warningText = new TextBlock
                {
                    Text = warning.Message,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#1a1a2e")),
                    TextWrapping = TextWrapping.Wrap
                };

                warningRow.Child = warningText;
                warningsList.Children.Add(warningRow);
            }
        }

        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 300,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = warningsList
        };
        contentPanel.Children.Add(scrollViewer);

        // Create and show the flyout attached to the target button
        var flyout = new Flyout
        {
            Content = contentPanel,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            ShowMode = FlyoutShowMode.Standard
        };

        // Determine which control to attach the flyout to
        var attachTarget = target ?? this;
        flyout.ShowAt(attachTarget);
    }

    /// <summary>
    /// Converts a WarningCategory enum value into a human-readable label for display in the warnings flyout.
    /// Uses space-separated words derived from the PascalCase enum name, with common acronyms preserved.
    /// </summary>
    private static string FormatWarningCategory(WarningCategory category)
    {
        // Insert spaces before uppercase letters to break PascalCase, e.g. "DuplicateId" -> "Duplicate Id"
        var name = category.ToString();
        var formatted = System.Text.RegularExpressions.Regex.Replace(name, "(?<!^)([A-Z])", " $1");
        return formatted;
    }

    private async Task<IStorageFile?> PickExcelFileAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Excel File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Excel Files")
                {
                    Patterns = new[] { "*.xlsx", "*.xls" }
                }
            }
        });

        return files.Count > 0 ? files[0] : null;
    }

    #endregion

    private void UpdatePersonFileItem(string fileName, bool hasWarnings)
    {
        if (!_personFileItems.TryGetValue(fileName, out var border)) return;
        if (border.Tag is not PersonFileItemData data) return;

        // Update background
        border.Background = hasWarnings 
            ? new SolidColorBrush(Color.Parse("#fee2e2")) 
            : Brushes.Transparent;
        
        if (hasWarnings)
            border.Classes.Add("error");
        else
            border.Classes.Remove("error");

        // Hide spinner, show status dot
        data.SpinnerContainer.IsVisible = false;
        data.StatusDot.IsVisible = true;
        data.StatusDot.Fill = hasWarnings 
            ? new SolidColorBrush(Color.Parse("#ef4444")) 
            : new SolidColorBrush(Color.Parse("#22c55e"));
        
        // Update ripple color
        data.Ripple.Fill = hasWarnings 
            ? new SolidColorBrush(Color.Parse("#ef4444")) 
            : new SolidColorBrush(Color.Parse("#22c55e"));

        // Update file name style
        data.FileNameText.Foreground = new SolidColorBrush(Color.Parse("#1a1a2e"));
        data.FileNameText.Classes.Remove("loading");

        // Show/hide warning button
        data.WarningBtn.IsVisible = hasWarnings;
        data.DeleteBtn.IsVisible = true;

        // Play ripple animation
        PlayRippleAnimation(data.Ripple, hasWarnings);
    }
}
