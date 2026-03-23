using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WorkshopAssignment.Models;
using WorkshopAssignment.ViewModels;

namespace WorkshopAssignment.Views;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    // Hero expand state — animation flags remain in View because they control
    // Avalonia visual tree transitions (ScaleTransform, Opacity, IsVisible).
    // Data state (IsHeroExpanded, HeroSection) lives in ViewModel.
    private bool _isHeroAnimating = false;

    // Persons table sort state — View owns this because it controls header
    // TextBlock arrow indicators (UI-only concern). Sort execution delegates
    // to ViewModel.SortHeroPersons().
    private string? _personSortColumn = null;
    private bool _personSortAscending = true;

    // Responsive breakpoint
    private const double LargeModeThreshold = 950;

    // Fixed content width for large mode (glass panel stays this size, only margins change)
    private const double LargeModeContentWidth = 930;

    // Column definitions - stored at runtime since x:Name on ColumnDefinition may not generate fields
    private ColumnDefinition? _leftColumnDef;
    private ColumnDefinition? _spacerColumnDef;
    private ColumnDefinition? _rightColumnDef;
    
    // ScaleTransform for hero animation - stored at runtime
    private ScaleTransform? _heroScaleTransform;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this; // Window is DataContext, bindings use ViewModel.PropertyName
        
        // Get column definitions from the WidgetGrid
        if (this.FindControl<Grid>("WidgetGrid") is Grid widgetGrid)
        {
            _leftColumnDef = widgetGrid.ColumnDefinitions.Count > 0 ? widgetGrid.ColumnDefinitions[0] : null;
            _spacerColumnDef = widgetGrid.ColumnDefinitions.Count > 1 ? widgetGrid.ColumnDefinitions[1] : null;
            _rightColumnDef = widgetGrid.ColumnDefinitions.Count > 2 ? widgetGrid.ColumnDefinitions[2] : null;
        }
        
        // Get scale transform from hero panel
        if (this.FindControl<Border>("HeroPanel") is Border heroPanel && 
            heroPanel.RenderTransform is ScaleTransform scaleTransform)
        {
            _heroScaleTransform = scaleTransform;
        }

        // Pass ViewModel to all widgets
        ImportWidget.ViewModel = ViewModel;
        ImportWidgetLarge.ViewModel = ViewModel;
        SettingsWidget.ViewModel = ViewModel;
        SettingsWidgetLarge.ViewModel = ViewModel;
        RuntimeWidget.ViewModel = ViewModel;
        ResultsWidget.ViewModel = ViewModel;
        ExportWidget.ViewModel = ViewModel;
        ExportWidgetLarge.ViewModel = ViewModel;
        // Small variants for single-column layouts
        DataWidgetSmall.ViewModel = ViewModel;
        RuntimeWidgetSmall.ViewModel = ViewModel;
        ResultsWidgetSmall.ViewModel = ViewModel;

        // Subscribe to auto-load dummy data from settings
        SettingsWidget.AutoLoadDummyDataRequested += OnAutoLoadDummyDataRequested;
        SettingsWidgetLarge.AutoLoadDummyDataRequested += OnAutoLoadDummyDataRequested;

        // Subscribe to expand requests from data widgets
        DataWidgetSmall.ExpandRequested += OnDataWidgetExpandRequested;

        // Subscribe to ViewModel property changes for export widget notifications
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Force all {Binding ViewModel[key]} bindings to re-evaluate on language change.
        // Avalonia doesn't propagate Item[] PropertyChanged through nested property paths
        // (DataContext=Window, binding=ViewModel[key]), so we toggle DataContext to force re-resolve.
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == "Item[]")
            {
                DataContext = null;
                DataContext = this;
            }
        };

        // Subscribe to window size changes for responsive layout
        this.PropertyChanged += (s, e) =>
        {
            if (e.Property == ClientSizeProperty)
                OnClientSizeChanged(ClientSize);
        };

        // Set initial responsive mode
        Dispatcher.UIThread.Post(() => UpdateResponsiveMode(ClientSize.Width), DispatcherPriority.Loaded);
    }



    private void OnClientSizeChanged(Size size)
    {
        UpdateResponsiveMode(size.Width);
    }

    private void UpdateResponsiveMode(double width)
    {
        // Toggle Classes for responsive layout
        if (width >= LargeModeThreshold)
        {
            Classes.Remove("small-mode");
            if (!Classes.Contains("large-mode"))
                Classes.Add("large-mode");

            // Glass panel stays at fixed width — only margins change on resize
            if (GlassContainer != null) GlassContainer.Width = LargeModeContentWidth;

            // Update column definitions for large mode - golden ratio (1:1.618)
            if (_leftColumnDef != null) { _leftColumnDef.Width = new GridLength(382, GridUnitType.Star); _leftColumnDef.MaxWidth = 420; }
            if (_spacerColumnDef != null) _spacerColumnDef.Width = new GridLength(20);
            if (_rightColumnDef != null) _rightColumnDef.Width = new GridLength(618, GridUnitType.Star);
        }
        else
        {
            Classes.Remove("large-mode");
            if (!Classes.Contains("small-mode"))
                Classes.Add("small-mode");

            // Small mode: glass panel stretches to full width
            if (GlassContainer != null) GlassContainer.Width = double.NaN;

            // Update column definitions for small mode (single column)
            if (_leftColumnDef != null) { _leftColumnDef.Width = new GridLength(1, GridUnitType.Star); _leftColumnDef.MaxWidth = double.PositiveInfinity; }
            if (_spacerColumnDef != null) _spacerColumnDef.Width = new GridLength(0);
            if (_rightColumnDef != null) _rightColumnDef.Width = new GridLength(0);
        }

        // Sync ImportWidget display state on layout switch.
        // Both ImportWidget and ImportWidgetLarge share the same ViewModel, but each
        // maintains its own View-owned UI state (_personFileItems, WorkshopFileItem.IsVisible).
        // When switching layouts, the newly-visible widget must rebuild its UI from
        // the shared ViewModel state, otherwise uploaded files appear to vanish.
        if (width >= LargeModeThreshold)
        {
            ImportWidgetLarge.UpdateDisplay();
        }
        else
        {
            ImportWidget.UpdateDisplay();
        }

        // Clear focus to prevent stuck focus on hidden elements
        FocusSink.Focus();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.HasResult))
        {
            // Notify export widgets when results become available
            ExportWidget.OnAlgorithmComplete();
            ExportWidgetLarge.OnAlgorithmComplete();
        }
    }



    private async void OnAutoLoadDummyDataRequested(object? sender, EventArgs e)
    {
        try
        {
            // Clear all existing data first
            ViewModel.ClearAllCommand.Execute(null);
            ImportWidget.ClearFileData();
            ImportWidget.UpdateDisplay();
            ImportWidgetLarge.ClearFileData();
            ImportWidgetLarge.UpdateDisplay();

            // Use AppContext.BaseDirectory for reliable path resolution
            var sampleDir = Path.Combine(AppContext.BaseDirectory, "assets", "sample_data");

            // Load test data — check for both people and workshops in the file
            var testDataFile = Path.Combine(sampleDir, "sample_testdata.xlsx");
            if (File.Exists(testDataFile))
            {
                await ImportWidgetLarge.LoadWorkshopFile(testDataFile);
                await ImportWidgetLarge.LoadPersonFile(testDataFile);
            }

            ImportWidget.UpdateDisplay();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load dummy data: {ex.Message}");
        }
    }



    // === Hero Expand ===

    private void OnDataWidgetExpandRequested(object? sender, string section)
    {
        if (_isHeroAnimating || ViewModel.IsHeroExpanded) return;
        if (sender is not Control sourceWidget) return;

        ExpandHero(section, sourceWidget);
    }

    /// <summary>
    /// Expands the hero overlay panel for the specified section.
    /// Data preparation is delegated to ViewModel.PrepareHeroData() which populates
    /// HeroWorkshops/HeroPersons collections, HeroTitle, and HeroCountText.
    /// This method handles only the visual expansion: animation triggers, container
    /// visibility, column header localization, and ItemsSource binding.
    /// </summary>
    private void ExpandHero(string section, Control sourceWidget)
    {
        _isHeroAnimating = true;

        // Delegate all data preparation to ViewModel
        ViewModel.PrepareHeroDataCommand.Execute(section);

        // Make overlay visible (but transparent) first so visual tree is built
        HeroPanel.Opacity = 0;
        HeroBackdrop.Opacity = 0;
        HeroOverlay.IsVisible = true;

        // Configure container visibility based on section
        if (section == "Workshops")
        {
            HeroWorkshopsContainer.IsVisible = true;
            HeroPersonsContainer.IsVisible = false;

            // Bind ItemsSource to ViewModel's prepared collection
            HeroWorkshopsList.ItemsSource = ViewModel.HeroWorkshops;

            // Set column headers from localization — named TextBlocks in ItemsControl header row
            WColId.Text = ViewModel["hero.colId"];
            WColName.Text = ViewModel["hero.colName"];
            WColGroup.Text = ViewModel["hero.colGroup"];
            WColMin.Text = ViewModel["hero.colMin"];
            WColMax.Text = ViewModel["hero.colMax"];
            WColWished.Text = ViewModel["hero.colWished"];
            WColAssigned.Text = ViewModel["hero.colAssigned"];
            WColUtilization.Text = ViewModel["hero.colUtilization"];
        }
        else
        {
            HeroWorkshopsContainer.IsVisible = false;
            HeroPersonsContainer.IsVisible = true;

            // Reset sort state for fresh open
            _personSortColumn = null;
            _personSortAscending = true;

            // Bind ItemsSource to ViewModel's prepared collection
            HeroPersonsList.ItemsSource = ViewModel.HeroPersons;

            // Set column headers from localization — named TextBlocks in ItemsControl header row
            PColName.Text = ViewModel["hero.colName"];
            PColInfo.Text = ViewModel["hero.colInfo"];
            PColFriend.Text = ViewModel["hero.colFriend"];
            PColWish1.Text = ViewModel["hero.colWish1"];
            PColWish2.Text = ViewModel["hero.colWish2"];
            PColWish3.Text = ViewModel["hero.colWish3"];
            PColAssigned.Text = ViewModel["hero.colAssigned"];
        }

        // Set title from ViewModel (already localized by PrepareHeroData)
        HeroTitle.Text = ViewModel.HeroTitle;

        // Set initial state for animation
        if (_heroScaleTransform != null)
        {
            _heroScaleTransform.ScaleX = 0.55;
            _heroScaleTransform.ScaleY = 0.55;
        }
        // Trigger animation by setting final values (Transitions handle animation)
        Dispatcher.UIThread.Post(() =>
        {
            if (_heroScaleTransform != null)
            {
                _heroScaleTransform.ScaleX = 1;
                _heroScaleTransform.ScaleY = 1;
            }
            HeroPanel.Opacity = 1;
            HeroBackdrop.Opacity = 1;

            // Set badge text after layout cycle — ensures visual tree is ready to render
            // HeroCountText is already computed by ViewModel.PrepareHeroData with localized format
            HeroCountText.Text = ViewModel.HeroCountText;
        }, DispatcherPriority.Render);

        ViewModel.IsHeroExpanded = true;

        // Release animation lock after animation duration
        Dispatcher.UIThread.Post(() =>
        {
            _isHeroAnimating = false;
        }, DispatcherPriority.Background);
    }



    private void CollapseHero()
    {
        if (_isHeroAnimating || !ViewModel.IsHeroExpanded) return;
        _isHeroAnimating = true;

        // Trigger collapse animation
        if (_heroScaleTransform != null)
        {
            _heroScaleTransform.ScaleX = 0.55;
            _heroScaleTransform.ScaleY = 0.55;
        }
        HeroPanel.Opacity = 0;
        HeroBackdrop.Opacity = 0;

        // After animation, hide and cleanup
        Dispatcher.UIThread.Post(() =>
        {
            HeroOverlay.IsVisible = false;
            MainScrollViewer.Opacity = 1;
            ViewModel.IsHeroExpanded = false;
            _isHeroAnimating = false;

            // Clear ItemsControl sources and badge text to free memory
            // Clearing badge text ensures next open triggers a property change for re-render
            HeroWorkshopsList.ItemsSource = null;
            HeroPersonsList.ItemsSource = null;
            HeroCountText.Text = "";
            HeroTitle.Text = "";
        }, DispatcherPriority.Background);
    }

    private void HeroBackButton_Click(object? sender, RoutedEventArgs e)
    {
        CollapseHero();
    }

    private void HeroBackdrop_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CollapseHero();
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ViewModel.IsHeroExpanded && !_isHeroAnimating)
        {
            CollapseHero();
            e.Handled = true;
        }
    }

    private void OnBackgroundPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Don't steal focus from interactive controls — this breaks flyouts on buttons
        // (e.g., ImportWidget warning flyout never appears because FocusSink.Focus()
        // fires before Button.Click, dismissing the flyout immediately)
        if (e.Source is Control c && (c is Button || c.FindAncestorOfType<Button>() != null))
        {
            e.Handled = true;
            return;
        }

        FocusSink.Focus();
    }



    // === Hero Grid Type Cell Click-to-Edit ===

    private void TypeCell_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBlock textBlock) return;
        if (textBlock.Parent is not Panel panel) return;

        // Find the ComboBox sibling in the Panel
        var comboBox = panel.Children.OfType<ComboBox>().FirstOrDefault();
        if (comboBox == null) return;

        // Hide TextBlock, show ComboBox, open dropdown
        textBlock.IsVisible = false;
        comboBox.IsVisible = true;
        comboBox.IsDropDownOpen = true;
        e.Handled = true;
    }

    private void TypeComboBox_DropDownClosed(object? sender, EventArgs e)
    {
        if (sender is not ComboBox comboBox) return;
        if (comboBox.Parent is not Panel panel) return;

        // Find the TextBlock sibling in the Panel
        var textBlock = panel.Children.OfType<TextBlock>().FirstOrDefault();
        if (textBlock == null) return;

        // Hide ComboBox, show TextBlock
        comboBox.IsVisible = false;
        textBlock.IsVisible = true;
    }

    // === Hero Grid Type ComboBox — delegates type change to ViewModel ===

    private void TypeComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox) return;
        if (comboBox.SelectedItem is not WorkshopTypeOption selectedOption) return;
        if (comboBox.DataContext is not HeroWorkshopDisplay display) return;

        // Delegate to ViewModel — it handles immutable record replacement,
        // SourceWorkshop sync, and grouped display refresh
        ViewModel.UpdateWorkshopType(display, selectedOption.Type);
    }

    /// <summary>
    /// Propagates Name/Min/Max TextBox edits to the immutable Workshop record via ViewModel.
    /// The HeroWorkshopDisplay's mutable properties are already updated by Avalonia two-way binding;
    /// this handler commits those changes to the underlying Workshop collection.
    /// </summary>
    private void WorkshopField_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        if (textBox.DataContext is not HeroWorkshopDisplay display) return;

        // Delegate to ViewModel — it creates a new Workshop record from the
        // display's current field values and replaces the old one in the collection
        ViewModel.UpdateWorkshopField(display);
    }

    /// <summary>
    /// Select-all on focus for Min/Max numeric TextBoxes.
    /// Uses Dispatcher.Post because Avalonia's default focus behavior sets the caret position
    /// after GotFocus fires — posting SelectAll ensures it runs after the caret is placed.
    /// </summary>
    private void NumericField_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is TextBox tb)
        {
            Dispatcher.UIThread.Post(() => tb.SelectAll());
        }
    }

    /// <summary>
    /// Numeric-only input filter for Min/Max TextBoxes.
    /// Rejects any non-digit characters by marking the event as handled.
    /// </summary>
    private void NumericField_TextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Text != null && !e.Text.All(char.IsDigit))
        {
            e.Handled = true;
        }
    }



    // === Persons Table Sort — delegates to ViewModel, View owns arrow indicators ===

    private void PersonSortHeader_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBlock header) return;
        var column = header.Tag as string;
        if (string.IsNullOrEmpty(column)) return;

        // Toggle direction if same column, otherwise default to ascending
        if (_personSortColumn == column)
            _personSortAscending = !_personSortAscending;
        else
        {
            _personSortColumn = column;
            _personSortAscending = true;
        }

        // Delegate sort execution to ViewModel
        ViewModel.SortHeroPersons(_personSortColumn, _personSortAscending);

        // Update sort indicators in column headers (UI-only concern)
        UpdatePersonSortIndicators();

        e.Handled = true;
    }

    private void UpdatePersonSortIndicators()
    {
        var arrow = _personSortAscending ? " \u25b2" : " \u25bc";
        var nameLabel = ViewModel["hero.colName"];
        var infoLabel = ViewModel["hero.colInfo"];

        PColName.Text = _personSortColumn == "Name" ? nameLabel + arrow : nameLabel;
        PColInfo.Text = _personSortColumn == "Info" ? infoLabel + arrow : infoLabel;
    }
}
