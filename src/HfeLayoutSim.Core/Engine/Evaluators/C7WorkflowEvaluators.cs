using System.Text.RegularExpressions;
using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Engine.Evaluators;

/// <summary>C7-01: fill-in sequence must follow left→right, top→bottom with no backward transitions.</summary>
public sealed class SequenceFlowEvaluator : IRuleEvaluator
{
    public string CheckName => "sequenceFlow";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var maxRatio = rule.GetDouble("maxBackflowRatio");
        var seq = ctx.Sequenced;
        if (seq.Count < 2) return RuleEvaluation.NotApplicable();

        var backflows = new List<string>();
        var transitions = seq.Count - 1;
        for (var i = 1; i < seq.Count; i++)
        {
            var prev = Rect.Of(seq[i - 1]);
            var next = Rect.Of(seq[i]);
            var forward =
                next.CenterY > prev.CenterY + ctx.RowTolerance ||                       // next row
                (Math.Abs(next.CenterY - prev.CenterY) <= ctx.RowTolerance &&
                 next.CenterX >= prev.CenterX);                                          // same row, rightward
            if (!forward)
            {
                backflows.Add(seq[i - 1].Id);
                backflows.Add(seq[i].Id);
            }
        }

        var count = backflows.Count / 2;
        if (transitions == 0 || (double)count / transitions <= maxRatio) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{count}", $"역류율 {maxRatio * 100:0}% 이하", backflows.Distinct()));
    }
}

/// <summary>C7-02: one role's block must precede another (Precondition before InspectionItem).</summary>
public sealed class RoleOrderEvaluator : IRuleEvaluator
{
    public string CheckName => "roleOrder";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var before = Enum.Parse<SemanticRole>(rule.GetString("before") ?? "", ignoreCase: true);
        var after = Enum.Parse<SemanticRole>(rule.GetString("after") ?? "", ignoreCase: true);

        var beforeEls = ctx.WithRole(before).ToList();
        var afterEls = ctx.WithRole(after).ToList();
        if (beforeEls.Count == 0 || afterEls.Count == 0) return RuleEvaluation.NotApplicable();

        var firstAfter = afterEls.Min(e => ctx.ReadingIndexOf(e));
        var offenders = beforeEls.Where(e => ctx.ReadingIndexOf(e) > firstAfter).ToList();
        if (offenders.Count == 0) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{before} {offenders.Count}개가 {after} 뒤", $"{before} 선행", offenders.Select(e => e.Id)));
    }
}

/// <summary>C7-03: the 8-block standard skeleton order (omission allowed, reordering not).</summary>
public sealed class BlockOrderEvaluator : IRuleEvaluator
{
    public string CheckName => "blockOrder";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var order = rule.GetStringList("order");
        if (order.Count == 0) return RuleEvaluation.NotApplicable();

        // Position of each present block = mean center-Y of its members ("Header" = element type).
        var positions = new List<(string Block, double Y, List<string> Ids)>();
        foreach (var block in order)
        {
            List<LayoutElement> members;
            if (block.Equals("Header", StringComparison.OrdinalIgnoreCase) &&
                !Enum.TryParse<SemanticRole>(block, ignoreCase: true, out _))
                members = ctx.OfType(ElementType.Header).ToList();
            else if (Enum.TryParse<SemanticRole>(block, ignoreCase: true, out var role))
                members = ctx.WithRole(role).ToList();
            else
                members = ctx.OfType(ElementType.Header).ToList();

            if (members.Count > 0)
                positions.Add((block, members.Average(e => Rect.Of(e).CenterY), members.Select(e => e.Id).ToList()));
        }
        if (positions.Count < 2) return RuleEvaluation.NotApplicable();

        var violations = new List<string>();
        var ids = new List<string>();
        for (var i = 1; i < positions.Count; i++)
        {
            if (positions[i].Y < positions[i - 1].Y - ctx.RowTolerance)
            {
                violations.Add($"{positions[i].Block}가 {positions[i - 1].Block}보다 앞");
                ids.AddRange(positions[i - 1].Ids);
                ids.AddRange(positions[i].Ids);
            }
        }
        if (violations.Count == 0) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            string.Join(", ", violations), string.Join("→", order), ids.Distinct()));
    }
}

/// <summary>C7-04: a completeness gate ("전 항목 기입 완료") must sit right before the final verdict.</summary>
public sealed class CompletenessGateEvaluator : IRuleEvaluator
{
    public string CheckName => "completenessGate";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var beforeRole = Enum.Parse<SemanticRole>(rule.GetString("requireBeforeRole") ?? "FinalVerdict", ignoreCase: true);
        var acceptTypes = rule.GetStringList("acceptTypes")
            .Select(t => Enum.Parse<ElementType>(t, ignoreCase: true)).ToHashSet();
        var keyword = rule.GetString("keyword") ?? "";

        var verdicts = ctx.WithRole(beforeRole).ToList();
        if (verdicts.Count == 0) return RuleEvaluation.NotApplicable();

        var verdictIndex = verdicts.Min(e => ctx.ReadingIndexOf(e));
        var regex = new Regex(keyword, RegexOptions.IgnoreCase);
        var gates = ctx.Layout.Elements
            .Where(e => acceptTypes.Contains(e.Type) &&
                        !string.IsNullOrWhiteSpace(e.Text) && regex.IsMatch(e.Text!))
            .ToList();

        if (gates.Any(g => ctx.ReadingIndexOf(g) < verdictIndex)) return RuleEvaluation.Pass();

        return RuleEvaluation.Violation(FindingDraft.Of(
            gates.Count == 0 ? "완결성 확인 요소 없음" : "완결성 확인이 판정보다 뒤",
            "판정 직전 완결성 체크", verdicts.Select(e => e.Id)));
    }
}

/// <summary>C7-05: a nonconformance-record block must exist (even for zero events).</summary>
public sealed class RoleExistsEvaluator : IRuleEvaluator
{
    public string CheckName => "roleExists";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var role = Enum.Parse<SemanticRole>(rule.GetString("role") ?? "", ignoreCase: true);
        if (ctx.WithRole(role).Any()) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of($"{role} 블록 없음", $"{role} 블록 존재"));
    }
}

/// <summary>C7-06: nothing to fill in after the signature (the workflow endpoint).</summary>
public sealed class SignatureLastEvaluator : IRuleEvaluator
{
    public string CheckName => "signatureLast";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var signatures = ctx.Layout.Elements
            .Where(e => e.Type == ElementType.SignatureBox || e.Semantics.Role == SemanticRole.Signature)
            .ToList();
        if (signatures.Count == 0) return RuleEvaluation.NotApplicable();

        var lastSig = signatures.Max(e => ctx.ReadingIndexOf(e));
        var offenders = ctx.InputElements
            .Where(e => ctx.ReadingIndexOf(e) > lastSig)
            .ToList();
        if (offenders.Count == 0) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"{offenders.Count}", "서명 이후 입력 요소 0개", offenders.Select(e => e.Id)));
    }
}
