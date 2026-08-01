namespace HfeLayoutSim.Core.Engine;

/// <summary>
/// WCAG 2.x relative-luminance contrast ratio (SPEC §6.1).
/// L = 0.2126 R' + 0.7152 G' + 0.0722 B'; c' = c/12.92 if c ≤ 0.03928 else ((c+0.055)/1.055)^2.4.
/// CR = (L1 + 0.05) / (L2 + 0.05).
/// </summary>
public static class ContrastCalculator
{
    public static double RelativeLuminance(Rgb color)
    {
        double Lin(byte channel)
        {
            var c = channel / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Lin(color.R) + 0.7152 * Lin(color.G) + 0.0722 * Lin(color.B);
    }

    public static double Ratio(Rgb a, Rgb b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var (l1, l2) = la >= lb ? (la, lb) : (lb, la);
        return (l1 + 0.05) / (l2 + 0.05);
    }

    /// <summary>Ratio from hex strings; returns null when either color cannot be parsed.</summary>
    public static double? Ratio(string? fgHex, string? bgHex)
    {
        if (!ColorUtil.TryParse(fgHex, out var fg) || !ColorUtil.TryParse(bgHex, out var bg))
            return null;
        return Ratio(fg, bg);
    }
}
