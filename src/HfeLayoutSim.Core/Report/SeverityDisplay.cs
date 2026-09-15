using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Report;

/// <summary>How a severity is shown: glyph + Korean label + verified colour.</summary>
public readonly record struct SeverityStyle(string Glyph, string Label, string Hex, string BackgroundHex);

/// <summary>
/// Single source for severity presentation, shared by the app UI and the exported HTML.
/// Severity is triple-coded (glyph + word + colour) exactly as C1-04 demands of the layouts this
/// tool judges — a report that coded severity by colour alone would fail its own rule, and would
/// be unreadable in grayscale print or to a colour-blind reviewer.
/// </summary>
public static class SeverityDisplay
{
    public static SeverityStyle Of(RuleSeverity severity) => severity switch
    {
        // colours: DESIGN_TOKENS verified AA-on-white (fail 5.62:1, warn 5.02:1, primary 5.75:1)
        RuleSeverity.Critical => new SeverityStyle("✕", "치명", "#C62828", "#FDECEA"),
        RuleSeverity.Major => new SeverityStyle("!", "중대", "#B45309", "#FFF8E1"),
        _ => new SeverityStyle("ⓘ", "경미", "#1565C0", "#E8F1FB"),
    };

    /// <summary>"✕ 치명" — glyph and word together so neither can be dropped independently.</summary>
    public static string Describe(RuleSeverity severity)
    {
        var s = Of(severity);
        return $"{s.Glyph} {s.Label}";
    }
}
