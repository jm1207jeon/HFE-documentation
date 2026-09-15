using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Engine.Evaluators;

/// <summary>
/// C5-01: inter-group gap must be ≥ minRatio × intra-group gap (Gestalt proximity).
/// intra = median nearest-neighbor distance inside a group; inter = closest distance
/// between a group and its nearest neighboring group.
/// </summary>
public sealed class GroupSpacingRatioEvaluator : IRuleEvaluator
{
    public string CheckName => "groupSpacingRatio";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var minRatio = rule.GetDouble("minRatio");
        var groups = ctx.Groups.Where(g => g.Value.Count >= 2).ToList();
        if (groups.Count < 2) return RuleEvaluation.NotApplicable();

        // Minimum meaningful intra distance so touching label+input pairs don't divide by zero.
        var floor = ctx.Medium == Medium.Paper ? 0.5 : 2.0;

        var drafts = new List<FindingDraft>();
        double worst = double.MaxValue;
        (string A, string B)? worstPair = null;
        List<string> worstIds = new();

        foreach (var (name, members) in groups)
        {
            var intra = Math.Max(floor, MedianNearestNeighbor(members));

            // nearest other group
            string? nearestName = null;
            double nearestDist = double.MaxValue;
            List<LayoutElement>? nearestMembers = null;
            foreach (var (otherName, otherMembers) in groups)
            {
                if (otherName == name) continue;
                var d = GroupDistance(members, otherMembers);
                if (d < nearestDist)
                {
                    nearestDist = d;
                    nearestName = otherName;
                    nearestMembers = otherMembers;
                }
            }
            if (nearestName is null) continue;

            var ratio = nearestDist / intra;
            if (ratio < minRatio && ratio < worst)
            {
                worst = ratio;
                worstPair = (name, nearestName);
                worstIds = members.Concat(nearestMembers!).Select(e => e.Id).ToList();
            }
        }

        if (worstPair is null) return RuleEvaluation.Pass();
        // Unlike the other aggregate rules, these ids ARE the offenders: the members of the two
        // groups are exactly what the user has to move apart, so a safety-critical one among them
        // must still raise the severity (SPEC §6.3).
        drafts.Add(FindingDraft.Of(
            $"{worst:0.0} ('{worstPair.Value.A}'↔'{worstPair.Value.B}')", $"{minRatio:0.0} 이상", worstIds));
        return RuleEvaluation.FromDrafts(drafts);
    }

    private static double MedianNearestNeighbor(List<LayoutElement> members)
    {
        var dists = new List<double>();
        foreach (var a in members)
        {
            var nearest = members.Where(b => !ReferenceEquals(a, b))
                .Min(b => Geometry.EdgeDistance(a, b));
            dists.Add(nearest);
        }
        dists.Sort();
        var mid = dists.Count / 2;
        return dists.Count % 2 == 1 ? dists[mid] : (dists[mid - 1] + dists[mid]) / 2;
    }

    private static double GroupDistance(List<LayoutElement> a, List<LayoutElement> b)
        => a.Min(x => b.Min(y => Geometry.EdgeDistance(x, y)));
}

/// <summary>C5-02: a label must sit close to its input so the pairing is unambiguous.</summary>
public sealed class LabelInputProximityEvaluator : IRuleEvaluator
{
    public string CheckName => "labelInputProximity";

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

/// <summary>
/// C5-03: inspection-item groups sized 3–7 (Miller's 7±2). Counts logical items:
/// a Table contributes its data rows, annotation Labels contribute nothing.
/// </summary>
public sealed class GroupItemCountEvaluator : IRuleEvaluator
{
    public string CheckName => "groupItemCount";

    public IEnumerable<string> Validate(RuleDefinition rule) =>
        rule.ValidateEnum<SemanticRole>("targetRole")
            .Concat(rule.ValidateInt("min", required: true, min: 1))
            .Concat(rule.ValidateInt("max", required: true, min: 1))
            .Concat(rule.GetInt("max", 7) < rule.GetInt("min", 3)
                ? new[] { $"{rule.Id}: params.max({rule.GetInt("max")}) 가 params.min({rule.GetInt("min")}) 보다 작습니다" }
                : Array.Empty<string>());

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var min = rule.GetInt("min");
        var max = rule.GetInt("max");
        var role = rule.GetEnum("targetRole", SemanticRole.InspectionItem);

        var groups = ctx.WithRole(role)
            .Where(e => !string.IsNullOrEmpty(e.EffectiveGroupId))
            .GroupBy(e => e.EffectiveGroupId!)
            .ToList();
        if (groups.Count == 0) return RuleEvaluation.NotApplicable();

        var drafts = new List<FindingDraft>();
        foreach (var g in groups)
        {
            var count = g.Sum(LogicalItemCount);
            if (count > 0 && (count < min || count > max))
                // the group's SIZE is the offence — its members are context, not offenders
                drafts.Add(FindingDraft.OfLayout($"{g.Key}({count}개)", $"{min}–{max}개", g.Select(e => e.Id)));
        }
        return RuleEvaluation.FromDrafts(drafts);

        static int LogicalItemCount(LayoutElement e) => e.Type switch
        {
            ElementType.Table => Math.Max(0, (e.Rows ?? 1) - 1), // minus header row
            ElementType.Label => 0,
            _ => 1
        };
    }
}

/// <summary>C5-04: at most N identical checkboxes in a row (habituation risk).</summary>
public sealed class ConsecutiveSameInputEvaluator : IRuleEvaluator
{
    public string CheckName => "consecutiveSameInput";

    public IEnumerable<string> Validate(RuleDefinition rule) =>
        rule.ValidateEnum<ElementType>("type")
            .Concat(rule.ValidateInt("maxRun", required: true, min: 1));

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var maxRun = rule.GetInt("maxRun");
        var type = rule.GetEnum("type", ElementType.Checkbox);
        if (!ctx.OfType(type).Any()) return RuleEvaluation.NotApplicable();

        // Walk reading order over inputs and structural breakers; labels are transparent.
        var breakers = new[] { ElementType.Divider, ElementType.InfoBox, ElementType.Section, ElementType.Header };
        var run = new List<LayoutElement>();
        List<LayoutElement>? worst = null;

        foreach (var el in ctx.ReadingOrder)
        {
            if (el.Type == type)
            {
                run.Add(el);
                continue;
            }
            var isBreaker = breakers.Contains(el.Type) || EvaluationContext.InputTypes.Contains(el.Type);
            if (isBreaker)
            {
                if (worst is null || run.Count > worst.Count) worst = new List<LayoutElement>(run);
                run.Clear();
            }
        }
        if (worst is null || run.Count > worst.Count) worst = run;

        if (worst.Count <= maxRun) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{worst.Count}", $"{maxRun}개 이하", worst.Select(e => e.Id)));
    }
}

/// <summary>C5-05: with many inspection items, none may remain ungrouped.</summary>
public sealed class UngroupedItemsEvaluator : IRuleEvaluator
{
    public string CheckName => "ungroupedItems";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var threshold = rule.GetInt("threshold");
        var maxUngrouped = rule.GetInt("maxUngrouped");

        var items = ctx.WithRole(SemanticRole.InspectionItem).ToList();
        if (items.Count <= threshold) return RuleEvaluation.NotApplicable();

        var ungrouped = items.Where(e => string.IsNullOrEmpty(e.EffectiveGroupId)).ToList();
        if (ungrouped.Count <= maxUngrouped) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{ungrouped.Count}", $"{maxUngrouped}개 이하", ungrouped.Select(e => e.Id)));
    }
}
