using System.Text.Json;

namespace HfeLayoutSim.Core.Model;

public sealed class LayoutFormatException : Exception
{
    public LayoutFormatException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Load/save of *.hfelayout.json with structural validation.</summary>
public static class LayoutSerializer
{
    public static Layout LoadFile(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LayoutFormatException($"레이아웃 파일을 읽을 수 없습니다: {path} — {ex.Message}", ex);
        }

        return Load(json, path);
    }

    public static Layout Load(string json, string? sourceName = null)
    {
        Layout? layout;
        try
        {
            layout = JsonSerializer.Deserialize<Layout>(json, JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            throw new LayoutFormatException(
                $"{sourceName ?? "layout"}: JSON 구문 오류 (줄 {ex.LineNumber + 1}, 위치 {ex.BytePositionInLine}) — {ex.Message}", ex);
        }

        if (layout is null)
            throw new LayoutFormatException($"{sourceName ?? "layout"}: 빈 문서입니다.");

        Normalize(layout);
        Validate(layout, sourceName);
        return layout;
    }

    public static string Save(Layout layout) => JsonSerializer.Serialize(layout, JsonDefaults.Options);

    /// <summary>Atomic save — a crash or full disk mid-write must not destroy the previous file.</summary>
    public static void SaveFile(Layout layout, string path) => AtomicFile.WriteAllText(path, Save(layout));

    /// <summary>
    /// System.Text.Json overwrites a property initializer with null when the JSON says
    /// "style": null, so a hand-edited file could load "successfully" and then throw
    /// NullReferenceException deep inside the engine. Restore the empty defaults first; anything
    /// structural that cannot be defaulted is reported by <see cref="Validate"/>.
    /// </summary>
    private static void Normalize(Layout layout)
    {
        layout.Meta ??= new LayoutMeta();
        layout.Canvas ??= new CanvasSpec();
        layout.Elements ??= new List<LayoutElement>();
        if (string.IsNullOrWhiteSpace(layout.Canvas.Background)) layout.Canvas.Background = "#FFFFFF";

        foreach (var el in layout.Elements)
        {
            if (el is null) continue; // reported by Validate
            el.Style ??= new ElementStyle();
            el.Semantics ??= new Semantics();
        }
    }

    public static void Validate(Layout layout, string? sourceName = null)
    {
        var errors = new List<string>();

        if (layout.Canvas is null || layout.Elements is null || layout.Meta is null)
        {
            throw new LayoutFormatException(
                $"{sourceName ?? "layout"}: meta/canvas/elements 중 필수 항목이 null 입니다.");
        }

        if (!double.IsFinite(layout.Canvas.Width) || !double.IsFinite(layout.Canvas.Height) ||
            layout.Canvas.Width <= 0 || layout.Canvas.Height <= 0)
            errors.Add($"canvas 크기가 유효하지 않습니다: {layout.Canvas.Width}×{layout.Canvas.Height}");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < layout.Elements.Count; i++)
        {
            if (layout.Elements[i] is null)
            {
                errors.Add($"elements[{i}] 가 null 입니다.");
                continue;
            }
        }
        if (errors.Count > 0)
            throw new LayoutFormatException(
                $"{sourceName ?? "layout"}: 레이아웃 검증 실패 —\n  - " + string.Join("\n  - ", errors));

        foreach (var el in layout.Elements)
        {
            if (el.Style is null || el.Semantics is null)
            {
                errors.Add($"요소 '{el.Id}' 의 style/semantics 가 null 입니다.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(el.Id))
                errors.Add($"요소 id가 비어 있습니다 (type={el.Type}).");
            else if (!seen.Add(el.Id))
                errors.Add($"요소 id 중복: '{el.Id}'");

            if (!double.IsFinite(el.X) || !double.IsFinite(el.Y))
                errors.Add($"요소 '{el.Id}' 좌표가 유효하지 않습니다: {el.X}, {el.Y}");

            if (!double.IsFinite(el.W) || !double.IsFinite(el.H) || el.W <= 0 || el.H <= 0)
                errors.Add($"요소 '{el.Id}' 크기가 유효하지 않습니다: {el.W}×{el.H}");

            if (el.Rotation is not (0 or 90))
                errors.Add($"요소 '{el.Id}' rotation은 0 또는 90만 허용됩니다: {el.Rotation}");

            if (layout.Medium == Medium.Paper && el.Type is ElementType.Button or ElementType.Stepper)
                errors.Add($"요소 '{el.Id}' ({el.Type})는 Screen 전용 요소입니다.");
        }

        if (errors.Count > 0)
            throw new LayoutFormatException(
                $"{sourceName ?? "layout"}: 레이아웃 검증 실패 —\n  - " + string.Join("\n  - ", errors));
    }
}
