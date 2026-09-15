using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Report;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Compare;

/// <summary>Per-variant summary — CategoryScores feeds the radar chart, Total the bar chart (SPEC §6.5).</summary>
public sealed class VariantSummary
{
    public string Name { get; init; } = "";
    public double Total { get; init; }
    public string Grade { get; init; } = "";
    public bool GradeCapped { get; init; }

    /// <summary>The engine's verdict for this variant, against the rules file's own passGrade —
    /// never re-derived from the letter, which would hard-code a threshold the JSON owns.</summary>
    public bool Pass { get; init; }

    public string PassGrade { get; init; } = "";
    public Medium Medium { get; init; }
    public int FindingCount { get; init; }
    public int CriticalCount { get; init; }

    /// <summary>Category id (C1..C9) → score 0–100.</summary>
    public IReadOnlyDictionary<string, double> CategoryScores { get; init; } =
        new Dictionary<string, double>();
}

/// <summary>One rule in a diff bucket.</summary>
public sealed class DiffItem
{
    public string RuleId { get; init; } = "";
    public string Category { get; init; } = "";
    public string Title { get; init; } = "";
    public RuleSeverity Severity { get; init; }
}

/// <summary>Rule-id set difference between the baseline variant and one other variant.</summary>
public sealed class FindingDiff
{
    public string BaselineName { get; init; } = "";
    public string VariantName { get; init; } = "";

    /// <summary>해소됨 — violated in baseline, clean in the variant.</summary>
    public IReadOnlyList<DiffItem> Resolved { get; init; } = Array.Empty<DiffItem>();

    /// <summary>신규 발생 — clean in baseline, violated in the variant.</summary>
    public IReadOnlyList<DiffItem> NewlyIntroduced { get; init; } = Array.Empty<DiffItem>();

    /// <summary>공통 잔존 — violated in both.</summary>
    public IReadOnlyList<DiffItem> Remaining { get; init; } = Array.Empty<DiffItem>();
}

public sealed class ComparisonResult
{
    public IReadOnlyList<VariantSummary> Variants { get; init; } = Array.Empty<VariantSummary>();

    /// <summary>Baseline (first report) vs each subsequent variant.</summary>
    public IReadOnlyList<FindingDiff> Diffs { get; init; } = Array.Empty<FindingDiff>();
}

/// <summary>Compares 2–3 evaluated variants of the same form (SPEC §6.5).</summary>
public static class VariantComparer
{
    public const int MaxVariants = 3;

    public static ComparisonResult Compare(IReadOnlyList<EvaluationReport> reports)
    {
        if (reports.Count is < 2 or > MaxVariants)
            throw new ArgumentException($"비교는 2~{MaxVariants}개 Variant만 지원합니다 (현재 {reports.Count}개).");

        // Paper and Screen are scored by different subsets of the rule base, so a rule that is simply
        // inapplicable to the other medium would appear in the diff as "해소됨" — a design improvement
        // that never happened.
        var media = reports.Select(r => r.Medium).Distinct().ToList();
        if (media.Count > 1)
            throw new ArgumentException(
                "매체가 다른 배치안은 비교할 수 없습니다 " +
                $"({string.Join(", ", media.Select(m => m == Medium.Paper ? "종이" : "화면"))}) — " +
                "같은 매체의 배치안끼리 비교하십시오.");

        var variants = reports.Select(Summarize).ToList();
        var baseline = reports[0];
        var diffs = reports.Skip(1).Select(other => Diff(baseline, other)).ToList();
        return new ComparisonResult { Variants = variants, Diffs = diffs };
    }

    public static VariantSummary Summarize(EvaluationReport report) => new()
    {
        Name = report.LayoutName,
        Total = report.ScoreCard.Total,
        Grade = report.ScoreCard.Grade,
        GradeCapped = report.ScoreCard.GradeCapped,
        Pass = report.ScoreCard.Pass,
        PassGrade = report.ScoreCard.PassGrade,
        Medium = report.Medium,
        FindingCount = report.Findings.Count,
        CriticalCount = report.Findings.Count(f => f.Severity == RuleSeverity.Critical),
        CategoryScores = report.ScoreCard.Categories.ToDictionary(c => c.Category, c => c.Score)
    };

    public static FindingDiff Diff(EvaluationReport baseline, EvaluationReport variant)
    {
        var baseViolations = ViolatedRules(baseline);
        var varViolations = ViolatedRules(variant);

        return new FindingDiff
        {
            BaselineName = baseline.LayoutName,
            VariantName = variant.LayoutName,
            Resolved = Bucket(baseViolations.Keys.Except(varViolations.Keys), baseViolations),
            NewlyIntroduced = Bucket(varViolations.Keys.Except(baseViolations.Keys), varViolations),
            Remaining = Bucket(baseViolations.Keys.Intersect(varViolations.Keys), varViolations)
        };
    }

    /// <summary>Rule id → representative (worst) finding of that rule.</summary>
    private static Dictionary<string, Finding> ViolatedRules(EvaluationReport report)
        => report.Findings
            .GroupBy(f => f.RuleId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.Severity).First());

    private static List<DiffItem> Bucket(IEnumerable<string> ruleIds, Dictionary<string, Finding> source)
        => ruleIds
            .Select(id => source[id])
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.RuleId)
            .Select(f => new DiffItem
            {
                RuleId = f.RuleId,
                Category = f.Category,
                Title = f.Title,
                Severity = f.Severity
            })
            .ToList();
}
