namespace HfeLayoutSim.Core.Model;

public sealed class LayoutMeta
{
    public string Name { get; set; } = "";
    public Medium Medium { get; set; }

    /// <summary>Name/id of the layout this variant was cloned from.</summary>
    public string? VariantOf { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary>Root document of a *.hfelayout.json file (SPEC §7).</summary>
public sealed class Layout
{
    public LayoutMeta Meta { get; set; } = new();
    public CanvasSpec Canvas { get; set; } = new();
    public List<LayoutElement> Elements { get; set; } = new();

    public Medium Medium => Meta.Medium;
}
