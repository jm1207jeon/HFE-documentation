namespace HfeLayoutSim.Core.Model;

/// <summary>Print margins in mm (Paper).</summary>
public sealed class Margins
{
    public double Top { get; set; }
    public double Bottom { get; set; }
    public double Left { get; set; }
    public double Right { get; set; }
}

/// <summary>Fixed chrome regions of a Screen canvas (DESIGN_TOKENS defaults).</summary>
public sealed class FixedRegions
{
    /// <summary>Top header bar height.</summary>
    public double HeaderPx { get; set; } = 56;

    /// <summary>Left global navigation width.</summary>
    public double GnbPx { get; set; } = 240;

    /// <summary>Context bar (below header) height — identification info lives here.</summary>
    public double ContextBarPx { get; set; } = 48;

    /// <summary>Bottom action bar height.</summary>
    public double ActionBarPx { get; set; } = 64;
}

/// <summary>Canvas description. Width/Height are mm (Paper) or px (Screen).</summary>
public sealed class CanvasSpec
{
    public double Width { get; set; }
    public double Height { get; set; }

    public string Background { get; set; } = "#FFFFFF";

    /// <summary>(Paper) print margins.</summary>
    public Margins? Margins { get; set; }

    /// <summary>(Screen) content padding inside the content area.</summary>
    public double? ContentPaddingPx { get; set; }

    /// <summary>(Screen) fixed chrome regions. Null = full-bleed content.</summary>
    public FixedRegions? FixedRegions { get; set; }

    /// <summary>(Screen) whether the content area scrolls (C4-06 sticky identification).</summary>
    public bool IsScrollable { get; set; }

    /// <summary>(Paper) number of pages the form spans.</summary>
    public int PageCount { get; set; } = 1;
}
