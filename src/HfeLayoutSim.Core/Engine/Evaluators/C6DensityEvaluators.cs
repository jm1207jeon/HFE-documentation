using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Engine.Evaluators;

/// <summary>C6-01: information density on paper (Σ element area / printable area) inside 40–60%.</summary>
public sealed class PaperDensityEvaluator : IRuleEvaluator
{
    public string CheckName => "paperDensity";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var min = rule.GetDouble("optimalMin");
        var max = rule.GetDouble("optimalMax");
        var warnMax = rule.GetDouble("warnMax", max);

        if (ctx.Layout.Elements.Count == 0) return RuleEvaluation.NotApplicable();

        var area = ctx.Layout.Elements
            .Where(e => e.Type is not (ElementType.Section or ElementType.Divider))
            .Sum(e => Rect.Of(e).Area);
        var density = area / Math.Max(1e-9, ctx.ContentRect.Area);

        if (density >= min && density <= max) return RuleEvaluation.Pass();

        var target = density > warnMax
            ? $"{min * 100:0}–{max * 100:0}% (경고 한계 {warnMax * 100:0}% 초과 — 페이지 분할 필수)"
            : $"{min * 100:0}–{max * 100:0}%";
        return RuleEvaluation.Violation(FindingDraft.Of($"{density * 100:0.#}", target));
    }
}

/// <summary>C6-02: number of input fields per page/step bounded.</summary>
public sealed class InputFieldCountEvaluator : IRuleEvaluator
{
    public string CheckName => "inputFieldCount";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var max = ctx.Medium == Medium.Paper
            ? rule.GetInt("paperMax")
            : rule.GetInt("screenMaxPerStep");

        var inputs = ctx.InputElements.ToList();
        if (inputs.Count == 0) return RuleEvaluation.NotApplicable();
        if (inputs.Count <= max) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{inputs.Count}", $"{max}개 이하", inputs.Select(e => e.Id)));
    }
}

/// <summary>C6-03: table column count ≤ optimalMax (hard limit hardMax).</summary>
public sealed class TableColumnCountEvaluator : IRuleEvaluator
{
    public string CheckName => "tableColumnCount";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var optimalMax = rule.GetInt("optimalMax");
        var hardMax = rule.GetInt("hardMax");

        var tables = ctx.OfType(ElementType.Table).Where(t => t.Columns is not null).ToList();
        if (tables.Count == 0) return RuleEvaluation.NotApplicable();

        var drafts = tables
            .Where(t => t.Columns! > optimalMax)
            .Select(t => FindingDraft.Of($"{t.Columns}",
                $"{optimalMax}열 이하 (분할 한계 {hardMax}열)", t.Id))
            .ToList();
        return RuleEvaluation.FromDrafts(drafts);
    }
}

/// <summary>C6-04: number of simultaneously visible top-level blocks (Screen).</summary>
public sealed class TopLevelBlockCountEvaluator : IRuleEvaluator
{
    public string CheckName => "topLevelBlockCount";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var optimalMax = rule.GetInt("optimalMax");
        var hardMax = rule.GetInt("hardMax");

        // Top-level = container-ish elements not nested inside another section's group.
        var sectionIds = ctx.OfType(ElementType.Section).Select(s => s.Id).ToHashSet();
        var blocks = ctx.Layout.Elements
            .Where(e => e.Type is ElementType.Section or ElementType.Table)
            .Where(e => e.EffectiveGroupId is null || !sectionIds.Contains(e.EffectiveGroupId))
            .ToList();
        if (blocks.Count == 0) return RuleEvaluation.NotApplicable();

        if (blocks.Count <= optimalMax) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{blocks.Count}", $"{optimalMax}개 이하 (한계 {hardMax}개)", blocks.Select(e => e.Id)));
    }
}

/// <summary>C6-05: elements must respect print margins / content padding.</summary>
public sealed class MarginComplianceEvaluator : IRuleEvaluator
{
    public string CheckName => "marginCompliance";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var canvas = ctx.Layout.Canvas;
        Rect allowed;
        if (ctx.Medium == Medium.Paper)
        {
            var top = rule.GetNestedDouble("paperMm", "top");
            var bottom = rule.GetNestedDouble("paperMm", "bottom");
            var left = rule.GetNestedDouble("paperMm", "left");
            var right = rule.GetNestedDouble("paperMm", "right");
            allowed = new Rect(left, top, canvas.Width - left - right, canvas.Height - top - bottom);
        }
        else
        {
            var pad = rule.GetDouble("screenPaddingPx");
            var c = ctx.ContentRect;
            allowed = new Rect(c.X + pad, c.Y + pad, c.W - 2 * pad, c.H - 2 * pad);
        }

        var tolerance = ctx.Medium == Medium.Paper ? 0.5 : 1.0;
        var offenders = ctx.Layout.Elements
            .Where(e => !InFixedChrome(e) && !allowed.ContainsRect(Rect.Of(e), tolerance))
            .ToList();
        if (offenders.Count == 0) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{offenders.Count}", "여백/패딩 내 배치", offenders.Select(e => e.Id)));

        // Screen chrome (header/GNB/context/action bar) hosts its own elements legally.
        bool InFixedChrome(LayoutElement e)
        {
            if (ctx.Medium == Medium.Paper) return false;
            var f = canvas.FixedRegions;
            if (f is null) return false;
            var r = Rect.Of(e);
            return r.CenterY <= f.HeaderPx + f.ContextBarPx ||
                   r.CenterX <= f.GnbPx ||
                   r.CenterY >= canvas.Height - f.ActionBarPx;
        }
    }
}
