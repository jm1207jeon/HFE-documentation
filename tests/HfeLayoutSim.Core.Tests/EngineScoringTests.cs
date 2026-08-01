using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Report;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Tests;

public class EngineScoringTests
{
    [Fact]
    public void ViolationOnCriticalElement_EscalatesOneLevel_AndCapsGrade()
    {
        var b = LayoutBuilder.Paper();
        // C1-01 is Major; on an isCritical element it must become Critical and cap the grade.
        b.Add(ElementType.Label, 20, 30, 60, 5, "위험 표시", el =>
        {
            el.Style.FgColor = "#F57C00";
            el.Semantics.IsCritical = true;
        });
        var report = TestData.Evaluate(b.Build());

        var finding = report.Findings.Single(f => f.RuleId == "C1-01");
        Assert.Equal(RuleSeverity.Critical, finding.Severity);
        Assert.True(finding.Escalated);
        Assert.Equal(20, finding.Penalty);

        Assert.True(report.ScoreCard.GradeCapped);
        Assert.Equal("C", report.ScoreCard.Grade);
    }

    [Fact]
    public void CategoryScore_Is100MinusPenalties_TotalIsWeightedSum()
    {
        var layout = TestData.LoadSample("bad_layout_paper.hfelayout.json");
        var report = TestData.Evaluate(layout);

        foreach (var cat in report.ScoreCard.Categories)
        {
            var penalties = report.Findings.Where(f => f.Category == cat.Category).Sum(f => f.Penalty);
            Assert.Equal(Math.Max(0, 100 - penalties), cat.Score);
        }

        var weighted = report.ScoreCard.Categories.Sum(c => c.Score * c.Weight);
        Assert.Equal(Math.Round(weighted, 1), report.ScoreCard.Total);
    }

    [Fact]
    public void EveryFinding_CarriesTheFiveMandatoryParts()
    {
        var layout = TestData.LoadSample("bad_layout_paper.hfelayout.json");
        var report = TestData.Evaluate(layout);
        Assert.NotEmpty(report.Findings);

        foreach (var f in report.Findings)
        {
            Assert.False(string.IsNullOrWhiteSpace(f.RuleId));
            Assert.False(string.IsNullOrWhiteSpace(f.Measured));
            Assert.False(string.IsNullOrWhiteSpace(f.Target));
            Assert.False(string.IsNullOrWhiteSpace(f.Recommendation));
            Assert.False(string.IsNullOrWhiteSpace(f.StandardRef));
        }
    }

    [Fact]
    public void ScreenOnlyRules_AreNotApplicableOnPaper()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 20, 30, 30, 5, "라벨");
        var report = TestData.Evaluate(b.Build());

        foreach (var rule in TestData.Rules.Rules.Where(r => r.AppliesTo == RuleApplicability.Screen))
            Assert.Equal(RuleStatus.NotApplicable,
                report.RuleOutcomes.Single(o => o.RuleId == rule.Id).Status);
    }

    [Fact]
    public void Evaluate_500Elements_UnderOneSecond()
    {
        var b = LayoutBuilder.Paper();
        for (var i = 0; i < 500; i++)
            b.Add(ElementType.Label, 20 + i % 8 * 22, 16 + i / 8 * 4, 20, 3, $"항목 {i}",
                el => el.GroupId = $"g{i / 10}");
        var layout = b.Build();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        TestData.Evaluate(layout);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 1000, $"평가 시간 {sw.ElapsedMilliseconds}ms ≥ 1s");
    }
}

public class SampleLayoutTests
{
    [Fact]
    public void GoodPaperSample_ScoresGradeA_NoFindings()
    {
        var report = TestData.Evaluate(TestData.LoadSample("incoming_inspection_paper.hfelayout.json"));
        Assert.Empty(report.Findings);
        Assert.Equal("A", report.ScoreCard.Grade);
        Assert.False(report.ScoreCard.GradeCapped);
        Assert.True(report.Strengths.Count >= 10);
    }

    [Fact]
    public void GoodScreenSample_ScoresGradeA_NoFindings()
    {
        var report = TestData.Evaluate(TestData.LoadSample("mes_inspection_screen.hfelayout.json"));
        Assert.Empty(report.Findings);
        Assert.Equal("A", report.ScoreCard.Grade);
    }

    [Fact]
    public void BadSample_DetectsAllPlantedViolations()
    {
        var report = TestData.Evaluate(TestData.LoadSample("bad_layout_paper.hfelayout.json"));
        var violated = report.RuleOutcomes
            .Where(o => o.Status == RuleStatus.Violation)
            .Select(o => o.RuleId)
            .ToHashSet();

        string[] planted =
        {
            "C1-01", // #F57C00 normal text 2.70:1
            "C1-04", // color-only judgment badge
            "C1-07", // #F57C00 not in verified palette
            "C2-06", // weak title (11pt, not bold)
            "C4-01", // LOT identification in bottom-left
            "C4-02", // critical/verdict elements in WFA
            "C4-03", // verdict outside terminal area
            "C4-05", // header mid-page
            "C5-04", // 12 consecutive checkboxes
            "C5-05", // 12 ungrouped inspection items
            "C6-01", // sparse density
            "C7-02", // precondition after inspection items
            "C7-04", // no completeness gate
            "C7-05", // no nonconformance block
            "C8-01", // LOT confirmed by mere checkbox
            "C9-02"  // no Page n/N
        };
        foreach (var ruleId in planted)
            Assert.Contains(ruleId, violated);

        Assert.Contains(report.Findings, f => f.Severity == RuleSeverity.Critical);
        Assert.True(report.ScoreCard.Total < 80);
    }

    [Fact]
    public void BadSample_FindingsCarryElementIdsForCanvasHighlight()
    {
        var report = TestData.Evaluate(TestData.LoadSample("bad_layout_paper.hfelayout.json"));
        var run = report.Findings.Single(f => f.RuleId == "C5-04");
        Assert.Equal(12, run.ElementIds.Count);
        Assert.Contains("cb-01", run.ElementIds);
        Assert.Contains("cb-12", run.ElementIds);
    }
}
