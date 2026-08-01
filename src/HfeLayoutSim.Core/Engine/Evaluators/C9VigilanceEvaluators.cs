using System.Text.RegularExpressions;
using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Engine.Evaluators;

/// <summary>
/// C9-01: long runs of repeated measurement cells must be sectioned (vigilance decrement).
/// Covers both individual NumInput runs and oversized measurement tables.
/// </summary>
public sealed class SampleSectioningEvaluator : IRuleEvaluator
{
    public string CheckName => "sampleSectioning";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var maxRun = rule.GetInt("maxRunWithoutBreak");
        var breakTypes = rule.GetStringList("breakTypes")
            .Select(t => Enum.Parse<ElementType>(t, ignoreCase: true)).ToHashSet();

        var hasMeasurements = ctx.OfType(ElementType.NumInput).Any() || ctx.OfType(ElementType.Table).Any();
        if (!hasMeasurements) return RuleEvaluation.NotApplicable();

        var drafts = new List<FindingDraft>();

        // Runs of loose NumInput cells in reading order, broken by any break element.
        var run = new List<LayoutElement>();
        List<LayoutElement>? worst = null;
        foreach (var el in ctx.ReadingOrder)
        {
            if (el.Type == ElementType.NumInput)
            {
                run.Add(el);
                continue;
            }
            if (breakTypes.Contains(el.Type))
            {
                if (worst is null || run.Count > worst.Count) worst = new List<LayoutElement>(run);
                run.Clear();
            }
        }
        if (worst is null || run.Count > worst.Count) worst = run;
        if (worst.Count > maxRun)
            drafts.Add(FindingDraft.Of($"{worst.Count}", $"{maxRun}개 이하 구간", worst.Select(e => e.Id)));

        // Tables representing >maxRun sample rows without internal sectioning.
        foreach (var table in ctx.OfType(ElementType.Table))
            if (table.Rows is int rows && rows - 1 > maxRun) // minus header row
                drafts.Add(FindingDraft.Of($"표 {rows - 1}행", $"{maxRun}행 단위 구간 분할", table.Id));

        return RuleEvaluation.FromDrafts(drafts);
    }
}

/// <summary>C9-02: paper forms must carry "Page n/N" so a lost sheet is detectable.</summary>
public sealed class PageNumberFormatEvaluator : IRuleEvaluator
{
    public string CheckName => "pageNumberFormat";

    // "Page 1/3", "1 / 3", "3쪽 중 1쪽" style — the total must be present.
    private static readonly Regex PagePattern = new(
        @"(page\s*\d+\s*/\s*\d+)|(\d+\s*/\s*\d+\s*(쪽|페이지|page))|(page\s*\d+\s*of\s*\d+)",
        RegexOptions.IgnoreCase);

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        if (!rule.GetBool("requireTotal")) return RuleEvaluation.NotApplicable();

        var found = ctx.TextBearing.Any(e => PagePattern.IsMatch(e.Text!));
        if (found) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of("Page n/N 표기 없음", "'Page n/N' 형식 표기"));
    }
}

/// <summary>C9-03: multi-input screens must visualize progress (stepper) — Nielsen H1.</summary>
public sealed class RoleExistsTypeEvaluator : IRuleEvaluator
{
    public string CheckName => "roleExistsType";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var types = rule.GetStringList("types")
            .Select(t => Enum.Parse<ElementType>(t, ignoreCase: true)).ToHashSet();
        var threshold = rule.GetInt("requiredWhenInputCountOver", int.MaxValue);

        var inputCount = ctx.InputElements.Count();
        if (inputCount <= threshold) return RuleEvaluation.NotApplicable();

        if (ctx.Layout.Elements.Any(e => types.Contains(e.Type))) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"입력 {inputCount}개, 진행 표시 없음", $"{string.Join("/", types)} 배치"));
    }
}
