using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using WorkshopAssignment.ViewModels;

namespace WorkshopAssignment.Views.Controls;

public partial class SettingsWidget : UserControl
{
    /// <summary>
    /// Tracks which language is currently selected for the sliding pill toggle.
    /// EN = true (default), DE = false. Mirrors ExportWidget's _isPdfSelected pattern.
    /// </summary>
    private bool _isEnglishSelected = true;

    /// <summary>
    /// Cached brushes for the language toggle text colors.
    /// Active option text is White (over the teal pill), inactive is TextGrey (#555555)
    /// to exactly match ExportWidget's UpdateToggleVisuals.
    /// </summary>
    private static readonly IBrush WhiteBrush = new SolidColorBrush(Colors.White);
    private static readonly IBrush TextGreyBrush = new SolidColorBrush(Color.Parse("#555555"));

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
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    /// <summary>
    /// Event raised when user requests to auto-load dummy data for testing.
    /// </summary>
    public event EventHandler? AutoLoadDummyDataRequested;

    /// <summary>
    /// Event raised when user clicks Apply to save slot name changes.
    /// </summary>
    public event EventHandler<SlotNamesChangedEventArgs>? SlotNamesChanged;

    public SettingsWidget()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Handles ViewModel property changes. Syncs the language toggle visual state
    /// when CurrentLanguage changes (e.g. when the other responsive variant of
    /// SettingsWidget switches language, or language is changed programmatically).
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentLanguage))
        {
            var isEn = ViewModel?.CurrentLanguage == "en";
            if (_isEnglishSelected != isEn)
            {
                _isEnglishSelected = isEn;
                AnimateLanguageToggle();
                UpdateLanguageToggleVisuals();
            }
        }
    }

    // Slot1Name/Slot2Name/Slot3Name local property wrappers REMOVED in MVVM refactoring.
    // TextBoxes now bind directly to ViewModel.Slot1Name/Slot2Name/Slot3Name via two-way
    // data binding in AXAML. No code-behind proxying needed.

    /// <summary>
    /// Handles Apply button click. TextBox values are already synced to ViewModel
    /// via two-way data binding (Slot1Name/Slot2Name/Slot3Name). This method only
    /// needs to push display-friendly names and execute ApplySettingsCommand.
    /// </summary>
    private void Apply_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            // Push slot display names to ViewModel so DataWidget labels update reactively.
            // Slot1/2/3Name are already current via two-way AXAML binding — no manual sync needed.
            ViewModel.FullSlotName = ViewModel.Slot1Name;
            ViewModel.EarlySlotName = ViewModel.Slot2Name;
            ViewModel.LateSlotName = ViewModel.Slot3Name;

            // Execute ApplySettingsCommand to set IsSettingsApplied = true,
            // which enables CanRunOptimization and unlocks the Run button
            ViewModel.ApplySettingsCommand.Execute(null);
        }

        var args = new SlotNamesChangedEventArgs(
            ViewModel?.Slot1Name ?? ViewModel?["settings.slot1Default"] ?? "Morning Session",
            ViewModel?.Slot2Name ?? ViewModel?["settings.slot2Default"] ?? "Early Block",
            ViewModel?.Slot3Name ?? ViewModel?["settings.slot3Default"] ?? "Late Block"
        );

        SlotNamesChanged?.Invoke(this, args);
    }

    private void AutoLoadDummyData_Click(object? sender, RoutedEventArgs e)
    {
        // Raise event for parent to handle dummy data loading
        AutoLoadDummyDataRequested?.Invoke(this, EventArgs.Empty);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // LANGUAGE TOGGLE HANDLERS
    // Mirrors ExportWidget's PdfOption_Click/ExcelOption_Click pattern exactly:
    // state bool + AnimateToggle + UpdateToggleVisuals
    // ══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Switches to English. Guards against redundant clicks (same as ExportWidget pattern).
    /// Uses PointerPressed (not Click) because the toggle options are Borders, not Buttons.
    /// </summary>
    private void LangEn_Click(object? sender, PointerPressedEventArgs e)
    {
        if (!_isEnglishSelected)
        {
            _isEnglishSelected = true;
            AnimateLanguageToggle();
            UpdateLanguageToggleVisuals();
            ViewModel?.SwitchLanguage("en");
        }
    }

    /// <summary>
    /// Switches to German. Guards against redundant clicks (same as ExportWidget pattern).
    /// </summary>
    private void LangDe_Click(object? sender, PointerPressedEventArgs e)
    {
        if (_isEnglishSelected)
        {
            _isEnglishSelected = false;
            AnimateLanguageToggle();
            UpdateLanguageToggleVisuals();
            ViewModel?.SwitchLanguage("de");
        }
    }

    /// <summary>
    /// Animates the teal pill sliding between EN and DE positions.
    /// Exact copy of ExportWidget.AnimateToggle — same easing (ease-in-out quadratic),
    /// same 200ms duration, same 16ms tick interval for ~60fps.
    /// </summary>
    private void AnimateLanguageToggle()
    {
        double targetX = _isEnglishSelected ? 0 : 72;

        if (LangActivePill.RenderTransform is TranslateTransform pillTransform)
        {
            var startX = pillTransform.X;
            var deltaX = targetX - startX;
            if (Math.Abs(deltaX) < 0.1) return;

            var duration = 200.0;
            var startTime = DateTime.Now;

            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };

            timer.Tick += (s, ev) =>
            {
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                var progress = Math.Min(elapsed / duration, 1.0);

                // Ease-in-out quadratic — same curve as ExportWidget
                var eased = progress < 0.5
                    ? 2.0 * progress * progress
                    : 1.0 - Math.Pow(-2.0 * progress + 2.0, 2) / 2.0;

                pillTransform.X = startX + deltaX * eased;

                if (progress >= 1.0)
                {
                    pillTransform.X = targetX;
                    timer.Stop();
                }
            };

            timer.Start();
        }
    }

    /// <summary>
    /// Updates EN/DE text foreground colors to match which option sits over the teal pill.
    /// Active = White (text over teal gradient pill), Inactive = TextGrey (#555555).
    /// Mirrors ExportWidget.UpdateToggleVisuals exactly.
    /// </summary>
    private void UpdateLanguageToggleVisuals()
    {
        if (_isEnglishSelected)
        {
            LangEnText.Foreground = WhiteBrush;
            LangDeText.Foreground = TextGreyBrush;
        }
        else
        {
            LangEnText.Foreground = TextGreyBrush;
            LangDeText.Foreground = WhiteBrush;
        }
    }

    /// <summary>
    /// Resets slot names to default values by writing to ViewModel properties.
    /// Two-way data binding propagates changes to the TextBoxes automatically.
    /// </summary>
    public void ResetSlotNames()
    {
        if (ViewModel == null) return;
        ViewModel.Slot1Name = ViewModel["settings.slot1Default"] ?? "Morning Session";
        ViewModel.Slot2Name = ViewModel["settings.slot2Default"] ?? "Early Block";
        ViewModel.Slot3Name = ViewModel["settings.slot3Default"] ?? "Late Block";
    }
}

/// <summary>
/// Event arguments for slot name changes.
/// </summary>
public class SlotNamesChangedEventArgs : EventArgs
{
    public string Slot1Name { get; }
    public string Slot2Name { get; }
    public string Slot3Name { get; }

    public SlotNamesChangedEventArgs(string slot1Name, string slot2Name, string slot3Name)
    {
        Slot1Name = slot1Name;
        Slot2Name = slot2Name;
        Slot3Name = slot3Name;
    }
}
