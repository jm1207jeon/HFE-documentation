using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
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

public enum StatusLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>What the user chose when asked about unsaved work.</summary>
public enum DiscardChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Dialogs the view owns; the view model decides WHEN to ask, the window HOW.</summary>
public interface IDialogService
{
    DiscardChoice AskUnsavedChanges(string layoutName, int elementCount);
    string? AskSavePath(string suggestedFileName, string? initialDirectory);
    string? AskOpenPath(string? initialDirectory);
    string? AskCatalogPath(string? initialDirectory);
    string? AskReportPath(string suggestedFileName, string? initialDirectory);
    Medium? AskMedium();
    IReadOnlyList<string> AskComparePaths(string? initialDirectory);
    bool Confirm(string title, string message, string confirmLabel, string cancelLabel);
    void ShowError(string title, string message);
    void ShowCompare(CompareViewModel compare);
}

/// <summary>
/// The editor document: one layout, its evaluation, and everything the user can do to it.
/// The invariants this class exists to keep:
///   · no edit is lost silently — everything is undoable, autosaved, and guarded on discard;
///   · no verdict is shown for a layout it no longer describes — edits mark the report stale;
///   · every edit re-runs the placement coach, so guidance never lags behind the canvas.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IElementEditHost
{
    private readonly IDialogService _dialogs;
    private readonly UndoStack _history = new();
    private readonly DispatcherTimer _autosaveTimer;
    private bool _suppressDirty;
    private bool _settingsSaveFailed;

    /// <summary>
    /// The document exactly as it sits on disk. Undo can walk the layout back to a state identical to
    /// the saved file, and a document that matches its file is not modified — claiming otherwise
    /// trains the user to click through the unsaved-changes prompt.
    /// </summary>
    private string? _savedSnapshot;
    private int _addCascade;

    public MainViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;

        Zoom = AppServices.Settings.Zoom;
        ShowZones = AppServices.Settings.ShowZones;
        ShowGuides = AppServices.Settings.ShowGuides;

        _autosaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(Math.Max(10, AppServices.Settings.AutosaveSeconds)),
        };
        _autosaveTimer.Tick += (_, _) => Autosave();
        if (AppServices.Settings.AutosaveEnabled) _autosaveTimer.Start();

        StatusText = "새 양식을 만들거나 기존 레이아웃을 열어 시작하십시오.";
        NoteKnowledgeSource();
    }

    // ================================================================ document

    [ObservableProperty]
    private Layout? layout;

    [ObservableProperty]
    private string? currentFilePath;

    [ObservableProperty]
    private bool isDirty;

    public ObservableCollection<ElementViewModel> Elements { get; } = new();

    public bool HasLayout => Layout is not null;

    public Medium Medium => Layout?.Medium ?? Medium.Paper;

    public string WindowTitle
    {
        get
        {
            if (Layout is null) return AppPaths.ProductTitle;
            var file = CurrentFilePath is null ? "저장 안 됨" : Path.GetFileName(CurrentFilePath);
            var medium = Medium == Medium.Paper ? "종이 A4" : "MES 화면";
            return $"{(IsDirty ? "● " : "")}{Layout.Meta.Name} — {file} [{medium}] · {AppPaths.ProductTitle}";
        }
    }

    /// <summary>Shown next to the title; the operator must always be able to see save state.</summary>
    public string DirtyIndicator => Layout is null ? "" : IsDirty ? "● 미저장 변경" : "✓ 저장됨";

    partial void OnLayoutChanged(Layout? value)
    {
        OnPropertyChanged(nameof(HasLayout));
        OnPropertyChanged(nameof(Medium));
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(Scale));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(DirtyIndicator));
        OnPropertyChanged(nameof(LayoutName));
        OnPropertyChanged(nameof(ElementCountLabel));
    }

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(DirtyIndicator));
    }

    partial void OnCurrentFilePathChanged(string? value) => OnPropertyChanged(nameof(WindowTitle));

    /// <summary>Editable document name — it ends up in the report and the exported HTML.</summary>
    public string LayoutName
    {
        get => Layout?.Meta.Name ?? "";
        set
        {
            if (Layout is null || value == Layout.Meta.Name) return;
            BeforeElementEdit("문서 이름 변경");
            Layout.Meta.Name = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
            MarkDirty();
            MarkReportStale();
        }
    }

    public string ElementCountLabel => Layout is null ? "" : $"요소 {Elements.Count}개";

    // ================================================================ selection

    [ObservableProperty]
    private ElementViewModel? selectedElement;

    partial void OnSelectedElementChanged(ElementViewModel? value)
    {
        foreach (var e in Elements) e.IsSelected = ReferenceEquals(e, value);
        RefreshAdvice();
        OnPropertyChanged(nameof(HasSelection));
    }

    public bool HasSelection => SelectedElement is not null;

    public void SelectElement(ElementViewModel? element) => SelectedElement = element;

    public ElementViewModel? FindElement(string id)
        => Elements.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    // ================================================================ canvas metrics

    [ObservableProperty]
    private double zoom = 1.0;

    [ObservableProperty]
    private bool showZones;

    [ObservableProperty]
    private bool showGuides = true;

    partial void OnZoomChanged(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            Zoom = 1.0;
            return;
        }
        OnPropertyChanged(nameof(Scale));
        OnPropertyChanged(nameof(ZoomLabel));
        AppServices.Settings.Zoom = value;
    }

    partial void OnShowZonesChanged(bool value)
    {
        RebuildOverlays();
        AppServices.Settings.ShowZones = value;
    }

    partial void OnShowGuidesChanged(bool value)
    {
        RebuildOverlays();
        AppServices.Settings.ShowGuides = value;
    }

    public double CanvasWidth => Layout?.Canvas.Width ?? 0;

    public double CanvasHeight => Layout?.Canvas.Height ?? 0;

    /// <summary>Native unit → device pixels. Paper is mm at 96dpi; Screen is shrunk to fit a desk monitor.</summary>
    public double Scale => (Medium == Medium.Paper ? UnitConverter.MmToPx(1) : 0.5) * Zoom;

    public string ZoomLabel => $"{Zoom * 100:0}%";

    public double SnapStep => Medium == Medium.Paper ? 1.0 : 8.0;

    /// <summary>IElementEditHost: outline for elements that declare none, from the verified palette.</summary>
    public string DefaultBorderColor =>
        AppServices.Rules.Palette.TryGetValue("border", out var token) && !string.IsNullOrEmpty(token.Hex)
            ? token.Hex
            : "#868E96";

    [RelayCommand]
    private void ZoomIn() => Zoom = Math.Min(4.0, Math.Round(Zoom + 0.1, 2));

    [RelayCommand]
    private void ZoomOut() => Zoom = Math.Max(0.25, Math.Round(Zoom - 0.1, 2));

    [RelayCommand]
    private void ZoomReset() => Zoom = 1.0;

    /// <summary>Set by the view so [화면 맞춤] can use the real viewport size.</summary>
    public Func<(double Width, double Height)>? ViewportProvider { get; set; }

    [RelayCommand]
    private void ZoomFit()
    {
        if (Layout is null || ViewportProvider is null) return;
        var (w, h) = ViewportProvider();
        if (w <= 0 || h <= 0) return;

        var unit = Medium == Medium.Paper ? UnitConverter.MmToPx(1) : 0.5;
        var fit = Math.Min(w / (CanvasWidth * unit), h / (CanvasHeight * unit));
        Zoom = Math.Clamp(Math.Round(fit, 2), 0.25, 4.0);
        SetStatus($"화면 맞춤 — 배율 {ZoomLabel}");
    }

    public double Snap(double value)
    {
        if (!double.IsFinite(value)) return 0;
        return Math.Round(value / SnapStep) * SnapStep;
    }

    /// <summary>Keep an element reachable: it may hang off the edge, but never entirely outside.</summary>
    public (double X, double Y) ClampToCanvas(double x, double y, double w, double h)
    {
        var margin = Medium == Medium.Paper ? 5.0 : 24.0;
        var maxX = Math.Max(0, CanvasWidth - margin);
        var maxY = Math.Max(0, CanvasHeight - margin);
        return (Math.Clamp(x, -(w - margin), maxX), Math.Clamp(y, -(h - margin), maxY));
    }

    // ================================================================ status

    [ObservableProperty]
    private string statusText = "";

    [ObservableProperty]
    private StatusLevel statusLevel = StatusLevel.Info;

    /// <summary>The last problem, kept visible until the next problem replaces it — an error that
    /// scrolls away with the next routine message is an error the operator never saw.</summary>
    [ObservableProperty]
    private string lastProblem = "";

    public void SetStatus(string message, StatusLevel level = StatusLevel.Info)
    {
        StatusText = $"[{DateTime.Now:HH:mm:ss}] {message}";
        StatusLevel = level;
        if (level == StatusLevel.Info) return;

        LastProblem = StatusText;
        if (level == StatusLevel.Error) AppLog.Error(message);
        else AppLog.Warn(message);
    }

    private void NoteKnowledgeSource()
    {
        var rules = AppServices.RulesLoad;
        if (rules.Source == KnowledgeSource.File) return;
        SetStatus(rules.Source == KnowledgeSource.EmbeddedBecauseMissing
            ? "규칙 파일(rules/hfe_rules.json)을 찾지 못해 내장 기본 규칙으로 평가합니다."
            : "규칙 파일에 오류가 있어 내장 기본 규칙으로 평가합니다 — 파일의 수정 내용은 반영되지 않습니다.",
            StatusLevel.Warning);
    }

    // ================================================================ dirty / history

    public void MarkDirty()
    {
        if (_suppressDirty) return;
        IsDirty = true;
    }

    /// <summary>IElementEditHost: one undo entry per user-visible change.</summary>
    public void BeforeElementEdit(string label)
    {
        if (Layout is null || _suppressDirty) return;
        _history.Record(label, Layout);
        RaiseHistoryChanged();
    }

    /// <summary>IElementEditHost: after a change the document is dirty, the report stale, coaching refreshed.</summary>
    public void AfterElementEdit(ElementViewModel element)
    {
        MarkDirty();
        MarkReportStale();
        if (ReferenceEquals(element, SelectedElement)) RefreshAdvice();
        OnPropertyChanged(nameof(ElementCountLabel));
    }

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;
    public string UndoLabel => _history.CanUndo ? $"실행 취소: {_history.UndoLabel}" : "실행 취소할 작업 없음";
    public string RedoLabel => _history.CanRedo ? $"다시 실행: {_history.RedoLabel}" : "다시 실행할 작업 없음";

    private void RaiseHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoLabel));
        OnPropertyChanged(nameof(RedoLabel));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (Layout is null) return;
        var restored = _history.Undo(Layout, out var label);
        if (restored is null) return;
        ApplyRestored(restored, $"실행 취소: {label}");
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        if (Layout is null) return;
        var restored = _history.Redo(Layout, out var label);
        if (restored is null) return;
        ApplyRestored(restored, $"다시 실행: {label}");
    }

    private void ApplyRestored(Layout restored, string message)
    {
        var selectedId = SelectedElement?.Id;
        _suppressDirty = true;
        try
        {
            SetLayout(restored, CurrentFilePath, resetHistory: false, keepReport: true);
        }
        finally
        {
            _suppressDirty = false;
        }
        IsDirty = _savedSnapshot is null || LayoutSerializer.Save(restored) != _savedSnapshot;
        if (selectedId is not null) SelectedElement = FindElement(selectedId);
        RaiseHistoryChanged();
        SetStatus(message);
    }

    // ================================================================ document commands

    [RelayCommand]
    private void NewPaper() => CreateNew(Medium.Paper);

    [RelayCommand]
    private void NewScreen() => CreateNew(Medium.Screen);

    private void CreateNew(Medium medium)
    {
        if (!ConfirmDiscard()) return;

        var layout = medium == Medium.Paper
            ? new Layout
            {
                Meta = new LayoutMeta { Name = "새 검사 양식", Medium = Medium.Paper, CreatedAt = DateTimeOffset.Now },
                Canvas = new CanvasSpec
                {
                    Width = 210,
                    Height = 297,
                    Margins = new Margins { Top = 15, Bottom = 15, Left = 20, Right = 12 },
                },
            }
            : new Layout
            {
                Meta = new LayoutMeta { Name = "새 MES 화면", Medium = Medium.Screen, CreatedAt = DateTimeOffset.Now },
                Canvas = new CanvasSpec
                {
                    Width = 1920,
                    Height = 1080,
                    ContentPaddingPx = 24,
                    FixedRegions = new FixedRegions(),
                    IsScrollable = true,
                },
            };

        SetLayout(layout, null, resetHistory: true);
        IsDirty = false;
        SetStatus($"{(medium == Medium.Paper ? "종이 양식" : "MES 화면")} 새로 만들기 — 왼쪽 팔레트에서 요소를 골라 배치하십시오.");
    }

    [RelayCommand]
    private void Open()
    {
        if (!ConfirmDiscard()) return;

        var path = _dialogs.AskOpenPath(AppServices.Settings.LastLayoutDirectory
                                        ?? AppServices.DirectoryOf("samples/incoming_inspection_paper.hfelayout.json"));
        if (path is null) return;
        OpenPath(path);
    }

    public void OpenPath(string path)
    {
        try
        {
            var layout = LayoutSerializer.LoadFile(path);
            SetLayout(layout, path, resetHistory: true);
            IsDirty = false;
            _savedSnapshot = LayoutSerializer.Save(layout);
            RememberDirectory(d => AppServices.Settings.LastLayoutDirectory = d, path);
            AppServices.Settings.RememberRecent(path);
            PersistSettings();
            SetStatus($"열기 완료: {Path.GetFileName(path)} (요소 {Elements.Count}개)");
        }
        catch (Exception ex)
        {
            AppLog.Error("레이아웃 열기 실패: " + path, ex);
            _dialogs.ShowError("레이아웃 열기 실패", Explain(ex, path));
            SetStatus("레이아웃을 열지 못했습니다: " + ex.Message, StatusLevel.Error);
        }
    }

    [RelayCommand]
    private void Save() => SaveTo(CurrentFilePath);

    [RelayCommand]
    private void SaveAs() => SaveTo(null);

    /// <summary>Returns true when the layout is safely on disk.</summary>
    public bool SaveTo(string? path)
    {
        if (Layout is null) return false;

        if (path is null)
        {
            var suggested = SuggestFileName();
            path = _dialogs.AskSavePath(suggested,
                AppServices.Settings.LastLayoutDirectory ?? Path.GetDirectoryName(CurrentFilePath));
            if (path is null) return false;
        }

        try
        {
            LayoutSerializer.SaveFile(Layout, path);
            CurrentFilePath = path;
            IsDirty = false;
            _savedSnapshot = LayoutSerializer.Save(Layout);
            AutosaveService.Clear();
            RememberDirectory(d => AppServices.Settings.LastLayoutDirectory = d, path);
            AppServices.Settings.RememberRecent(path);
            PersistSettings();
            SetStatus($"저장 완료: {Path.GetFileName(path)}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("레이아웃 저장 실패: " + path, ex);
            _dialogs.ShowError("레이아웃 저장 실패", Explain(ex, path));
            SetStatus("저장하지 못했습니다: " + ex.Message, StatusLevel.Error);
            return false;
        }
    }

    private string SuggestFileName()
    {
        if (CurrentFilePath is not null) return Path.GetFileName(CurrentFilePath);
        var name = string.IsNullOrWhiteSpace(Layout?.Meta.Name) ? "layout" : Layout!.Meta.Name;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Trim() + ".hfelayout.json";
    }

    [RelayCommand]
    private void GenerateFromCatalog()
    {
        if (!ConfirmDiscard()) return;

        var medium = _dialogs.AskMedium();
        if (medium is null) return;

        var path = _dialogs.AskCatalogPath(AppServices.Settings.LastCatalogDirectory
                                           ?? AppServices.DirectoryOf("catalog/incoming_inspection.catalog.json"));
        if (path is null) return;

        try
        {
            var catalog = InspectionCatalog.LoadFile(path);
            var composed = new LayoutComposer(AppServices.Presets, AppServices.Rules).Compose(catalog, medium.Value);
            SetLayout(composed, null, resetHistory: true);
            IsDirty = true;
            RememberDirectory(d => AppServices.Settings.LastCatalogDirectory = d, path);
            PersistSettings();
            SetStatus($"카탈로그로 생성 완료: {catalog.ProcessName} (요소 {Elements.Count}개) — [평가 실행]으로 확인하십시오.");
        }
        catch (Exception ex)
        {
            AppLog.Error("카탈로그 생성 실패: " + path, ex);
            _dialogs.ShowError("카탈로그로 생성 실패", Explain(ex, path));
            SetStatus("카탈로그로 생성하지 못했습니다: " + ex.Message, StatusLevel.Error);
        }
    }

    /// <summary>
    /// Replaces the document. Every path that discards work goes through ConfirmDiscard first —
    /// this method itself never asks, so it can also be used by undo/redo and recovery.
    /// </summary>
    /// <param name="keepReport">
    /// True when the document's identity is unchanged (undo/redo of an edit): the findings stay on
    /// screen under the stale banner, because a user working through a list of 37 findings must not
    /// lose the list for pressing Ctrl+Z. A new/opened/generated document clears it — it describes
    /// something else now.
    /// </param>
    private void SetLayout(Layout newLayout, string? path, bool resetHistory, bool keepReport = false)
    {
        // A slot left over from a document the user has just replaced would be offered back after
        // the next crash as if it were this session's work.
        if (!keepReport)
        {
            AutosaveService.Clear();
            _savedSnapshot = null;
        }

        DetachElements();
        Layout = newLayout;
        CurrentFilePath = path;
        _addCascade = 0;

        Elements.Clear();
        foreach (var element in newLayout.Elements)
            Elements.Add(new ElementViewModel(element, this));

        SelectedElement = null;
        if (keepReport) MarkReportStale();
        else ClearReport();
        RefreshPalette();
        RebuildOverlays();

        if (resetHistory)
        {
            _history.Clear();
            RaiseHistoryChanged();
        }

        OnPropertyChanged(nameof(ElementCountLabel));
        OnPropertyChanged(nameof(LayoutName));
        OnPropertyChanged(nameof(Scale));
    }

    private void DetachElements()
    {
        foreach (var element in Elements) element.IsSelected = false;
    }

    /// <summary>False means "the user cancelled — do not continue".</summary>
    public bool ConfirmDiscard()
    {
        if (Layout is null || !IsDirty) return true;

        return _dialogs.AskUnsavedChanges(Layout.Meta.Name, Elements.Count) switch
        {
            DiscardChoice.Save => SaveTo(CurrentFilePath),
            DiscardChoice.Discard => true,
            _ => false,
        };
    }

    // ================================================================ editing

    public ObservableCollection<PresetDefinition> PaletteItems { get; } = new();

    private void RefreshPalette()
    {
        PaletteItems.Clear();
        if (Layout is null) return;
        foreach (var preset in AppServices.Presets.All.Where(p => p.SupportsMedium(Layout.Medium)))
            PaletteItems.Add(preset);
    }

    public void AddPreset(PresetDefinition preset)
    {
        if (Layout is null)
        {
            SetStatus("먼저 새 양식을 만들거나 레이아웃을 여십시오.", StatusLevel.Warning);
            return;
        }

        try
        {
            var (x, y) = NextPlacement(preset);
            var element = AppServices.Presets.Instantiate(preset.Id, Layout.Medium, x, y);
            element.Id = UniqueId(element.Id);

            BeforeElementEdit($"'{preset.Name}' 배치");
            Layout.Elements.Add(element);

            var vm = new ElementViewModel(element, this);
            Elements.Add(vm);
            SelectedElement = vm;
            MarkDirty();
            MarkReportStale();
            OnPropertyChanged(nameof(ElementCountLabel));
            SetStatus($"'{preset.Name}' 배치 — 드래그로 이동, 우측 [코칭] 탭에서 위치 진단을 확인하십시오.");
        }
        catch (Exception ex)
        {
            AppLog.Error($"요소 배치 실패: {preset.Id}", ex);
            SetStatus($"'{preset.Name}' 을(를) 배치하지 못했습니다: {ex.Message}", StatusLevel.Error);
        }
    }

    /// <summary>
    /// Cascades new elements so they do not stack exactly, and keeps them fully inside the content
    /// area — a preset wider than the remaining space is placed at the content origin instead of
    /// being pushed off the page.
    /// </summary>
    private (double X, double Y) NextPlacement(PresetDefinition preset)
    {
        var content = new EvaluationContext(Layout!, AppServices.Rules).ContentRect;
        var size = preset.Size[Medium == Medium.Paper ? "paper" : "screen"];
        var w = size[0];
        var h = size.Length > 1 ? size[1] : size[0];

        var step = SnapStep * (Medium == Medium.Paper ? 4 : 3);
        var offset = _addCascade * step;
        var maxOffsetX = Math.Max(0, content.W - w);
        var maxOffsetY = Math.Max(0, content.H - h);

        // wrap the cascade instead of repeating the same slot every ninth add
        var ox = maxOffsetX <= 0 ? 0 : offset % (maxOffsetX + step / 2);
        var oy = maxOffsetY <= 0 ? 0 : offset % (maxOffsetY + step / 2);
        _addCascade = (_addCascade + 1) % 64;

        return (Snap(content.X + Math.Min(ox, maxOffsetX)), Snap(content.Y + Math.Min(oy, maxOffsetY)));
    }

    private string UniqueId(string baseId)
    {
        if (Layout!.Elements.All(e => e.Id != baseId)) return baseId;
        for (var n = 2; ; n++)
        {
            var candidate = $"{baseId}-{n}";
            if (Layout.Elements.All(e => e.Id != candidate)) return candidate;
        }
    }

    public bool CanDeleteSelected => SelectedElement is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private void DeleteSelected()
    {
        if (Layout is null || SelectedElement is null) return;

        var target = SelectedElement;
        if (!_dialogs.Confirm("요소 삭제",
                $"'{target.Id}' ({target.TypeLabel}) 요소를 삭제합니다.\n실행 취소(Ctrl+Z)로 되돌릴 수 있습니다.",
                "요소 삭제", "삭제 취소"))
            return;

        BeforeElementEdit($"'{target.Id}' 삭제");
        Layout.Elements.Remove(target.Model);
        Elements.Remove(target);
        SelectedElement = null;
        MarkDirty();
        MarkReportStale();
        OnPropertyChanged(nameof(ElementCountLabel));
        SetStatus($"요소 '{target.Id}' 삭제 — 되돌리려면 Ctrl+Z.");
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private void DuplicateSelected()
    {
        if (Layout is null || SelectedElement is null) return;

        var source = SelectedElement.Model;
        var clone = LayoutSerializer.Load(
            LayoutSerializer.Save(new Layout { Meta = Layout.Meta, Canvas = Layout.Canvas, Elements = { source } }),
            "duplicate").Elements[0];

        clone.Id = UniqueId(source.Id);
        var offset = SnapStep * 2;
        // Clamp like every other move: a clone nudged past the edge would be scored but unclickable,
        // and there is no element list to select it from.
        var (cx, cy) = ClampToCanvas(source.X + offset, source.Y + offset,
            SelectedElement.DisplayW, SelectedElement.DisplayH);
        clone.X = Snap(cx);
        clone.Y = Snap(cy);

        BeforeElementEdit($"'{source.Id}' 복제");
        Layout.Elements.Add(clone);
        var vm = new ElementViewModel(clone, this);
        Elements.Add(vm);
        SelectedElement = vm;
        MarkDirty();
        MarkReportStale();
        OnPropertyChanged(nameof(ElementCountLabel));
        SetStatus($"'{clone.Id}' (으)로 복제했습니다.");
    }

    /// <summary>Arrow-key nudge: one snap step, or ten with Shift.</summary>
    public void NudgeSelected(double dx, double dy, bool large)
    {
        if (SelectedElement is null) return;
        var step = SnapStep * (large ? 10 : 1);
        var element = SelectedElement;

        element.BeginGesture("요소 이동");
        var (x, y) = ClampToCanvas(element.X + dx * step, element.Y + dy * step, element.DisplayW, element.DisplayH);
        element.SetGeometry(Snap(x), Snap(y), element.W, element.H);
        element.EndGesture();
        MarkDirty();
        MarkReportStale();
    }

    // ================================================================ coaching

    public ObservableCollection<AdviceItem> Advice { get; } = new();

    [ObservableProperty]
    private string adviceHeader = "요소를 선택하면 위치·색상·폰트·크기·순서 진단이 표시됩니다.";

    private CancellationTokenSource? _adviceCts;

    /// <summary>
    /// Coaching runs the whole rule engine, which is milliseconds on a small form but noticeable on
    /// a 500-element one. It therefore runs off the UI thread against a snapshot, and a newer
    /// selection cancels the previous request so clicking through elements never queues up work.
    /// </summary>
    private void RefreshAdvice()
    {
        _adviceCts?.Cancel();
        Advice.Clear();

        if (Layout is null || SelectedElement is null)
        {
            AdviceHeader = Layout is null
                ? "레이아웃을 열면 요소별 배치 코칭을 볼 수 있습니다."
                : "요소를 선택하면 위치·색상·폰트·크기·순서 진단이 표시됩니다.";
            return;
        }

        var element = SelectedElement;
        AdviceHeader = $"{element.Id} ({element.TypeLabel}) · {element.GeometrySummary}";

        string snapshotJson;
        try
        {
            snapshotJson = LayoutSerializer.Save(Layout);
        }
        catch (Exception ex)
        {
            AppLog.Error("코칭용 스냅샷 실패", ex);
            return;
        }

        var cts = new CancellationTokenSource();
        _adviceCts = cts;
        var elementId = element.Id;

        _ = RunAdviceAsync(snapshotJson, elementId, cts);
    }

    private async Task RunAdviceAsync(string snapshotJson, string elementId, CancellationTokenSource cts)
    {
        try
        {
            var advice = await Task.Run(() =>
            {
                var snapshot = LayoutSerializer.Load(snapshotJson, "advice");
                return AppServices.Advisor.Advise(snapshot, elementId).ToList();
            }, cts.Token);

            if (cts.IsCancellationRequested || !ReferenceEquals(_adviceCts, cts)) return;

            Advice.Clear();
            foreach (var item in advice) Advice.Add(item);
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer selection
        }
        catch (Exception ex)
        {
            AppLog.Error("배치 코칭 실패: " + elementId, ex);
            if (ReferenceEquals(_adviceCts, cts))
                SetStatus("배치 코칭을 계산하지 못했습니다: " + ex.Message, StatusLevel.Warning);
        }
        finally
        {
            if (ReferenceEquals(_adviceCts, cts)) _adviceCts = null;
            cts.Dispose();
        }
    }

    // ================================================================ autosave / settings

    private void Autosave()
    {
        if (Layout is null || !IsDirty || IsEvaluating) return;
        if (AutosaveService.TryWrite(Layout, CurrentFilePath)) return;

        _autosaveTimer.Stop();
        SetStatus("자동 저장에 실패해 중단했습니다 — 직접 저장(Ctrl+S)하십시오.", StatusLevel.Error);
    }

    private void PersistSettings()
    {
        if (SettingsService.TrySave(AppServices.Settings) || _settingsSaveFailed) return;
        _settingsSaveFailed = true;
        SetStatus("설정을 저장하지 못했습니다 — 다음 실행에서 창 크기·최근 파일이 복원되지 않을 수 있습니다.",
            StatusLevel.Warning);
    }

    /// <summary>Called by the window as it closes; returns false to cancel the close.</summary>
    public bool PrepareToClose(double width, double height, bool maximized)
    {
        if (!ConfirmDiscard()) return false;

        _autosaveTimer.Stop();
        AutosaveService.Clear();

        AppServices.Settings.WindowWidth = width;
        AppServices.Settings.WindowHeight = height;
        AppServices.Settings.WindowMaximized = maximized;
        PersistSettings();
        return true;
    }

    /// <summary>Offers back the layout an interrupted previous run left behind.</summary>
    public void OfferRecovery()
    {
        var pending = AutosaveService.FindPending();
        if (pending is null) return;

        var origin = pending.OriginalPath is null ? "저장된 적 없는 새 레이아웃" : Path.GetFileName(pending.OriginalPath);
        var restore = _dialogs.Confirm("이전 작업 복구",
            $"이전 실행이 정상 종료되지 않았습니다.\n\n" +
            $"레이아웃: {pending.LayoutName}\n원본: {origin}\n자동 저장 시각: {pending.SavedAt:yyyy-MM-dd HH:mm:ss}\n\n" +
            "이 작업을 복구하시겠습니까?",
            "작업 복구", "복구하지 않음");

        if (!restore)
        {
            // Declining — or dismissing the prompt with Esc — sets the snapshot aside rather than
            // deleting it, and says where, so one keystroke can never be the end of an hour's work.
            var parked = AutosaveService.Park();
            SetStatus(parked is null
                ? "이전 자동 저장본을 복구하지 않았습니다."
                : $"이전 자동 저장본을 복구하지 않고 보관했습니다: {parked}", StatusLevel.Warning);
            return;
        }

        try
        {
            var layout = LayoutSerializer.LoadFile(pending.SlotPath);
            SetLayout(layout, pending.OriginalPath, resetHistory: true);
            IsDirty = true;
            SetStatus($"이전 작업을 복구했습니다 ({pending.SavedAt:HH:mm:ss} 시점) — 저장(Ctrl+S)하십시오.",
                StatusLevel.Warning);
        }
        catch (Exception ex)
        {
            AppLog.Error("자동 저장본 복구 실패", ex);
            _dialogs.ShowError("복구 실패", "자동 저장본을 읽지 못했습니다.\n\n" + ex.Message);
            AutosaveService.Clear();
        }
    }

    private static void RememberDirectory(Action<string?> set, string path)
    {
        try { set(Path.GetDirectoryName(Path.GetFullPath(path))); } catch { /* keep previous */ }
    }

    /// <summary>Turns an exception into something a QC manager can act on.</summary>
    private static string Explain(Exception ex, string path) => ex switch
    {
        UnauthorizedAccessException =>
            $"파일에 접근할 권한이 없습니다.\n\n{path}\n\n다른 폴더(예: 내 문서)를 선택하거나 관리자에게 문의하십시오.",
        DirectoryNotFoundException =>
            $"폴더를 찾을 수 없습니다.\n\n{path}\n\n네트워크 드라이브라면 연결 상태를 확인하십시오.",
        IOException io when io.Message.Contains("space", StringComparison.OrdinalIgnoreCase) =>
            $"디스크 공간이 부족합니다.\n\n{path}",
        IOException io =>
            $"파일을 사용할 수 없습니다 (다른 프로그램이 열고 있을 수 있습니다).\n\n{path}\n\n{io.Message}",
        LayoutFormatException or CatalogLoadException or PresetLoadException =>
            $"{ex.Message}\n\n파일: {path}",
        _ => $"{ex.Message}\n\n파일: {path}\n진단 로그: {AppLog.FilePath}",
    };
}
