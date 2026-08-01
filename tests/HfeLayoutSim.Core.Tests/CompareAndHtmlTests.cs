using HfeLayoutSim.Core.Compare;
using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Report;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Tests;

public class VariantComparerTests
{
    /// <summary>Variant A: bad contrast + missing NC block. Variant B: contrast fixed, italic introduced, NC still missing.</summary>
    private static (EvaluationReport A, EvaluationReport B) BuildPair()
    {
        var a = LayoutBuilder.Paper();
        a.Add(ElementType.Label, 20, 30, 60, 5, "주의 문구", el => el.Style.FgColor = "#F57C00");
        var reportA = TestData.Evaluate(a.Build());

        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 20, 30, 60, 5, "주의 문구", el => el.Style.Italic = true);
        var reportB = TestData.Evaluate(b.Build());
        return (reportA, reportB);
    }

    [Fact]
    public void Diff_ClassifiesResolvedNewAndRemaining()
    {
        var (reportA, reportB) = BuildPair();
        var diff = VariantComparer.Diff(reportA, reportB);

        Assert.Contains(diff.Resolved, d => d.RuleId == "C1-01");        // contrast fixed in B
        Assert.Contains(diff.Resolved, d => d.RuleId == "C1-07");        // off-palette color gone
        Assert.Contains(diff.NewlyIntroduced, d => d.RuleId == "C2-05"); // italic introduced in B
        Assert.Contains(diff.Remaining, d => d.RuleId == "C7-05");       // NC block missing in both
        Assert.DoesNotContain(diff.NewlyIntroduced, d => d.RuleId == "C7-05");
    }

    [Fact]
    public void Compare_ProducesSummariesWithRadarData()
    {
        var (reportA, reportB) = BuildPair();
        var result = VariantComparer.Compare(new[] { reportA, reportB });

        Assert.Equal(2, result.Variants.Count);
        Assert.Single(result.Diffs);

        foreach (var v in result.Variants)
        {
            Assert.Equal(9, v.CategoryScores.Count); // radar chart axes C1..C9
            Assert.All(v.CategoryScores.Values, s => Assert.InRange(s, 0, 100));
        }
        // A carries the heavier C1 penalty than B
        Assert.True(result.Variants[0].CategoryScores["C1"] < result.Variants[1].CategoryScores["C1"]);
    }

    [Fact]
    public void Compare_RejectsWrongVariantCount()
    {
        var (reportA, _) = BuildPair();
        Assert.Throws<ArgumentException>(() => VariantComparer.Compare(new[] { reportA }));
        Assert.Throws<ArgumentException>(() =>
            VariantComparer.Compare(new[] { reportA, reportA, reportA, reportA }));
    }

    [Fact]
    public void Compare_ThreeVariants_DiffsEachAgainstBaseline()
    {
        var (reportA, reportB) = BuildPair();
        var c = LayoutBuilder.Paper();
        c.Add(ElementType.Label, 20, 30, 60, 5, "주의 문구");
        var reportC = TestData.Evaluate(c.Build());

        var result = VariantComparer.Compare(new[] { reportA, reportB, reportC });
        Assert.Equal(2, result.Diffs.Count);
        Assert.All(result.Diffs, d => Assert.Equal(reportA.LayoutName, d.BaselineName));
        // C fixed the contrast without introducing italic
        Assert.Contains(result.Diffs[1].Resolved, d => d.RuleId == "C1-01");
        Assert.DoesNotContain(result.Diffs[1].NewlyIntroduced, d => d.RuleId == "C2-05");
    }

    [Fact]
    public void DiffItems_OrderedBySeverityDescending()
    {
        var report = TestData.Evaluate(TestData.LoadSample("bad_layout_paper.hfelayout.json"));
        var clean = TestData.Evaluate(TestData.LoadSample("incoming_inspection_paper.hfelayout.json"));

        var diff = VariantComparer.Diff(report, clean);
        Assert.NotEmpty(diff.Resolved);
        Assert.Empty(diff.Remaining);

        var severities = diff.Resolved.Select(d => d.Severity).ToList();
        var sorted = severities.OrderByDescending(s => s).ToList();
        Assert.Equal(sorted, severities);
        Assert.Equal(RuleSeverity.Critical, severities.First());
    }
}

public class HtmlReportExporterTests
{
    [Fact]
    public void Export_ContainsScoreFindingsAndStrengths()
    {
        var report = TestData.Evaluate(TestData.LoadSample("bad_layout_paper.hfelayout.json"));
        var html = HtmlReportExporter.Export(report);

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains(report.ScoreCard.Total.ToString("0.0"), html);
        Assert.Contains("C5-04", html);                    // a known finding
        Assert.Contains("cb-01", html);                    // element ids for highlight cross-reference
        Assert.Contains("지적 사항", html);
        Assert.Contains("@media print", html);             // print-ready
        Assert.Contains("#C62828", html);                  // verified fail color from DESIGN_TOKENS
    }

    [Fact]
    public void Export_CleanReport_ShowsNoFindingsNotice()
    {
        var report = TestData.Evaluate(TestData.LoadSample("incoming_inspection_paper.hfelayout.json"));
        var html = HtmlReportExporter.Export(report);
        Assert.Contains("지적 사항 없음", html);
        Assert.Contains("강점", html);
    }

    [Fact]
    public void Export_EscapesHtmlInLayoutContent()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 20, 30, 60, 5, "<script>alert(1)</script>",
            el => el.Style.FgColor = "#F57C00");
        var layout = b.Build();
        layout.Meta.Name = "이름 <b>주입</b>";

        var html = HtmlReportExporter.Export(TestData.Evaluate(layout));
        Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("<b>주입</b>", html);
        Assert.Contains("&lt;b&gt;주입&lt;/b&gt;", html);
    }

    [Fact]
    public void Export_CappedGrade_ShowsDeploymentBlockNotice()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.JudgmentBadge, 130, 250, 40, 10, cfg: el =>
            el.Semantics.Judgment = new JudgmentCoding { HasColor = true });
        var html = HtmlReportExporter.Export(TestData.Evaluate(b.Build()));
        Assert.Contains("배포 부적합", html);
    }
}
