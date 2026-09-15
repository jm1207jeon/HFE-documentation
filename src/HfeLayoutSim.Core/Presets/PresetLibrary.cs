using System.Text.Json;
using HfeLayoutSim.Core.Model;

namespace HfeLayoutSim.Core.Presets;

public sealed class PresetLoadException : Exception
{
    public PresetLoadException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Style defaults of a preset — pt/px variants resolved per medium at instantiation.</summary>
public sealed class PresetStyle
{
    public string? FontFamily { get; set; }
    public double? FontSizePt { get; set; }
    public double? FontSizePx { get; set; }
    public bool Bold { get; set; }
    public string? FgColor { get; set; }
    public string? BgColor { get; set; }
    public string? BorderColor { get; set; }
    public double BorderWidth { get; set; }
}

/// <summary>One customizable element preset from rules/element_presets.json.</summary>
public sealed class PresetDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ElementType ElementType { get; set; }
    public string? DefaultText { get; set; }

    /// <summary>"paper"/"screen" → [W,H] in native units. A missing medium means "not for this medium".</summary>
    public Dictionary<string, double[]> Size { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public PresetStyle? Style { get; set; }
    public Semantics? Semantics { get; set; }
    public IReadOnlyList<string> RecommendedZones { get; set; } = Array.Empty<string>();
    public string? Guidance { get; set; }

    // type-specific defaults
    public int? OptionCount { get; set; }
    public ButtonKind? ButtonKind { get; set; }
    public int? Rows { get; set; }
    public int? Columns { get; set; }
    public List<string>? ColumnRoles { get; set; }
    public string? SignerRole { get; set; }
    public string? DefaultUnit { get; set; }

    public bool SupportsMedium(Medium medium)
        => Size.ContainsKey(medium == Medium.Paper ? "paper" : "screen");
}

/// <summary>Loads the preset library and instantiates customized elements from it.</summary>
public sealed class PresetLibrary
{
    private readonly Dictionary<string, PresetDefinition> _presets;
    private int _instanceCounter;

    public string Version { get; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> IconSets { get; }

    public IReadOnlyCollection<PresetDefinition> All => _presets.Values;

    private PresetLibrary(string version, Dictionary<string, PresetDefinition> presets,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> iconSets)
    {
        Version = version;
        _presets = presets;
        IconSets = iconSets;
    }

    public static PresetLibrary LoadFile(string path)
    {
        try
        {
            return Load(File.ReadAllText(path), path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PresetLoadException($"프리셋 파일을 읽을 수 없습니다: {path} — {ex.Message}", ex);
        }
    }

    public static PresetLibrary Load(string json, string? sourceName = null)
    {
        var src = sourceName ?? "element_presets.json";
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            throw new PresetLoadException(
                $"{src}: JSON 구문 오류 (줄 {ex.LineNumber + 1}) — {ex.Message}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var presets = new Dictionary<string, PresetDefinition>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<string>();

            if (root.TryGetProperty("presets", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in arr.EnumerateArray())
                {
                    PresetDefinition? def;
                    try
                    {
                        def = p.Deserialize<PresetDefinition>(JsonDefaults.Options);
                    }
                    catch (JsonException ex)
                    {
                        errors.Add($"프리셋 파싱 실패: {ex.Message}");
                        continue;
                    }
                    if (def is null || string.IsNullOrWhiteSpace(def.Id))
                    {
                        errors.Add("id가 없는 프리셋이 있습니다.");
                        continue;
                    }
                    // System.Text.Json replaces the settable dictionary (and its ignore-case
                    // comparer) with a fresh case-sensitive one, which would hide "Paper".
                    def.Size = new Dictionary<string, double[]>(def.Size, StringComparer.OrdinalIgnoreCase);
                    if (!presets.TryAdd(def.Id, def))
                        errors.Add($"프리셋 id 중복: '{def.Id}'");
                    errors.AddRange(ValidateSize(def));
                }
            }
            else errors.Add("presets 배열 누락");

            var iconSets = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("iconSets", out var icons) && icons.ValueKind == JsonValueKind.Object)
            {
                foreach (var set in icons.EnumerateObject())
                {
                    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var item in set.Value.EnumerateObject())
                        if (item.Value.ValueKind == JsonValueKind.String)
                            map[item.Name] = item.Value.GetString()!;
                    iconSets[set.Name] = map;
                }
            }

            if (errors.Count > 0)
                throw new PresetLoadException($"{src}: 프리셋 로드 실패 —\n  - " + string.Join("\n  - ", errors));

            var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
            return new PresetLibrary(version, presets, iconSets);
        }
    }

    /// <summary>
    /// A size entry must be exactly [width, height], both positive and finite, under a known medium
    /// key. Without this a hand-added preset with "size": {"paper":[40]} places fine and then makes
    /// the coaching panel throw IndexOutOfRange for that element forever.
    /// </summary>
    private static IEnumerable<string> ValidateSize(PresetDefinition def)
    {
        if (def.Size.Count == 0)
        {
            yield return $"프리셋 '{def.Id}': size 누락";
            yield break;
        }

        foreach (var (medium, size) in def.Size)
        {
            if (!medium.Equals("paper", StringComparison.OrdinalIgnoreCase) &&
                !medium.Equals("screen", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"프리셋 '{def.Id}': size.{medium} 은 알 수 없는 매체입니다 (paper 또는 screen)";
                continue;
            }
            if (size is null || size.Length != 2)
            {
                yield return $"프리셋 '{def.Id}': size.{medium} 은 [폭, 높이] 두 값이어야 합니다";
                continue;
            }
            if (size.Any(v => !double.IsFinite(v) || v <= 0))
                yield return $"프리셋 '{def.Id}': size.{medium} 의 값은 0보다 큰 유한한 수여야 합니다";
        }
    }

    public PresetDefinition? Find(string presetId)
        => _presets.TryGetValue(presetId, out var p) ? p : null;

    /// <summary>Preset backing an element: explicit PresetId first, else best match by type + role.</summary>
    public PresetDefinition? FindFor(LayoutElement el)
    {
        if (el.PresetId is not null && _presets.TryGetValue(el.PresetId, out var exact))
            return exact;
        return _presets.Values.FirstOrDefault(p =>
                   p.ElementType == el.Type &&
                   (p.Semantics?.Role ?? SemanticRole.None) == el.Semantics.Role)
               ?? _presets.Values.FirstOrDefault(p => p.ElementType == el.Type);
    }

    /// <summary>Create a customized element from a preset at the given position.</summary>
    public LayoutElement Instantiate(string presetId, Medium medium, double x, double y,
        string? id = null, Action<LayoutElement>? customize = null)
    {
        var preset = Find(presetId)
            ?? throw new PresetLoadException($"프리셋 '{presetId}'가 라이브러리에 없습니다.");
        if (!preset.SupportsMedium(medium))
            throw new PresetLoadException($"프리셋 '{presetId}'는 {medium} 매체를 지원하지 않습니다.");

        var size = preset.Size[medium == Medium.Paper ? "paper" : "screen"];
        var el = new LayoutElement
        {
            Id = id ?? $"{presetId}-{++_instanceCounter}",
            PresetId = preset.Id,
            Type = preset.ElementType,
            X = x, Y = y,
            W = size[0], H = size.Length > 1 ? size[1] : size[0],
            Text = preset.DefaultText,
            OptionCount = preset.OptionCount,
            ButtonKind = preset.ButtonKind,
            Rows = preset.Rows,
            Columns = preset.Columns,
            ColumnRoles = preset.ColumnRoles?.ToList(),
            SignerRole = preset.SignerRole,
            Unit = preset.DefaultUnit,
            Style = ResolveStyle(preset.Style, medium),
            Semantics = Clone(preset.Semantics)
        };
        customize?.Invoke(el);
        return el;
    }

    private static ElementStyle ResolveStyle(PresetStyle? s, Medium medium)
    {
        if (s is null) return new ElementStyle();
        return new ElementStyle
        {
            FontFamily = s.FontFamily,
            FontSize = medium == Medium.Paper ? s.FontSizePt : s.FontSizePx,
            Bold = s.Bold,
            FgColor = s.FgColor,
            BgColor = s.BgColor,
            BorderColor = s.BorderColor,
            BorderWidth = s.BorderWidth
        };
    }

    private static Semantics Clone(Semantics? s)
    {
        if (s is null) return new Semantics();
        return new Semantics
        {
            Role = s.Role,
            Sequence = s.Sequence,
            GroupId = s.GroupId,
            IsCritical = s.IsCritical,
            IsRequired = s.IsRequired,
            IsDestructiveAction = s.IsDestructiveAction,
            InputKind = s.InputKind,
            SpecLimitShown = s.SpecLimitShown,
            Judgment = s.Judgment is null ? null : new JudgmentCoding
            {
                HasColor = s.Judgment.HasColor,
                HasIcon = s.Judgment.HasIcon,
                HasText = s.Judgment.HasText
            }
        };
    }
}
