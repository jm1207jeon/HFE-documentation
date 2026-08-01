using HfeLayoutSim.Core.Model;

namespace HfeLayoutSim.Core.Engine;

public readonly record struct Rect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
    public double CenterX => X + W / 2;
    public double CenterY => Y + H / 2;
    public double Area => W * H;

    public bool Contains(double px, double py)
        => px >= X && px <= Right && py >= Y && py <= Bottom;

    public bool ContainsRect(Rect other, double tolerance = 0)
        => other.X >= X - tolerance && other.Y >= Y - tolerance &&
           other.Right <= Right + tolerance && other.Bottom <= Bottom + tolerance;

    public static Rect Of(LayoutElement el)
    {
        var (w, h) = el.OrientedSize;
        return new Rect(el.X, el.Y, w, h);
    }
}

public static class Geometry
{
    /// <summary>Shortest edge-to-edge distance between two rectangles (0 when overlapping/touching).</summary>
    public static double EdgeDistance(Rect a, Rect b)
    {
        var dx = Math.Max(0, Math.Max(a.X, b.X) - Math.Min(a.Right, b.Right));
        var dy = Math.Max(0, Math.Max(a.Y, b.Y) - Math.Min(a.Bottom, b.Bottom));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static double EdgeDistance(LayoutElement a, LayoutElement b)
        => EdgeDistance(Rect.Of(a), Rect.Of(b));
}
