using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Engine.Evaluators;

/// <summary>C1-01: normal text contrast ≥ minRatio.</summary>
public sealed class TextContrastEvaluator : IRuleEvaluator
{
    public string CheckName => "textContrast";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var min = rule.GetDouble("minRatio");
        var drafts = ContrastHelper.FailingPairs(ctx, largeText: false, min);
        return drafts.Count == 0 && !ctx.TextBearing.Any(e => !ctx.IsLargeText(e))
            ? RuleEvaluation.NotApplicable()
            : RuleEvaluation.FromDrafts(drafts);
    }
}

/// <summary>C1-02: large text contrast ≥ minRatio.</summary>
public sealed class LargeTextContrastEvaluator : IRuleEvaluator
{
    public string CheckName => "largeTextContrast";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var min = rule.GetDouble("minRatio");
        var drafts = ContrastHelper.FailingPairs(ctx, largeText: true, min);
        return drafts.Count == 0 && !ctx.TextBearing.Any(ctx.IsLargeText)
            ? RuleEvaluation.NotApplicable()
            : RuleEvaluation.FromDrafts(drafts);
    }
}

internal static class ContrastHelper
{
    /// <summary>Failing (fg,bg) pairs grouped so identical color combinations yield one finding.</summary>
    public static List<FindingDraft> FailingPairs(EvaluationContext ctx, bool largeText, double min)
    {
        var failing = new Dictionary<(string Fg, string Bg), List<string>>();
        var ratios = new Dictionary<(string, string), double>();

        foreach (var el in ctx.TextBearing)
        {
            if (ctx.IsLargeText(el) != largeText) continue;
            var fg = ctx.ForegroundOf(el);
            var bg = ctx.EffectiveBackgroundOf(el);
            var cr = ContrastCalculator.Ratio(fg, bg);
            if (cr is null || cr >= min) continue;
            var key = (fg.ToUpperInvariant(), bg.ToUpperInvariant());
            if (!failing.TryGetValue(key, out var ids))
                failing[key] = ids = new List<string>();
            ids.Add(el.Id);
            ratios[key] = cr.Value;
        }

        return failing
            .Select(kv => FindingDraft.Of($"{ratios[kv.Key]:0.00} ({kv.Key.Fg} on {kv.Key.Bg})", $"{min:0.0}", kv.Value))
            .ToList();
    }
}

/// <summary>C1-03: non-text UI component (border) contrast ≥ 3:1 (Screen).</summary>
public sealed class NonTextContrastEvaluator : IRuleEvaluator
{
    public string CheckName => "nonTextContrast";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var min = rule.GetDouble("minRatio");
        var targets = ctx.Layout.Elements
            .Where(e => EvaluationContext.InputTypes.Contains(e.Type) || e.Type == ElementType.Button)
            .Where(e => !ColorUtil.IsTransparent(e.Style.BorderColor))
            .ToList();
        if (targets.Count == 0) return RuleEvaluation.NotApplicable();

        var drafts = new List<FindingDraft>();
        foreach (var el in targets)
        {
            var bg = ctx.BackgroundBehind(el);
            var cr = ContrastCalculator.Ratio(el.Style.BorderColor, bg);
            if (cr is not null && cr < min)
                drafts.Add(FindingDraft.Of($"{cr:0.00} ({el.Style.BorderColor})", $"{min:0.0}", el.Id));
        }
        return RuleEvaluation.FromDrafts(drafts);
    }
}

/// <summary>C1-04: judgment info must be triple-coded (color + icon + text).</summary>
public sealed class JudgmentTripleCodingEvaluator : IRuleEvaluator
{
    public string CheckName => "judgmentTripleCoding";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var required = rule.GetStringList("requireAll");
        var candidates = ctx.Layout.Elements
            .Where(e => e.Type == ElementType.JudgmentBadge || e.Semantics.Judgment is not null)
            .ToList();
        if (candidates.Count == 0) return RuleEvaluation.NotApplicable();

        var drafts = new List<FindingDraft>();
        foreach (var el in candidates)
        {
            var j = el.Semantics.Judgment ?? new JudgmentCoding();
            var present = new List<string>();
            var missing = false;
            foreach (var req in required)
            {
                var has = req.ToLowerInvariant() switch
                {
                    "hascolor" => j.HasColor,
                    "hasicon" => j.HasIcon,
                    "hastext" => j.HasText,
                    _ => true
                };
                if (has) present.Add(KoreanName(req));
                else missing = true;
            }
            if (missing)
                drafts.Add(FindingDraft.Of(present.Count == 0 ? "코딩 없음" : string.Join("+", present),
                    "색+아이콘+텍스트", el.Id));
        }
        return RuleEvaluation.FromDrafts(drafts);

        static string KoreanName(string req) => req.ToLowerInvariant() switch
        {
            "hascolor" => "색상",
            "hasicon" => "아이콘",
            "hastext" => "텍스트",
            _ => req
        };
    }
}

/// <summary>C1-05: chromatic (semantic) background area ≤ maxAreaRatio of the content area.</summary>
public sealed class SemanticColorAreaEvaluator : IRuleEvaluator
{
    public string CheckName => "semanticColorArea";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var maxRatio = rule.GetDouble("maxAreaRatio");
        var colored = ctx.Layout.Elements
            .Where(e => ColorUtil.IsChromatic(e.Style.BgColor))
            .ToList();
        if (colored.Count == 0) return RuleEvaluation.NotApplicable();

        var area = colored.Sum(e => Rect.Of(e).Area);
        var ratio = area / Math.Max(1e-9, ctx.ContentRect.Area);
        if (ratio <= maxRatio) return RuleEvaluation.Pass();

        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{ratio * 100:0.#}", $"{maxRatio * 100:0}% 이하", colored.Select(e => e.Id)));
    }
}

/// <summary>C1-06: red and green elements distinguished by color alone must not sit adjacent.</summary>
public sealed class RedGreenAdjacencyEvaluator : IRuleEvaluator
{
    public string CheckName => "redGreenAdjacency";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var maxDist = ctx.NativeParam(rule, "maxDistanceMm", "maxDistancePx");

        // "Color-only" elements: colored but carrying no text/icon differentiator.
        List<(LayoutElement El, ColorUtil.HueFamily Hue)> colorOnly = ctx.Layout.Elements
            .Select(e => (El: e, Hue: HueOf(e)))
            .Where(t => t.Hue != ColorUtil.HueFamily.Other && IsColorOnly(t.El))
            .ToList();

        var reds = colorOnly.Where(t => t.Hue == ColorUtil.HueFamily.Red).ToList();
        var greens = colorOnly.Where(t => t.Hue == ColorUtil.HueFamily.Green).ToList();
        if (reds.Count == 0 || greens.Count == 0) return RuleEvaluation.NotApplicable();

        // One systematic colour-coding mistake is ONE finding: charging it per red×green pair
        // multiplied the penalty quadratically and could emit thousands of identical findings.
        var pairs = new List<string>();
        var involved = new List<string>();
        var closest = double.MaxValue;
        foreach (var r in reds)
            foreach (var g in greens)
            {
                var d = Geometry.EdgeDistance(r.El, g.El);
                if (d > maxDist) continue;
                closest = Math.Min(closest, d);
                pairs.Add($"{r.El.Id}↔{g.El.Id}");
                if (!involved.Contains(r.El.Id)) involved.Add(r.El.Id);
                if (!involved.Contains(g.El.Id)) involved.Add(g.El.Id);
            }

        if (pairs.Count == 0) return RuleEvaluation.Pass();

        var detail = string.Join(", ", pairs.Take(5)) + (pairs.Count > 5 ? " 외" : "");
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{ctx.FormatLen(closest)} · {pairs.Count}쌍 ({detail})",
            $"{ctx.FormatLen(maxDist)} 초과 이격 또는 형태 병행", involved));

        static ColorUtil.HueFamily HueOf(LayoutElement e)
        {
            var byBg = ColorUtil.ClassifyHue(e.Style.BgColor);
            return byBg != ColorUtil.HueFamily.Other ? byBg : ColorUtil.ClassifyHue(e.Style.FgColor);
        }

        static bool IsColorOnly(LayoutElement e)
        {
            var j = e.Semantics.Judgment;
            var hasIconOrText = (j?.HasIcon ?? false) || (j?.HasText ?? false) ||
                                !string.IsNullOrWhiteSpace(e.Text);
            return !hasIconOrText;
        }
    }
}

/// <summary>C1-07: chromatic colors must come from the verified semantic palette.</summary>
public sealed class PaletteComplianceEvaluator : IRuleEvaluator
{
    public string CheckName => "paletteCompliance";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var allowed = new HashSet<string>(
            rule.GetStringList("allowedSemanticColors").Select(c => c.ToUpperInvariant()));

        var offenders = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var el in ctx.Layout.Elements)
        {
            foreach (var color in new[] { el.Style.FgColor, el.Style.BgColor, el.Style.BorderColor })
            {
                if (!ColorUtil.IsChromatic(color)) continue; // neutral grays are always allowed
                var norm = color!.ToUpperInvariant();
                if (allowed.Contains(norm)) continue;
                if (!offenders.TryGetValue(norm, out var ids))
                    offenders[norm] = ids = new List<string>();
                if (!ids.Contains(el.Id)) ids.Add(el.Id);
            }
        }

        if (offenders.Count == 0) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(offenders.Select(kv =>
            FindingDraft.Of(kv.Key, "검증 팔레트 등재 색", kv.Value)));
    }
}
