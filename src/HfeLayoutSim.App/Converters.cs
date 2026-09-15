using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using HfeLayoutSim.App.ViewModels;
using HfeLayoutSim.Core.Advisor;

namespace HfeLayoutSim.App;

// Every converter declares what it emits with [ValueConversion]. tools/XamlLint reads those
// attributes and fails the build when a converter's output cannot be assigned to the bound
// property — the defect that left the PASS/FAIL banner permanently collapsed (a bool converter
// on a Visibility property silently falls back).

/// <summary>"#RRGGBB"/"#AARRGGBB" → brush. Unparsable or empty falls back to the parameter, then transparent.</summary>
[ValueConversion(typeof(string), typeof(Brush))]
public sealed class HexBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var brush = Parse(value as string) ?? Parse(parameter as string);
        return brush ?? Brushes.Transparent;
    }

    internal static Brush? Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            var color = ColorConverter.ConvertFromString(hex);
            if (color is Color c)
            {
                var brush = new SolidColorBrush(c);
                brush.Freeze();
                return brush;
            }
        }
        catch (FormatException) { /* fall through */ }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>true → Bold.</summary>
[ValueConversion(typeof(bool), typeof(FontWeight))]
public sealed class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FontWeights.Bold : FontWeights.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Non-null → true (for IsEnabled).</summary>
[ValueConversion(typeof(object), typeof(bool))]
public sealed class NotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Non-null → Visible, null → Collapsed.</summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Non-empty string → Visible (used for the stale-report notice and escalation note).</summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class NonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>true → Collapsed (the inverse of BooleanToVisibilityConverter).</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Advice level → its marker glyph (paired with the topic text, never colour alone).</summary>
[ValueConversion(typeof(AdviceLevel), typeof(string))]
public sealed class AdviceLevelToGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            AdviceLevel.Ok => "✓",
            AdviceLevel.Info => "ⓘ",
            AdviceLevel.Warning => "!",
            AdviceLevel.Critical => "✕",
            _ => "·",
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Advice level → verified token colour.</summary>
[ValueConversion(typeof(AdviceLevel), typeof(Brush))]
public sealed class AdviceLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            AdviceLevel.Ok => Brush("#2E7D32"),
            AdviceLevel.Info => Brush("#1565C0"),
            AdviceLevel.Warning => Brush("#B45309"),
            AdviceLevel.Critical => Brush("#C62828"),
            _ => Brush("#212529"),
        };

    private static Brush Brush(string hex) => HexBrushConverter.Parse(hex) ?? Brushes.Black;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Status level → foreground brush (bold weight is handled separately).</summary>
[ValueConversion(typeof(StatusLevel), typeof(Brush))]
public sealed class StatusLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            StatusLevel.Error => HexBrushConverter.Parse("#C62828")!,
            StatusLevel.Warning => HexBrushConverter.Parse("#B45309")!,
            _ => HexBrushConverter.Parse("#212529")!,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Anything but Info → Bold, so a problem is coded by weight as well as colour.</summary>
[ValueConversion(typeof(StatusLevel), typeof(FontWeight))]
public sealed class StatusLevelToWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is StatusLevel.Info or null ? FontWeights.Normal : FontWeights.Bold;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Pass → green tint, fail → red tint (the banner also carries the PASS/FAIL word and glyph).</summary>
[ValueConversion(typeof(bool), typeof(Brush))]
public sealed class PassToBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => HexBrushConverter.Parse(value is true ? "#EAF4EB" : "#FDECEA")!;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Pass → green, fail → red, for the verdict word itself.</summary>
[ValueConversion(typeof(bool), typeof(Brush))]
public sealed class PassToForegroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => HexBrushConverter.Parse(value is true ? "#2E7D32" : "#C62828")!;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
