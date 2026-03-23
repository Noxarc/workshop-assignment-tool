using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WorkshopAssignment.Views.Controls;

/// <summary>
/// Progress bar widget showing 4-step workflow: Import → Settings → Run → Export.
/// Each step can be pending, active, or done. Progress fill animates based on completion.
/// </summary>
public partial class ProgressBarWidget : UserControl
{
    // Color constants matching design spec
    private static readonly Color TealActive = Color.Parse("#55acbf");
    private static readonly Color SuccessGreen = Color.Parse("#22c55e");
    private static readonly Color PendingGray = Color.Parse("#CCCCCC");
    private static readonly Color TextMuted = Color.Parse("#6b7280");

    #region Styled Properties

    public static readonly StyledProperty<bool> IsImportDoneProperty =
        AvaloniaProperty.Register<ProgressBarWidget, bool>(nameof(IsImportDone), defaultValue: false);

    public static readonly StyledProperty<bool> IsSettingsDoneProperty =
        AvaloniaProperty.Register<ProgressBarWidget, bool>(nameof(IsSettingsDone), defaultValue: false);

    public static readonly StyledProperty<bool> IsRunDoneProperty =
        AvaloniaProperty.Register<ProgressBarWidget, bool>(nameof(IsRunDone), defaultValue: false);

    public static readonly StyledProperty<bool> IsExportDoneProperty =
        AvaloniaProperty.Register<ProgressBarWidget, bool>(nameof(IsExportDone), defaultValue: false);

    public bool IsImportDone
    {
        get => GetValue(IsImportDoneProperty);
        set => SetValue(IsImportDoneProperty, value);
    }

    public bool IsSettingsDone
    {
        get => GetValue(IsSettingsDoneProperty);
        set => SetValue(IsSettingsDoneProperty, value);
    }

    public bool IsRunDone
    {
        get => GetValue(IsRunDoneProperty);
        set => SetValue(IsRunDoneProperty, value);
    }

    public bool IsExportDone
    {
        get => GetValue(IsExportDoneProperty);
        set => SetValue(IsExportDoneProperty, value);
    }

    #endregion

    #region Computed Properties

    /// <summary>
    /// Current step index (0-3), or 4 if all done.
    /// Step 0 = Import active, Step 1 = Settings active, etc.
    /// </summary>
    public int CurrentStep
    {
        get
        {
            if (!IsImportDone) return 0;
            if (!IsSettingsDone) return 1;
            if (!IsRunDone) return 2;
            if (!IsExportDone) return 3;
            return 4; // All done
        }
    }

    /// <summary>
    /// Progress percentage (0, 25, 50, 75, 100) based on completed steps.
    /// </summary>
    public int ProgressPercentage
    {
        get
        {
            int completedSteps = 0;
            if (IsImportDone) completedSteps++;
            if (IsSettingsDone) completedSteps++;
            if (IsRunDone) completedSteps++;
            if (IsExportDone) completedSteps++;
            return completedSteps * 25;
        }
    }

    #endregion

    public ProgressBarWidget()
    {
        InitializeComponent();

        // Subscribe to property changes
        IsImportDoneProperty.Changed.AddClassHandler<ProgressBarWidget>((s, e) => s.UpdateVisualStates());
        IsSettingsDoneProperty.Changed.AddClassHandler<ProgressBarWidget>((s, e) => s.UpdateVisualStates());
        IsRunDoneProperty.Changed.AddClassHandler<ProgressBarWidget>((s, e) => s.UpdateVisualStates());
        IsExportDoneProperty.Changed.AddClassHandler<ProgressBarWidget>((s, e) => s.UpdateVisualStates());

        // Update on size change to recalculate fill width
        this.PropertyChanged += (s, e) => 
        {
            if (e.Property == BoundsProperty)
                UpdateProgressFillWidth();
        };
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        UpdateVisualStates();
    }


    /// <summary>
    /// Updates all visual states based on current step completion.
    /// </summary>
    private void UpdateVisualStates()
    {
        UpdateStepState(1, IsImportDone, CurrentStep == 0);
        UpdateStepState(2, IsSettingsDone, CurrentStep == 1);
        UpdateStepState(3, IsRunDone, CurrentStep == 2);
        UpdateStepState(4, IsExportDone, CurrentStep == 3);
        UpdateProgressFillWidth();
    }

    /// <summary>
    /// Updates visual state for a single step.
    /// </summary>
    /// <param name="stepNum">Step number (1-4)</param>
    /// <param name="isDone">Whether step is complete</param>
    /// <param name="isActive">Whether step is currently active</param>
    private void UpdateStepState(int stepNum, bool isDone, bool isActive)
    {
        var container = this.FindControl<Border>($"Step{stepNum}Container");
        var badge = this.FindControl<Border>($"Step{stepNum}Badge");
        var number = this.FindControl<TextBlock>($"Step{stepNum}Number");
        var check = this.FindControl<TextBlock>($"Step{stepNum}Check");
        var label = this.FindControl<TextBlock>($"Step{stepNum}Label");

        if (container == null || badge == null || number == null || check == null || label == null)
            return;

        if (isDone)
        {
            // Done state: green badge with checkmark, green text
            container.Background = Brushes.Transparent;
            container.BoxShadow = default;
            badge.Background = new SolidColorBrush(SuccessGreen);
            number.IsVisible = false;
            check.IsVisible = true;
            check.Foreground = Brushes.White;
            label.Foreground = new SolidColorBrush(SuccessGreen);
        }
        else if (isActive)
        {
            // Active state: teal background with glow, white badge with teal number
            container.Background = new SolidColorBrush(TealActive);
            container.BoxShadow = BoxShadows.Parse("0 0 12 4 #4055acbf");
            badge.Background = Brushes.White;
            number.IsVisible = true;
            number.Foreground = new SolidColorBrush(TealActive);
            check.IsVisible = false;
            label.Foreground = Brushes.White;
        }
        else
        {
            // Pending state: gray badge with white number, muted text
            container.Background = Brushes.Transparent;
            container.BoxShadow = default;
            badge.Background = new SolidColorBrush(PendingGray);
            number.IsVisible = true;
            number.Foreground = Brushes.White;
            check.IsVisible = false;
            label.Foreground = new SolidColorBrush(TextMuted);
        }
    }

    /// <summary>
    /// Updates the progress fill width based on container width and progress percentage.
    /// </summary>
    private void UpdateProgressFillWidth()
    {
        if (ProgressFill == null || Bounds.Width <= 0)
            return;

        // Calculate fill width as percentage of container
        double fillWidth = (Bounds.Width - 8) * (ProgressPercentage / 100.0); // -8 for padding
        ProgressFill.Width = fillWidth;
        ProgressFill.Height = Bounds.Height > 8 ? Bounds.Height - 8 : Bounds.Height; // -8 for padding
    }

    /// <summary>
    /// Resets all steps to pending state.
    /// </summary>
    public void Reset()
    {
        IsImportDone = false;
        IsSettingsDone = false;
        IsRunDone = false;
        IsExportDone = false;
    }

    /// <summary>
    /// Marks all steps as complete.
    /// </summary>
    public void CompleteAll()
    {
        IsImportDone = true;
        IsSettingsDone = true;
        IsRunDone = true;
        IsExportDone = true;
    }
}
