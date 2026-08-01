using HfeLayoutSim.Core.Engine;
using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Presets;
using HfeLayoutSim.Core.Report;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Advisor;

public enum AdviceLevel
{
    Ok,
    Info,
    Warning,
    Critical
}

/// <summary>One concrete piece of placement coaching for a single element.</summary>
public sealed class AdviceItem
{
    public string ElementId { get; init; } = "";
    public AdviceLevel Level { get; init; }

    /// <summary>위치 / 색상 / 폰트 / 크기 / 아이콘 / 순서 / 그룹 / 밀도 / 오류방지 / 부하 / 스타일</summary>
    public string Topic { get; init; } = "";

    public string Message { get; init; } = "";
    public string? Suggestion { get; init; }
    public string? RuleId { get; init; }
}

/// <summary>
/// Interactive placement coach: given the current layout and one (newly placed) element,
/// says whether the position is fine, why another position is required, which font/color/
/// size/icon to use, and what error risk the current ordering carries.
/// The WPF editor calls <see cref="Advise"/> after every drop; the CLI walks all elements.
/// </summary>
public sealed class PlacementAdvisor
{
    private readonly RuleSet _rules;
    private readonly PresetLibrary? _presets;
    private readonly HfeEngine _engine = new();

    public PlacementAdvisor(RuleSet rules, PresetLibrary? presets = null)
    {
        _rules = rules;
        _presets = presets;
    }

    /// <summary>Coaching for one element in the context of the whole current layout.</summary>
    public IReadOnlyList<AdviceItem> Advise(Layout layout, string elementId)
    {
        var report = _engine.Evaluate(layout, _rules);
        return AdviseFromReport(layout, report, elementId);
    }

    /// <summary>Coaching for every element (placement order), reusing a single evaluation pass.</summary>
    public IReadOnlyList<(LayoutElement Element, IReadOnlyList<AdviceItem> Advice)> AdviseAll(Layout layout)
    {
        var report = _engine.Evaluate(layout, _rules);
        var ordered = layout.Elements
            .OrderBy(e => e.Semantics.Sequence >= 1 ? e.Semantics.Sequence : int.MaxValue)
            .ThenBy(e => e.Y).ThenBy(e => e.X)
            .ToList();
        return ordered
            .Select(e => (e, AdviseFromReport(layout, report, e.Id)))
            .ToList();
    }

    private IReadOnlyList<AdviceItem> AdviseFromReport(Layout layout, EvaluationReport report, string elementId)
    {
        var element = layout.Elements.FirstOrDefault(e => e.Id == elementId)
            ?? throw new ArgumentException($"요소 '{elementId}'가 레이아웃에 없습니다.");
        var ctx = new EvaluationContext(layout, _rules);
        var items = new List<AdviceItem>();

        // 1) Rule findings touching this element — the authoritative "why not here / what's wrong".
        foreach (var f in report.Findings.Where(f => f.ElementIds.Contains(elementId)))
        {
            items.Add(new AdviceItem
            {
                ElementId = elementId,
                Level = f.Severity switch
                {
                    RuleSeverity.Critical => AdviceLevel.Critical,
                    RuleSeverity.Major => AdviceLevel.Warning,
                    _ => AdviceLevel.Info
                },
                Topic = TopicOf(f.Category),
                Message = f.Message,
                Suggestion = f.Recommendation,
                RuleId = f.RuleId
            });
        }

        // 2) Preset-driven prescriptions (recommended zone, font, color, size, icon).
        var preset = _presets?.FindFor(element);
        if (preset is not null)
            items.AddRange(PresetAdvice(element, preset, ctx, items));

        if (items.Count == 0)
        {
            items.Add(new AdviceItem
            {
                ElementId = elementId,
                Level = AdviceLevel.Ok,
                Topic = "종합",
                Message = "현재 위치·서식 적절 — 관련 규칙 위반 없음.",
                Suggestion = preset?.Guidance
            });
        }
        return items;
    }

    private IEnumerable<AdviceItem> PresetAdvice(LayoutElement el, PresetDefinition preset,
        EvaluationContext ctx, IReadOnlyList<AdviceItem> existing)
    {
        // Zone recommendation — skip when a C4 placement rule already flagged this element.
        var hasPlacementFinding = existing.Any(i => i.RuleId?.StartsWith("C4") == true);
        if (!hasPlacementFinding && preset.RecommendedZones.Count > 0)
        {
            var zone = ctx.ZoneOf(el).ToString();
            if (!preset.RecommendedZones.Contains(zone, StringComparer.OrdinalIgnoreCase))
            {
                var target = preset.RecommendedZones[0];
                var (sx, sy) = SuggestPosition(target, ctx);
                yield return new AdviceItem
                {
                    ElementId = el.Id,
                    Level = AdviceLevel.Warning,
                    Topic = "위치",
                    Message = $"현재 {zone} 구역 — '{preset.Name}'의 권장 구역은 {string.Join("/", preset.RecommendedZones)}.",
                    Suggestion = $"{ZoneKorean(target)} 방향으로 이동 (예: x≈{ctx.FormatLen(sx)}, y≈{ctx.FormatLen(sy)}). {preset.Guidance}"
                };
            }
        }

        var style = preset.Style;
        if (style is not null)
        {
            if (!string.IsNullOrEmpty(style.FontFamily) &&
                !string.Equals(el.Style.FontFamily, style.FontFamily, StringComparison.OrdinalIgnoreCase))
                yield return Item(el, AdviceLevel.Warning, "폰트",
                    $"서체 '{el.Style.FontFamily ?? "기본"}' 사용 중 — 이 요소는 등폭 서체가 원칙.",
                    $"'{style.FontFamily}' 적용 (자릿수 정렬·오독 방지).");

            var presetSize = ctx.Medium == Medium.Paper ? style.FontSizePt : style.FontSizePx;
            var unit = ctx.Medium == Medium.Paper ? "pt" : "px";
            if (presetSize is not null && el.Style.FontSize is not null && el.Style.FontSize < presetSize)
                yield return Item(el, AdviceLevel.Info, "크기",
                    $"폰트 {el.Style.FontSize:0.#}{unit} — 프리셋 기준 {presetSize:0.#}{unit}보다 작음.",
                    $"{presetSize:0.#}{unit} 이상 사용.");

            if (!string.IsNullOrEmpty(style.FgColor) && el.Style.FgColor is not null &&
                !ColorUtil.SameColor(el.Style.FgColor, style.FgColor))
                yield return Item(el, AdviceLevel.Info, "색상",
                    $"전경색 {el.Style.FgColor} — 검증 색상 아님/프리셋과 다름.",
                    $"검증 색 {style.FgColor} 사용 (rules JSON palette 대비율 검증 완료).");

            if (style.Bold && !el.Style.Bold)
                yield return Item(el, AdviceLevel.Info, "스타일",
                    "굵기 보통 — 이 요소는 Bold가 기준.", "Bold 적용 (이탤릭·밑줄 강조는 금지).");
        }

        // Judgment icon coaching from the icon set data.
        if (el.Type == ElementType.JudgmentBadge &&
            (el.Semantics.Judgment is null || !el.Semantics.Judgment.HasIcon) &&
            _presets!.IconSets.TryGetValue("judgment", out var icons))
        {
            yield return Item(el, AdviceLevel.Warning, "아이콘",
                "판정 배지에 아이콘 코딩 없음 — 색·텍스트만으로는 흑백 인쇄에서 구분 불가.",
                $"적합 {icons.GetValueOrDefault("pass")}({icons.GetValueOrDefault("passBw")}) / " +
                $"부적합 {icons.GetValueOrDefault("fail")}({icons.GetValueOrDefault("failBw")}) / " +
                $"보류 {icons.GetValueOrDefault("hold")}({icons.GetValueOrDefault("holdBw")}) 병기.");
        }

        // Undersized vs the preset's proven default.
        if (preset.SupportsMedium(ctx.Medium))
        {
            var size = preset.Size[ctx.Medium == Medium.Paper ? "paper" : "screen"];
            var (w, h) = el.OrientedSize;
            if (w < size[0] * 0.8 || h < size[1] * 0.8)
                yield return Item(el, AdviceLevel.Info, "크기",
                    $"크기 {ctx.FormatLen(w)}×{ctx.FormatLen(h)} — 프리셋 기본 {ctx.FormatLen(size[0])}×{ctx.FormatLen(size[1])}보다 작음.",
                    "기본 크기 이상 확보 (판독성·조작성).");
        }
    }

    private static AdviceItem Item(LayoutElement el, AdviceLevel level, string topic,
        string message, string suggestion) => new()
    {
        ElementId = el.Id,
        Level = level,
        Topic = topic,
        Message = message,
        Suggestion = suggestion
    };

    /// <summary>Representative coordinates inside a target zone for a concrete "move here" hint.</summary>
    private static (double X, double Y) SuggestPosition(string zone, EvaluationContext ctx)
    {
        var c = ctx.ContentRect;
        return zone.ToUpperInvariant() switch
        {
            "POA" => (c.X + c.W * 0.05, c.Y + c.H * 0.05),
            "SFA" => (c.X + c.W * 0.60, c.Y + c.H * 0.05),
            "WFA" => (c.X + c.W * 0.05, c.Y + c.H * 0.75),
            _ => (c.X + c.W * 0.60, c.Y + c.H * 0.80) // TA
        };
    }

    private static string ZoneKorean(string zone) => zone.ToUpperInvariant() switch
    {
        "POA" => "좌상단(POA)",
        "SFA" => "우상단(SFA)",
        "WFA" => "좌하단(WFA)",
        _ => "우하단(TA)"
    };

    private static string TopicOf(string category) => category switch
    {
        "C1" => "색상",
        "C2" => "폰트",
        "C3" => "크기",
        "C4" => "위치",
        "C5" => "그룹",
        "C6" => "밀도",
        "C7" => "순서",
        "C8" => "오류방지",
        "C9" => "부하",
        _ => "일반"
    };
}
