using HfeLayoutSim.Core.Advisor;
using HfeLayoutSim.Core.Catalog;
using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Presets;
using HfeLayoutSim.Core.Report;
using HfeLayoutSim.Core.Rules;
using HfeLayoutSim.Core.Tuning;

namespace HfeLayoutSim.Core.Tests;

public static class QualityFixtures
{
    public static PresetLibrary Presets { get; } =
        PresetLibrary.LoadFile(Path.Combine(TestData.BaseDir, "rules", "element_presets.json"));

    public static InspectionCatalog Catalog { get; } =
        InspectionCatalog.LoadFile(Path.Combine(TestData.BaseDir, "catalog", "incoming_inspection.catalog.json"));
}

public class NewRuleTests
{
    [Fact]
    public void C7_07_SingleSigner_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.SignatureBox, 124, 260, 60, 10, cfg: el =>
        {
            el.SignerRole = "검사자";
            el.Semantics.Role = SemanticRole.Signature;
        });
        Assert.Equal(RuleStatus.Violation, TestData.Run("C7-07", b.Build()).Status);
    }

    [Fact]
    public void C7_07_DistinctInspectorAndApprover_Passes()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.SignatureBox, 124, 246, 60, 10, cfg: el =>
        {
            el.SignerRole = "검사자";
            el.Semantics.Role = SemanticRole.Signature;
        });
        b.Add(ElementType.SignatureBox, 124, 260, 60, 10, cfg: el =>
        {
            el.SignerRole = "승인자";
            el.Semantics.Role = SemanticRole.Signature;
        });
        Assert.Equal(RuleStatus.Pass, TestData.Run("C7-07", b.Build()).Status);
    }

    [Fact]
    public void C7_07_TwoBoxesSameRole_StillViolates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.SignatureBox, 124, 246, 60, 10, cfg: el => el.SignerRole = "검사자");
        b.Add(ElementType.SignatureBox, 124, 260, 60, 10, cfg: el => el.SignerRole = "검사자");
        Assert.Equal(RuleStatus.Violation, TestData.Run("C7-07", b.Build()).Status);
    }

    [Fact]
    public void C8_07_TwoOptionVerdict_Violates()
    {
        var b = LayoutBuilder.Screen();
        b.Add(ElementType.RadioGroup, 1300, 880, 300, 32, "판정 *", el =>
        {
            el.Semantics.Role = SemanticRole.FinalVerdict;
            el.OptionCount = 2; // 적합/부적합만 — 보류 경로 차단
        });
        Assert.Equal(RuleStatus.Violation, TestData.Run("C8-07", b.Build()).Status);

        var ok = LayoutBuilder.Screen();
        ok.Add(ElementType.RadioGroup, 1300, 880, 300, 32, "판정 *", el =>
        {
            el.Semantics.Role = SemanticRole.FinalVerdict;
            el.OptionCount = 3;
        });
        Assert.Equal(RuleStatus.Pass, TestData.Run("C8-07", ok.Build()).Status);

        var na = LayoutBuilder.Paper();
        na.Add(ElementType.Label, 20, 30, 30, 5, "라벨");
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C8-07", na.Build()).Status);
    }
}

public class VerifierVerdictTests
{
    [Fact]
    public void GoodSample_IsPass_WithCategoryGrades()
    {
        var report = TestData.Evaluate(TestData.LoadSample("incoming_inspection_paper.hfelayout.json"));
        Assert.True(report.ScoreCard.Pass);
        Assert.Equal("B", report.ScoreCard.PassGrade);
        Assert.All(report.ScoreCard.Categories, c => Assert.Equal("A", c.Grade));
    }

    [Fact]
    public void BadSample_IsFail()
    {
        var report = TestData.Evaluate(TestData.LoadSample("bad_layout_paper.hfelayout.json"));
        Assert.False(report.ScoreCard.Pass);
    }

    [Fact]
    public void HtmlReport_ShowsVerifierBanner()
    {
        var report = TestData.Evaluate(TestData.LoadSample("incoming_inspection_paper.hfelayout.json"));
        var html = HtmlReportExporter.Export(report);
        Assert.Contains("PASS ✓", html);
        Assert.Contains("paramgrades", html);

        var bad = TestData.Evaluate(TestData.LoadSample("bad_layout_paper.hfelayout.json"));
        Assert.Contains("FAIL ✕", HtmlReportExporter.Export(bad));
    }
}

public class RuleRegressionTests
{
    [Fact]
    public void RelaxedMaxRun_FlipsC5_04_AndRaisesScore()
    {
        var baselineJson = File.ReadAllText(Path.Combine(TestData.BaseDir, "rules", "hfe_rules.json"));
        var modified = RuleLoader.Load(baselineJson.Replace("\"maxRun\": 4", "\"maxRun\": 5"), "tuned");

        var b = LayoutBuilder.Paper();
        for (var i = 0; i < 5; i++)
            b.Add(ElementType.Checkbox, 20, 40 + i * 6, 5, 5, $"확인{i}");
        var layout = b.Build();

        var report = RuleRegression.Compare(TestData.Rules, modified, new[] { ("cb5", layout) });
        var row = report.Rows.Single();

        Assert.True(report.AnyChange);
        Assert.True(row.TotalB > row.TotalA);
        var change = row.Changes.Single(c => c.RuleId == "C5-04");
        Assert.Equal(RuleStatus.Violation, change.StatusA);
        Assert.Equal(RuleStatus.Pass, change.StatusB);
    }

    [Fact]
    public void IdenticalRules_ReportNoChange()
    {
        var layout = TestData.LoadSample("incoming_inspection_paper.hfelayout.json");
        var report = RuleRegression.Compare(TestData.Rules, TestData.Rules, new[] { ("same", layout) });
        Assert.False(report.AnyChange);
        Assert.Empty(report.Rows.Single().Changes);
    }
}

public class PresetLibraryTests
{
    [Fact]
    public void LoadsAllPresets_WithIconSets()
    {
        var lib = QualityFixtures.Presets;
        Assert.True(lib.All.Count >= 15);
        Assert.NotNull(lib.Find("judgment-badge"));
        Assert.Equal("✓", lib.IconSets["judgment"]["pass"]);
    }

    [Fact]
    public void Instantiate_AppliesMediumSpecificDefaults()
    {
        var lib = QualityFixtures.Presets;
        var paper = lib.Instantiate("judgment-badge", Medium.Paper, 124, 230);
        Assert.Equal(ElementType.JudgmentBadge, paper.Type);
        Assert.Equal(42, paper.W);
        Assert.Equal(14, paper.Style.FontSize); // pt
        Assert.True(paper.Semantics.Judgment!.HasIcon);
        Assert.Equal("judgment-badge", paper.PresetId);

        var screen = lib.Instantiate("judgment-badge", Medium.Screen, 1620, 880);
        Assert.Equal(140, screen.W);
        Assert.Equal(20, screen.Style.FontSize); // px
    }

    [Fact]
    public void Instantiate_ClonesSemantics_NoSharedState()
    {
        var lib = QualityFixtures.Presets;
        var a = lib.Instantiate("input-lot", Medium.Paper, 46, 40);
        var b = lib.Instantiate("input-lot", Medium.Paper, 46, 50);
        a.Semantics.IsCritical = false;
        Assert.True(b.Semantics.IsCritical);
    }

    [Fact]
    public void ScreenOnlyPreset_RejectsPaper()
    {
        var lib = QualityFixtures.Presets;
        Assert.Throws<PresetLoadException>(() => lib.Instantiate("button-primary", Medium.Paper, 20, 20));
    }
}

public class PlacementAdvisorTests
{
    private static PlacementAdvisor Advisor => new(TestData.Rules, QualityFixtures.Presets);

    [Fact]
    public void WellPlacedElement_GetsOk()
    {
        var layout = TestData.LoadSample("incoming_inspection_paper.hfelayout.json");
        var advice = Advisor.Advise(layout, "in-lot");
        Assert.Single(advice);
        Assert.Equal(AdviceLevel.Ok, advice[0].Level);
    }

    [Fact]
    public void MisplacedCriticalIdentification_GetsPositionCoaching()
    {
        var layout = TestData.LoadSample("bad_layout_paper.hfelayout.json");
        var advice = Advisor.Advise(layout, "cb-lot");

        Assert.Contains(advice, a => a.Level == AdviceLevel.Critical && a.Topic == "위치" && a.RuleId == "C4-01");
        Assert.Contains(advice, a => a.Topic == "오류방지" && a.RuleId == "C8-01"); // 체크 방식 → 전기 방식
    }

    [Fact]
    public void ColorOnlyBadge_GetsIconAndColorCoaching()
    {
        var layout = TestData.LoadSample("bad_layout_paper.hfelayout.json");
        var advice = Advisor.Advise(layout, "badge-verdict");
        Assert.Contains(advice, a => a.RuleId == "C1-04");            // 3중 코딩 위반
        Assert.Contains(advice, a => a.Topic == "아이콘" && a.Suggestion!.Contains("✓")); // 아이콘 제안
    }

    [Fact]
    public void WrongZonePreset_SuggestsTargetCoordinates()
    {
        var b = LayoutBuilder.Paper();
        // judgment badge dropped top-left; preset recommends TA
        b.Add(ElementType.JudgmentBadge, 20, 60, 42, 10, "적합 ■", el =>
        {
            el.PresetId = "judgment-badge";
            el.Semantics.Role = SemanticRole.FinalVerdict;
            el.Semantics.Judgment = new JudgmentCoding { HasColor = true, HasIcon = true, HasText = true };
        });
        var advice = Advisor.Advise(b.Build(), b.Build().Elements[0].Id);
        var position = advice.FirstOrDefault(a => a.Topic == "위치");
        Assert.NotNull(position);
        Assert.Contains("TA", position!.Message + position.Suggestion);
    }

    [Fact]
    public void NonMonoNumericInput_GetsFontCoaching()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.NumInput, 83, 176, 16, 8, cfg: el =>
        {
            el.PresetId = "input-num";
            el.Unit = "mm";
            el.Semantics.SpecLimitShown = true;
        });
        var layout = b.Build();
        var advice = Advisor.Advise(layout, layout.Elements[0].Id);
        Assert.Contains(advice, a => a.Topic == "폰트" && a.Suggestion!.Contains("D2Coding"));
    }

    [Fact]
    public void AdviseAll_CoversEveryElement()
    {
        var layout = TestData.LoadSample("mes_inspection_screen.hfelayout.json");
        var all = Advisor.AdviseAll(layout);
        Assert.Equal(layout.Elements.Count, all.Count);
        Assert.All(all, pair => Assert.NotEmpty(pair.Advice));
    }
}

public class LayoutComposerTests
{
    private static LayoutComposer Composer => new(QualityFixtures.Presets, TestData.Rules);

    [Fact]
    public void ComposedPaperLayout_PassesEvaluation()
    {
        var layout = Composer.Compose(QualityFixtures.Catalog, Medium.Paper);
        LayoutSerializer.Validate(layout);
        var report = TestData.Evaluate(layout);

        Assert.DoesNotContain(report.Findings, f => f.Severity == RuleSeverity.Critical);
        Assert.True(report.ScoreCard.Pass,
            $"생성 레이아웃 FAIL: {report.ScoreCard.Total} — " +
            string.Join("; ", report.Findings.Select(f => $"{f.RuleId} {f.Message}")));
    }

    [Fact]
    public void ComposedScreenLayout_PassesEvaluation()
    {
        var layout = Composer.Compose(QualityFixtures.Catalog, Medium.Screen);
        LayoutSerializer.Validate(layout);
        var report = TestData.Evaluate(layout);

        Assert.DoesNotContain(report.Findings, f => f.Severity == RuleSeverity.Critical);
        Assert.True(report.ScoreCard.Pass,
            $"생성 레이아웃 FAIL: {report.ScoreCard.Total} — " +
            string.Join("; ", report.Findings.Select(f => $"{f.RuleId} {f.Message}")));
    }

    [Fact]
    public void CriticalCheckItem_BecomesTranscriptionInput()
    {
        var layout = Composer.Compose(QualityFixtures.Catalog, Medium.Paper);
        // "포장 라벨 LOT 일치" (critical check) must be a transcription TextInput, not a Checkbox
        var critical = layout.Elements.Where(e => e.Semantics.IsCritical && e.Type == ElementType.TextInput &&
                                                  e.Semantics.Role == SemanticRole.InspectionItem).ToList();
        Assert.NotEmpty(critical);
        Assert.All(critical, e => Assert.Equal(InputKind.Transcribe, e.Semantics.InputKind));
    }

    [Fact]
    public void UserContent_AppearsInLayout()
    {
        var layout = Composer.Compose(QualityFixtures.Catalog, Medium.Paper);
        var texts = string.Join("\n", layout.Elements.Select(e => e.Text));
        Assert.Contains("수입검사 — Nitinol Wire", texts);   // process name
        Assert.Contains("치수 검사", texts);                  // step
        Assert.Contains("멸균 전 제품", texts);               // alarm
        Assert.Contains("hook_angle_diagram.png", texts);     // attached image ref
        Assert.Contains(layout.Elements, e => e.Type == ElementType.SignatureBox && e.SignerRole == "승인자");
    }

    [Fact]
    public void OversizedCatalog_FailsWithGuidance()
    {
        var catalog = InspectionCatalog.Load(System.Text.Json.JsonSerializer.Serialize(new
        {
            processName = "과대 공정",
            steps = Enumerable.Range(1, 12).Select(i => new
            {
                name = $"공정{i}",
                order = i,
                items = Enumerable.Range(1, 7).Select(j => new
                {
                    name = $"항목{j}", kind = "measure", unit = "mm", criterion = "1±0.1"
                })
            })
        }));
        var ex = Assert.Throws<CatalogLoadException>(() => Composer.Compose(catalog, Medium.Paper));
        Assert.Contains("초과", ex.Message);
    }

    [Fact]
    public void CatalogValidation_RejectsMeasureWithoutUnit()
    {
        var ex = Assert.Throws<CatalogLoadException>(() => InspectionCatalog.Load("""
        { "processName": "x", "steps": [ { "name": "s", "order": 1,
            "items": [ { "name": "외경", "kind": "measure" } ] } ] }
        """));
        Assert.Contains("unit", ex.Message);
    }

    [Fact]
    public void Chunk_BalancesSizes()
    {
        var chunks = LayoutComposer.Chunk(Enumerable.Range(1, 8).ToList(), 7);
        Assert.Equal(2, chunks.Count);
        Assert.Equal(4, chunks[0].Count);
        Assert.Equal(4, chunks[1].Count);
    }
}
