using System.Text.Json;

namespace HfeLayoutSim.Core.Rules;

public enum RuleSeverity
{
    Minor,
    Major,
    Critical
}

public enum RuleApplicability
{
    Paper,
    Screen,
    Both
}

/// <summary>
/// One scoring rule from rules/hfe_rules.json. All thresholds live in <see cref="Params"/> —
/// evaluators must read them from here, never hardcode (CLAUDE.md principle #1).
/// </summary>
public sealed class RuleDefinition
{
    public string Id { get; init; } = "";
    public string Category { get; init; } = "";
    public RuleApplicability AppliesTo { get; init; } = RuleApplicability.Both;
    public RuleSeverity Severity { get; init; }
    public bool HighlightOnPass { get; init; }
    public string Title { get; init; } = "";
    public string Check { get; init; } = "";
    public string Message { get; init; } = "";
    public string Recommendation { get; init; } = "";
    public string Ref { get; init; } = "";

    public IReadOnlyDictionary<string, JsonElement> Params { get; init; } =
        new Dictionary<string, JsonElement>();

    // ---- typed parameter accessors ----

    public bool Has(string name) => Params.ContainsKey(name);

    public double GetDouble(string name, double fallback = 0)
        => Params.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.Number &&
           v.TryGetDouble(out var d) && double.IsFinite(d) ? d : fallback;

    /// <summary>
    /// Integer parameter. A fractional value (e.g. "maxRun": 4.5) must not throw here — the
    /// matching Validate() call reports it at load time, naming the rule, so the evaluator never
    /// sees it in practice.
    /// </summary>
    public int GetInt(string name, int fallback = 0)
        => Params.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.Number &&
           v.TryGetInt32(out var i) ? i : fallback;

    public bool GetBool(string name, bool fallback = false)
        => Params.TryGetValue(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : fallback;

    public string? GetString(string name)
        => Params.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public IReadOnlyList<string> GetStringList(string name)
    {
        if (!Params.TryGetValue(name, out var v)) return Array.Empty<string>();
        if (v.ValueKind == JsonValueKind.String) return new[] { v.GetString()! };
        if (v.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        var list = new List<string>();
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String)
                list.Add(item.GetString()!);
        return list;
    }

    public IReadOnlyList<double> GetDoubleList(string name)
    {
        if (!Params.TryGetValue(name, out var v) || v.ValueKind != JsonValueKind.Array)
            return Array.Empty<double>();
        var list = new List<double>();
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Number)
                list.Add(item.GetDouble());
        return list;
    }

    /// <summary>Nested object accessor, e.g. paperMm.top for C6-05.</summary>
    public double GetNestedDouble(string objName, string propName, double fallback = 0)
    {
        if (Params.TryGetValue(objName, out var v) && v.ValueKind == JsonValueKind.Object &&
            v.TryGetProperty(propName, out var p) && p.ValueKind == JsonValueKind.Number)
            return p.GetDouble();
        return fallback;
    }

    // ---- enum / regex parameters ----
    // The knowledge base is hand-edited, so a typo like "type": "Checkbo" must never crash the
    // engine mid-evaluation. Evaluators read enums through these (which fall back), and declare
    // the same parameters in Validate() so a bad file is rejected at load with the rule id.

    /// <summary>Enum parameter; returns <paramref name="fallback"/> when absent or unparsable.</summary>
    public T GetEnum<T>(string name, T fallback) where T : struct, Enum
        => Enum.TryParse<T>(GetString(name), ignoreCase: true, out var v) ? v : fallback;

    /// <summary>Enum list parameter; unparsable entries are skipped (Validate reports them).</summary>
    public IReadOnlyList<T> GetEnumList<T>(string name) where T : struct, Enum
    {
        var list = new List<T>();
        foreach (var s in GetStringList(name))
            if (Enum.TryParse<T>(s, ignoreCase: true, out var v))
                list.Add(v);
        return list;
    }

    /// <summary>Validation messages for a single-value enum parameter (empty when it is fine).</summary>
    public IEnumerable<string> ValidateEnum<T>(string name, bool required = false) where T : struct, Enum
    {
        var raw = GetString(name);
        if (string.IsNullOrEmpty(raw))
        {
            if (required) yield return $"{Id}: params.{name} 누락 (필수)";
            yield break;
        }
        if (!Enum.TryParse<T>(raw, ignoreCase: true, out _))
            yield return $"{Id}: params.{name}='{raw}' 는 {typeof(T).Name} 값이 아닙니다 " +
                         $"(허용: {string.Join(", ", Enum.GetNames<T>())})";
    }

    /// <summary>Validation errors for an enum list parameter.</summary>
    public IEnumerable<string> ValidateEnumList<T>(string name, bool required = false) where T : struct, Enum
    {
        var values = GetStringList(name);
        if (values.Count == 0)
        {
            if (required) yield return $"{Id}: params.{name} 누락 (필수)";
            yield break;
        }
        foreach (var s in values)
            if (!Enum.TryParse<T>(s, ignoreCase: true, out _))
                yield return $"{Id}: params.{name} 의 '{s}' 는 {typeof(T).Name} 값이 아닙니다 " +
                             $"(허용: {string.Join(", ", Enum.GetNames<T>())})";
    }

    /// <summary>
    /// Validation messages for an integer parameter: present, numeric, whole, and within
    /// <paramref name="min"/>..<paramref name="max"/>. Thresholds like "max": 0 are not merely odd —
    /// they make the composer chunk into 2.1 billion groups and the checkbox-run rule fire on every
    /// element — so they are rejected with the rule id instead of degrading silently.
    /// </summary>
    public IEnumerable<string> ValidateInt(string name, bool required = false,
        int min = int.MinValue, int max = int.MaxValue)
    {
        if (!Params.TryGetValue(name, out var v))
        {
            if (required) yield return $"{Id}: params.{name} 누락 (필수)";
            yield break;
        }
        if (v.ValueKind != JsonValueKind.Number)
        {
            yield return $"{Id}: params.{name} 은 숫자여야 합니다 (현재 {v.ValueKind})";
            yield break;
        }
        if (!v.TryGetInt32(out var value))
        {
            yield return $"{Id}: params.{name}={v} 는 정수가 아닙니다";
            yield break;
        }
        if (value < min || value > max)
            yield return $"{Id}: params.{name}={value} 는 허용 범위({min}~{max})를 벗어납니다";
    }

    /// <summary>Validation messages for a real-number parameter.</summary>
    public IEnumerable<string> ValidateNumber(string name, bool required = false,
        double min = double.NegativeInfinity, double max = double.PositiveInfinity)
    {
        if (!Params.TryGetValue(name, out var v))
        {
            if (required) yield return $"{Id}: params.{name} 누락 (필수)";
            yield break;
        }
        if (v.ValueKind != JsonValueKind.Number)
        {
            yield return $"{Id}: params.{name} 은 숫자여야 합니다 (현재 {v.ValueKind})";
            yield break;
        }
        if (!v.TryGetDouble(out var value) || !double.IsFinite(value))
        {
            yield return $"{Id}: params.{name}={v} 는 유효한 수가 아닙니다";
            yield break;
        }
        if (value < min || value > max)
            yield return $"{Id}: params.{name}={value} 는 허용 범위({min}~{max})를 벗어납니다";
    }

    /// <summary>Validation messages for a regex parameter (empty when it compiles).</summary>
    public IEnumerable<string> ValidateRegex(string name)
    {
        var pattern = GetString(name);
        if (string.IsNullOrEmpty(pattern)) yield break;
        string? error = null;
        try { _ = new System.Text.RegularExpressions.Regex(pattern); }
        catch (ArgumentException ex) { error = $"{Id}: params.{name} 정규식 오류 — {ex.Message}"; }
        if (error is not null) yield return error;
    }

    /// <summary>Compile a regex parameter, falling back when the pattern is invalid (Validate reports it).</summary>
    public System.Text.RegularExpressions.Regex GetRegex(string name, string fallbackPattern)
    {
        var pattern = GetString(name);
        if (!string.IsNullOrEmpty(pattern))
        {
            try
            {
                return new System.Text.RegularExpressions.Regex(
                    pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                    TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException) { /* fall through to the safe default */ }
        }
        return new System.Text.RegularExpressions.Regex(
            fallbackPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    }
}
