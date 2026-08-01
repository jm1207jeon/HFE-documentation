using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using HfeLayoutSim.Core.Advisor;

namespace HfeLayoutSim.App;

/// <summary>"#RRGGBB"/"#AARRGGBB" → SolidColorBrush (fallback: transparent or the converter parameter).</summary>
public sealed class HexBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = value as string;
        if (string.IsNullOrWhiteSpace(hex))
            hex = parameter as string;
        try
        {
            if (!string.IsNullOrWhiteSpace(hex))
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch (FormatException)
        {
            // fall through to transparent
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>true → Bold.</summary>
public sealed class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? FontWeights.Bold : FontWeights.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>AdviceLevel → marker glyph.</summary>
public sealed class AdviceLevelToGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            AdviceLevel.Ok => "✓",
            AdviceLevel.Info => "ⓘ",
            AdviceLevel.Warning => "!",
            AdviceLevel.Critical => "✕",
            _ => "·"
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>AdviceLevel → verified token color.</summary>
public sealed class AdviceLevelToBrushConverter : IValueConverter
{
    private static SolidColorBrush Brush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            AdviceLevel.Ok => Brush("#2E7D32"),
            AdviceLevel.Info => Brush("#1565C0"),
            AdviceLevel.Warning => Brush("#B45309"),
            AdviceLevel.Critical => Brush("#C62828"),
            _ => Brush("#212529")
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>pass(true) → green banner, fail(false) → red banner.</summary>
public sealed class PassToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EAF4EB"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDECEA"));

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>null → false (for IsEnabled bindings).</summary>
public sealed class NotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
