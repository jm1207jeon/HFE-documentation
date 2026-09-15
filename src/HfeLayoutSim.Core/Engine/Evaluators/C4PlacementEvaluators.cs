using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Engine.Evaluators;

/// <summary>
/// C4-01 / C4-03 / C4-05: role (or type) must live in a required zone/band.
/// topBandRatio → element center within the top band (POA additionally requires left 60%).
/// bottomBandRatio → center within the bottom band (TA additionally requires right 50%+).
/// </summary>
public sealed class RoleInZoneEvaluator : IRuleEvaluator
{
    public string CheckName => "roleInZone";

    public IEnumerable<string> Validate(RuleDefinition rule) =>
        rule.ValidateEnum<SemanticRole>("role")
            .Concat(rule.ValidateEnumList<SemanticRole>("roles"))
            .Concat(rule.ValidateEnumList<ElementType>("types"))
            .Concat(rule.ValidateEnumList<Zone>("requiredZones"));

    /// <summary>POA horizontal leniency: identification blocks often span wide, so centers up to 60% width count as "left".</summary>
    private const double PoaMaxXRatio = 0.6;

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var targets = EvaluatorCommon.TargetElements(rule, ctx);
        if (targets.Count == 0) return RuleEvaluation.NotApplicable();

        var zones = rule.GetStringList("requiredZones");
        var topBand = rule.GetDouble("topBandRatio", double.NaN);
        var bottomBand = rule.GetDouble("bottomBandRatio", double.NaN);
        var content = ctx.ContentRect;

        var drafts = new List<FindingDraft>();
        foreach (var el in targets)
        {
            var r = Rect.Of(el);
            bool ok;
            if (!double.IsNaN(topBand))
            {
                ok = r.CenterY <= content.Y + content.H * topBand;
                if (ok && zones.Contains("POA"))
                    ok = r.CenterX <= content.X + content.W * PoaMaxXRatio;
            }
            else if (!double.IsNaN(bottomBand))
            {
                ok = r.CenterY >= content.Y + content.H * (1 - bottomBand);
                if (ok && zones.Contains("TA"))
                    ok = r.CenterX >= content.X + content.W * 0.5;
            }
            else
            {
                ok = zones.Count == 0 || zones.Contains(ctx.ZoneOf(el).ToString());
            }

            if (!ok)
                drafts.Add(FindingDraft.Of(ctx.ZoneOf(el).ToString(),
                    zones.Count > 0 ? string.Join("/", zones) : "요구 구역", el.Id));
        }
        return RuleEvaluation.FromDrafts(drafts);
    }
}

/// <summary>C4-02: given roles (and critical elements) must NOT sit in forbidden zones (WFA blind spot).</summary>
public sealed class RoleNotInZoneEvaluator : IRuleEvaluator
{
    public string CheckName => "roleNotInZone";

    public IEnumerable<string> Validate(RuleDefinition rule) =>
        rule.ValidateEnumList<SemanticRole>("roles", required: true)
            .Concat(rule.ValidateEnumList<Zone>("forbiddenZones", required: true));

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var roles = rule.GetEnumList<SemanticRole>("roles").ToHashSet();
        var includeCritical = rule.GetBool("alsoCriticalElements");
        var forbidden = rule.GetEnumList<Zone>("forbiddenZones").ToHashSet();

        var targets = ctx.Layout.Elements
            .Where(e => roles.Contains(e.Semantics.Role) || (includeCritical && e.Semantics.IsCritical))
            .ToList();
        if (targets.Count == 0) return RuleEvaluation.NotApplicable();

        var drafts = targets
            .Where(e => forbidden.Contains(ctx.ZoneOf(e)))
            .Select(e => FindingDraft.Of(
                e.Semantics.Role != SemanticRole.None ? e.Semantics.Role.ToString() : $"Critical '{e.Id}'",
                $"{string.Join("/", forbidden)} 밖", e.Id))
            .ToList();
        return RuleEvaluation.FromDrafts(drafts);
    }
}

/// <summary>C4-04: primary action button anchored bottom-right (Screen).</summary>
public sealed class PrimaryActionPositionEvaluator : IRuleEvaluator
{
    public string CheckName => "primaryActionPosition";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var primaries = ctx.OfType(ElementType.Button)
            .Where(b => b.ButtonKind == ButtonKind.Primary).ToList();
        if (primaries.Count == 0) return RuleEvaluation.NotApplicable();

        // Bottom-right of the full canvas: action bar included, so measure on canvas halves.
        var canvas = new Rect(0, 0, ctx.Layout.Canvas.Width, ctx.Layout.Canvas.Height);
        var drafts = new List<FindingDraft>();
        foreach (var b in primaries)
        {
            var r = Rect.Of(b);
            var ok = r.CenterX >= canvas.W / 2 && r.CenterY >= canvas.H / 2;
            if (!ok)
                drafts.Add(FindingDraft.Of(ctx.ZoneOf(b).ToString(), rule.GetString("zone") ?? "TA", b.Id));
        }
        return RuleEvaluation.FromDrafts(drafts);
    }
}

/// <summary>C4-06: scrollable screens must keep identification visible in the fixed context bar.</summary>
public sealed class IdentificationStickyEvaluator : IRuleEvaluator
{
    public string CheckName => "identificationSticky";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        if (!rule.GetBool("requireStickyIfScrollable")) return RuleEvaluation.NotApplicable();
        if (!ctx.Layout.Canvas.IsScrollable) return RuleEvaluation.NotApplicable();

        var fixedTop = ctx.Layout.Canvas.FixedRegions ?? new FixedRegions();
        var stickyBottom = fixedTop.HeaderPx + fixedTop.ContextBarPx;

        var sticky = ctx.WithRole(SemanticRole.Identification)
            .Any(e => Rect.Of(e).CenterY <= stickyBottom);
        if (sticky) return RuleEvaluation.Pass();

        return RuleEvaluation.Violation(FindingDraft.Of(
            "고정 영역 내 식별 정보 없음", "컨텍스트 바에 LOT/품명 고정",
            ctx.WithRole(SemanticRole.Identification).Select(e => e.Id)));
    }
}

internal static class EvaluatorCommon
{
    /// <summary>Resolve rule targets from "role", "roles" and/or "types" params.</summary>
    public static List<LayoutElement> TargetElements(RuleDefinition rule, EvaluationContext ctx)
    {
        var result = new List<LayoutElement>();

        var roles = new List<SemanticRole>();
        var single = rule.GetString("role");
        if (single is not null && Enum.TryParse<SemanticRole>(single, ignoreCase: true, out var sr))
            roles.Add(sr);
        foreach (var r in rule.GetStringList("roles"))
            if (Enum.TryParse<SemanticRole>(r, ignoreCase: true, out var pr))
                roles.Add(pr);

        if (roles.Count > 0)
            result.AddRange(ctx.Layout.Elements.Where(e => roles.Contains(e.Semantics.Role)));

        foreach (var t in rule.GetStringList("types"))
            if (Enum.TryParse<ElementType>(t, ignoreCase: true, out var et))
                result.AddRange(ctx.OfType(et).Where(e => !result.Contains(e)));

        return result;
    }
}
