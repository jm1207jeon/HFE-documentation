namespace HfeLayoutSim.Core.Model;

/// <summary>
/// A single placed element. Geometry is in the native unit of the medium
/// (mm for Paper, px for Screen).
/// </summary>
public sealed class LayoutElement
{
    public string Id { get; set; } = "";
    public ElementType Type { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public int Z { get; set; }

    /// <summary>0 or 90 degrees.</summary>
    public int Rotation { get; set; }

    /// <summary>Displayed caption / content text.</summary>
    public string? Text { get; set; }

    /// <summary>Logical group membership (element-level shortcut; Semantics.GroupId also honored).</summary>
    public string? GroupId { get; set; }

    public ElementStyle Style { get; set; } = new();
    public Semantics Semantics { get; set; } = new();

    // ---- type-specific extras ----

    /// <summary>(Table) row count including header row.</summary>
    public int? Rows { get; set; }

    /// <summary>(Table) column count.</summary>
    public int? Columns { get; set; }

    /// <summary>(Table) role of each column: 항목/기준/측정/판정 ...</summary>
    public List<string>? ColumnRoles { get; set; }

    /// <summary>(RadioGroup) number of options.</summary>
    public int? OptionCount { get; set; }

    /// <summary>(Button) visual kind.</summary>
    public ButtonKind? ButtonKind { get; set; }

    /// <summary>(NumInput) fixed unit shown at the input (mm, N ...).</summary>
    public string? Unit { get; set; }

    /// <summary>(SignatureBox) signer role: 검사자/승인자 ...</summary>
    public string? SignerRole { get; set; }

    /// <summary>Pre-selected default value; judgment elements must not default to pass (C8-04).</summary>
    public string? DefaultValue { get; set; }

    /// <summary>GroupId with Semantics fallback.</summary>
    public string? EffectiveGroupId => GroupId ?? Semantics.GroupId;

    /// <summary>Width/height honoring 90° rotation.</summary>
    public (double W, double H) OrientedSize => Rotation == 90 ? (H, W) : (W, H);
}
