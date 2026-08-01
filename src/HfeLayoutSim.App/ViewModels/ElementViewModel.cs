using CommunityToolkit.Mvvm.ComponentModel;
using HfeLayoutSim.Core.Engine;
using HfeLayoutSim.Core.Model;

namespace HfeLayoutSim.App.ViewModels;

/// <summary>Wraps one LayoutElement for canvas binding. Geometry stays in native units (mm/px).</summary>
public partial class ElementViewModel : ObservableObject
{
    public LayoutElement Model { get; }
    private readonly Medium _medium;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isHighlighted;

    public ElementViewModel(LayoutElement model, Medium medium)
    {
        Model = model;
        _medium = medium;
    }

    public string Id => Model.Id;

    public double X
    {
        get => Model.X;
        set { Model.X = value; OnPropertyChanged(); }
    }

    public double Y
    {
        get => Model.Y;
        set { Model.Y = value; OnPropertyChanged(); }
    }

    public double W
    {
        get => Model.W;
        set { Model.W = Math.Max(1, value); OnPropertyChanged(); }
    }

    public double H
    {
        get => Model.H;
        set { Model.H = Math.Max(1, value); OnPropertyChanged(); }
    }

    public string? Text
    {
        get => Model.Text;
        set { Model.Text = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    public string DisplayText => string.IsNullOrWhiteSpace(Model.Text) ? $"[{Model.Type}]" : Model.Text!;

    /// <summary>Font size in canvas (native) units so it scales with the zoom transform.</summary>
    public double DisplayFontSize
    {
        get
        {
            if (_medium == Medium.Paper)
            {
                var pt = Model.Style.FontSize ?? 10;
                return Math.Max(2.4, UnitConverter.PtToMm(pt));
            }
            return Model.Style.FontSize ?? 14;
        }
    }

    public string FillColor
        => !string.IsNullOrWhiteSpace(Model.Style.BgColor) ? Model.Style.BgColor! : "#FFFFFF";

    public string StrokeColor
        => !string.IsNullOrWhiteSpace(Model.Style.BorderColor) ? Model.Style.BorderColor! : "#ADB5BD";

    public string TextColor
        => !string.IsNullOrWhiteSpace(Model.Style.FgColor) ? Model.Style.FgColor! : "#212529";

    public bool IsBold => Model.Style.Bold;

    public string Tooltip
    {
        get
        {
            var role = Model.Semantics.Role == SemanticRole.None ? "" : $" · {Model.Semantics.Role}";
            var crit = Model.Semantics.IsCritical ? " · CRITICAL" : "";
            return $"{Model.Id} ({Model.Type}{role}{crit})";
        }
    }

    public void NotifyGeometryChanged()
    {
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(W));
        OnPropertyChanged(nameof(H));
    }
}
