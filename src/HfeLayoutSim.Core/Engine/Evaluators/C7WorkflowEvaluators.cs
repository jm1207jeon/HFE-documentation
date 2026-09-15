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

    public IEnumerable<string> Validate(RuleDefinition rule) =>
        rule.ValidateEnum<SemanticRole>("before", required: true)
            .Concat(rule.ValidateEnum<SemanticRole>("after", required: true));

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var before = rule.GetEnum("before", SemanticRole.Precondition);
        var after = rule.GetEnum("after", SemanticRole.InspectionItem);

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

    /// <summary>
    /// Entries of params.order must be exactly "Header" (the element type) or a SemanticRole NAME.
    /// Enum.TryParse alone is not enough: it accepts "3" and "99" as numbers and accepts "None",
    /// which would average every unroled element into one phantom block position.
    /// </summary>
    public IEnumerable<string> Validate(RuleDefinition rule)
    {
        foreach (var block in rule.GetStringList("order"))
        {
            if (IsHeaderBlock(block)) continue;
            if (Enum.GetNames<SemanticRole>().Contains(block, StringComparer.OrdinalIgnoreCase) &&
                !block.Equals(nameof(SemanticRole.None), StringComparison.OrdinalIgnoreCase))
                continue;

            yield return $"{rule.Id}: params.order 의 '{block}' 는 'Header' 도, 유효한 역할 이름도 아닙니다 " +
                         $"(허용: Header, {string.Join(", ", Enum.GetNames<SemanticRole>().Where(n => n != nameof(SemanticRole.None)))})";
        }
    }

    private static bool IsHeaderBlock(string block)
        => block.Equals(nameof(ElementType.Header), StringComparison.OrdinalIgnoreCase);

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var order = rule.GetStringList("order");
        if (order.Count == 0) return RuleEvaluation.NotApplicable();

        // Position of each present block = mean center-Y of its members ("Header" = element type).
        var positions = new List<(string Block, double Y, List<string> Ids)>();
        foreach (var block in order)
        {
            List<LayoutElement> members;
            if (IsHeaderBlock(block))
                members = ctx.OfType(ElementType.Header).ToList();
            else if (Enum.TryParse<SemanticRole>(block, ignoreCase: true, out var role) &&
                     Enum.IsDefined(role) && role != SemanticRole.None)
                members = ctx.WithRole(role).ToList();
            else
                continue; // unknown name — Validate already reported it; never invent a position

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

    public IEnumerable<string> Validate(RuleDefinition rule) =>
        rule.ValidateEnum<SemanticRole>("requireBeforeRole")
            .Concat(rule.ValidateEnumList<ElementType>("acceptTypes", required: true))
            .Concat(rule.ValidateRegex("keyword"));

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var beforeRole = rule.GetEnum("requireBeforeRole", SemanticRole.FinalVerdict);
        var acceptTypes = rule.GetEnumList<ElementType>("acceptTypes").ToHashSet();

        var verdicts = ctx.WithRole(beforeRole).ToList();
        if (verdicts.Count == 0) return RuleEvaluation.NotApplicable();

        var verdictIndex = verdicts.Min(e => ctx.ReadingIndexOf(e));
        var regex = rule.GetRegex("keyword", "기입 완료|공란 없음|완결");
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

    public IEnumerable<string> Validate(RuleDefinition rule) =>
        rule.ValidateEnum<SemanticRole>("role", required: true);

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var role = rule.GetEnum("role", SemanticRole.NonconformanceRecord);
        if (ctx.WithRole(role).Any()) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of($"{role} 블록 없음", $"{role} 블록 존재"));
    }
}

/// <summary>
/// Existence of an element TYPE (as opposed to a semantic role), e.g. the document title block.
/// Absence of content must be scored as a defect: without this an empty canvas satisfied every
/// rule vacuously and was certified "PASS ✓ 배포 적합".
/// </summary>
public sealed class TypeExistsEvaluator : IRuleEvaluator
{
    public string CheckName => "typeExists";

    public IEnumerable<string> Validate(RuleDefinition rule) =>
        rule.ValidateEnumList<ElementType>("types", required: true);

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var types = rule.GetEnumList<ElementType>("types").ToHashSet();
        var min = Math.Max(1, rule.GetInt("minCount", 1));

        var found = ctx.Layout.Elements.Count(e => types.Contains(e.Type));
        if (found >= min) return RuleEvaluation.Pass();

        var names = string.Join("/", types.Select(t => t.ToString()));
        return RuleEvaluation.Violation(FindingDraft.Of(
            found == 0 ? $"{names} 요소 없음" : $"{found}개",
            $"{names} {min}개 이상"));
    }
}

/// <summary>C7-07: dual-control signature composition — distinct inspector/approver signers.</summary>
public sealed class SignatureCompositionEvaluator : IRuleEvaluator
{
    public string CheckName => "signatureComposition";

    public RuleEvaluation Evaluate(RuleDefinition rule, EvaluationContext ctx)
    {
        var minSigners = rule.GetInt("minSigners", 2);
        var distinctRoles = rule.GetBool("distinctRoles");

        var sigs = ctx.OfType(ElementType.SignatureBox).ToList();
        var distinct = sigs
            .Select(s => (s.SignerRole ?? "").Trim())
            .Where(r => r.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var effective = distinctRoles ? distinct : sigs.Count;

        if (effective >= minSigners) return RuleEvaluation.Pass();
        return RuleEvaluation.Violation(FindingDraft.Of(
            $"서명란 {sigs.Count}개·역할 {distinct}종", $"역할 구분 {minSigners}인 이상",
            sigs.Select(s => s.Id)));
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
