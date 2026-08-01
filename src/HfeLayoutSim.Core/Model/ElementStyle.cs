namespace HfeLayoutSim.Core.Model;

/// <summary>Visual style. FontSize is pt on Paper, px on Screen (native unit of the medium).</summary>
public sealed class ElementStyle
{
    public string? FontFamily { get; set; }

    /// <summary>pt (Paper) or px (Screen). Null = application body default.</summary>
    public double? FontSize { get; set; }

    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }

    /// <summary>Foreground (text) color, #RRGGBB. Null = default body text color from the rule palette.</summary>
    public string? FgColor { get; set; }

    /// <summary>Background fill, #RRGGBB. Null/transparent = see-through to elements below / canvas.</summary>
    public string? BgColor { get; set; }

    public string? BorderColor { get; set; }
    public double BorderWidth { get; set; }
    public string? Align { get; set; }
}
