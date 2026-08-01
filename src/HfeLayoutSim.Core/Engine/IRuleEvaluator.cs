using HfeLayoutSim.Core.Report;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Engine;

/// <summary>Draft of one finding produced by an evaluator (severity is finalized by the engine).</summary>
public sealed class FindingDraft
{
    public string Measured { get; init; } = "";
    public string Target { get; init; } = "";
    public IReadOnlyList<string> ElementIds { get; init; } = Array.Empty<string>();

    public static FindingDraft Of(string measured, string target, params string[] elementIds)
        => new() { Measured = measured, Target = target, ElementIds = elementIds };

    public static FindingDraft Of(string measured, string target, IEnumerable<string> elementIds)
        => new() { Measured = measured, Target = target, ElementIds = elementIds.ToList() };
}

public sealed class RuleEvaluation
{
    public RuleStatus Status { get; init; }
    public IReadOnlyList<FindingDraft> Findings { get; init; } = Array.Empty<FindingDraft>();

    public static RuleEvaluation Pass() => new() { Status = RuleStatus.Pass };

    public static RuleEvaluation NotApplicable() => new() { Status = RuleStatus.NotApplicable };

    public static RuleEvaluation Violation(params FindingDraft[] findings)
        => new() { Status = RuleStatus.Violation, Findings = findings };

    public static RuleEvaluation Violation(IEnumerable<FindingDraft> findings)
    {
        var list = findings.ToList();
        return list.Count == 0 ? Pass() : new RuleEvaluation { Status = RuleStatus.Violation, Findings = list };
    }

    /// <summary>Violation when any draft exists, otherwise Pass.</summary>
    public static RuleEvaluation FromDrafts(IEnumerable<FindingDraft> drafts) => Violation(drafts);
}

/// <summary>One rule-check implementation; registered by check name (the "check" field in rules JSON).</summary>
public interface IRuleEvaluator
{
    string CheckName { get; }
    RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx);
}
