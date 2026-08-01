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
        => Params.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;

    public int GetInt(string name, int fallback = 0)
        => Params.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : fallback;

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
}
