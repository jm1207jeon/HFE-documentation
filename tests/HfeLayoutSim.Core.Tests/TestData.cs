using HfeLayoutSim.Core.Engine;
using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Report;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Tests;

/// <summary>Shared fixtures: the real knowledge base and a layout builder DSL.</summary>
public static class TestData
{
    public static string BaseDir => AppContext.BaseDirectory;

    public static RuleSet Rules { get; } =
        RuleLoader.LoadFile(Path.Combine(BaseDir, "rules", "hfe_rules.json"));

    public static HfeEngine Engine { get; } = new();

    public static EvaluationReport Evaluate(Layout layout) => Engine.Evaluate(layout, Rules);

    /// <summary>Run the full engine and project the outcome of a single rule.</summary>
    public static (RuleStatus Status, List<Finding> Findings) Run(string ruleId, Layout layout)
    {
        var report = Engine.Evaluate(layout, Rules);
        var outcome = report.RuleOutcomes.Single(o => o.RuleId == ruleId);
        return (outcome.Status, report.Findings.Where(f => f.RuleId == ruleId).ToList());
    }

    public static Layout LoadSample(string fileName)
        => LayoutSerializer.LoadFile(Path.Combine(BaseDir, "samples", fileName));
}

/// <summary>Terse builder for test layouts with realistic canvas defaults.</summary>
public sealed class LayoutBuilder
{
    private readonly Layout _layout;
    private int _auto;

    private LayoutBuilder(Layout layout) => _layout = layout;

    public static LayoutBuilder Paper() => new(new Layout
    {
        Meta = new LayoutMeta { Name = "test-paper", Medium = Medium.Paper },
        Canvas = new CanvasSpec
        {
            Width = 210,
            Height = 297,
            Margins = new Margins { Top = 15, Bottom = 15, Left = 20, Right = 12 }
        }
    });

    public static LayoutBuilder Screen(bool scrollable = false) => new(new Layout
    {
        Meta = new LayoutMeta { Name = "test-screen", Medium = Medium.Screen },
        Canvas = new CanvasSpec
        {
            Width = 1920,
            Height = 1080,
            ContentPaddingPx = 24,
            FixedRegions = new FixedRegions(),
            IsScrollable = scrollable
        }
    });

    public LayoutElement Add(ElementType type, double x, double y, double w, double h,
        string? text = null, Action<LayoutElement>? cfg = null)
    {
        var el = new LayoutElement
        {
            Id = $"e{++_auto}",
            Type = type,
            X = x, Y = y, W = w, H = h,
            Text = text
        };
        cfg?.Invoke(el);
        _layout.Elements.Add(el);
        return el;
    }

    public Layout Build() => _layout;
}
