using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HfeLayoutSim.App.Services;
using HfeLayoutSim.Core.Advisor;
using HfeLayoutSim.Core.Catalog;
using HfeLayoutSim.Core.Engine;
using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Presets;
using HfeLayoutSim.Core.Report;

namespace HfeLayoutSim.App.ViewModels;

public sealed class ZoneOverlayViewModel
{
    public string Name { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double W { get; init; }
    public double H { get; init; }
    public string Fill { get; init; } = "#101565C0";
}

public sealed class CategoryGradeViewModel
{
    public string Category { get; init; } = "";
    public string Grade { get; init; } = "";
    public double Score { get; init; }
    public string Summary => $"{Category} [{Grade}] {Score:0.#}";
}

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private Layout? layout;

    public ObservableCollection<ElementViewModel> Elements { get; } = new();
    public ObservableCollection<PresetDefinition> PaletteItems { get; } = new();
    public ObservableCollection<AdviceItem> Advice { get; } = new();
    public ObservableCollection<Finding> Findings { get; } = new();
    public ObservableCollection<Strength> Strengths { get; } = new();
    public ObservableCollection<CategoryGradeViewModel> CategoryGrades { get; } = new();
    public ObservableCollection<ZoneOverlayViewModel> ZoneOverlays { get; } = new();

    [ObservableProperty]
    private ElementViewModel? selectedElement;

    [ObservableProperty]
    private double zoom = 1.0;

    [ObservableProperty]
    private bool showZones;

    [ObservableProperty]
    private string statusText = "새로 만들기(Paper/Screen) 또는 샘플 열기로 시작하십시오.";

    [ObservableProperty]
    private EvaluationReport? report;

    [ObservableProperty]
    private string verdictText = "";

    [ObservableProperty]
    private string verdictDetail = "";

    [ObservableProperty]
    private bool verdictPass;

    [ObservableProperty]
    private string? currentFilePath;

    private int _addCascade;

    public bool HasLayout => Layout is not null;
    public Medium Medium => Layout?.Medium ?? Medium.Paper;

    public double CanvasWidth => Layout?.Canvas.Width ?? 0;
    public double CanvasHeight => Layout?.Canvas.Height ?? 0;

    /// <summary>Native-unit → screen scale (A4 at 96dpi / FHD shrunk to fit) times user zoom.</summary>
    public double Scale => (Medium == Medium.Paper ? UnitConverter.MmToPx(1) : 0.55) * Zoom;

    public double SnapStep => Medium == Medium.Paper ? 1.0 : 8.0;

    public string WindowTitle => Layout is null
        ? "HFE Layout Simulator"
        : $"HFE Layout Simulator — {Layout.Meta.Name} [{(Medium == Medium.Paper ? "Paper" : "Screen")}]";

    partial void OnZoomChanged(double value) => OnPropertyChanged(nameof(Scale));

    partial void OnShowZonesChanged(bool value) => RebuildZoneOverlays();

    // ---------------------------------------------------------------- document

    [RelayCommand]
    private void NewPaper() => LoadLayout(new Layout
    {
        Meta = new LayoutMeta { Name = "새 검사 양식", Medium = Medium.Paper, CreatedAt = DateTimeOffset.Now },
        Canvas = new CanvasSpec
        {
            Width = 210, Height = 297,
            Margins = new Margins { Top = 15, Bottom = 15, Left = 20, Right = 12 }
        }
    }, null);

    [RelayCommand]
    private void NewScreen() => LoadLayout(new Layout
    {
        Meta = new LayoutMeta { Name = "새 MES 화면", Medium = Medium.Screen, CreatedAt = DateTimeOffset.Now },
        Canvas = new CanvasSpec
        {
            Width = 1920, Height = 1080,
            ContentPaddingPx = 24,
            FixedRegions = new FixedRegions(),
            IsScrollable = true
        }
    }, null);

    public void OpenFile(string path)
    {
        LoadLayout(LayoutSerializer.LoadFile(path), path);
        StatusText = $"열기 완료: {Path.GetFileName(path)}";
    }

    public void SaveFile(string path)
    {
        if (Layout is null) return;
        LayoutSerializer.SaveFile(Layout, path);
        CurrentFilePath = path;
        StatusText = $"저장 완료: {Path.GetFileName(path)}";
    }

    public void GenerateFromCatalog(string catalogPath, Medium medium)
    {
        var catalog = InspectionCatalog.LoadFile(catalogPath);
        var composed = new LayoutComposer(AppServices.Presets, AppServices.Rules).Compose(catalog, medium);
        LoadLayout(composed, null);
        StatusText = $"카탈로그로 생성 완료: {catalog.ProcessName} ({Elements.Count}개 요소) — [평가 실행]으로 확인해 보세요.";
    }

    private void LoadLayout(Layout newLayout, string? path)
    {
        Layout = newLayout;
        CurrentFilePath = path;
        SelectedElement = null;
        Report = null;
        _addCascade = 0;
        Advice.Clear();
        Findings.Clear();
        Strengths.Clear();
        CategoryGrades.Clear();
        VerdictText = "";
        VerdictDetail = "";

        Elements.Clear();
        foreach (var el in newLayout.Elements)
            Elements.Add(new ElementViewModel(el, newLayout.Medium));

        RefreshPalette();
        RebuildZoneOverlays();
        OnPropertyChanged(nameof(HasLayout));
        OnPropertyChanged(nameof(Medium));
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(Scale));
        OnPropertyChanged(nameof(WindowTitle));
    }

    private void RefreshPalette()
    {
        PaletteItems.Clear();
        if (Layout is null) return;
        foreach (var preset in AppServices.Presets.All.Where(p => p.SupportsMedium(Layout.Medium)))
            PaletteItems.Add(preset);
    }

    // ---------------------------------------------------------------- editing

    public void AddPreset(PresetDefinition preset)
    {
        if (Layout is null) return;

        var ctx = new EvaluationContext(Layout, AppServices.Rules);
        var offset = (_addCascade++ % 8) * SnapStep * 4;
        var x = Snap(ctx.ContentRect.X + offset);
        var y = Snap(ctx.ContentRect.Y + offset);

        var element = AppServices.Presets.Instantiate(preset.Id, Layout.Medium, x, y);
        // ensure unique id inside this layout
        var baseId = element.Id;
        var n = 1;
        while (Layout.Elements.Any(e => e.Id == element.Id))
            element.Id = $"{baseId}-{n++}";

        Layout.Elements.Add(element);
        var vm = new ElementViewModel(element, Layout.Medium);
        Elements.Add(vm);
        SelectElement(vm);
        RunAdvisor(vm);
        StatusText = $"'{preset.Name}' 배치됨 — 드래그로 이동, 우측 코칭 탭에서 위치 진단을 확인하세요.";
    }

    public void SelectElement(ElementViewModel? vm)
    {
        foreach (var e in Elements)
            e.IsSelected = ReferenceEquals(e, vm);
        SelectedElement = vm;
        if (vm is not null)
            RunAdvisor(vm);
    }

    public double Snap(double value) => Math.Round(value / SnapStep) * SnapStep;

    /// <summary>Called after a drag ends — re-coach the moved element.</summary>
    public void OnElementMoved(ElementViewModel vm)
    {
        vm.NotifyGeometryChanged();
        RunAdvisor(vm);
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (Layout is null || SelectedElement is null) return;
        Layout.Elements.Remove(SelectedElement.Model);
        Elements.Remove(SelectedElement);
        SelectedElement = null;
        Advice.Clear();
        StatusText = "요소 삭제됨.";
    }

    private void RunAdvisor(ElementViewModel vm)
    {
        if (Layout is null) return;
        Advice.Clear();
        try
        {
            foreach (var item in AppServices.Advisor.Advise(Layout, vm.Id))
                Advice.Add(item);
        }
        catch (Exception ex)
        {
            StatusText = $"코칭 실패: {ex.Message}";
        }
    }

    // ---------------------------------------------------------------- evaluation

    [RelayCommand]
    private void Evaluate()
    {
        if (Layout is null) return;
        try
        {
            Report = AppServices.Engine.Evaluate(Layout, AppServices.Rules);
        }
        catch (Exception ex)
        {
            StatusText = $"평가 실패: {ex.Message}";
            return;
        }

        Findings.Clear();
        foreach (var f in Report.Findings) Findings.Add(f);
        Strengths.Clear();
        foreach (var s in Report.Strengths) Strengths.Add(s);
        CategoryGrades.Clear();
        foreach (var c in Report.ScoreCard.Categories)
            CategoryGrades.Add(new CategoryGradeViewModel { Category = c.Category, Grade = c.Grade, Score = c.Score });

        var sc = Report.ScoreCard;
        VerdictPass = sc.Pass;
        VerdictText = sc.Pass ? "PASS ✓" : "FAIL ✕";
        VerdictDetail = $"등급 {sc.Grade} · 총점 {sc.Total:0.0}/100 · 합격 기준 {sc.PassGrade} 이상" +
                        (sc.GradeCapped ? " · Critical 위반 → 배포 부적합" : "");
        StatusText = $"평가 완료 — 지적 {Findings.Count}건, 강점 {Strengths.Count}건.";
    }

    public void HighlightFinding(Finding? finding)
    {
        foreach (var e in Elements)
            e.IsHighlighted = finding is not null && finding.ElementIds.Contains(e.Id);
    }

    public void ExportHtml(string path)
    {
        if (Report is null) return;
        HtmlReportExporter.ExportFile(Report, path);
        StatusText = $"HTML 리포트 저장: {Path.GetFileName(path)}";
    }

    // ---------------------------------------------------------------- overlays

    private void RebuildZoneOverlays()
    {
        ZoneOverlays.Clear();
        if (!ShowZones || Layout is null) return;

        var content = new EvaluationContext(Layout, AppServices.Rules).ContentRect;
        var halfW = content.W / 2;
        var halfH = content.H / 2;
        ZoneOverlays.Add(new ZoneOverlayViewModel { Name = "POA", X = content.X, Y = content.Y, W = halfW, H = halfH, Fill = "#141565C0" });
        ZoneOverlays.Add(new ZoneOverlayViewModel { Name = "SFA", X = content.X + halfW, Y = content.Y, W = halfW, H = halfH, Fill = "#0D2E7D32" });
        ZoneOverlays.Add(new ZoneOverlayViewModel { Name = "WFA", X = content.X, Y = content.Y + halfH, W = halfW, H = halfH, Fill = "#14C62828" });
        ZoneOverlays.Add(new ZoneOverlayViewModel { Name = "TA", X = content.X + halfW, Y = content.Y + halfH, W = halfW, H = halfH, Fill = "#140D47A1" });
    }
}
