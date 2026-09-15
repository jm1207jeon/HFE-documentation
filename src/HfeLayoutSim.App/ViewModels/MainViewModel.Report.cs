using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HfeLayoutSim.App.Services;
using HfeLayoutSim.Core.Compare;
using HfeLayoutSim.Core.Engine;
using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Report;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.App.ViewModels;

/// <summary>One graded category, presented the way a verifier prints its parameter table.</summary>
public sealed class CategoryGradeViewModel
{
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public string Grade { get; init; } = "";
    public double Score { get; init; }
    public double Weight { get; init; }
    public int PassCount { get; init; }
    public int ViolationCount { get; init; }
    public int NotApplicableCount { get; init; }

    public string GradeBrushHex => Grade switch
    {
        "A" or "B" => "#2E7D32",
        "C" or "D" => "#B45309",
        _ => "#C62828",
    };

    public string Counts => $"통과 {PassCount} / 위반 {ViolationCount} / 해당없음 {NotApplicableCount}";
    public string WeightLabel => $"{Weight:0.00}";
}

/// <summary>A finding as the report list shows it: severity triple-coded, measured vs target visible.</summary>
public sealed class FindingViewModel
{
    public FindingViewModel(Finding finding)
    {
        Finding = finding;
        var style = SeverityDisplay.Of(finding.Severity);
        SeverityGlyph = style.Glyph;
        SeverityLabel = style.Label;
        SeverityHex = style.Hex;
        SeverityBgHex = style.BackgroundHex;
    }

    public Finding Finding { get; }

    public string RuleId => Finding.RuleId;
    public string Category => Finding.Category;
    public RuleSeverity Severity => Finding.Severity;
    public string Title => Finding.Title;
    public string Message => Finding.Message;
    public string Recommendation => Finding.Recommendation;
    public string StandardRef => Finding.StandardRef;
    public string Measured => Finding.Measured;
    public string Target => Finding.Target;
    public int Penalty => Finding.Penalty;
    public IReadOnlyList<string> ElementIds => Finding.ElementIds;

    public string SeverityGlyph { get; }
    public string SeverityLabel { get; }
    public string SeverityHex { get; }
    public string SeverityBgHex { get; }

    public string EscalationNote => Finding.Escalated ? "안전 관련 요소 → 심각도 상향" : "";
    public bool HasEscalation => Finding.Escalated;

    /// <summary>"실측 12개 · 기준 4개 이하" — a verifier always shows the measurement against the limit.</summary>
    public string MeasuredVsTarget => $"실측 {Measured}   |   기준 {Target}";

    public string ElementList => ElementIds.Count == 0
        ? "레이아웃 전체"
        : string.Join(", ", ElementIds.Take(8)) + (ElementIds.Count > 8 ? $" 외 {ElementIds.Count - 8}개" : "");

    public string PenaltyLabel => $"−{Penalty}";
}

public sealed record SeverityFilterOption(string Label, RuleSeverity? Value);

public sealed record CategoryFilterOption(string Label, string? Value);

public sealed partial class MainViewModel
{
    public ObservableCollection<FindingViewModel> Findings { get; } = new();
    public ObservableCollection<Strength> Strengths { get; } = new();
    public ObservableCollection<CategoryGradeViewModel> CategoryGrades { get; } = new();

    private IReadOnlyList<FindingViewModel> _allFindings = Array.Empty<FindingViewModel>();

    [ObservableProperty]
    private EvaluationReport? report;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EvaluateCommand))]
    private bool isEvaluating;

    /// <summary>
    /// True once the layout changed after the shown evaluation. The verdict is kept on screen
    /// (the findings list is the user's work queue) but is visibly marked out of date, and export
    /// is blocked — exporting a PASS for a layout that no longer exists would be a false record.
    /// </summary>
    [ObservableProperty]
    private bool reportIsStale;

    [ObservableProperty]
    private string verdictText = "";

    [ObservableProperty]
    private string verdictDetail = "";

    [ObservableProperty]
    private string verdictConditions = "";

    [ObservableProperty]
    private bool verdictPass;

    public bool HasReport => Report is not null;

    partial void OnReportChanged(EvaluationReport? value)
    {
        OnPropertyChanged(nameof(HasReport));
        ExportHtmlCommand.NotifyCanExecuteChanged();
    }

    partial void OnReportIsStaleChanged(bool value)
    {
        OnPropertyChanged(nameof(StaleNotice));
        ExportHtmlCommand.NotifyCanExecuteChanged();
    }

    public string StaleNotice => ReportIsStale
        ? "레이아웃이 수정되었습니다 — 아래 판정은 수정 전 기준입니다. [평가 실행]으로 갱신하십시오."
        : "";

    public void MarkReportStale()
    {
        if (Report is not null) ReportIsStale = true;
    }

    private void ClearReport()
    {
        Report = null;
        ReportIsStale = false;
        _allFindings = Array.Empty<FindingViewModel>();
        Findings.Clear();
        Strengths.Clear();
        CategoryGrades.Clear();
        VerdictText = "";
        VerdictDetail = "";
        VerdictConditions = "";
        VerdictPass = false;
        foreach (var e in Elements) e.IsHighlighted = false;
    }

    // ---------------------------------------------------------------- evaluate

    public bool CanEvaluate => HasLayout && !IsEvaluating;

    [RelayCommand(CanExecute = nameof(CanEvaluate))]
    private async Task EvaluateAsync()
    {
        if (Layout is null) return;

        IsEvaluating = true;
        EvaluateCommand.NotifyCanExecuteChanged();
        SetStatus("평가 중…");

        try
        {
            // Snapshot first: the engine must not walk a graph the UI thread is still editing.
            var snapshot = LayoutSerializer.Load(LayoutSerializer.Save(Layout), "evaluate");
            var rules = AppServices.Rules;
            var report = await Task.Run(() => AppServices.Engine.Evaluate(snapshot, rules));
            ApplyReport(report);
        }
        catch (Exception ex)
        {
            AppLog.Error("평가 실패", ex);
            _dialogs.ShowError("평가 실패",
                "레이아웃을 평가하지 못했습니다.\n\n" + ex.Message + $"\n\n진단 로그: {AppLog.FilePath}");
            SetStatus("평가에 실패했습니다: " + ex.Message, StatusLevel.Error);
        }
        finally
        {
            IsEvaluating = false;
            EvaluateCommand.NotifyCanExecuteChanged();
        }
    }

    private void ApplyReport(EvaluationReport report)
    {
        Report = report;
        ReportIsStale = false;

        _allFindings = report.Findings.Select(f => new FindingViewModel(f)).ToList();

        Strengths.Clear();
        foreach (var s in report.Strengths) Strengths.Add(s);

        CategoryGrades.Clear();
        foreach (var c in report.ScoreCard.Categories)
            CategoryGrades.Add(new CategoryGradeViewModel
            {
                Category = c.Category,
                Name = CategoryName(c.Category),
                Grade = c.Grade,
                Score = c.Score,
                Weight = c.Weight,
                PassCount = c.PassCount,
                ViolationCount = c.ViolationCount,
                NotApplicableCount = c.NotApplicableCount,
            });

        RebuildCategoryFilter();
        ApplyFilters();

        var card = report.ScoreCard;
        VerdictPass = card.Pass;
        VerdictText = card.Pass ? "PASS ✓" : "FAIL ✕";
        VerdictDetail = $"등급 {card.Grade} · 총점 {card.Total:0.0} / 100 · 합격 기준 {card.PassGrade} 이상" +
                        (card.GradeCapped ? " · Critical 위반으로 등급 상한 적용 (배포 부적합)" : "");
        VerdictConditions =
            $"규칙 {report.RulesVersion} ({KnowledgeSourceLabel()}) · " +
            $"{(report.Medium == Medium.Paper ? "종이 A4" : "MES 화면")} · " +
            $"요소 {Layout?.Elements.Count ?? 0}개 · 평가 {report.EvaluatedAt:yyyy-MM-dd HH:mm:ss}";

        VerdictReadyRequested?.Invoke();

        var criticals = report.Findings.Count(f => f.Severity == RuleSeverity.Critical);
        SetStatus(
            $"평가 완료 — {VerdictText} 등급 {card.Grade} · 지적 {report.Findings.Count}건" +
            (criticals > 0 ? $" (치명 {criticals}건)" : "") + $" · 강점 {report.Strengths.Count}건",
            card.Pass ? StatusLevel.Info : StatusLevel.Warning);
    }

    private static string KnowledgeSourceLabel() => AppServices.RulesLoad.Source switch
    {
        KnowledgeSource.File => System.IO.Path.GetFileName(AppServices.RulesLoad.Path),
        KnowledgeSource.EmbeddedBecauseMissing => "내장 규칙(파일 없음)",
        _ => "내장 규칙(파일 오류)",
    };

    /// <summary>Full provenance for the tooltip — which exact file the verdict came from.</summary>
    public string KnowledgeSourceDetail => Describe("규칙", AppServices.RulesLoad)
                                           + "   |   " + Describe("프리셋", AppServices.PresetsLoad);

    private static string Describe(string what, KnowledgeLoad load) => load.Source == KnowledgeSource.File
        ? $"{what} 파일: {load.Path}"
        : $"내장 {what} 사용 — {load.Path} ({load.Problem ?? "파일 없음"})";

    public static string CategoryName(string id) => id switch
    {
        "C1" => "색상·대비",
        "C2" => "타이포그래피",
        "C3" => "크기·조작",
        "C4" => "배치·시선 흐름",
        "C5" => "그룹화·근접성",
        "C6" => "인지 부하·밀도",
        "C7" => "순서·업무 흐름",
        "C8" => "오류 방지·강건성",
        "C9" => "경계·부하 관리",
        _ => id,
    };

    // ---------------------------------------------------------------- filters

    public IReadOnlyList<SeverityFilterOption> SeverityFilters { get; } = new[]
    {
        new SeverityFilterOption("전체 심각도", null),
        new SeverityFilterOption("✕ 치명만", RuleSeverity.Critical),
        new SeverityFilterOption("! 중대만", RuleSeverity.Major),
        new SeverityFilterOption("ⓘ 경미만", RuleSeverity.Minor),
    };

    public ObservableCollection<CategoryFilterOption> CategoryFilters { get; } = new();

    [ObservableProperty]
    private SeverityFilterOption? severityFilter;

    [ObservableProperty]
    private CategoryFilterOption? categoryFilter;

    partial void OnSeverityFilterChanged(SeverityFilterOption? value) => ApplyFilters();

    partial void OnCategoryFilterChanged(CategoryFilterOption? value) => ApplyFilters();

    private void RebuildCategoryFilter()
    {
        var previous = CategoryFilter?.Value;
        CategoryFilters.Clear();
        CategoryFilters.Add(new CategoryFilterOption("전체 카테고리", null));
        foreach (var category in _allFindings.Select(f => f.Category).Distinct().OrderBy(c => c))
            CategoryFilters.Add(new CategoryFilterOption($"{category} {CategoryName(category)}", category));

        CategoryFilter = CategoryFilters.FirstOrDefault(c => c.Value == previous) ?? CategoryFilters[0];
        SeverityFilter ??= SeverityFilters[0];
    }

    private void ApplyFilters()
    {
        Findings.Clear();
        var severity = SeverityFilter?.Value;
        var category = CategoryFilter?.Value;

        foreach (var f in _allFindings)
        {
            if (severity is not null && f.Severity != severity) continue;
            if (category is not null && f.Category != category) continue;
            Findings.Add(f);
        }

        OnPropertyChanged(nameof(FindingsSummary));
    }

    public string FindingsSummary
    {
        get
        {
            if (Report is null) return "평가를 실행하면 지적 사항이 표시됩니다.";
            if (_allFindings.Count == 0) return "지적 사항 없음 — 전 규칙 통과.";

            var critical = _allFindings.Count(f => f.Severity == RuleSeverity.Critical);
            var major = _allFindings.Count(f => f.Severity == RuleSeverity.Major);
            var minor = _allFindings.Count(f => f.Severity == RuleSeverity.Minor);
            var shown = Findings.Count == _allFindings.Count ? "" : $" · 표시 {Findings.Count}건";
            return $"✕ 치명 {critical} · ! 중대 {major} · ⓘ 경미 {minor}{shown}";
        }
    }

    // ---------------------------------------------------------------- highlight

    [ObservableProperty]
    private FindingViewModel? selectedFinding;

    partial void OnSelectedFindingChanged(FindingViewModel? value)
    {
        foreach (var e in Elements)
            e.IsHighlighted = value is not null && value.ElementIds.Contains(e.Id);

        if (value is null) return;

        var first = value.ElementIds.Select(FindElement).FirstOrDefault(e => e is not null);
        if (first is not null)
        {
            SelectedElement = first;
            ScrollToElementRequested?.Invoke(first);
        }
        else if (value.ElementIds.Count > 0)
        {
            SetStatus($"이 지적이 가리키는 요소가 더 이상 없습니다 ({value.ElementList}) — 재평가가 필요합니다.",
                StatusLevel.Warning);
        }
    }

    /// <summary>Raised so the view can bring the highlighted element into view.</summary>
    public event Action<ElementViewModel>? ScrollToElementRequested;

    /// <summary>Raised after an evaluation so the view can show the verdict without a hunt.</summary>
    public event Action? VerdictReadyRequested;

    /// <summary>Drops the red finding marks (the user has moved on to editing).</summary>
    public void ClearFindingHighlight()
    {
        if (SelectedFinding is null) return;
        SelectedFinding = null;
        foreach (var e in Elements) e.IsHighlighted = false;
    }

    // ---------------------------------------------------------------- export

    public bool CanExportHtml => Report is not null && !ReportIsStale;

    [RelayCommand(CanExecute = nameof(CanExportHtml))]
    private void ExportHtml()
    {
        if (Report is null) return;

        var suggested = (string.IsNullOrWhiteSpace(Report.LayoutName) ? "hfe_report" : Report.LayoutName) + ".html";
        foreach (var c in Path.GetInvalidFileNameChars()) suggested = suggested.Replace(c, '_');

        var path = _dialogs.AskReportPath(suggested, AppServices.Settings.LastReportDirectory);
        if (path is null) return;

        try
        {
            HtmlReportExporter.ExportFile(Report, path);
            RememberDirectory(d => AppServices.Settings.LastReportDirectory = d, path);
            PersistSettings();
            SetStatus($"HTML 리포트 저장: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            AppLog.Error("HTML 리포트 저장 실패: " + path, ex);
            _dialogs.ShowError("리포트 저장 실패", Explain(ex, path));
            SetStatus("리포트를 저장하지 못했습니다: " + ex.Message, StatusLevel.Error);
        }
    }

    // ---------------------------------------------------------------- compare

    public bool CanCompare => HasLayout;

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private void Compare()
    {
        if (Layout is null) return;

        var paths = _dialogs.AskComparePaths(AppServices.Settings.LastLayoutDirectory);
        if (paths.Count == 0) return;

        if (paths.Count > VariantComparer.MaxVariants - 1)
        {
            SetStatus($"현재 레이아웃을 포함해 최대 {VariantComparer.MaxVariants}개까지 비교할 수 있습니다.",
                StatusLevel.Warning);
            paths = paths.Take(VariantComparer.MaxVariants - 1).ToList();
        }

        try
        {
            var reports = new List<EvaluationReport>
            {
                AppServices.Engine.Evaluate(
                    LayoutSerializer.Load(LayoutSerializer.Save(Layout), "compare"), AppServices.Rules),
            };
            foreach (var path in paths)
                reports.Add(AppServices.Engine.Evaluate(LayoutSerializer.LoadFile(path), AppServices.Rules));

            var result = VariantComparer.Compare(reports);
            _dialogs.ShowCompare(new CompareViewModel(result));
            SetStatus($"{reports.Count}개 배치안을 비교했습니다.");
        }
        catch (Exception ex)
        {
            AppLog.Error("비교 실패", ex);
            _dialogs.ShowError("비교 실패", ex.Message);
            SetStatus("비교하지 못했습니다: " + ex.Message, StatusLevel.Error);
        }
    }
}
