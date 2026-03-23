using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WorkshopAssignment.Services;
using WorkshopAssignment.Views;

namespace WorkshopAssignment;

/// <summary>
/// Avalonia application class - handles app lifecycle and provides
/// global services access.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Gets the localization service singleton instance.
    /// </summary>
    public static ILocalizationService Localization { get; } = new LocalizationService();

    /// <summary>
    /// Initializes the application by loading XAML resources.
    /// Called by Avalonia framework during app startup.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Called when the Avalonia framework has finished initialization.
    /// This is where we set up the main window and any services.
    /// Replaces WinUI's OnLaunched method.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Create and show the main window
            desktop.MainWindow = new MainWindow()
            {
                Width = 1200,
                Height = 800
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
