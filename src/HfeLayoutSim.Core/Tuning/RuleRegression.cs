using HfeLayoutSim.Core.Engine;
using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Report;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Tuning;

/// <summary>Outcome flip of one rule on one layout between two rule sets.</summary>
public sealed class RuleOutcomeChange
{
    public string RuleId { get; init; } = "";
    public string Title { get; init; } = "";
    public RuleStatus StatusA { get; init; }
    public RuleStatus StatusB { get; init; }
    public int FindingsA { get; init; }
    public int FindingsB { get; init; }
}

/// <summary>Score/grade movement of one layout between two rule sets.</summary>
public sealed class LayoutRegressionRow
{
    public string Name { get; init; } = "";
    public double TotalA { get; init; }
    public double TotalB { get; init; }
    public string GradeA { get; init; } = "";
    public string GradeB { get; init; } = "";
    public bool PassA { get; init; }
    public bool PassB { get; init; }
    public IReadOnlyList<RuleOutcomeChange> Changes { get; init; } = Array.Empty<RuleOutcomeChange>();

    public double Delta => Math.Round(TotalB - TotalA, 1);
}

public sealed class RegressionReport
{
    public string RulesVersionA { get; init; } = "";
    public string RulesVersionB { get; init; } = "";
    public IReadOnlyList<LayoutRegressionRow> Rows { get; init; } = Array.Empty<LayoutRegressionRow>();

    public bool AnyChange => Rows.Any(r => r.Changes.Count > 0 || Math.Abs(r.Delta) > 0.05);
}

/// <summary>
/// Rule-tuning regression: evaluate the same layouts under a baseline and a modified rule set
/// and report exactly which rules flipped and how scores moved — so a threshold change
/// (e.g. C5-04 maxRun 4→5) is reviewed with evidence instead of gut feeling.
/// </summary>
public static class RuleRegression
{
    public static RegressionReport Compare(RuleSet baseline, RuleSet modified,
        IEnumerable<(string Name, Layout Layout)> layouts)
    {
        var engine = new HfeEngine();
        var rows = new List<LayoutRegressionRow>();

        foreach (var (name, layout) in layouts)
        {
            var a = engine.Evaluate(layout, baseline);
            var b = engine.Evaluate(layout, modified);

            var outcomesA = a.RuleOutcomes.ToDictionary(o => o.RuleId);
            var outcomesB = b.RuleOutcomes.ToDictionary(o => o.RuleId);
            var titles = modified.Rules.Concat(baseline.Rules)
                .GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First().Title);

            var changes = new List<RuleOutcomeChange>();
            foreach (var ruleId in outcomesA.Keys.Union(outcomesB.Keys).OrderBy(k => k))
            {
                var oa = outcomesA.GetValueOrDefault(ruleId);
                var ob = outcomesB.GetValueOrDefault(ruleId);
                var statusA = oa?.Status ?? RuleStatus.NotApplicable;
                var statusB = ob?.Status ?? RuleStatus.NotApplicable;
                var findingsA = oa?.FindingCount ?? 0;
                var findingsB = ob?.FindingCount ?? 0;
                if (statusA == statusB && findingsA == findingsB) continue;

                changes.Add(new RuleOutcomeChange
                {
                    RuleId = ruleId,
                    Title = titles.GetValueOrDefault(ruleId, ""),
                    StatusA = statusA,
                    StatusB = statusB,
                    FindingsA = findingsA,
                    FindingsB = findingsB
                });
            }

            rows.Add(new LayoutRegressionRow
            {
                Name = name,
                TotalA = a.ScoreCard.Total,
                TotalB = b.ScoreCard.Total,
                GradeA = a.ScoreCard.Grade,
                GradeB = b.ScoreCard.Grade,
                PassA = a.ScoreCard.Pass,
                PassB = b.ScoreCard.Pass,
                Changes = changes
            });
        }

        return new RegressionReport
        {
            RulesVersionA = baseline.Version,
            RulesVersionB = modified.Version,
            Rows = rows
        };
    }
}
