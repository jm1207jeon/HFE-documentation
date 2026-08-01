using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Engine.Evaluators;

/// <summary>
/// C8-01: safety-critical confirmations must transcribe the value, not just tick a box
/// (mistake proofing — a copied LOT number proves the operator actually read it).
/// </summary>
public sealed class CriticalTranscriptionEvaluator : IRuleEvaluator
{
    public string CheckName => "criticalTranscription";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var keywords = rule.GetStringList("targetKeywords");

        bool MatchesKeyword(LayoutElement e)
        {
            var texts = new List<string?> { e.Text, ctx.PairedLabelOf(e)?.Text };
            return texts.Any(t => t is not null &&
                keywords.Any(k => t.Contains(k, StringComparison.OrdinalIgnoreCase)));
        }

        // Candidates: critical confirmations, or checkbox confirmations of the listed safety keywords.
        var candidates = ctx.InputElements
            .Where(e => e.Semantics.IsCritical || (e.Type == ElementType.Checkbox && MatchesKeyword(e)))
            .ToList();
        if (candidates.Count == 0) return RuleEvaluation.NotApplicable();

        var offenders = candidates.Where(e =>
                e.Semantics.InputKind == InputKind.Check ||
                (e.Semantics.InputKind == InputKind.None && e.Type == ElementType.Checkbox))
            .ToList();
        if (offenders.Count == 0) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{offenders.Count}", "전기(transcribe) 방식", offenders.Select(e => e.Id)));
    }
}

/// <summary>C8-02: measurement inputs must show the spec limits alongside (no recall from memory).</summary>
public sealed class SpecLimitShownEvaluator : IRuleEvaluator
{
    public string CheckName => "specLimitShown";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var type = Enum.Parse<ElementType>(rule.GetString("targetType") ?? "NumInput", ignoreCase: true);
        var targets = ctx.OfType(type).ToList();
        if (targets.Count == 0) return RuleEvaluation.NotApplicable();

        var offenders = targets.Where(e => !e.Semantics.SpecLimitShown).ToList();
        if (offenders.Count == 0) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{offenders.Count}", "규격·한계값 병기", offenders.Select(e => e.Id)));
    }
}

/// <summary>C8-03: required fields visibly marked (label carries the marker).</summary>
public sealed class RequiredMarkEvaluator : IRuleEvaluator
{
    public string CheckName => "requiredMark";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var marker = rule.GetString("marker") ?? "*";
        var required = ctx.Layout.Elements.Where(e => e.Semantics.IsRequired).ToList();
        if (required.Count == 0) return RuleEvaluation.NotApplicable();

        var offenders = new List<LayoutElement>();
        foreach (var e in required)
        {
            var ownText = e.Text ?? "";
            var labelText = ctx.PairedLabelOf(e)?.Text ?? "";
            if (!ownText.Contains(marker) && !labelText.Contains(marker))
                offenders.Add(e);
        }
        if (offenders.Count == 0) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{offenders.Count}", $"라벨에 '{marker}' 표기", offenders.Select(e => e.Id)));
    }
}

/// <summary>C8-04: judgment must not be pre-selected as pass — force an active choice (Screen).</summary>
public sealed class JudgmentExplicitEvaluator : IRuleEvaluator
{
    public string CheckName => "judgmentExplicit";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        if (!rule.GetBool("forbidDefaultPass")) return RuleEvaluation.NotApplicable();

        var judgments = ctx.Layout.Elements
            .Where(e => e.Semantics.Role == SemanticRole.FinalVerdict &&
                        e.Type is ElementType.RadioGroup or ElementType.JudgmentBadge or ElementType.Checkbox)
            .ToList();
        if (judgments.Count == 0) return RuleEvaluation.NotApplicable();

        // Any pre-selected default defeats the active-choice requirement.
        var offenders = judgments.Where(e => !string.IsNullOrWhiteSpace(e.DefaultValue)).ToList();
        if (offenders.Count == 0) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            string.Join(", ", offenders.Select(e => $"'{e.DefaultValue}'")), "기본 미선택",
            offenders.Select(e => e.Id)));
    }
}

/// <summary>C8-05: measurement inputs carry a fixed unit annotation.</summary>
public sealed class UnitShownEvaluator : IRuleEvaluator
{
    public string CheckName => "unitShown";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var type = Enum.Parse<ElementType>(rule.GetString("targetType") ?? "NumInput", ignoreCase: true);
        var targets = ctx.OfType(type).ToList();
        if (targets.Count == 0) return RuleEvaluation.NotApplicable();

        var offenders = targets.Where(e => string.IsNullOrWhiteSpace(e.Unit)).ToList();
        if (offenders.Count == 0) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{offenders.Count}", "단위 고정 표기", offenders.Select(e => e.Id)));
    }
}

/// <summary>C8-06: button labels must state the complete action, not a bare "확인/OK" (Screen).</summary>
public sealed class ButtonLabelQualityEvaluator : IRuleEvaluator
{
    public string CheckName => "buttonLabelQuality";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var forbidden = new HashSet<string>(rule.GetStringList("forbiddenAlone"), StringComparer.OrdinalIgnoreCase);
        var buttons = ctx.OfType(ElementType.Button).ToList();
        if (buttons.Count == 0) return RuleEvaluation.NotApplicable();

        var offenders = buttons
            .Where(b => forbidden.Contains((b.Text ?? "").Trim()))
            .ToList();
        if (offenders.Count == 0) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            string.Join(", ", offenders.Select(b => $"'{b.Text}'")), "동사+목적어 라벨",
            offenders.Select(b => b.Id)));
    }
}
