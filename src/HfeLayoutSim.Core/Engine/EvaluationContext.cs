using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Engine;

/// <summary>Gutenberg quadrants of the content area (SPEC §6.1).</summary>
public enum Zone
{
    /// <summary>Primary Optical Area — top-left.</summary>
    POA,
    /// <summary>Strong Fallow Area — top-right.</summary>
    SFA,
    /// <summary>Weak Fallow Area — bottom-left (blind spot).</summary>
    WFA,
    /// <summary>Terminal Area — bottom-right (gaze endpoint).</summary>
    TA
}

/// <summary>
/// Shared per-evaluation computed state: content bounds, zones, reading order,
/// effective backgrounds, element groupings. Pure — no UI, no I/O.
/// </summary>
public sealed class EvaluationContext
{
    public Layout Layout { get; }
    public RuleSet Rules { get; }
    public Medium Medium => Layout.Medium;

    /// <summary>Row tolerance for "same row" decisions, in native units (5mm / 20px).</summary>
    public double RowTolerance { get; }

    /// <summary>Content area: inside print margins (Paper) or minus fixed chrome regions (Screen).</summary>
    public Rect ContentRect { get; }

    private readonly Dictionary<LayoutElement, int> _paintIndex;
    private readonly List<LayoutElement> _readingOrder;
    private readonly Dictionary<string, int> _readingIndex;

    public EvaluationContext(Layout layout, RuleSet rules)
    {
        Layout = layout;
        Rules = rules;
        RowTolerance = layout.Medium == Medium.Paper ? 5.0 : 20.0;
        ContentRect = ComputeContentRect(layout);
        // LayoutElement does not override Equals, so the default comparer is reference identity —
        // exactly what paint order needs (two elements may legitimately be value-identical).
        _paintIndex = new Dictionary<LayoutElement, int>(layout.Elements.Count);
        for (var i = 0; i < layout.Elements.Count; i++) _paintIndex[layout.Elements[i]] = i;

        _readingOrder = ComputeReadingOrder(layout.Elements, RowTolerance);
        _readingIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < _readingOrder.Count; i++)
            _readingIndex[_readingOrder[i].Id] = i;
    }

    private static Rect ComputeContentRect(Layout layout)
    {
        var c = layout.Canvas;
        if (layout.Medium == Medium.Paper)
        {
            var m = c.Margins;
            if (m is null) return new Rect(0, 0, c.Width, c.Height);
            return new Rect(m.Left, m.Top, c.Width - m.Left - m.Right, c.Height - m.Top - m.Bottom);
        }

        var f = c.FixedRegions;
        if (f is null) return new Rect(0, 0, c.Width, c.Height);
        var x = f.GnbPx;
        var y = f.HeaderPx + f.ContextBarPx;
        return new Rect(x, y, c.Width - x, c.Height - y - f.ActionBarPx);
    }

    private static List<LayoutElement> ComputeReadingOrder(IEnumerable<LayoutElement> elements, double rowTol)
    {
        // Row-major: cluster into rows by center-Y, then left→right within a row.
        var byY = elements.OrderBy(e => Rect.Of(e).CenterY).ToList();
        var result = new List<LayoutElement>(byY.Count);
        var row = new List<LayoutElement>();
        double rowY = double.NaN;
        foreach (var el in byY)
        {
            var cy = Rect.Of(el).CenterY;
            if (row.Count > 0 && cy - rowY > rowTol)
            {
                result.AddRange(row.OrderBy(e => Rect.Of(e).CenterX));
                row.Clear();
            }
            if (row.Count == 0) rowY = cy;
            row.Add(el);
        }
        result.AddRange(row.OrderBy(e => Rect.Of(e).CenterX));
        return result;
    }

    // ---- element queries ----

    public IReadOnlyList<LayoutElement> ReadingOrder => _readingOrder;

    public int ReadingIndexOf(LayoutElement el)
        => _readingIndex.TryGetValue(el.Id, out var i) ? i : -1;

    public IEnumerable<LayoutElement> WithRole(SemanticRole role)
        => Layout.Elements.Where(e => e.Semantics.Role == role);

    public IEnumerable<LayoutElement> OfType(ElementType type)
        => Layout.Elements.Where(e => e.Type == type);

    public static readonly ElementType[] InputTypes =
    {
        ElementType.TextInput, ElementType.NumInput, ElementType.Checkbox, ElementType.RadioGroup
    };

    public IEnumerable<LayoutElement> InputElements
        => Layout.Elements.Where(e => InputTypes.Contains(e.Type));

    /// <summary>Elements ordered by explicit sequence (sequence ≥ 1).</summary>
    public IReadOnlyList<LayoutElement> Sequenced
        => Layout.Elements.Where(e => e.Semantics.Sequence >= 1)
                          .OrderBy(e => e.Semantics.Sequence)
                          .ToList();

    /// <summary>Groups (2+ semantics-bearing) keyed by effective group id.</summary>
    public IReadOnlyDictionary<string, List<LayoutElement>> Groups
        => Layout.Elements.Where(e => !string.IsNullOrEmpty(e.EffectiveGroupId))
                          .GroupBy(e => e.EffectiveGroupId!)
                          .ToDictionary(g => g.Key, g => g.ToList());

    // ---- geometry / zones ----

    public Zone ZoneOf(LayoutElement el)
    {
        var r = Rect.Of(el);
        var left = r.CenterX < ContentRect.X + ContentRect.W / 2;
        var top = r.CenterY < ContentRect.Y + ContentRect.H / 2;
        return (left, top) switch
        {
            (true, true) => Zone.POA,
            (false, true) => Zone.SFA,
            (true, false) => Zone.WFA,
            (false, false) => Zone.TA
        };
    }

    /// <summary>Pick a rule length parameter in the layout's native unit (mm-param for Paper, px-param for Screen).</summary>
    public double NativeParam(RuleDefinition rule, string mmName, string pxName, double fallback = 0)
        => Medium == Medium.Paper ? rule.GetDouble(mmName, fallback) : rule.GetDouble(pxName, fallback);

    // ---- text metrics ----

    /// <summary>Elements that render visible text.</summary>
    public IEnumerable<LayoutElement> TextBearing
        => Layout.Elements.Where(e => !string.IsNullOrWhiteSpace(e.Text));

    /// <summary>WCAG large-text decision per SPEC §6.1 (constants defined by WCAG, not tunable rules).</summary>
    public bool IsLargeText(LayoutElement el)
    {
        var size = el.Style.FontSize;
        if (size is null) return false;
        return Medium == Medium.Paper
            ? size >= 18 || (size >= 14 && el.Style.Bold)
            : size >= 24 || (size >= 18.5 && el.Style.Bold);
    }

    // ---- colors ----

    public string ForegroundOf(LayoutElement el)
        => ColorUtil.IsTransparent(el.Style.FgColor) ? Rules.DefaultTextColor : el.Style.FgColor!;

    /// <summary>
    /// Effective background of an element: its own opaque BgColor, else the topmost lower-Z
    /// element whose rect contains this element's center, else the canvas background (SPEC §6.1).
    /// </summary>
    public string EffectiveBackgroundOf(LayoutElement el)
    {
        if (!ColorUtil.IsTransparent(el.Style.BgColor))
            return el.Style.BgColor!;
        return BackgroundBehind(el);
    }

    /// <summary>
    /// Background visible directly behind the element (ignores the element's own fill).
    /// Paint order decides what the operator actually sees: the canvas draws elements in document
    /// order, so among the fills under this element the LAST painted one wins. Comparing Z alone
    /// picked the bottom-most panel and measured contrast against a colour nobody can see.
    /// </summary>
    public string BackgroundBehind(LayoutElement el)
    {
        var r = Rect.Of(el);
        var self = PaintKey(el);

        string? found = null;
        (int Z, int Index) best = (int.MinValue, int.MinValue);

        foreach (var other in Layout.Elements)
        {
            if (ReferenceEquals(other, el)) continue;
            if (ColorUtil.IsTransparent(other.Style.BgColor)) continue;
            if (!Rect.Of(other).Contains(r.CenterX, r.CenterY)) continue;

            var key = PaintKey(other);
            if (Compare(key, self) >= 0) continue;   // painted on top of el — not its background
            if (Compare(key, best) <= 0) continue;   // something later already covers it

            best = key;
            found = other.Style.BgColor;
        }

        return found ?? Layout.Canvas.Background;

        static int Compare((int Z, int Index) a, (int Z, int Index) b)
            => a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.Index.CompareTo(b.Index);
    }

    /// <summary>Paint order of an element: explicit Z first, then position in the document.</summary>
    private (int Z, int Index) PaintKey(LayoutElement el)
        => (el.Z, _paintIndex.TryGetValue(el, out var i) ? i : 0);

    // ---- pairing ----

    /// <summary>
    /// The input element a label most plausibly annotates: nearest input in the same group,
    /// else nearest input overall. Null when the layout has no inputs.
    /// </summary>
    public LayoutElement? PairedInputOf(LayoutElement label)
    {
        var group = label.EffectiveGroupId;
        var candidates = InputElements.Where(e => group == null || e.EffectiveGroupId == group).ToList();
        if (candidates.Count == 0) candidates = InputElements.ToList();
        return candidates.OrderBy(e => Geometry.EdgeDistance(label, e)).FirstOrDefault();
    }

    /// <summary>The label element nearest to an input (same-group preferred).</summary>
    public LayoutElement? PairedLabelOf(LayoutElement input)
    {
        var group = input.EffectiveGroupId;
        var labels = OfType(ElementType.Label)
            .Where(l => group == null || l.EffectiveGroupId == group || l.EffectiveGroupId == null)
            .ToList();
        if (labels.Count == 0) labels = OfType(ElementType.Label).ToList();
        return labels.OrderBy(l => Geometry.EdgeDistance(input, l)).FirstOrDefault();
    }

    /// <summary>Roles whose labels are captions, not input annotations.</summary>
    private static readonly SemanticRole[] NonAnnotatingRoles =
    {
        SemanticRole.Decoration, SemanticRole.Reference, SemanticRole.Navigation
    };

    /// <summary>
    /// Label→input annotation pairs used by proximity/travel rules: mutual-nearest pairs only,
    /// skipping caption labels (Decoration/Reference/Navigation) and labels whose group holds no input.
    /// </summary>
    public IEnumerable<(LayoutElement Label, LayoutElement Input, double Distance)> LabelInputPairs()
    {
        foreach (var label in OfType(ElementType.Label))
        {
            if (NonAnnotatingRoles.Contains(label.Semantics.Role)) continue;

            var group = label.EffectiveGroupId;
            if (group is not null && !InputElements.Any(e => e.EffectiveGroupId == group))
                continue; // caption label of a group without inputs (verdict/signature blocks...)

            var input = PairedInputOf(label);
            if (input is null) continue;
            if (!ReferenceEquals(PairedLabelOf(input), label))
                continue; // some other label owns this input

            yield return (label, input, Geometry.EdgeDistance(label, input));
        }
    }

    /// <summary>Format a native length for messages: "12.0mm" / "12px".</summary>
    public string FormatLen(double value)
        => Medium == Medium.Paper ? $"{value:0.#}mm" : $"{value:0}px";
}
