using System;
using System.Threading.Tasks;
using Avalonia;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using WorkshopAssignment.ViewModels;

namespace WorkshopAssignment.Views.Controls;

public partial class ExportWidget : UserControl
{
    public MainViewModel? ViewModel { get; set; }

    // Brushes for state management
    private static readonly IBrush TealBrush = new SolidColorBrush(Color.Parse("#55acbf"));
    private static readonly IBrush GreyBrush = new SolidColorBrush(Color.Parse("#888888"));
    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.Parse("#22c55e"));
    private static readonly IBrush WhiteBrush = new SolidColorBrush(Color.Parse("#ffffff"));
    private static readonly IBrush TextGreyBrush = new SolidColorBrush(Color.Parse("#555555"));

    public ExportWidget()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Sync pill position to persisted ViewModel state instantly (no animation on load).
        // Without this, the pill stays at X=0 (PDF) even when ViewModel.IsPdfSelected is false (Excel),
        // causing a visual mismatch where text colors show Excel active but the pill sits on PDF.
        if (ActivePill?.RenderTransform is TranslateTransform pillTransform)
        {
            bool isPdf = ViewModel?.IsPdfSelected ?? true;
            pillTransform.X = isPdf ? 0 : 72;  // 0 = PDF (left), 72 = Excel (right)
        }

        UpdateToggleVisuals();
        UpdateButtonStates();
    }

    #region Toggle Logic

    private void PdfOption_Click(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel != null && !ViewModel.IsPdfSelected)
        {
            ViewModel.IsPdfSelected = true;
            AnimateToggle();
            UpdateToggleVisuals();
            UpdateFormatBadges();
        }
    }

    private void ExcelOption_Click(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel != null && ViewModel.IsPdfSelected)
        {
            ViewModel.IsPdfSelected = false;
            AnimateToggle();
            UpdateToggleVisuals();
            UpdateFormatBadges();
        }
    }

    private void AnimateToggle()
    {
        bool isPdf = ViewModel?.IsPdfSelected ?? true;
        double targetX = isPdf ? 0 : 72;
        
        if (ActivePill.RenderTransform is TranslateTransform pillTransform)
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
            
            timer.Tick += (s, e) =>
            {
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                var progress = Math.Min(elapsed / duration, 1.0);
                
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

    private void UpdateToggleVisuals()
    {
        bool isPdf = ViewModel?.IsPdfSelected ?? true;
        if (isPdf)
        {
            PdfIcon.Foreground = WhiteBrush;
            PdfText.Foreground = WhiteBrush;
            ExcelIcon.Foreground = TextGreyBrush;
            ExcelText.Foreground = TextGreyBrush;
        }
        else
        {
            PdfIcon.Foreground = TextGreyBrush;
            PdfText.Foreground = TextGreyBrush;
            ExcelIcon.Foreground = WhiteBrush;
            ExcelText.Foreground = WhiteBrush;
        }
    }

    private void UpdateFormatBadges()
    {
        bool isPdf = ViewModel?.IsPdfSelected ?? true;
        string format = isPdf ? (ViewModel?["export.togglePdf"] ?? "PDF") : (ViewModel?["export.toggleExcel"] ?? "Excel");
        WorkshopsFormatText.Text = format;
        PeopleFormatText.Text = format;
    }

    #endregion

    #region Export Buttons

    private async void WorkshopsBtn_Click(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel?.Result == null) return;

        var file = await PickSaveFileAsync("Workshops");
        if (file != null)
        {
            await ViewModel.ExportWorkshopsCommand.ExecuteAsync(file.Path.LocalPath);
            UpdateButtonStates();
            ShowExportStatus(ViewModel?["export.successWorkshops"] ?? "Workshops exported successfully!");
        }
    }

    private async void PeopleBtn_Click(object? sender, PointerPressedEventArgs e)
    {
        if (ViewModel?.Result == null) return;

        var file = await PickSaveFileAsync("People");
        if (file != null)
        {
            await ViewModel.ExportPersonsCommand.ExecuteAsync(file.Path.LocalPath);
            UpdateButtonStates();
            ShowExportStatus(ViewModel?["export.successPeople"] ?? "People exported successfully!");
        }
    }

    private async Task<IStorageFile?> PickSaveFileAsync(string suggestedName)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return null;

        bool isPdf = ViewModel?.IsPdfSelected ?? true;
        var extension = isPdf ? "pdf" : "xlsx";
        var fileType = isPdf 
            ? new FilePickerFileType("PDF Files") { Patterns = new[] { "*.pdf" } }
            : new FilePickerFileType("Excel Files") { Patterns = new[] { "*.xlsx" } };

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Export File",
            SuggestedFileName = $"{suggestedName}.{extension}",
            FileTypeChoices = new[] { fileType },
            DefaultExtension = extension
        });

        return file;
    }

    #endregion


    #region State Management

    private void UpdateButtonStates()
    {
        var state = ViewModel?.CurrentExportState ?? ExportState.Disabled;
        
        switch (state)
        {
            case ExportState.Disabled:
                ApplyDisabledState();
                break;
            case ExportState.Attention:
                ApplyAttentionState();
                break;
            case ExportState.Partial:
                ApplyPartialState();
                break;
            case ExportState.Complete:
                ApplyCompleteState();
                break;
        }
    }

    private void ApplyDisabledState()
    {
        WorkshopsBtn.Opacity = 0.5;
        WorkshopsBtn.BorderBrush = GreyBrush;
        WorkshopsBtn.Classes.Remove("pulse-glow");
        WorkshopsCheckBadge.IsVisible = false;
        
        PeopleBtn.Opacity = 0.5;
        PeopleBtn.BorderBrush = GreyBrush;
        PeopleBtn.Classes.Remove("pulse-glow");
        PeopleCheckBadge.IsVisible = false;
    }

    private void ApplyAttentionState()
    {
        WorkshopsBtn.Opacity = 1;
        WorkshopsBtn.BorderBrush = TealBrush;
        WorkshopsBtn.Classes.Add("pulse-glow");
        WorkshopsCheckBadge.IsVisible = false;
        
        PeopleBtn.Opacity = 1;
        PeopleBtn.BorderBrush = TealBrush;
        PeopleBtn.Classes.Add("pulse-glow");
        PeopleCheckBadge.IsVisible = false;
    }

    private void ApplyPartialState()
    {
        bool workshopsExported = ViewModel?.WorkshopsExported ?? false;
        if (workshopsExported)
        {
            WorkshopsBtn.Opacity = 1;
            WorkshopsBtn.BorderBrush = GreenBrush;
            WorkshopsBtn.Classes.Remove("pulse-glow");
            WorkshopsCheckBadge.IsVisible = true;
            
            PeopleBtn.Opacity = 1;
            PeopleBtn.BorderBrush = TealBrush;
            PeopleBtn.Classes.Add("pulse-glow");
            PeopleCheckBadge.IsVisible = false;
        }
        else
        {
            PeopleBtn.Opacity = 1;
            PeopleBtn.BorderBrush = GreenBrush;
            PeopleBtn.Classes.Remove("pulse-glow");
            PeopleCheckBadge.IsVisible = true;
            
            WorkshopsBtn.Opacity = 1;
            WorkshopsBtn.BorderBrush = TealBrush;
            WorkshopsBtn.Classes.Add("pulse-glow");
            WorkshopsCheckBadge.IsVisible = false;
        }
    }

    private void ApplyCompleteState()
    {
        WorkshopsBtn.Opacity = 1;
        WorkshopsBtn.BorderBrush = GreenBrush;
        WorkshopsBtn.Classes.Remove("pulse-glow");
        WorkshopsCheckBadge.IsVisible = true;
        
        PeopleBtn.Opacity = 1;
        PeopleBtn.BorderBrush = GreenBrush;
        PeopleBtn.Classes.Remove("pulse-glow");
        PeopleCheckBadge.IsVisible = true;
    }

    #endregion

    #region Status Display

    private void ShowExportStatus(string message)
    {
        ExportStatusText.Text = message;
        ExportStatusText.IsVisible = true;

        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        timer.Tick += (s, e) =>
        {
            ExportStatusText.IsVisible = false;
            timer.Stop();
        };
        timer.Start();
    }

    #endregion

    #region Public API

    public void OnAlgorithmComplete()
    {
        if (ViewModel != null)
        {
            ViewModel.WorkshopsExported = false;
            ViewModel.PersonsExported = false;
        }
        UpdateButtonStates();
    }

    #endregion
}
