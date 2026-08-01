using System.Globalization;

namespace HfeLayoutSim.Core.Engine;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

public static class ColorUtil
{
    /// <summary>Channel spread above which a color is considered chromatic (carries hue meaning).</summary>
    private const int ChromaticSpread = 24;

    public static bool TryParse(string? hex, out Rgb rgb)
    {
        rgb = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 3)
            s = $"{s[0]}{s[0]}{s[1]}{s[1]}{s[2]}{s[2]}";
        if (s.Length != 6) return false;
        if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        rgb = new Rgb((byte)(v >> 16), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
        return true;
    }

    public static bool IsTransparent(string? hex)
        => string.IsNullOrWhiteSpace(hex) ||
           hex.Equals("transparent", StringComparison.OrdinalIgnoreCase) ||
           (hex.StartsWith("#") && hex.Length == 9 && hex.Substring(1, 2) == "00");

    /// <summary>True when the color carries hue (not a neutral gray/near-gray).</summary>
    public static bool IsChromatic(string? hex)
    {
        if (!TryParse(hex, out var c)) return false;
        int max = Math.Max(c.R, Math.Max(c.G, c.B));
        int min = Math.Min(c.R, Math.Min(c.G, c.B));
        return max - min > ChromaticSpread;
    }

    public enum HueFamily { Other, Red, Green }

    /// <summary>Coarse red/green classification for the red-green adjacency rule (C1-06).</summary>
    public static HueFamily ClassifyHue(string? hex)
    {
        if (!TryParse(hex, out var c) || !IsChromatic(hex)) return HueFamily.Other;
        if (c.R > c.G + ChromaticSpread && c.R > c.B) return HueFamily.Red;
        if (c.G > c.R + ChromaticSpread && c.G > c.B) return HueFamily.Green;
        return HueFamily.Other;
    }

    public static bool SameColor(string? a, string? b)
        => TryParse(a, out var ca) && TryParse(b, out var cb) && ca == cb;
}
