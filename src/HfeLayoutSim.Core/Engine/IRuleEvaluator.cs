using HfeLayoutSim.Core.Report;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Engine;

/// <summary>Draft of one finding produced by an evaluator (severity is finalized by the engine).</summary>
public sealed class FindingDraft
{
    public string Measured { get; init; } = "";
    public string Target { get; init; } = "";

    /// <summary>Elements the report highlights on the canvas — offenders AND their context.</summary>
    public IReadOnlyList<string> ElementIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Elements this finding is actually ABOUT, when that is narrower than <see cref="ElementIds"/>.
    /// Whole-layout measurements (field count, block count, font-family count) list every element
    /// they counted so the canvas can show them, but those elements are context, not offenders —
    /// escalating such a finding because one counted element happens to be safety-critical would
    /// let an unrelated LOT field flip the whole verdict to FAIL. Set it to an empty list to say
    /// "this finding is about the layout, not about any element".
    /// </summary>
    public IReadOnlyList<string>? SubjectIds { get; init; }

    /// <summary>The set severity escalation considers (subjects when declared, else the highlighted ids).</summary>
    public IReadOnlyList<string> EscalationIds => SubjectIds ?? ElementIds;

    public static FindingDraft Of(string measured, string target, params string[] elementIds)
        => new() { Measured = measured, Target = target, ElementIds = elementIds };

    public static FindingDraft Of(string measured, string target, IEnumerable<string> elementIds)
        => new() { Measured = measured, Target = target, ElementIds = elementIds.ToList() };

    /// <summary>A layout-wide measurement: the listed ids are context for the canvas, never offenders.</summary>
    public static FindingDraft OfLayout(string measured, string target, IEnumerable<string> contextIds)
        => new()
        {
            Measured = measured,
            Target = target,
            ElementIds = contextIds.ToList(),
            SubjectIds = Array.Empty<string>(),
        };
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

    /// <summary>
    /// Parameters this evaluator requires of a rule, checked once at load time so a typo in the
    /// hand-edited knowledge base stops start-up with the rule id instead of throwing mid-evaluation.
    /// Return one message per problem; an empty sequence means the rule is usable.
    /// </summary>
    IEnumerable<string> Validate(RuleDefinition rule) => Array.Empty<string>();
}
