using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Report;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Engine;

/// <summary>
/// The evaluation engine: Layout + RuleSet → EvaluationReport.
/// Pure function style — no UI, no I/O — so it unit-tests and batch-runs from a CLI (CLAUDE.md #2).
/// </summary>
public sealed class HfeEngine
{
    private readonly EvaluatorRegistry _registry;

    public HfeEngine(EvaluatorRegistry? registry = null)
    {
        _registry = registry ?? new EvaluatorRegistry();
    }

    public EvaluationReport Evaluate(Layout layout, RuleSet rules)
    {
        _registry.EnsureCoverage(rules);
        var ctx = new EvaluationContext(layout, rules);

        var findings = new List<Finding>();
        var outcomes = new List<RuleOutcome>();
        var strengths = new List<Strength>();
        var perCategory = new Dictionary<string, (int Pass, int Violation, int Na, int Penalty)>();

        foreach (var rule in rules.Rules)
        {
            RuleEvaluation eval;
            if (!Applies(rule, layout.Medium))
            {
                eval = RuleEvaluation.NotApplicable();
            }
            else
            {
                var evaluator = _registry.Find(rule.Check)!;
                eval = evaluator.Evaluate(rule, ctx);
            }

            var stats = perCategory.TryGetValue(rule.Category, out var s) ? s : (0, 0, 0, 0);
            switch (eval.Status)
            {
                case RuleStatus.Pass:
                    stats.Item1++;
                    if (rule.HighlightOnPass)
                        strengths.Add(new Strength
                        {
                            RuleId = rule.Id,
                            Category = rule.Category,
                            Title = rule.Title,
                            StandardRef = rule.Ref
                        });
                    break;

                case RuleStatus.NotApplicable:
                    stats.Item3++;
                    break;

                case RuleStatus.Violation:
                    stats.Item2++;
                    foreach (var draft in eval.Findings)
                    {
                        var severity = FinalSeverity(rule, draft, layout, rules);
                        var penalty = rules.PenaltyOf(severity);
                        stats.Item4 += penalty;
                        findings.Add(new Finding
                        {
                            RuleId = rule.Id,
                            Category = rule.Category,
                            Severity = severity,
                            Escalated = severity != rule.Severity,
                            Title = rule.Title,
                            Message = FormatMessage(rule.Message, draft),
                            Measured = draft.Measured,
                            Target = draft.Target,
                            ElementIds = draft.ElementIds,
                            Recommendation = rule.Recommendation,
                            StandardRef = rule.Ref,
                            Penalty = penalty
                        });
                    }
                    break;
            }
            perCategory[rule.Category] = stats;

            outcomes.Add(new RuleOutcome
            {
                RuleId = rule.Id,
                Category = rule.Category,
                Status = eval.Status,
                FindingCount = eval.Findings.Count
            });
        }

        // ---- scoring ----
        var categories = new List<CategoryScore>();
        double total = 0;
        foreach (var (category, weight) in rules.Weights.OrderBy(kv => kv.Key))
        {
            var s = perCategory.TryGetValue(category, out var st) ? st : (0, 0, 0, 0);
            var score = Math.Max(0, 100 - s.Item4);
            total += score * weight;
            categories.Add(new CategoryScore
            {
                Category = category,
                Weight = weight,
                Score = score,
                Grade = rules.GradeOf(score),
                PassCount = s.Item1,
                ViolationCount = s.Item2,
                NotApplicableCount = s.Item3
            });
        }

        var grade = rules.GradeOf(total);
        var capped = false;
        if (rules.CriticalViolationGradeCap is { } cap &&
            findings.Any(f => f.Severity == RuleSeverity.Critical) &&
            rules.RankOf(grade) < rules.RankOf(cap))
        {
            grade = cap;
            capped = true;
        }

        return new EvaluationReport
        {
            LayoutName = layout.Meta.Name,
            Medium = layout.Medium,
            RulesVersion = rules.Version,
            EvaluatedAt = DateTimeOffset.Now,
            ScoreCard = new ScoreCard
            {
                Total = Math.Round(total, 1),
                Grade = grade,
                GradeCapped = capped,
                Pass = rules.IsPass(grade),
                PassGrade = rules.PassGrade,
                Categories = categories
            },
            Findings = findings
                .OrderByDescending(f => f.Severity)
                .ThenBy(f => f.Category)
                .ThenBy(f => f.RuleId)
                .ToList(),
            Strengths = strengths,
            RuleOutcomes = outcomes
        };
    }

    private static bool Applies(RuleDefinition rule, Medium medium) => rule.AppliesTo switch
    {
        RuleApplicability.Both => true,
        RuleApplicability.Paper => medium == Medium.Paper,
        RuleApplicability.Screen => medium == Medium.Screen,
        _ => false
    };

    /// <summary>Escalate one level when a critical element is involved (rules JSON switch).</summary>
    private static RuleSeverity FinalSeverity(RuleDefinition rule, FindingDraft draft, Layout layout, RuleSet rules)
    {
        if (!rules.CriticalElementSeverityEscalation || rule.Severity == RuleSeverity.Critical)
            return rule.Severity;

        // Only the elements the finding is ABOUT may escalate it — see FindingDraft.SubjectIds.
        var subjects = draft.EscalationIds;
        if (subjects.Count == 0) return rule.Severity;

        var critical = layout.Elements
            .Where(e => subjects.Contains(e.Id))
            .Any(e => e.Semantics.IsCritical);
        if (!critical) return rule.Severity;

        return rule.Severity == RuleSeverity.Minor ? RuleSeverity.Major : RuleSeverity.Critical;
    }

    private static string FormatMessage(string template, FindingDraft draft)
        => template.Replace("{measured}", draft.Measured).Replace("{target}", draft.Target);
}
