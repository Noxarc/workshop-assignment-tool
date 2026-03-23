using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WorkshopAssignment.ViewModels;

namespace WorkshopAssignment.Views.Controls;

public partial class ResultsWidget : UserControl
{
    private MainViewModel? _viewModel;
    private IBrush? _segment1OriginalBackground;
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

    public ResultsWidget()
    {
        InitializeComponent();
        _segment1OriginalBackground = Segment1.Background;
        ResetToPlaceholder();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Result) ||
            e.PropertyName == nameof(MainViewModel.HasResult) ||
            e.PropertyName == nameof(MainViewModel.Wish1Count) ||
            e.PropertyName == nameof(MainViewModel.CancelledWorkshopNames))
        {
            UpdateDisplay();
        }
    }

    private void ResetToPlaceholder()
    {
        // Show single empty grey bar — no fake proportions before results exist
        StackedBarContainer.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        for (int i = 1; i < 7; i++)
            StackedBarContainer.ColumnDefinitions[i].Width = new GridLength(0);
        Segment1.Background = new SolidColorBrush(Color.Parse("#e5e7eb"));

        // Reset legend text for all 7 legends
        Legend1Text.Text = $"{ViewModel?["results.legendWish1"] ?? "1st"}: --";
        Legend2Text.Text = $"{ViewModel?["results.legendWish2"] ?? "2nd"}: --";
        Legend3Text.Text = $"{ViewModel?["results.legendWish3"] ?? "3rd"}: --";
        Legend4Text.Text = $"{ViewModel?["results.legendWish4"] ?? "4th"}: --";
        Legend5Text.Text = $"{ViewModel?["results.legendWish5"] ?? "5th"}: --";
        Legend6Text.Text = $"{ViewModel?["results.legendWish6"] ?? "6th"}: --";
        Legend7Text.Text = $"{ViewModel?["results.legendUnassigned"] ?? "None"}: --";

        // Reset summary
        AssignedCountText.Text = "--";
        TotalCountText.Text = "--";

        // Reset cancelled workshops
        CancelledHeader.Text = ViewModel?["results.cancelledHeader"] ?? "Cancelled Workshops";
        CancelledChipsContainer.Children.Clear();
        CancelledChipsContainer.Children.Add(NoCancelledPlaceholder);
        NoCancelledPlaceholder.IsVisible = true;
    }

    public void UpdateDisplay()
    {
        if (ViewModel?.Result == null)
        {
            ResetToPlaceholder();
            return;
        }

        // Read pre-computed values from ViewModel (6 ranks + unassigned)
        int wish1Count = ViewModel.Wish1Count;
        int wish2Count = ViewModel.Wish2Count;
        int wish3Count = ViewModel.Wish3Count;
        int wish4Count = ViewModel.Wish4Count;
        int wish5Count = ViewModel.Wish5Count;
        int wish6Count = ViewModel.Wish6Count;
        int unassignedCount = ViewModel.UnassignedCount;         // slot-weighted (doubled) for bar
        int unassignedPersons = ViewModel.UnassignedPersonCount; // actual persons for legend text
        int totalAssigned = ViewModel.TotalAssigned;
        int total = ViewModel.ResultTotalPersons;

        // Update 7 segment proportions (using star sizing)
        // Minimum of 0.01 to prevent zero-width segments when there are values
        double seg1 = total > 0 ? Math.Max(wish1Count, 0.01) : 1;
        double seg2 = total > 0 ? Math.Max(wish2Count, 0.01) : 0;
        double seg3 = total > 0 ? Math.Max(wish3Count, 0.01) : 0;
        double seg4 = total > 0 ? Math.Max(wish4Count, 0.01) : 0;
        double seg5 = total > 0 ? Math.Max(wish5Count, 0.01) : 0;
        double seg6 = total > 0 ? Math.Max(wish6Count, 0.01) : 0;
        double seg7 = total > 0 ? Math.Max(unassignedCount, 0.01) : 0;

        // Restore Segment1 gradient (was grey in placeholder state)
        Segment1.Background = _segment1OriginalBackground;

        StackedBarContainer.ColumnDefinitions[0].Width = new GridLength(seg1, GridUnitType.Star);
        StackedBarContainer.ColumnDefinitions[1].Width = new GridLength(seg2, GridUnitType.Star);
        StackedBarContainer.ColumnDefinitions[2].Width = new GridLength(seg3, GridUnitType.Star);
        StackedBarContainer.ColumnDefinitions[3].Width = new GridLength(seg4, GridUnitType.Star);
        StackedBarContainer.ColumnDefinitions[4].Width = new GridLength(seg5, GridUnitType.Star);
        StackedBarContainer.ColumnDefinitions[5].Width = new GridLength(seg6, GridUnitType.Star);
        StackedBarContainer.ColumnDefinitions[6].Width = new GridLength(seg7, GridUnitType.Star);

        // Update legend text with counts for all 7 legends
        Legend1Text.Text = $"{ViewModel["results.legendWish1"]}: {wish1Count}";
        Legend2Text.Text = $"{ViewModel["results.legendWish2"]}: {wish2Count}";
        Legend3Text.Text = $"{ViewModel["results.legendWish3"]}: {wish3Count}";
        Legend4Text.Text = $"{ViewModel["results.legendWish4"]}: {wish4Count}";
        Legend5Text.Text = $"{ViewModel["results.legendWish5"]}: {wish5Count}";
        Legend6Text.Text = $"{ViewModel["results.legendWish6"]}: {wish6Count}";
        Legend7Text.Text = $"{ViewModel["results.legendUnassigned"]}: {unassignedCount}";

        // Update summary
        AssignedCountText.Text = totalAssigned.ToString();
        TotalCountText.Text = total.ToString();

        // Update cancelled workshops with chips using ViewModel-resolved names
        UpdateCancelledChips(ViewModel.CancelledWorkshopNames);

        // Update segment corner radius for first/last visible segments
        UpdateSegmentCorners();
    }

    /// <summary>
    /// Updates corner radius on the first and last visible segments of the stacked bar
    /// so the bar maintains its rounded pill shape. Only the leftmost visible segment
    /// gets left corners, only the rightmost visible segment gets right corners.
    /// </summary>
    private void UpdateSegmentCorners()
    {
        var segments = new[] { Segment1, Segment2, Segment3, Segment4, Segment5, Segment6, Segment7 };
        int firstVisible = -1;
        int lastVisible = -1;

        for (int i = 0; i < segments.Length; i++)
        {
            var width = StackedBarContainer.ColumnDefinitions[i].Width;
            bool isVisible = width.IsStar && width.Value > 0.02;
            if (isVisible)
            {
                if (firstVisible == -1) firstVisible = i;
                lastVisible = i;
            }
        }

        const double r = 20;
        for (int i = 0; i < segments.Length; i++)
        {
            bool isFirst = i == firstVisible;
            bool isLast = i == lastVisible;
            segments[i].CornerRadius = new CornerRadius(
                isFirst ? r : 0,  // top-left
                isLast ? r : 0,   // top-right
                isLast ? r : 0,   // bottom-right
                isFirst ? r : 0   // bottom-left
            );
        }
    }

    private void UpdateCancelledChips(List<string> cancelledNames)
    {
        CancelledHeader.Text = $"{ViewModel?["results.cancelledHeader"]} ({cancelledNames.Count})";

        // Clear existing chips (except placeholder)
        CancelledChipsContainer.Children.Clear();

        if (cancelledNames.Count == 0)
        {
            CancelledChipsContainer.Children.Add(NoCancelledPlaceholder);
            NoCancelledPlaceholder.IsVisible = true;
            return;
        }

        // Create a chip for each cancelled workshop
        foreach (var name in cancelledNames)
        {
            var chip = CreateCancelledChip(name);
            CancelledChipsContainer.Children.Add(chip);
        }
    }

    private Border CreateCancelledChip(string workshopName)
    {
        // Chip style: light background, subtle border, workshop name
        var chip = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#fef2f2")),  // Light red tint
            BorderBrush = new SolidColorBrush(Color.Parse("#fecaca")), // Red border
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 4),
            Margin = new Thickness(2)
        };

        var textBlock = new TextBlock
        {
            Text = workshopName,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#991b1b")), // Dark red text
            VerticalAlignment = VerticalAlignment.Center
        };

        chip.Child = textBlock;
        return chip;
    }
}
