using HfeLayoutSim.Core.Model;

namespace HfeLayoutSim.App.ViewModels;

/// <summary>An enum value with the Korean label the UI shows for it.</summary>
public sealed record EnumOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Korean labels for the semantic vocabulary. These drive the property panel, where the operator
/// sets the meaning that half of the HFE rules are evaluated against (SPEC §5) — so every option
/// names the rule intent, not just the enum.
/// </summary>
public static class EnumCatalog
{
    public static IReadOnlyList<EnumOption<SemanticRole>> Roles { get; } = new[]
    {
        new EnumOption<SemanticRole>(SemanticRole.None, "지정 안 함"),
        new EnumOption<SemanticRole>(SemanticRole.Identification, "식별 정보 (품명·LOT)"),
        new EnumOption<SemanticRole>(SemanticRole.Precondition, "검사 조건 게이트"),
        new EnumOption<SemanticRole>(SemanticRole.SamplingInfo, "샘플링 정보"),
        new EnumOption<SemanticRole>(SemanticRole.InspectionItem, "검사 항목"),
        new EnumOption<SemanticRole>(SemanticRole.NonconformanceRecord, "부적합 기록"),
        new EnumOption<SemanticRole>(SemanticRole.FinalVerdict, "종합 판정"),
        new EnumOption<SemanticRole>(SemanticRole.Signature, "서명"),
        new EnumOption<SemanticRole>(SemanticRole.Warning, "경고·주의"),
        new EnumOption<SemanticRole>(SemanticRole.Reference, "참고·도해"),
        new EnumOption<SemanticRole>(SemanticRole.Navigation, "내비게이션"),
        new EnumOption<SemanticRole>(SemanticRole.Action, "행위 버튼"),
        new EnumOption<SemanticRole>(SemanticRole.Decoration, "장식·비정보"),
    };

    public static IReadOnlyList<EnumOption<InputKind>> InputKinds { get; } = new[]
    {
        new EnumOption<InputKind>(InputKind.None, "지정 안 함"),
        new EnumOption<InputKind>(InputKind.Check, "확인 체크 (안전 항목에는 금지)"),
        new EnumOption<InputKind>(InputKind.Transcribe, "전기 — 값을 옮겨 적음"),
        new EnumOption<InputKind>(InputKind.Auto, "자동 입력 (시스템 채움)"),
    };

    public static IReadOnlyList<EnumOption<ButtonKind>> ButtonKinds { get; } = new[]
    {
        new EnumOption<ButtonKind>(ButtonKind.Primary, "Primary — 주 행위"),
        new EnumOption<ButtonKind>(ButtonKind.Secondary, "Secondary — 보조"),
        new EnumOption<ButtonKind>(ButtonKind.Danger, "Danger — 파괴적"),
        new EnumOption<ButtonKind>(ButtonKind.Ghost, "Ghost — 최소 강조"),
    };

    public static string RoleLabel(SemanticRole role)
        => Roles.FirstOrDefault(r => r.Value == role)?.Label ?? role.ToString();

    public static string TypeLabel(ElementType type) => type switch
    {
        ElementType.Header => "제목",
        ElementType.Label => "라벨",
        ElementType.TextInput => "텍스트 기입란",
        ElementType.NumInput => "측정값 기입란",
        ElementType.Checkbox => "체크박스",
        ElementType.RadioGroup => "라디오 선택",
        ElementType.Table => "표",
        ElementType.JudgmentBadge => "판정 배지",
        ElementType.Button => "버튼",
        ElementType.SignatureBox => "서명란",
        ElementType.WarningBox => "경고 박스",
        ElementType.InfoBox => "안내 박스",
        ElementType.Image => "이미지·도해",
        ElementType.Barcode => "바코드",
        ElementType.Section => "섹션",
        ElementType.Divider => "구분선",
        ElementType.Stepper => "진행 스텝퍼",
        _ => type.ToString(),
    };
}
