using System.Text.Json;

namespace HfeLayoutSim.Core.Rules;

/// <summary>One palette token from the rules knowledge base.</summary>
public sealed class PaletteColor
{
    public string Hex { get; init; } = "";
    public double? CrOnWhite { get; init; }
    public string? Bg { get; init; }
}

/// <summary>The whole rules/hfe_rules.json knowledge base — single source of truth for scoring.</summary>
public sealed class RuleSet
{
    public string Version { get; init; } = "";

    /// <summary>Penalty points per severity (Minor/Major/Critical).</summary>
    public IReadOnlyDictionary<RuleSeverity, int> Penalties { get; init; } =
        new Dictionary<RuleSeverity, int>();

    /// <summary>Violations on isCritical elements escalate one severity level.</summary>
    public bool CriticalElementSeverityEscalation { get; init; }

    /// <summary>Grade thresholds, e.g. A→90. Anything below the lowest is F.</summary>
    public IReadOnlyDictionary<string, double> Grades { get; init; } = new Dictionary<string, double>();

    /// <summary>Grade cap when any Critical violation exists (e.g. "C").</summary>
    public string? CriticalViolationGradeCap { get; init; }

    /// <summary>Minimum grade that counts as an overall PASS (verifier-style verdict).</summary>
    public string PassGrade { get; init; } = "C";

    /// <summary>Category weights (must sum to ~1.0).</summary>
    public IReadOnlyDictionary<string, double> Weights { get; init; } = new Dictionary<string, double>();

    /// <summary>Verified color tokens.</summary>
    public IReadOnlyDictionary<string, PaletteColor> Palette { get; init; } =
        new Dictionary<string, PaletteColor>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<RuleDefinition> Rules { get; init; } = Array.Empty<RuleDefinition>();

    public int PenaltyOf(RuleSeverity severity)
        => Penalties.TryGetValue(severity, out var p) ? p : 0;

    /// <summary>Default body text color from the palette ("text" token), fallback near-black.</summary>
    public string DefaultTextColor
        => Palette.TryGetValue("text", out var c) && !string.IsNullOrEmpty(c.Hex) ? c.Hex : "#212529";

    public string GradeOf(double totalScore)
    {
        // highest threshold first
        foreach (var (grade, min) in Grades.OrderByDescending(kv => kv.Value))
            if (totalScore >= min)
                return grade;
        return "F";
    }

    /// <summary>Grade rank — 0 is the best grade; grades below every threshold rank last.</summary>
    public int RankOf(string grade)
    {
        var ordered = Grades.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
        var idx = ordered.FindIndex(g => g.Equals(grade, StringComparison.OrdinalIgnoreCase));
        return idx < 0 ? ordered.Count : idx;
    }

    /// <summary>Verifier-style verdict: PASS when the grade meets the configured pass grade.</summary>
    public bool IsPass(string grade) => RankOf(grade) <= RankOf(PassGrade);
}
