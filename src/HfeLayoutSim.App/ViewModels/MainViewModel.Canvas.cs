using System.Collections.ObjectModel;
using HfeLayoutSim.App.Services;
using HfeLayoutSim.Core.Engine;
using HfeLayoutSim.Core.Model;

namespace HfeLayoutSim.App.ViewModels;

/// <summary>A tinted quadrant drawn under the elements (POA/SFA/WFA/TA).</summary>
public sealed class ZoneOverlayViewModel
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double W { get; init; }
    public double H { get; init; }
    public string FillHex { get; init; } = "#141565C0";
    public string LabelHex { get; init; } = "#495057";
}

/// <summary>A guide line/box: print margins, content padding, or a fixed screen region.</summary>
public sealed class GuideViewModel
{
    public string Name { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double W { get; init; }
    public double H { get; init; }
    public string StrokeHex { get; init; } = "#868E96";
    public bool Dashed { get; init; } = true;
    public bool ShowLabel { get; init; }
}

public sealed partial class MainViewModel
{
    public ObservableCollection<ZoneOverlayViewModel> ZoneOverlays { get; } = new();
    public ObservableCollection<GuideViewModel> Guides { get; } = new();

    /// <summary>Content area used by the engine — guides must show the SAME rectangle the rules judge.</summary>
    public Rect ContentRect => Layout is null
        ? new Rect(0, 0, 0, 0)
        : new EvaluationContext(Layout, AppServices.Rules).ContentRect;

    private void RebuildOverlays()
    {
        ZoneOverlays.Clear();
        Guides.Clear();
        if (Layout is null) return;

        var content = ContentRect;
        if (content.W <= 0 || content.H <= 0) return;

        if (ShowZones) BuildZones(content);
        if (ShowGuides) BuildGuides(content);
    }

    /// <summary>
    /// Quadrants of the CONTENT rect, matching EvaluationContext.ZoneOf exactly — an overlay drawn
    /// over the whole page would tell the user a different zone than the one the rule used.
    /// </summary>
    private void BuildZones(Rect content)
    {
        var halfW = content.W / 2;
        var halfH = content.H / 2;

        ZoneOverlays.Add(new ZoneOverlayViewModel
        {
            Name = "POA", Description = "시선 시작 — 식별 정보",
            X = content.X, Y = content.Y, W = halfW, H = halfH, FillHex = "#221565C0",
        });
        ZoneOverlays.Add(new ZoneOverlayViewModel
        {
            Name = "SFA", Description = "보조 영역",
            X = content.X + halfW, Y = content.Y, W = halfW, H = halfH, FillHex = "#182E7D32",
        });
        ZoneOverlays.Add(new ZoneOverlayViewModel
        {
            Name = "WFA", Description = "사각지대 — 중요 정보 금지",
            X = content.X, Y = content.Y + halfH, W = halfW, H = halfH, FillHex = "#22C62828",
        });
        ZoneOverlays.Add(new ZoneOverlayViewModel
        {
            Name = "TA", Description = "시선 종착 — 판정·서명",
            X = content.X + halfW, Y = content.Y + halfH, W = halfW, H = halfH, FillHex = "#220D47A1",
        });
    }

    private void BuildGuides(Rect content)
    {
        var canvas = Layout!.Canvas;

        Guides.Add(new GuideViewModel
        {
            Name = Medium == Medium.Paper ? "인쇄 여백" : "콘텐츠 영역",
            X = content.X, Y = content.Y, W = content.W, H = content.H,
            StrokeHex = "#1565C0", ShowLabel = true,
        });

        if (Medium != Medium.Screen || canvas.FixedRegions is null) return;

        var f = canvas.FixedRegions;
        Guides.Add(new GuideViewModel
        {
            Name = $"헤더 {f.HeaderPx:0}px",
            X = 0, Y = 0, W = canvas.Width, H = f.HeaderPx, StrokeHex = "#868E96", ShowLabel = true,
        });
        Guides.Add(new GuideViewModel
        {
            Name = $"컨텍스트 바 {f.ContextBarPx:0}px — 식별 정보 고정",
            X = 0, Y = f.HeaderPx, W = canvas.Width, H = f.ContextBarPx, StrokeHex = "#868E96", ShowLabel = true,
        });
        Guides.Add(new GuideViewModel
        {
            Name = $"GNB {f.GnbPx:0}px",
            X = 0, Y = f.HeaderPx + f.ContextBarPx, W = f.GnbPx,
            H = Math.Max(0, canvas.Height - f.HeaderPx - f.ContextBarPx - f.ActionBarPx),
            StrokeHex = "#868E96", ShowLabel = true,
        });
        Guides.Add(new GuideViewModel
        {
            Name = $"액션 바 {f.ActionBarPx:0}px — 주 행위 버튼",
            X = 0, Y = Math.Max(0, canvas.Height - f.ActionBarPx), W = canvas.Width, H = f.ActionBarPx,
            StrokeHex = "#868E96", ShowLabel = true,
        });
    }
}
