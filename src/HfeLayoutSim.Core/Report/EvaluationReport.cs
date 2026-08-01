using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Report;

public enum RuleStatus
{
    Pass,
    Violation,
    NotApplicable
}

/// <summary>
/// One concrete deficiency. Always carries the 5 mandatory parts:
/// rule id, measured vs target, element ids, recommendation, standard reference (CLAUDE.md #6).
/// </summary>
public sealed class Finding
{
    public string RuleId { get; init; } = "";
    public string Category { get; init; } = "";
    public RuleSeverity Severity { get; init; }

    /// <summary>True when severity was escalated because an isCritical element is involved.</summary>
    public bool Escalated { get; init; }

    public string Title { get; init; } = "";

    /// <summary>Message template with {measured}/{target} substituted.</summary>
    public string Message { get; init; } = "";

    public string Measured { get; init; } = "";
    public string Target { get; init; } = "";
    public IReadOnlyList<string> ElementIds { get; init; } = Array.Empty<string>();
    public string Recommendation { get; init; } = "";
    public string StandardRef { get; init; } = "";
    public int Penalty { get; init; }
}

/// <summary>A noteworthy passed rule (highlightOnPass=true in the knowledge base).</summary>
public sealed class Strength
{
    public string RuleId { get; init; } = "";
    public string Category { get; init; } = "";
    public string Title { get; init; } = "";
    public string StandardRef { get; init; } = "";
}

public sealed class CategoryScore
{
    public string Category { get; init; } = "";
    public double Weight { get; init; }
    public double Score { get; init; }
    public int PassCount { get; init; }
    public int ViolationCount { get; init; }
    public int NotApplicableCount { get; init; }
}

public sealed class ScoreCard
{
    public double Total { get; init; }
    public string Grade { get; init; } = "";

    /// <summary>True when the grade was capped due to a Critical violation (배포 부적합).</summary>
    public bool GradeCapped { get; init; }

    public IReadOnlyList<CategoryScore> Categories { get; init; } = Array.Empty<CategoryScore>();
}

/// <summary>Per-rule outcome trace (useful for compare view diffs and regression tests).</summary>
public sealed class RuleOutcome
{
    public string RuleId { get; init; } = "";
    public string Category { get; init; } = "";
    public RuleStatus Status { get; init; }
    public int FindingCount { get; init; }
}

public sealed class EvaluationReport
{
    public string LayoutName { get; init; } = "";
    public Medium Medium { get; init; }
    public string RulesVersion { get; init; } = "";
    public DateTimeOffset EvaluatedAt { get; init; }

    public ScoreCard ScoreCard { get; init; } = new();
    public IReadOnlyList<Finding> Findings { get; init; } = Array.Empty<Finding>();
    public IReadOnlyList<Strength> Strengths { get; init; } = Array.Empty<Strength>();
    public IReadOnlyList<RuleOutcome> RuleOutcomes { get; init; } = Array.Empty<RuleOutcome>();
}
