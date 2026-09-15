using System.Text.Json;

namespace HfeLayoutSim.Core.Rules;

public sealed class RuleLoadException : Exception
{
    public RuleLoadException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Parses rules/hfe_rules.json. Fails loudly with rule id / json position so a broken
/// knowledge base never silently degrades scoring (SPEC §7).
/// </summary>
public static class RuleLoader
{
    public static RuleSet LoadFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RuleLoadException($"규칙 파일을 읽을 수 없습니다: {path} — {ex.Message}", ex);
        }

        return Load(json, path);
    }

    public static RuleSet Load(string json, string? sourceName = null)
    {
        var src = sourceName ?? "hfe_rules.json";
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            throw new RuleLoadException(
                $"{src}: JSON 구문 오류 (줄 {ex.LineNumber + 1}, 위치 {ex.BytePositionInLine}) — {ex.Message}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var errors = new List<string>();

            var penalties = new Dictionary<RuleSeverity, int>();
            if (root.TryGetProperty("penalties", out var pen) && pen.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in pen.EnumerateObject())
                {
                    if (!Enum.TryParse<RuleSeverity>(p.Name, ignoreCase: true, out var sev))
                        errors.Add($"penalties.{p.Name}: 알 수 없는 심각도");
                    else if (p.Value.ValueKind != JsonValueKind.Number || !p.Value.TryGetInt32(out var penalty))
                        errors.Add($"penalties.{p.Name}: 0 이상의 정수여야 합니다 (현재 {p.Value})");
                    else if (penalty < 0)
                        errors.Add($"penalties.{p.Name}={penalty}: 음수 감점은 허용되지 않습니다");
                    else
                        penalties[sev] = penalty;
                }
            }
            else errors.Add("penalties 섹션 누락");

            var grades = ReadNumberMap(root, "grades");
            var weights = ReadNumberMap(root, "weights");
            if (weights.Count == 0) errors.Add("weights 섹션 누락");
            else
            {
                var sum = weights.Values.Sum();
                if (Math.Abs(sum - 1.0) > 0.001)
                    errors.Add($"weights 합계가 1.0이 아닙니다: {sum:0.###}");
            }

            var palette = new Dictionary<string, PaletteColor>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("palette", out var pal) && pal.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in pal.EnumerateObject())
                {
                    if (p.Value.ValueKind != JsonValueKind.Object || !p.Value.TryGetProperty("hex", out var hex))
                        continue; // skip notes and non-color entries
                    palette[p.Name] = new PaletteColor
                    {
                        Hex = hex.ValueKind == JsonValueKind.String ? hex.GetString() ?? "" : "",
                        CrOnWhite = p.Value.TryGetProperty("crOnWhite", out var cr) && cr.ValueKind == JsonValueKind.Number
                            ? cr.GetDouble() : null,
                        Bg = Str(p.Value, "bg")
                    };
                }
            }

            var rules = new List<RuleDefinition>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("rules", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var r in arr.EnumerateArray())
                {
                    var id = Str(r, "id") ?? "";
                    var where = string.IsNullOrEmpty(id) ? $"rules[{index}]" : $"규칙 {id}";

                    if (string.IsNullOrEmpty(id)) errors.Add($"{where}: id 누락");
                    else if (!ids.Add(id)) errors.Add($"{where}: id 중복");

                    var severityStr = Str(r, "severity");
                    if (!Enum.TryParse<RuleSeverity>(severityStr, ignoreCase: true, out var severity))
                        errors.Add($"{where}: severity '{severityStr}' 인식 불가");

                    var appliesStr = Str(r, "appliesTo") ?? "Both";
                    if (!Enum.TryParse<RuleApplicability>(appliesStr, ignoreCase: true, out var applies))
                        errors.Add($"{where}: appliesTo '{appliesStr}' 인식 불가");

                    var check = Str(r, "check") ?? "";
                    if (string.IsNullOrEmpty(check)) errors.Add($"{where}: check 누락");

                    var category = Str(r, "category") ?? "";
                    if (string.IsNullOrEmpty(category)) errors.Add($"{where}: category 누락");
                    else if (weights.Count > 0 && !weights.ContainsKey(category))
                        errors.Add($"{where}: category '{category}'의 weight 누락");

                    var prms = new Dictionary<string, JsonElement>();
                    if (r.TryGetProperty("params", out var prmEl) && prmEl.ValueKind == JsonValueKind.Object)
                        foreach (var p in prmEl.EnumerateObject())
                            prms[p.Name] = p.Value.Clone();

                    rules.Add(new RuleDefinition
                    {
                        Id = id,
                        Category = category,
                        AppliesTo = applies,
                        Severity = severity,
                        HighlightOnPass = r.TryGetProperty("highlightOnPass", out var hl) &&
                                          hl.ValueKind == JsonValueKind.True,
                        Title = Str(r, "title") ?? "",
                        Check = check,
                        Message = Str(r, "message") ?? "",
                        Recommendation = Str(r, "recommendation") ?? "",
                        Ref = Str(r, "ref") ?? "",
                        Params = prms
                    });
                    index++;
                }
            }
            else errors.Add("rules 배열 누락");

            if (errors.Count > 0)
                throw new RuleLoadException($"{src}: 규칙 로드 실패 —\n  - " + string.Join("\n  - ", errors));

            return new RuleSet
            {
                Version = Str(root, "version") ?? "",
                Penalties = penalties,
                CriticalElementSeverityEscalation =
                    root.TryGetProperty("criticalElementSeverityEscalation", out var esc) &&
                    esc.ValueKind == JsonValueKind.True,
                Grades = grades,
                CriticalViolationGradeCap = Str(root, "criticalViolationGradeCap"),
                PassGrade = Str(root, "passGrade") ?? "C",
                Weights = weights,
                Palette = palette,
                Rules = rules
            };
        }
    }

    /// <summary>Reads a string property without throwing when the JSON holds a number/bool there.</summary>
    private static string? Str(JsonElement owner, string name)
        => owner.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static Dictionary<string, double> ReadNumberMap(JsonElement root, string prop)
    {
        var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.Object)
            foreach (var p in el.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Number &&
                    p.Value.TryGetDouble(out var value) && double.IsFinite(value))
                    map[p.Name] = value;
        return map;
    }
}
