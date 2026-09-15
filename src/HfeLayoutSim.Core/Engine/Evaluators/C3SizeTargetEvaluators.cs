using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Engine.Evaluators;

/// <summary>C3-01 / C3-06: minimum W×H for a given element type (checkbox, signature box...).</summary>
public sealed class MinElementSizeEvaluator : IRuleEvaluator
{
    public string CheckName => "minElementSize";

    public IEnumerable<string> Validate(RuleDefinition rule) =>
        rule.ValidateEnum<ElementType>("type", required: true);

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var type = rule.GetEnum("type", ElementType.Checkbox);
        var min = ctx.Medium == Medium.Paper ? rule.GetDoubleList("paperMinMm") : rule.GetDoubleList("screenMinPx");
        if (min.Count < 2) return RuleEvaluation.NotApplicable();

        var targets = ctx.OfType(type).ToList();
        if (targets.Count == 0) return RuleEvaluation.NotApplicable();

        var drafts = new List<FindingDraft>();
        foreach (var el in targets)
        {
            var (w, h) = el.OrientedSize;
            if (w < min[0] || h < min[1])
                drafts.Add(FindingDraft.Of(
                    $"{ctx.FormatLen(w)}×{ctx.FormatLen(h)}",
                    $"{ctx.FormatLen(min[0])}×{ctx.FormatLen(min[1])}", el.Id));
        }
        return RuleEvaluation.FromDrafts(drafts);
    }
}

/// <summary>C3-02: click targets ≥ minPx on the shorter side (Screen).</summary>
public sealed class MinTargetSizeEvaluator : IRuleEvaluator
{
    public string CheckName => "minTargetSize";

    public IEnumerable<string> Validate(RuleDefinition rule) =>
        rule.ValidateEnumList<ElementType>("types", required: true);

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var min = rule.GetDouble("minPx");
        var types = rule.GetEnumList<ElementType>("types").ToHashSet();

        var targets = ctx.Layout.Elements.Where(e => types.Contains(e.Type)).ToList();
        if (targets.Count == 0) return RuleEvaluation.NotApplicable();

        var drafts = targets
            .Where(e => Math.Min(e.OrientedSize.W, e.OrientedSize.H) < min)
            .Select(e => FindingDraft.Of($"{Math.Min(e.OrientedSize.W, e.OrientedSize.H):0}", $"{min:0}", e.Id))
            .ToList();
        return RuleEvaluation.FromDrafts(drafts);
    }
}

/// <summary>C3-03: primary action button height ≥ minHeightPx (Screen).</summary>
public sealed class PrimaryButtonSizeEvaluator : IRuleEvaluator
{
    public string CheckName => "primaryButtonSize";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var min = rule.GetDouble("minHeightPx");
        var primaries = ctx.OfType(ElementType.Button)
            .Where(b => b.ButtonKind == ButtonKind.Primary).ToList();
        if (primaries.Count == 0) return RuleEvaluation.NotApplicable();

        var drafts = primaries
            .Where(b => b.OrientedSize.H < min)
            .Select(b => FindingDraft.Of($"{b.OrientedSize.H:0}", $"{min:0}px 이상", b.Id))
            .ToList();
        return RuleEvaluation.FromDrafts(drafts);
    }
}

/// <summary>C3-04: minimum gap between adjacent buttons (Screen).</summary>
public sealed class TargetSpacingEvaluator : IRuleEvaluator
{
    public string CheckName => "targetSpacing";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var min = rule.GetDouble("minGapPx");
        var buttons = ctx.OfType(ElementType.Button).ToList();
        if (buttons.Count < 2) return RuleEvaluation.NotApplicable();

        var drafts = new List<FindingDraft>();
        for (var i = 0; i < buttons.Count; i++)
            for (var j = i + 1; j < buttons.Count; j++)
            {
                var gap = Geometry.EdgeDistance(buttons[i], buttons[j]);
                if (gap < min)
                    drafts.Add(FindingDraft.Of($"{gap:0}", $"{min:0}px 이상", buttons[i].Id, buttons[j].Id));
            }
        return RuleEvaluation.FromDrafts(drafts);
    }
}

/// <summary>C3-05: destructive buttons physically isolated from other buttons (Screen).</summary>
public sealed class DestructiveSeparationEvaluator : IRuleEvaluator
{
    public string CheckName => "destructiveSeparation";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var min = rule.GetDouble("minGapPx");
        var buttons = ctx.OfType(ElementType.Button).ToList();
        if (buttons.Count < 2 || !buttons.Any(IsDestructive)) return RuleEvaluation.NotApplicable();

        // Triangular walk so a danger/danger pair is charged once, not once per direction;
        // the danger element is always listed first so the message names the right button.
        var drafts = new List<FindingDraft>();
        for (var i = 0; i < buttons.Count; i++)
            for (var j = i + 1; j < buttons.Count; j++)
            {
                var a = buttons[i];
                var b = buttons[j];
                if (!IsDestructive(a) && !IsDestructive(b)) continue;

                var gap = Geometry.EdgeDistance(a, b);
                if (gap >= min) continue;

                var (danger, other) = IsDestructive(a) ? (a, b) : (b, a);
                drafts.Add(FindingDraft.Of($"{gap:0}", $"{min:0}px 이상", danger.Id, other.Id));
            }
        return RuleEvaluation.FromDrafts(drafts);
    }

    private static bool IsDestructive(LayoutElement button)
        => button.ButtonKind == ButtonKind.Danger || button.Semantics.IsDestructiveAction;
}

/// <summary>C3-07: label→input travel distance bounded.</summary>
public sealed class LabelInputTravelEvaluator : IRuleEvaluator
{
    public string CheckName => "labelInputTravel";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var max = ctx.NativeParam(rule, "maxMm", "maxPx");
        var pairs = ctx.LabelInputPairs().ToList();
        if (pairs.Count == 0) return RuleEvaluation.NotApplicable();

        var drafts = pairs
            .Where(p => p.Distance > max)
            .Select(p => FindingDraft.Of(ctx.FormatLen(p.Distance), $"{ctx.FormatLen(max)} 이하",
                p.Label.Id, p.Input.Id))
            .ToList();
        return RuleEvaluation.FromDrafts(drafts);
    }
}
