using System.Text.Json;
using HfeLayoutSim.Core.Model;

namespace HfeLayoutSim.Core.Catalog;

public sealed class CatalogLoadException : Exception
{
    public CatalogLoadException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>An alarm/caution the user wants surfaced on the form or screen.</summary>
public sealed class CatalogAlarm
{
    public string Text { get; set; } = "";

    /// <summary>warning | info</summary>
    public string Level { get; set; } = "warning";

    /// <summary>"danger" alarms are hazard warnings, not advisories: they get the critical palette
    /// and mark the box safety-related so findings against it escalate.</summary>
    public bool IsDanger => string.Equals(Level, "danger", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One user-defined inspection item.</summary>
public sealed class CatalogItem
{
    public string Name { get; set; } = "";

    /// <summary>measure(측정값 기입) | check(확인 체크) | text(자유 기입)</summary>
    public string Kind { get; set; } = "measure";

    /// <summary>Spec/criterion shown next to the input (예: "1.20±0.05").</summary>
    public string? Criterion { get; set; }

    public string? Unit { get; set; }

    /// <summary>Safety-related item — composer renders it as a transcription input, never a checkbox.</summary>
    public bool Critical { get; set; }

    /// <summary>Reference image (path/name) to place next to the item block.</summary>
    public string? Image { get; set; }

    public CatalogAlarm? Alarm { get; set; }
}

/// <summary>One inspection process step (공정), ordered.</summary>
public sealed class CatalogStep
{
    public string Name { get; set; } = "";
    public int Order { get; set; }
    public List<CatalogItem> Items { get; set; } = new();
}

/// <summary>A precondition gate entry (계측기 교정 확인 등).</summary>
public sealed class CatalogPrecondition
{
    public string Text { get; set; } = "";

    /// <summary>transcribe → 기입란(전기), check → 체크박스.</summary>
    public InputKind InputKind { get; set; } = InputKind.Check;

    /// <summary>Label for transcribe inputs (예: "계측기 관리번호").</summary>
    public string? Label { get; set; }
}

/// <summary>
/// User-authored definition of an inspection document: process name, step order, items,
/// criteria, images, alarms, signers. The composer turns this into a standards-compliant layout.
/// </summary>
public sealed class InspectionCatalog
{
    public string ProcessName { get; set; } = "";
    public string? DocumentNo { get; set; }

    /// <summary>Identification fields (기본: 품명 / LOT No. / 입고일).</summary>
    public List<string> IdentificationFields { get; set; } = new();

    public List<CatalogPrecondition> Preconditions { get; set; } = new();
    public string? SamplingNote { get; set; }
    public List<CatalogStep> Steps { get; set; } = new();

    /// <summary>Document-level alarms shown near the top.</summary>
    public List<CatalogAlarm> Alarms { get; set; } = new();

    /// <summary>Signer roles in signing order (기본: 검사자 / 승인자).</summary>
    public List<string> Signers { get; set; } = new();

    public static InspectionCatalog LoadFile(string path)
    {
        try
        {
            return Load(File.ReadAllText(path), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CatalogLoadException($"카탈로그 파일을 읽을 수 없습니다: {path} — {ex.Message}", ex);
        }
    }

    public static InspectionCatalog Load(string json, string? sourceName = null)
    {
        var src = sourceName ?? "catalog.json";
        InspectionCatalog? catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<InspectionCatalog>(json, JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            throw new CatalogLoadException(
                $"{src}: JSON 구문 오류 (줄 {ex.LineNumber + 1}) — {ex.Message}", ex);
        }
        if (catalog is null) throw new CatalogLoadException($"{src}: 빈 문서입니다.");

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(catalog.ProcessName)) errors.Add("processName 누락");
        if (catalog.Steps.Count == 0) errors.Add("steps가 비어 있습니다 — 검사 공정을 1개 이상 정의하십시오.");
        foreach (var step in catalog.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Name)) errors.Add("이름 없는 step이 있습니다.");
            if (step.Items.Count == 0) errors.Add($"step '{step.Name}': items가 비어 있습니다.");
            foreach (var item in step.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Name)) errors.Add($"step '{step.Name}': 이름 없는 item");
                if (item.Kind is not ("measure" or "check" or "text"))
                    errors.Add($"item '{item.Name}': kind는 measure|check|text 중 하나여야 합니다 ('{item.Kind}').");
                if (item.Kind == "measure" && string.IsNullOrWhiteSpace(item.Unit))
                    errors.Add($"item '{item.Name}': 측정 항목에는 unit이 필요합니다 (단위 혼동 방지).");
                if (item.Alarm is not null) errors.AddRange(AlarmErrors(item.Alarm, $"item '{item.Name}'"));
            }
        }
        foreach (var alarm in catalog.Alarms) errors.AddRange(AlarmErrors(alarm, "alarms"));

        var dupOrders = catalog.Steps.GroupBy(s => s.Order).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupOrders.Count > 0) errors.Add($"step order 중복: {string.Join(", ", dupOrders)}");

        if (errors.Count > 0)
            throw new CatalogLoadException($"{src}: 카탈로그 검증 실패 —\n  - " + string.Join("\n  - ", errors));

        catalog.Steps = catalog.Steps.OrderBy(s => s.Order).ToList();
        if (catalog.IdentificationFields.Count == 0)
            catalog.IdentificationFields = new List<string> { "품명", "LOT No.", "입고일" };
        if (catalog.Signers.Count == 0)
            catalog.Signers = new List<string> { "검사자", "승인자" };
        return catalog;
    }

    private static IEnumerable<string> AlarmErrors(CatalogAlarm alarm, string where)
    {
        if (string.IsNullOrWhiteSpace(alarm.Text))
            yield return $"{where}: alarm.text가 비어 있습니다.";
        if (alarm.Level is not ("warning" or "danger"))
            yield return $"{where}: alarm.level은 warning|danger 중 하나여야 합니다 ('{alarm.Level}').";
    }
}
