using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using WorkshopAssignment.ViewModels;

namespace WorkshopAssignment.Views.Controls;

/// <summary>
/// RuntimeWidget with circular run button, progress ring, and preset pills.
/// State (RunState, SelectedRuntimeSeconds, CanRunButton) lives in MainViewModel.
/// This widget owns only visual/animation concerns: timers, progress arc, comet trail.
/// </summary>
public partial class RuntimeWidget : UserControl
{
    private MainViewModel? _viewModel;
    private DispatcherTimer? _elapsedTimer;
    private DateTime _runStartTime;
    private int _elapsedSeconds;

    // Preset buttons for easy iteration
    private List<Button>? _presetButtons;



    public MainViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }
            _viewModel = value;
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
            SyncFromViewModel();
        }
    }

    public RuntimeWidget()
    {
        InitializeComponent();
        InitializeElapsedTimer();
        InitializePresetButtons();
    }

    private void InitializeElapsedTimer()
    {
        _elapsedTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100) // Update 10x/sec for smooth progress
        };
        _elapsedTimer.Tick += ElapsedTimer_Tick;
    }

    private void InitializePresetButtons()
    {
        // Gather preset buttons after InitializeComponent
        _presetButtons = new List<Button>
        {
            Preset30s, Preset1m, Preset5m, Preset15m, Preset60m
        };
    }

    /// <summary>
    /// Syncs all visual state from the ViewModel. Called when ViewModel is assigned.
    /// </summary>
    private void SyncFromViewModel()
    {
        if (_viewModel == null) return;
        RunButton.IsEnabled = _viewModel.CanRunButton;
        UpdateVisualState();
    }


    private void ElapsedTimer_Tick(object? sender, EventArgs e)
    {
        if (_viewModel == null) return;

        _elapsedSeconds = (int)(DateTime.Now - _runStartTime).TotalSeconds;

        // Update elapsed time display
        int minutes = _elapsedSeconds / 60;
        int seconds = _elapsedSeconds % 60;
        ElapsedTimeText.Text = $"{minutes}:{seconds:D2}";

        // Check if time limit reached (visual only - solver handles its own timeout)
        if (_elapsedSeconds >= _viewModel.SelectedRuntimeSeconds)
        {
            // Time is up visually
        }
    }

    /// <summary>
    /// Updates visual elements based on ViewModel.RunState.
    /// </summary>
    private void UpdateVisualState()
    {
        if (_viewModel == null) return;

        // Clear all state classes first
        RunButton.Classes.Remove("running");
        RunButton.Classes.Remove("done");
        StatusText.Classes.Remove("running");
        StatusText.Classes.Remove("done");

        switch (_viewModel.RunState)
        {
            case RunWidgetState.Idle:
                // Play icon visible, breathing glow, comet trail hidden
                PlayIcon.IsVisible = true;
                ElapsedTimeText.IsVisible = false;
                CheckmarkIcon.IsVisible = false;
                CometTrailCanvas.IsVisible = false;
                StatusText.Text = _viewModel.RuntimeStatusText;
                SetPresetsEnabled(true);
                break;

            case RunWidgetState.Running:
                // Elapsed time visible, pulsing glow, comet trail visible
                PlayIcon.IsVisible = false;
                ElapsedTimeText.IsVisible = true;
                CheckmarkIcon.IsVisible = false;
                CometTrailCanvas.IsVisible = true;
                RunButton.Classes.Add("running");
                StatusText.Text = _viewModel.RuntimeStatusText;
                StatusText.Classes.Add("running");
                SetPresetsEnabled(false);
                break;

            case RunWidgetState.Done:
                // Checkmark visible, static green glow, comet trail hidden
                PlayIcon.IsVisible = false;
                ElapsedTimeText.IsVisible = false;
                CheckmarkIcon.IsVisible = true;
                CometTrailCanvas.IsVisible = false;
                RunButton.Classes.Add("done");
                StatusText.Text = _viewModel.RuntimeStatusText;
                StatusText.Classes.Add("done");
                SetPresetsEnabled(true);
                break;
        }
    }

    /// <summary>
    /// Enable/disable preset pill buttons.
    /// </summary>
    private void SetPresetsEnabled(bool enabled)
    {
        if (_presetButtons == null) return;
        foreach (var button in _presetButtons)
        {
            button.IsEnabled = enabled;
        }
    }


    /// <summary>
    /// Handles preset pill button clicks.
    /// </summary>
    private void Preset_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton) return;
        if (clickedButton.Tag is not string tagValue) return;
        if (!int.TryParse(tagValue, out int seconds)) return;
        if (_viewModel == null) return;

        // Update selection on ViewModel
        _viewModel.SelectedRuntimeSeconds = seconds;

        // Update visual selection (remove 'selected' from all, add to clicked)
        if (_presetButtons != null)
        {
            foreach (var button in _presetButtons)
            {
                button.Classes.Remove("selected");
            }
        }
        clickedButton.Classes.Add("selected");

        // If in done state, clicking a preset resets to idle
        if (_viewModel.RunState == RunWidgetState.Done)
        {
            _viewModel.RunState = RunWidgetState.Idle;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.RunState):
                UpdateVisualState();
                break;
            case nameof(MainViewModel.CanRunButton):
                RunButton.IsEnabled = _viewModel?.CanRunButton ?? false;
                break;
            case nameof(MainViewModel.RuntimeStatusText):
                if (_viewModel != null)
                    StatusText.Text = _viewModel.RuntimeStatusText;
                break;
        }
    }

    /// <summary>
    /// Thin click handler — delegates solver lifecycle to ViewModel's RunSolverCommand.
    /// Only manages the elapsed timer (start/stop), which is a legitimate view concern
    /// (DispatcherTimer tied to UI thread for smooth elapsed time display).
    /// </summary>
    private async void Run_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        // Done-state reset is handled by the command — no timer needed
        if (_viewModel.RunState == RunWidgetState.Done)
        {
            await _viewModel.RunSolverCommand.ExecuteAsync(null);
            return;
        }

        // Start elapsed timer (view concern — DispatcherTimer for smooth UI updates)
        _elapsedSeconds = 0;
        _runStartTime = DateTime.Now;
        ElapsedTimeText.Text = "0:00";
        _elapsedTimer?.Start();

        try
        {
            // Delegate full lifecycle (time conversion, state transitions, optimization) to ViewModel
            await _viewModel.RunSolverCommand.ExecuteAsync(null);
        }
        finally
        {
            // Stop elapsed timer — state transition handled by ViewModel
            _elapsedTimer?.Stop();
        }
    }
}
