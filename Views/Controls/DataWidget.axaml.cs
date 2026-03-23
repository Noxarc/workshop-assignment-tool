using System;
using Avalonia.Controls;
using Avalonia.Input;
using WorkshopAssignment.ViewModels;

namespace WorkshopAssignment.Views.Controls;

/// <summary>
/// Data widget showing summary cards for workshops and people.
/// Rich Summary Cards design with clickable cards that expand to hero overlay.
/// </summary>
public partial class DataWidget : UserControl
{
    private MainViewModel? _viewModel;

    /// <summary>
    /// Event raised when user clicks a card to expand (Workshops or Persons).
    /// </summary>
    public event EventHandler<string>? ExpandRequested;
    
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
                RefreshDisplay();
            }
        }
    }

    public DataWidget()
    {
        InitializeComponent();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.TotalWorkshops) ||
            e.PropertyName == nameof(MainViewModel.TotalPersons) ||
            e.PropertyName == nameof(MainViewModel.HasData) ||
            e.PropertyName == nameof(MainViewModel.WorkshopsBySlot) ||
            e.PropertyName == nameof(MainViewModel.InfoGroupCount) ||
            e.PropertyName == nameof(MainViewModel.FriendGroupCount))
        {
            RefreshDisplay();
        }

        // Update capacity labels when slot display names change
        if (e.PropertyName == nameof(MainViewModel.EarlySlotName) ||
            e.PropertyName == nameof(MainViewModel.LateSlotName))
        {
            UpdateSlotLabels();
        }
    }

    private void RefreshDisplay()
    {
        if (ViewModel == null) return;

        // Workshop count
        WorkshopCountText.Text = ViewModel.TotalWorkshops.ToString();

        // Slot capacities from WorkshopsBySlot (index 0 = Slot 2, index 1 = Slot 3)
        if (ViewModel.WorkshopsBySlot.Count >= 2)
        {
            Slot2CapacityText.Text = ViewModel.WorkshopsBySlot[0].Capacity.ToString();
            Slot3CapacityText.Text = ViewModel.WorkshopsBySlot[1].Capacity.ToString();
        }
        else
        {
            Slot2CapacityText.Text = "0";
            Slot3CapacityText.Text = "0";
        }

        // Person count and derived stats — all from ViewModel properties
        PersonCountText.Text = ViewModel.TotalPersons.ToString();
        InfoGroupCountText.Text = ViewModel.InfoGroupCount.ToString();
        FriendGroupCountText.Text = ViewModel.FriendGroupCount.ToString();

        // Keep slot labels in sync with display names
        UpdateSlotLabels();
    }

    /// <summary>
    /// Updates capacity label text to reflect current slot display names from ViewModel.
    /// </summary>
    private void UpdateSlotLabels()
    {
        if (ViewModel == null) return;
        Slot2CapacityLabel.Text = $"{ViewModel.EarlySlotName} {ViewModel["data.slotCapacityPrefix"]}";
        Slot3CapacityLabel.Text = $"{ViewModel.LateSlotName} {ViewModel["data.slotCapacityPrefix"]}";
    }

    /// <summary>
    /// Handles click on the Workshops card to show full workshop list in hero overlay.
    /// </summary>
    private void WorkshopsCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ExpandRequested?.Invoke(this, "Workshops");
    }

    /// <summary>
    /// Handles click on the People card to show full persons list in hero overlay.
    /// </summary>
    private void PeopleCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        ExpandRequested?.Invoke(this, "Persons");
    }
}
