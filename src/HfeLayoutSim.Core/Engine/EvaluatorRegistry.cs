using HfeLayoutSim.Core.Engine.Evaluators;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Engine;

/// <summary>Maps rule "check" names to evaluator implementations.</summary>
public sealed class EvaluatorRegistry
{
    private readonly Dictionary<string, IRuleEvaluator> _byCheck;

    public EvaluatorRegistry(IEnumerable<IRuleEvaluator>? evaluators = null)
    {
        _byCheck = (evaluators ?? CreateDefaultEvaluators())
            .ToDictionary(e => e.CheckName, StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<IRuleEvaluator> CreateDefaultEvaluators() => new IRuleEvaluator[]
    {
        // C1 색상·대비
        new TextContrastEvaluator(),
        new LargeTextContrastEvaluator(),
        new NonTextContrastEvaluator(),
        new JudgmentTripleCodingEvaluator(),
        new SemanticColorAreaEvaluator(),
        new RedGreenAdjacencyEvaluator(),
        new PaletteComplianceEvaluator(),
        // C2 타이포그래피
        new MinFontSizeEvaluator(),
        new FontFamilyCountEvaluator(),
        new FontSizeLevelsEvaluator(),
        new MonoForNumericEvaluator(),
        new ForbiddenEmphasisEvaluator(),
        new TitleHierarchyEvaluator(),
        // C3 크기·조작
        new MinElementSizeEvaluator(),
        new MinTargetSizeEvaluator(),
        new PrimaryButtonSizeEvaluator(),
        new TargetSpacingEvaluator(),
        new DestructiveSeparationEvaluator(),
        new LabelInputTravelEvaluator(),
        // C4 배치·시선 흐름
        new RoleInZoneEvaluator(),
        new RoleNotInZoneEvaluator(),
        new PrimaryActionPositionEvaluator(),
        new IdentificationStickyEvaluator(),
        // C5 그룹화·근접성
        new GroupSpacingRatioEvaluator(),
        new LabelInputProximityEvaluator(),
        new GroupItemCountEvaluator(),
        new ConsecutiveSameInputEvaluator(),
        new UngroupedItemsEvaluator(),
        // C6 인지 부하·밀도
        new PaperDensityEvaluator(),
        new InputFieldCountEvaluator(),
        new TableColumnCountEvaluator(),
        new TopLevelBlockCountEvaluator(),
        new MarginComplianceEvaluator(),
        // C7 순서·업무 흐름
        new SequenceFlowEvaluator(),
        new RoleOrderEvaluator(),
        new BlockOrderEvaluator(),
        new CompletenessGateEvaluator(),
        new RoleExistsEvaluator(),
        new SignatureLastEvaluator(),
        new SignatureCompositionEvaluator(),
        // C8 오류 방지·강건성
        new CriticalTranscriptionEvaluator(),
        new SpecLimitShownEvaluator(),
        new RequiredMarkEvaluator(),
        new JudgmentExplicitEvaluator(),
        new UnitShownEvaluator(),
        new ButtonLabelQualityEvaluator(),
        new JudgmentOptionsEvaluator(),
        // C9 경계·부하 관리
        new SampleSectioningEvaluator(),
        new PageNumberFormatEvaluator(),
        new RoleExistsTypeEvaluator()
    };

    public IRuleEvaluator? Find(string checkName)
        => _byCheck.TryGetValue(checkName, out var e) ? e : null;

    /// <summary>Fail fast when the knowledge base references a check with no implementation.</summary>
    public void EnsureCoverage(RuleSet rules)
    {
        var missing = rules.Rules
            .Where(r => Find(r.Check) is null)
            .Select(r => $"{r.Id} → '{r.Check}'")
            .ToList();
        if (missing.Count > 0)
            throw new RuleLoadException(
                "구현되지 않은 check가 규칙에 참조되었습니다:\n  - " + string.Join("\n  - ", missing));
    }
}
