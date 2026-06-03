using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WorkshopAssignment.Converters;

/// <summary>
/// Two-way Avalonia value converter between a nullable int (int?) source and a
/// TextBox string target, for the hero workshop table's editable "Min" (MinCapacity)
/// and "Max" (Capacity) cells.
///
/// WHY THIS EXISTS (v0.3.0 InvalidCastException fix): Making HeroWorkshopDisplay.Capacity/
/// .MinCapacity int? and adding TargetNullValue='' on the bindings was NOT enough. Avalonia's
/// BUILT-IN string->value-type conversion still runs on the WRITE (source) path and throws
/// "Could not convert \"\" (System.String) to System.Nullable`1[System.Int32]" when the user
/// clears the cell. TargetNullValue only affects the source->target (display) direction, never
/// the user-typed-empty -> source write. That throw surfaced as a DataValidationException, which
/// expanded the validation-errors adorner and shifted the whole table.
///
/// THE FIX: when a Converter is specified on a binding, Avalonia uses IT instead of its built-in
/// type conversion. So ConvertBack("") -> null below never hits the throwing built-in path, no
/// DataValidationException is raised, and the table never shifts. ConvertBack NEVER throws and
/// always returns a valid int?; the cleared (null) value is resolved on commit by
/// MainViewModel.UpdateWorkshopField (Min null -> 0, Max null -> revert to previous stored capacity).
/// </summary>
public class NullableIntConverter : IValueConverter
{
    /// <summary>
    /// Source (int?) -> target (string). null renders as an empty cell; otherwise the number text.
    /// This makes TargetNullValue='' on the binding redundant.
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue.ToString(culture);
        }
        // null (cleared cell) or any non-int -> empty string.
        return string.Empty;
    }

    /// <summary>
    /// Target (string) -> source (int?). MUST never throw and MUST return a valid int? so that
    /// Avalonia never raises a DataValidationException (which would shift the table):
    ///   null / empty / whitespace -> null (a cleared cell; commit logic resolves it)
    ///   parseable integer         -> the parsed int
    ///   anything unparseable      -> null (treated like a cleared cell rather than an error)
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text && !string.IsNullOrWhiteSpace(text))
        {
            if (int.TryParse(text.Trim(), NumberStyles.Integer, culture, out var parsed))
            {
                return parsed;
            }
        }
        // Empty, whitespace, null, or unparseable -> null. Never throw, never surface a validation error.
        return null;
    }
}
