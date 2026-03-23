using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using WorkshopAssignment.Models;

namespace WorkshopAssignment.Converters;

/// <summary>
/// Converts a boolean value to IsVisible property (bool).
/// True = true (visible), False = false (collapsed).
/// Avalonia uses bool for visibility instead of Visibility enum.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool visibility)
        {
            return visibility;
        }
        return false;
    }
}

/// <summary>
/// Converts a boolean value to inverse IsVisible property (bool).
/// True = false (hidden), False = true (visible).
/// </summary>
public class BoolToInverseVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool visibility)
        {
            return !visibility;
        }
        return true;
    }
}

/// <summary>
/// Converts a boolean to a SolidColorBrush.
/// True = Active color (bright teal), False = Inactive color (dark/muted).
/// </summary>
public class BoolToProgressBarBrushConverter : IValueConverter
{
    // Active/loaded state - bright teal/accent color
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromArgb(255, 85, 172, 191)); // #55acbf

    // Inactive/not loaded state - dark muted color
    private static readonly SolidColorBrush InactiveBrush = new(Color.FromArgb(255, 80, 80, 80)); // #505050

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? ActiveBrush : InactiveBrush;
        }
        return InactiveBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a boolean to a foreground brush for capsule progress segments.
/// True = bright white (active/completed segment), False = muted dark tone (pending segment).
/// </summary>
public class BoolToActiveSegmentForegroundConverter : IValueConverter
{
    // Active segment: bright white text and icon
    private static readonly SolidColorBrush ActiveBrush = new(Color.FromArgb(255, 255, 255, 255)); // #FFFFFF

    // Pending segment: muted blue-gray text and icon
    private static readonly SolidColorBrush PendingBrush = new(Color.FromArgb(255, 136, 153, 170)); // #8899AA

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? ActiveBrush : PendingBrush;
        }
        return PendingBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a WorkshopType to a background brush for workshop chips.
/// Each type gets a distinct subtle background color for visual scanning.
/// </summary>
public class WorkshopTypeToChipBackgroundConverter : IValueConverter
{
    // Subtle pastel backgrounds for each workshop type
    private static readonly SolidColorBrush Type1Brush = new(Color.FromArgb(255, 232, 245, 233)); // Light green #E8F5E9
    private static readonly SolidColorBrush Type2Brush = new(Color.FromArgb(255, 227, 242, 253)); // Light blue #E3F2FD
    private static readonly SolidColorBrush Type3Brush = new(Color.FromArgb(255, 255, 243, 224)); // Light orange #FFF3E0
    private static readonly SolidColorBrush Type4Brush = new(Color.FromArgb(255, 243, 229, 245)); // Light purple #F3E5F5
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromArgb(255, 238, 238, 238)); // Light gray #EEEEEE

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is WorkshopType workshopType)
        {
            return workshopType switch
            {
                WorkshopType.Type1 => Type1Brush,
                WorkshopType.Type2 => Type2Brush,
                WorkshopType.Type3 => Type3Brush,
                WorkshopType.Type4 => Type4Brush,
                _ => DefaultBrush
            };
        }
        return DefaultBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a WorkshopType to a border brush for workshop chips.
/// Each type gets a distinct border color that's slightly darker than the background.
/// </summary>
public class WorkshopTypeToChipBorderConverter : IValueConverter
{
    // Subtle border colors for each workshop type (slightly darker than background)
    private static readonly SolidColorBrush Type1Brush = new(Color.FromArgb(255, 165, 214, 167)); // Green #A5D6A7
    private static readonly SolidColorBrush Type2Brush = new(Color.FromArgb(255, 144, 202, 249)); // Blue #90CAF9
    private static readonly SolidColorBrush Type3Brush = new(Color.FromArgb(255, 255, 204, 128)); // Orange #FFCC80
    private static readonly SolidColorBrush Type4Brush = new(Color.FromArgb(255, 206, 147, 216)); // Purple #CE93D8
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromArgb(255, 189, 189, 189)); // Gray #BDBDBD

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is WorkshopType workshopType)
        {
            return workshopType switch
            {
                WorkshopType.Type1 => Type1Brush,
                WorkshopType.Type2 => Type2Brush,
                WorkshopType.Type3 => Type3Brush,
                WorkshopType.Type4 => Type4Brush,
                _ => DefaultBrush
            };
        }
        return DefaultBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}



/// <summary>
/// Converts WorkshopType to a filled badge background brush for hero table.
/// Design spec: Slot1=teal(15%), Slot2=green(12%), Slot3=purple(12%), FullDay=navy(10%).
/// Uses subtle transparent backgrounds for visual distinction.
/// </summary>
public class WorkshopTypeToBadgeBackgroundConverter : IValueConverter
{
    // Filled badge backgrounds with transparency per C1 design spec
    private static readonly SolidColorBrush Slot1Brush = new(Color.FromArgb(38, 85, 172, 191));   // rgba(85,172,191,0.15) teal
    private static readonly SolidColorBrush Slot2Brush = new(Color.FromArgb(31, 34, 197, 94));   // rgba(34,197,94,0.12) green
    private static readonly SolidColorBrush Slot3Brush = new(Color.FromArgb(31, 139, 92, 246));  // rgba(139,92,246,0.12) purple
    private static readonly SolidColorBrush FullDayBrush = new(Color.FromArgb(26, 2, 56, 90));   // rgba(2,56,90,0.1) navy
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromArgb(26, 128, 128, 128)); // neutral gray

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is WorkshopType workshopType)
        {
            return workshopType switch
            {
                WorkshopType.Type1 => Slot1Brush,
                WorkshopType.Type2 => Slot2Brush,
                WorkshopType.Type3 => Slot3Brush,
                WorkshopType.Type4 => FullDayBrush,
                _ => DefaultBrush
            };
        }
        return DefaultBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts WorkshopType to a text foreground brush for badge text.
/// Design spec: Slot1=#3D8A9A, Slot2=#16a34a, Slot3=#7c3aed, FullDay=#02385a.
/// </summary>
public class WorkshopTypeToBadgeForegroundConverter : IValueConverter
{
    // Text colors per C1 design spec
    private static readonly SolidColorBrush Slot1Brush = new(Color.FromArgb(255, 61, 138, 154));  // #3D8A9A teal-600
    private static readonly SolidColorBrush Slot2Brush = new(Color.FromArgb(255, 22, 163, 74));   // #16a34a green
    private static readonly SolidColorBrush Slot3Brush = new(Color.FromArgb(255, 124, 58, 237));  // #7c3aed purple
    private static readonly SolidColorBrush FullDayBrush = new(Color.FromArgb(255, 2, 56, 90));   // #02385a navy
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromArgb(255, 85, 85, 85)); // neutral gray

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is WorkshopType workshopType)
        {
            return workshopType switch
            {
                WorkshopType.Type1 => Slot1Brush,
                WorkshopType.Type2 => Slot2Brush,
                WorkshopType.Type3 => Slot3Brush,
                WorkshopType.Type4 => FullDayBrush,
                _ => DefaultBrush
            };
        }
        return DefaultBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
