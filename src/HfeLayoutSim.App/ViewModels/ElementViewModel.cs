using CommunityToolkit.Mvvm.ComponentModel;
using HfeLayoutSim.Core.Engine;
using HfeLayoutSim.Core.Model;

namespace HfeLayoutSim.App.ViewModels;

/// <summary>What an element editor needs from the document it belongs to.</summary>
public interface IElementEditHost
{
    Medium Medium { get; }

    /// <summary>Keeps a typed coordinate reachable — an element pushed off-canvas cannot be clicked back.</summary>
    (double X, double Y) ClampToCanvas(double x, double y, double w, double h);

    /// <summary>Outline colour for elements that declare none — read from the rule palette, not hardcoded.</summary>
    string DefaultBorderColor { get; }

    /// <summary>Called once immediately before a property mutation, so it can be undone.</summary>
    void BeforeElementEdit(string label);

    /// <summary>Called after a mutation: marks the document dirty and refreshes coaching.</summary>
    void AfterElementEdit(ElementViewModel element);
}

/// <summary>
/// Editable view of one <see cref="LayoutElement"/>.
/// Geometry, style AND semantics are all editable, because half of the HFE rules are evaluated
/// against semantics (SPEC §5) — an element whose role, order or criticality cannot be set is an
/// element whose findings the operator cannot act on.
/// Every setter records one undo step and re-runs the placement coach.
/// </summary>
public sealed partial class ElementViewModel : ObservableObject
{
    public LayoutElement Model { get; }
    private readonly IElementEditHost _host;

    /// <summary>True while a drag/resize is in progress: geometry changes are coalesced into one undo step.</summary>
    private bool _suspendHistory;

    public ElementViewModel(LayoutElement model, IElementEditHost host)
    {
        Model = model;
        _host = host;
    }

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isHighlighted;

    public string Id => Model.Id;

    public ElementType Type => Model.Type;

    public Medium Medium => _host.Medium;

    // ---------------------------------------------------------------- geometry

    public double X
    {
        get => Model.X;
        set => Edit("요소 이동", () =>
        {
            var (x, _) = _host.ClampToCanvas(Sanitize(value, Model.X), Model.Y, DisplayW, DisplayH);
            Model.X = x;
        }, nameof(X), nameof(GeometrySummary));
    }

    public double Y
    {
        get => Model.Y;
        set => Edit("요소 이동", () =>
        {
            var (_, y) = _host.ClampToCanvas(Model.X, Sanitize(value, Model.Y), DisplayW, DisplayH);
            Model.Y = y;
        }, nameof(Y), nameof(GeometrySummary));
    }

    public double W
    {
        get => Model.W;
        set => Edit("크기 변경", () => Model.W = Math.Max(MinSize, Sanitize(value, Model.W)),
            nameof(W), nameof(DisplayW), nameof(GeometrySummary));
    }

    public double H
    {
        get => Model.H;
        set => Edit("크기 변경", () => Model.H = Math.Max(MinSize, Sanitize(value, Model.H)),
            nameof(H), nameof(DisplayH), nameof(GeometrySummary));
    }

    /// <summary>Paint order; the canvas binds Panel.ZIndex to it so what is drawn matches what is scored.</summary>
    public int Z
    {
        get => Model.Z;
        set => Edit("겹침 순서 변경", () => Model.Z = value, nameof(Z));
    }

    public int Rotation
    {
        get => Model.Rotation;
        set => Edit("회전", () => Model.Rotation = value == 90 ? 90 : 0,
            nameof(Rotation), nameof(DisplayW), nameof(DisplayH));
    }

    /// <summary>Smallest sensible extent: 0.5mm on paper, 2px on screen — never zero or negative.</summary>
    private double MinSize => Medium == Medium.Paper ? 0.5 : 2;

    /// <summary>A text box can produce NaN/∞ via binding; keep the previous value rather than corrupting the model.</summary>
    private static double Sanitize(double value, double previous)
        => double.IsFinite(value) ? value : previous;

    /// <summary>Width as drawn (honours 90° rotation).</summary>
    public double DisplayW => Model.OrientedSize.W;

    public double DisplayH => Model.OrientedSize.H;

    /// <summary>Move/resize without one undo step per mouse move; the view brackets the gesture.</summary>
    public void BeginGesture(string label)
    {
        _host.BeforeElementEdit(label);
        _suspendHistory = true;
    }

    public void EndGesture()
    {
        _suspendHistory = false;
        _host.AfterElementEdit(this);
    }

    public void SetGeometry(double x, double y, double w, double h)
    {
        Model.X = Sanitize(x, Model.X);
        Model.Y = Sanitize(y, Model.Y);
        Model.W = Math.Max(MinSize, Sanitize(w, Model.W));
        Model.H = Math.Max(MinSize, Sanitize(h, Model.H));
        NotifyGeometry();
    }

    public void NotifyGeometry()
    {
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(W));
        OnPropertyChanged(nameof(H));
        OnPropertyChanged(nameof(DisplayW));
        OnPropertyChanged(nameof(DisplayH));
        OnPropertyChanged(nameof(GeometrySummary));
    }

    public string GeometrySummary => Medium == Medium.Paper
        ? $"{Model.X:0.#}, {Model.Y:0.#} · {DisplayW:0.#}×{DisplayH:0.#} mm"
        : $"{Model.X:0}, {Model.Y:0} · {DisplayW:0}×{DisplayH:0} px";

    // ---------------------------------------------------------------- content & style

    public string? Text
    {
        get => Model.Text;
        set => Edit("문구 변경", () => Model.Text = value, nameof(Text), nameof(DisplayText));
    }

    public string? FontFamily
    {
        get => Model.Style.FontFamily;
        set => Edit("서체 변경", () => Model.Style.FontFamily = Blank(value),
            nameof(FontFamily), nameof(DisplayFontFamily));
    }

    public double? FontSize
    {
        get => Model.Style.FontSize;
        set => Edit("글자 크기 변경",
            () => Model.Style.FontSize = value is null || !double.IsFinite(value.Value) || value <= 0
                ? null
                : value,
            nameof(FontSize), nameof(DisplayFontSize));
    }

    public bool Bold
    {
        get => Model.Style.Bold;
        set => Edit("굵게", () => Model.Style.Bold = value, nameof(Bold), nameof(DisplayFontWeightBold));
    }

    public bool Italic
    {
        get => Model.Style.Italic;
        set => Edit("이탤릭", () => Model.Style.Italic = value, nameof(Italic));
    }

    public bool Underline
    {
        get => Model.Style.Underline;
        set => Edit("밑줄", () => Model.Style.Underline = value, nameof(Underline));
    }

    public string? FgColor
    {
        get => Model.Style.FgColor;
        set => Edit("전경색 변경", () => Model.Style.FgColor = Blank(value), nameof(FgColor), nameof(TextColor));
    }

    public string? BgColor
    {
        get => Model.Style.BgColor;
        set => Edit("배경색 변경", () => Model.Style.BgColor = Blank(value), nameof(BgColor), nameof(FillColor));
    }

    public string? BorderColor
    {
        get => Model.Style.BorderColor;
        set => Edit("테두리색 변경", () => Model.Style.BorderColor = Blank(value),
            nameof(BorderColor), nameof(StrokeColor));
    }

    // ---------------------------------------------------------------- semantics (rule inputs)

    public SemanticRole Role
    {
        get => Model.Semantics.Role;
        set => Edit("역할 변경", () => Model.Semantics.Role = value,
            nameof(Role), nameof(Tooltip), nameof(RoleLabel));
    }

    public int Sequence
    {
        get => Model.Semantics.Sequence;
        set => Edit("입력 순서 변경", () => Model.Semantics.Sequence = Math.Max(0, value), nameof(Sequence));
    }

    public string? GroupId
    {
        get => Model.GroupId ?? Model.Semantics.GroupId;
        set => Edit("그룹 변경", () =>
        {
            Model.GroupId = Blank(value);
            Model.Semantics.GroupId = Blank(value);
        }, nameof(GroupId));
    }

    public bool IsCritical
    {
        get => Model.Semantics.IsCritical;
        set => Edit("안전 관련 표시", () => Model.Semantics.IsCritical = value,
            nameof(IsCritical), nameof(Tooltip));
    }

    public bool IsRequired
    {
        get => Model.Semantics.IsRequired;
        set => Edit("필수 표시", () => Model.Semantics.IsRequired = value, nameof(IsRequired));
    }

    public bool IsDestructiveAction
    {
        get => Model.Semantics.IsDestructiveAction;
        set => Edit("파괴적 행위 표시", () => Model.Semantics.IsDestructiveAction = value, nameof(IsDestructiveAction));
    }

    public InputKind InputKind
    {
        get => Model.Semantics.InputKind;
        set => Edit("입력 방식 변경", () => Model.Semantics.InputKind = value, nameof(InputKind));
    }

    public bool SpecLimitShown
    {
        get => Model.Semantics.SpecLimitShown;
        set => Edit("규격 병기", () => Model.Semantics.SpecLimitShown = value, nameof(SpecLimitShown));
    }

    // Judgment triple coding (C1-04): colour + icon + text, so grayscale printing still distinguishes.

    public bool HasJudgmentColor
    {
        get => Model.Semantics.Judgment?.HasColor ?? false;
        set => Edit("판정 색상 코딩", () => Judgment().HasColor = value, nameof(HasJudgmentColor));
    }

    public bool HasJudgmentIcon
    {
        get => Model.Semantics.Judgment?.HasIcon ?? false;
        set => Edit("판정 아이콘 코딩", () => Judgment().HasIcon = value, nameof(HasJudgmentIcon));
    }

    public bool HasJudgmentText
    {
        get => Model.Semantics.Judgment?.HasText ?? false;
        set => Edit("판정 텍스트 코딩", () => Judgment().HasText = value, nameof(HasJudgmentText));
    }

    /// <summary>Judgment coding only applies to verdict-bearing elements.</summary>
    public bool IsJudgmentElement
        => Model.Type == ElementType.JudgmentBadge || Model.Semantics.Judgment is not null ||
           Model.Semantics.Role == SemanticRole.FinalVerdict;

    private JudgmentCoding Judgment() => Model.Semantics.Judgment ??= new JudgmentCoding();

    // ---------------------------------------------------------------- type-specific

    public string? Unit
    {
        get => Model.Unit;
        set => Edit("단위 변경", () => Model.Unit = Blank(value), nameof(Unit));
    }

    public int? OptionCount
    {
        get => Model.OptionCount;
        set => Edit("선택지 수 변경",
            () => Model.OptionCount = value is null ? null : Math.Max(0, value.Value), nameof(OptionCount));
    }

    public ButtonKind? ButtonKind
    {
        get => Model.ButtonKind;
        set => Edit("버튼 종류 변경", () => Model.ButtonKind = value, nameof(ButtonKind));
    }

    public int? Rows
    {
        get => Model.Rows;
        set => Edit("표 행 수 변경", () => Model.Rows = value is null ? null : Math.Max(1, value.Value), nameof(Rows));
    }

    public int? Columns
    {
        get => Model.Columns;
        set => Edit("표 열 수 변경",
            () => Model.Columns = value is null ? null : Math.Max(1, value.Value), nameof(Columns));
    }

    public string? SignerRole
    {
        get => Model.SignerRole;
        set => Edit("서명자 역할 변경", () => Model.SignerRole = Blank(value), nameof(SignerRole));
    }

    public string? DefaultValue
    {
        get => Model.DefaultValue;
        set => Edit("기본 선택값 변경", () => Model.DefaultValue = Blank(value), nameof(DefaultValue));
    }

    // Which extra editors the property panel should show for this element type.
    public bool ShowsUnit => Model.Type == ElementType.NumInput;
    public bool ShowsOptionCount => Model.Type == ElementType.RadioGroup;
    public bool ShowsButtonKind => Model.Type == ElementType.Button;
    public bool ShowsTableSize => Model.Type == ElementType.Table;
    public bool ShowsSignerRole => Model.Type == ElementType.SignatureBox;
    public bool ShowsDefaultValue =>
        Model.Type is ElementType.RadioGroup or ElementType.Checkbox or ElementType.JudgmentBadge;

    // ---------------------------------------------------------------- canvas presentation

    public string DisplayText => string.IsNullOrWhiteSpace(Model.Text)
        ? $"[{EnumCatalog.TypeLabel(Model.Type)}]"
        : Model.Text!;

    public string DisplayFontFamily =>
        string.IsNullOrWhiteSpace(Model.Style.FontFamily) ? "Segoe UI" : Model.Style.FontFamily!;

    /// <summary>Font size expressed in canvas units so it scales with the zoom transform.</summary>
    public double DisplayFontSize
    {
        get
        {
            if (Medium == Medium.Paper)
            {
                var pt = Model.Style.FontSize ?? 10;
                return Math.Clamp(UnitConverter.PtToMm(pt), 1.0, 60);
            }
            return Math.Clamp(Model.Style.FontSize ?? 14, 4, 200);
        }
    }

    public bool DisplayFontWeightBold => Model.Style.Bold;

    /// <summary>
    /// An element without a declared background is transparent, not white: painting it white hid
    /// the POA/SFA/WFA/TA overlay the element is being judged against, and the engine treats an
    /// undeclared fill as see-through too.
    /// </summary>
    public string FillColor =>
        string.IsNullOrWhiteSpace(Model.Style.BgColor) ? "#00000000" : Model.Style.BgColor!;

    /// <summary>Undeclared outlines use the verified border token so the element stays visible (3.32:1).</summary>
    public string StrokeColor =>
        string.IsNullOrWhiteSpace(Model.Style.BorderColor) ? _host.DefaultBorderColor : Model.Style.BorderColor!;

    public string TextColor =>
        string.IsNullOrWhiteSpace(Model.Style.FgColor) ? "#212529" : Model.Style.FgColor!;

    public string RoleLabel => EnumCatalog.RoleLabel(Model.Semantics.Role);

    public string TypeLabel => EnumCatalog.TypeLabel(Model.Type);

    public string Tooltip
    {
        get
        {
            var role = Model.Semantics.Role == SemanticRole.None ? "" : $" · {RoleLabel}";
            var critical = Model.Semantics.IsCritical ? " · 안전 관련" : "";
            return $"{Model.Id} ({TypeLabel}{role}{critical})";
        }
    }

    /// <summary>Refresh everything the property panel and canvas read (after undo/redo or a reload).</summary>
    public void RefreshAll()
    {
        OnPropertyChanged(string.Empty);
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// One mutation = one undo step + the property notifications it invalidates + a coaching refresh.
    /// Routing every setter through here is what makes "every edit is undoable and re-coached" true
    /// by construction rather than by remembering to wire each property.
    /// </summary>
    private void Edit(string label, Action apply, params string[] changedProperties)
    {
        if (!_suspendHistory) _host.BeforeElementEdit(label);
        apply();
        foreach (var name in changedProperties) OnPropertyChanged(name);
        if (!_suspendHistory) _host.AfterElementEdit(this);
    }
}
