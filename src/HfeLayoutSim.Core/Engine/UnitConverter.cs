namespace HfeLayoutSim.Core.Engine;

/// <summary>
/// mm ↔ px ↔ pt conversion at 96 dpi (CLAUDE.md principle #3: 1mm = 3.7795px).
/// Layouts are stored in their native unit; rules compare via these conversions.
/// </summary>
public static class UnitConverter
{
    public const double Dpi = 96.0;
    public const double MmPerInch = 25.4;
    public const double PtPerInch = 72.0;

    public static double MmToPx(double mm) => mm * Dpi / MmPerInch;
    public static double PxToMm(double px) => px * MmPerInch / Dpi;

    public static double PtToPx(double pt) => pt * Dpi / PtPerInch;
    public static double PxToPt(double px) => px * PtPerInch / Dpi;

    public static double PtToMm(double pt) => PxToMm(PtToPx(pt));
    public static double MmToPt(double mm) => PxToPt(MmToPx(mm));
}
