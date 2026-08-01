using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Engine.Evaluators;

/// <summary>C2-01: minimum font size (8pt Paper / 12px Screen).</summary>
public sealed class MinFontSizeEvaluator : IRuleEvaluator
{
    public string CheckName => "minFontSize";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var min = ctx.NativeParam(rule, "paperMinPt", "screenMinPx");
        var unit = ctx.Medium == Medium.Paper ? "pt" : "px";
        var drafts = ctx.TextBearing
            .Where(e => e.Style.FontSize is not null && e.Style.FontSize < min)
            .Select(e => FindingDraft.Of($"{e.Style.FontSize:0.#}{unit}", $"{min:0.#}{unit}", e.Id))
            .ToList();
        return RuleEvaluation.FromDrafts(drafts);
    }
}

/// <summary>C2-02: at most N distinct font families (body + mono).</summary>
public sealed class FontFamilyCountEvaluator : IRuleEvaluator
{
    public string CheckName => "fontFamilyCount";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var max = rule.GetInt("max");
        var used = ctx.Layout.Elements
            .Where(e => !string.IsNullOrWhiteSpace(e.Style.FontFamily))
            .GroupBy(e => e.Style.FontFamily!, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (used.Count <= max) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{used.Count} ({string.Join(", ", used.Select(g => g.Key))})", $"{max}종 이하",
            used.SelectMany(g => g.Select(e => e.Id))));
    }
}

/// <summary>C2-03: at most N distinct font size levels.</summary>
public sealed class FontSizeLevelsEvaluator : IRuleEvaluator
{
    public string CheckName => "fontSizeLevels";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var max = rule.GetInt("max");
        var sizes = ctx.Layout.Elements
            .Where(e => e.Style.FontSize is not null)
            .Select(e => e.Style.FontSize!.Value)
            .Distinct()
            .OrderBy(s => s)
            .ToList();
        if (sizes.Count <= max) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{sizes.Count} ({string.Join(", ", sizes.Select(s => $"{s:0.#}"))})", $"{max}단계 이하",
            Array.Empty<string>()));
    }
}

/// <summary>C2-04: measurement / identification fields must use a monospaced family.</summary>
public sealed class MonoForNumericEvaluator : IRuleEvaluator
{
    public string CheckName => "monoForNumeric";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var targetTypes = rule.GetStringList("targetTypes")
            .Select(t => Enum.Parse<ElementType>(t, ignoreCase: true)).ToHashSet();
        var monoFamilies = new HashSet<string>(rule.GetStringList("monoFamilies"), StringComparer.OrdinalIgnoreCase);

        var targets = ctx.Layout.Elements.Where(e => targetTypes.Contains(e.Type)).ToList();
        if (targets.Count == 0) return RuleEvaluation.NotApplicable();

        var offenders = targets
            .Where(e => string.IsNullOrWhiteSpace(e.Style.FontFamily) || !monoFamilies.Contains(e.Style.FontFamily!))
            .ToList();
        if (offenders.Count == 0) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{offenders.Count}", "등폭 서체 적용", offenders.Select(e => e.Id)));
    }
}

/// <summary>C2-05: no italic emphasis; underline reserved for links (Navigation role).</summary>
public sealed class ForbiddenEmphasisEvaluator : IRuleEvaluator
{
    public string CheckName => "forbiddenEmphasis";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var forbidItalic = rule.GetBool("forbidItalic");
        var underlineOnlyLinks = rule.GetBool("underlineOnlyForLinks");

        var offenders = ctx.Layout.Elements.Where(e =>
                (forbidItalic && e.Style.Italic) ||
                (underlineOnlyLinks && e.Style.Underline && e.Semantics.Role != SemanticRole.Navigation))
            .ToList();
        if (offenders.Count == 0) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{offenders.Count}", "이탤릭 금지·밑줄은 링크 전용", offenders.Select(e => e.Id)));
    }
}

/// <summary>C2-06: title must be visually superior to body (size + bold).</summary>
public sealed class TitleHierarchyEvaluator : IRuleEvaluator
{
    public string CheckName => "titleHierarchy";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var min = ctx.NativeParam(rule, "headerMinPt", "headerMinPx");
        var unit = ctx.Medium == Medium.Paper ? "pt" : "px";
        var headers = ctx.OfType(ElementType.Header).ToList();
        if (headers.Count == 0) return RuleEvaluation.NotApplicable();

        var drafts = new List<FindingDraft>();
        foreach (var h in headers)
        {
            var size = h.Style.FontSize;
            var ok = size is not null && size >= min && h.Style.Bold;
            if (!ok)
                drafts.Add(FindingDraft.Of(
                    $"{(size is null ? "본문 크기" : $"{size:0.#}{unit}")}{(h.Style.Bold ? " Bold" : " 보통 굵기")}",
                    $"{min:0.#}{unit}+ Bold", h.Id));
        }
        return RuleEvaluation.FromDrafts(drafts);
    }
}
