using HfeLayoutSim.Core.Compare;
using HfeLayoutSim.Core.Engine;
using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Presets;
using HfeLayoutSim.Core.Report;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Tests;

/// <summary>
/// Regressions for the defects an audit of the whole tool confirmed: a hand-edited knowledge base
/// or layout file must never crash the engine or silently change a verdict.
/// </summary>
public class RuleKnowledgeBaseRobustnessTests
{
    private static RuleSet LoadWith(string find, string replace)
        => RuleLoader.Load(File.ReadAllText(Path.Combine(TestData.BaseDir, "rules", "hfe_rules.json"))
            .Replace(find, replace), "tuned");

    [Fact]
    public void BadEnumInRuleParams_IsRejectedAtLoadWithRuleId_NotThrownMidEvaluation()
    {
        var rules = LoadWith("\"type\": \"Checkbox\", \"paperMinMm\"", "\"type\": \"Checkbo\", \"paperMinMm\"");

        var ex = Assert.Throws<RuleLoadException>(() => new EvaluatorRegistry().EnsureCoverage(rules));
        Assert.Contains("C3-01", ex.Message);
        Assert.Contains("Checkbo", ex.Message);
        Assert.Contains("ElementType", ex.Message);
    }

    [Fact]
    public void BrokenRegexInRuleParams_IsRejectedAtLoad()
    {
        var rules = LoadWith("\"keyword\": \"기입 완료|공란 없음|완결\"", "\"keyword\": \"기입 완료|[\"");

        var ex = Assert.Throws<RuleLoadException>(() => new EvaluatorRegistry().EnsureCoverage(rules));
        Assert.Contains("C7-04", ex.Message);
        Assert.Contains("정규식", ex.Message);
    }

    [Fact]
    public void FractionalIntegerThreshold_IsRejectedAtLoad()
    {
        var rules = LoadWith("\"maxRun\": 4,", "\"maxRun\": 4.5,");

        var ex = Assert.Throws<RuleLoadException>(() => new EvaluatorRegistry().EnsureCoverage(rules));
        Assert.Contains("C5-04", ex.Message);
        Assert.Contains("정수", ex.Message);
    }

    [Fact]
    public void ZeroGroupBound_IsRejectedBeforeItCanHangTheComposer()
    {
        var rules = LoadWith("\"min\": 3, \"max\": 7, \"targetRole\"", "\"min\": 3, \"max\": 0, \"targetRole\"");

        var ex = Assert.Throws<RuleLoadException>(() => new EvaluatorRegistry().EnsureCoverage(rules));
        Assert.Contains("C5-03", ex.Message);
    }

    [Fact]
    public void UnknownBlockNameInOrder_IsRejectedInsteadOfBecomingAPhantomHeaderBlock()
    {
        var rules = LoadWith("\"SamplingInfo\", \"InspectionItem\"", "\"Sampling\", \"InspectionItem\"");

        var ex = Assert.Throws<RuleLoadException>(() => new EvaluatorRegistry().EnsureCoverage(rules));
        Assert.Contains("C7-03", ex.Message);
        Assert.Contains("Sampling", ex.Message);
    }

    [Fact]
    public void NoneIsNotAcceptedAsABlockName()
    {
        var rules = LoadWith("\"SamplingInfo\", \"InspectionItem\"", "\"None\", \"InspectionItem\"");
        Assert.Throws<RuleLoadException>(() => new EvaluatorRegistry().EnsureCoverage(rules));
    }

    [Fact]
    public void FractionalPenalty_IsRejectedByTheLoader()
    {
        var ex = Assert.Throws<RuleLoadException>(() =>
            LoadWith("\"Minor\": 3,", "\"Minor\": 2.5,"));
        Assert.Contains("penalties", ex.Message);
    }

    [Fact]
    public void WrongJsonTypeForRuleId_DoesNotThrowRawInvalidOperation()
    {
        const string json = """
        {
          "penalties": { "Minor": 3, "Major": 8, "Critical": 20 },
          "weights": { "C1": 1.0 },
          "rules": [ { "id": 17, "category": "C1", "severity": "Minor", "check": "textContrast" } ]
        }
        """;
        var ex = Assert.Throws<RuleLoadException>(() => RuleLoader.Load(json));
        Assert.Contains("id", ex.Message);
    }

    [Fact]
    public void RealKnowledgeBase_PassesParameterValidation()
        => new EvaluatorRegistry().EnsureCoverage(TestData.Rules); // throws on any bad parameter
}

public class LayoutFileRobustnessTests
{
    [Fact]
    public void ExplicitNullStyleAndSemantics_LoadAsEmptyDefaults_NotNullReferences()
    {
        const string json = """
        {
          "meta": { "name": "널 테스트", "medium": "paper" },
          "canvas": { "width": 210, "height": 297, "margins": { "top": 15, "bottom": 15, "left": 20, "right": 12 } },
          "elements": [
            { "id": "a", "type": "label", "x": 20, "y": 20, "w": 30, "h": 5, "text": "제목",
              "style": null, "semantics": null }
          ]
        }
        """;
        var layout = LayoutSerializer.Load(json, "nulls");

        Assert.NotNull(layout.Elements[0].Style);
        Assert.NotNull(layout.Elements[0].Semantics);
        TestData.Evaluate(layout); // must not throw
    }

    [Fact]
    public void NullCanvasBackground_IsRestoredSoContrastCanBeComputed()
    {
        const string json = """
        {
          "meta": { "name": "배경 없음", "medium": "paper" },
          "canvas": { "width": 210, "height": 297, "background": null },
          "elements": [ { "id": "a", "type": "label", "x": 20, "y": 20, "w": 30, "h": 5, "text": "본문" } ]
        }
        """;
        var layout = LayoutSerializer.Load(json, "nullbg");
        Assert.Equal("#FFFFFF", layout.Canvas.Background);
        TestData.Evaluate(layout);
    }

    [Fact]
    public void NullElementEntry_IsReportedAsAFormatError()
    {
        const string json = """
        {
          "meta": { "name": "널 요소", "medium": "paper" },
          "canvas": { "width": 210, "height": 297 },
          "elements": [ null ]
        }
        """;
        var ex = Assert.Throws<LayoutFormatException>(() => LayoutSerializer.Load(json, "nullel"));
        Assert.Contains("elements[0]", ex.Message);
    }

    [Fact]
    public void NonFiniteGeometry_IsRejected()
    {
        var b = LayoutBuilder.Paper();
        var el = b.Add(ElementType.Label, 20, 20, 30, 5, "본문");
        var layout = b.Build();
        el.X = double.NaN;

        var ex = Assert.Throws<LayoutFormatException>(() => LayoutSerializer.Validate(layout));
        Assert.Contains("좌표", ex.Message);
    }
}

public class PresetRobustnessTests
{
    [Fact]
    public void SizeArrayWithOneValue_IsRejectedAtLoad()
    {
        const string json = """
        {
          "version": "t",
          "presets": [ { "id": "stamp", "name": "도장", "elementType": "Image", "size": { "paper": [40] } } ]
        }
        """;
        var ex = Assert.Throws<PresetLoadException>(() => PresetLibrary.Load(json, "presets"));
        Assert.Contains("stamp", ex.Message);
        Assert.Contains("폭", ex.Message);
    }

    [Fact]
    public void UnknownMediumKey_IsRejectedInsteadOfSilentlyHidingThePreset()
    {
        const string json = """
        {
          "version": "t",
          "presets": [ { "id": "x", "name": "x", "elementType": "Label", "size": { "A4": [10, 10] } } ]
        }
        """;
        var ex = Assert.Throws<PresetLoadException>(() => PresetLibrary.Load(json, "presets"));
        Assert.Contains("알 수 없는 매체", ex.Message);
    }

    [Fact]
    public void MediumKeyCasing_DoesNotHideAPreset()
    {
        const string json = """
        {
          "version": "t",
          "presets": [ { "id": "x", "name": "x", "elementType": "Label", "size": { "Paper": [10, 10] } } ]
        }
        """;
        var library = PresetLibrary.Load(json, "presets");
        Assert.True(library.Find("x")!.SupportsMedium(Medium.Paper));
    }

    [Fact]
    public void RealPresetLibrary_CoversEveryRoleARuleRequiresToExist()
    {
        var library = QualityFixtures.Presets;
        var requiredRoles = new[]
        {
            SemanticRole.Identification, SemanticRole.Precondition, SemanticRole.InspectionItem,
            SemanticRole.NonconformanceRecord, SemanticRole.FinalVerdict, SemanticRole.Signature,
        };

        foreach (var role in requiredRoles)
            Assert.True(library.All.Any(p => p.Semantics?.Role == role),
                $"역할 {role} 을(를) 만들 수 있는 팔레트 프리셋이 없습니다 — 해당 규칙을 사용자가 만족시킬 방법이 없습니다.");
    }
}

public class EscalationSubjectTests
{
    /// <summary>
    /// Rules that measure the layout — an ordering, a total area, a group's size, a missing partner —
    /// list elements so the user can find them, but those elements are not the offenders. If such a
    /// finding escalated off one of them, marking an unrelated element safety-related would flip a
    /// passing form to FAIL, which is the opposite of what IsCritical is for.
    /// </summary>
    private static readonly string[] LayoutLevelRules =
    {
        // C5-01 is deliberately absent: the elements it names are the ones that must move,
        // so a safety-critical member among them must still raise its severity.
        "C1-05", "C2-02", "C5-03", "C6-02", "C6-04", "C7-03", "C7-07", "C9-01",
    };

    [Theory]
    [InlineData("incoming_inspection_paper.hfelayout.json")]
    [InlineData("bad_layout_paper.hfelayout.json")]
    [InlineData("mes_inspection_screen.hfelayout.json")]
    public void LayoutLevelFindings_NeverEscalate_EvenWhenEveryElementIsCritical(string sample)
    {
        var layout = TestData.LoadSample(sample);
        foreach (var element in layout.Elements) element.Semantics.IsCritical = true;

        var escalated = TestData.Evaluate(layout).Findings
            .Where(f => f.Escalated && LayoutLevelRules.Contains(f.RuleId))
            .Select(f => f.RuleId)
            .ToList();

        Assert.True(escalated.Count == 0,
            "레이아웃 단위 지적이 문맥 요소의 안전 표시로 상향됐습니다: " + string.Join(", ", escalated));
    }

    [Fact]
    public void AMissingSignatureBlock_IsChargedOnce_NotTwice()
    {
        var layout = TestData.LoadSample("incoming_inspection_paper.hfelayout.json");
        foreach (var sig in layout.Elements
                     .Where(e => e.Type == ElementType.SignatureBox ||
                                 e.Semantics.Role == SemanticRole.Signature)
                     .ToList())
            layout.Elements.Remove(sig);

        var findings = TestData.Evaluate(layout).Findings;

        // C7-10 reports the absence; C7-07 is about how existing signatures are composed
        Assert.Contains(findings, f => f.RuleId == "C7-10");
        Assert.DoesNotContain(findings, f => f.RuleId == "C7-07");
    }

    [Fact]
    public void ADeclaredSignerWithNoBoxToSign_IsStillReported()
    {
        var layout = TestData.LoadSample("incoming_inspection_paper.hfelayout.json");
        foreach (var box in layout.Elements.Where(e => e.Type == ElementType.SignatureBox).ToList())
            layout.Elements.Remove(box);
        Assert.Contains(layout.Elements, e => e.Semantics.Role == SemanticRole.Signature);

        // the form still says it will be signed — it just has nowhere to sign
        Assert.Contains(TestData.Evaluate(layout).Findings, f => f.RuleId == "C7-07");
    }

    [Fact]
    public void MissingSecondSigner_StaysMajor_WhenTheRemainingBoxIsCritical()
    {
        var layout = TestData.LoadSample("incoming_inspection_paper.hfelayout.json");
        var signatures = layout.Elements.Where(e => e.Type == ElementType.SignatureBox).ToList();
        Assert.True(signatures.Count >= 2, "샘플에 서명란이 2개 이상 있어야 하는 테스트입니다.");

        // leave one signer and make it safety-related: the offence is the signer that is GONE
        foreach (var extra in signatures.Skip(1)) layout.Elements.Remove(extra);
        signatures[0].Semantics.IsCritical = true;

        var finding = Assert.Single(TestData.Evaluate(layout).Findings.Where(f => f.RuleId == "C7-07"));
        Assert.False(finding.Escalated);
        Assert.Equal(RuleSeverity.Major, finding.Severity);
        Assert.NotEmpty(finding.ElementIds);      // still highlightable on the canvas
    }
}

public class ComparisonIntegrityTests
{
    [Fact]
    public void CrossMediumComparison_IsRefused_RatherThanReportingFalseImprovements()
    {
        var paper = TestData.Evaluate(TestData.LoadSample("incoming_inspection_paper.hfelayout.json"));
        var screen = TestData.Evaluate(TestData.LoadSample("mes_inspection_screen.hfelayout.json"));

        var ex = Assert.Throws<ArgumentException>(() => VariantComparer.Compare(new[] { paper, screen }));
        Assert.Contains("매체", ex.Message);
    }

    [Fact]
    public void VariantVerdict_ComesFromTheRulesFile_NotAHardCodedLetter()
    {
        var good = TestData.Evaluate(TestData.LoadSample("incoming_inspection_paper.hfelayout.json"));
        var bad = TestData.Evaluate(TestData.LoadSample("bad_layout_paper.hfelayout.json"));

        var result = VariantComparer.Compare(new[] { good, bad });

        Assert.Equal(good.ScoreCard.Pass, result.Variants[0].Pass);
        Assert.Equal(bad.ScoreCard.Pass, result.Variants[1].Pass);
        Assert.Equal(TestData.Rules.PassGrade, result.Variants[0].PassGrade);
        Assert.Equal(Medium.Paper, result.Variants[0].Medium);
    }
}

public class VerdictIntegrityTests
{
    [Fact]
    public void EmptyLayout_Fails_InsteadOfBeingCertified()
    {
        var report = TestData.Evaluate(LayoutBuilder.Paper().Build());

        Assert.False(report.ScoreCard.Pass);
        Assert.Contains(report.Findings, f => f.Severity == RuleSeverity.Critical);
        Assert.Contains(report.Findings, f => f.RuleId == "C7-08"); // identification block
        Assert.Contains(report.Findings, f => f.RuleId == "C7-09"); // final verdict
        Assert.Contains(report.Findings, f => f.RuleId == "C7-11"); // header
    }

    [Fact]
    public void EmptyScreenLayout_AlsoFails()
    {
        var report = TestData.Evaluate(LayoutBuilder.Screen().Build());
        Assert.False(report.ScoreCard.Pass);
    }

    [Fact]
    public void GoodSamples_StillPass()
    {
        foreach (var name in new[] { "incoming_inspection_paper.hfelayout.json", "mes_inspection_screen.hfelayout.json" })
        {
            var report = TestData.Evaluate(TestData.LoadSample(name));
            Assert.True(report.ScoreCard.Pass, $"{name}: {string.Join("; ", report.Findings.Select(f => f.RuleId))}");
        }
    }

    [Fact]
    public void LayoutWideFinding_IsNotEscalatedByAnUnrelatedCriticalElement()
    {
        // 26 inputs trips C6-02 (Major). One of them is a critical LOT field, which is context for
        // the count, not its subject — the verdict must not flip to FAIL because of it.
        var b = LayoutBuilder.Paper();
        for (var i = 0; i < 26; i++)
            b.Add(ElementType.TextInput, 20 + i % 2 * 90, 16 + i / 2 * 9, 60, 6, cfg: el =>
            {
                if (i == 0) el.Semantics.IsCritical = true;
            });

        var report = TestData.Evaluate(b.Build());
        var finding = report.Findings.Single(f => f.RuleId == "C6-02");

        Assert.Equal(RuleSeverity.Major, finding.Severity);
        Assert.False(finding.Escalated);
    }

    [Fact]
    public void OffenderFinding_IsStillEscalatedByItsOwnCriticalElement()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 20, 30, 60, 5, "위험 표시", el =>
        {
            el.Style.FgColor = "#F57C00"; // 2.70:1 — fails C1-01 (Major)
            el.Semantics.IsCritical = true;
        });

        var finding = TestData.Evaluate(b.Build()).Findings.Single(f => f.RuleId == "C1-01");
        Assert.Equal(RuleSeverity.Critical, finding.Severity);
        Assert.True(finding.Escalated);
    }
}

public class ContrastContextTests
{
    [Fact]
    public void ContrastUsesTheColourActuallyVisible_NotTheBottomMostPanel()
    {
        // dark panel painted first, white card painted over it, black text inside the card:
        // on screen this is 15.43:1 and must pass.
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Section, 20, 15, 170, 120, cfg: el => el.Style.BgColor = "#212529");
        b.Add(ElementType.InfoBox, 25, 20, 160, 100, cfg: el => el.Style.BgColor = "#FFFFFF");
        b.Add(ElementType.Label, 30, 25, 100, 6, "측정 결과", el => el.Style.FgColor = "#212529");

        var (status, findings) = TestData.Run("C1-01", b.Build());
        Assert.True(status != RuleStatus.Violation,
            "보이는 배경(흰 카드) 대신 아래 패널 색으로 대비를 계산했습니다: " +
            string.Join("; ", findings.Select(f => f.Measured)));
    }

    [Fact]
    public void TextOnAPanelPaintedOverIt_IsStillJudgedAgainstThePanelBelowIt()
    {
        // white card first, dark panel painted over it, white text on the dark panel → passes
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.InfoBox, 20, 15, 170, 120, cfg: el => el.Style.BgColor = "#FFFFFF");
        b.Add(ElementType.Section, 25, 20, 160, 100, cfg: el => el.Style.BgColor = "#212529");
        b.Add(ElementType.Label, 30, 25, 100, 6, "측정 결과", el => el.Style.FgColor = "#FFFFFF");

        Assert.NotEqual(RuleStatus.Violation, TestData.Run("C1-01", b.Build()).Status);
    }
}

public class PairwiseFindingTests
{
    [Fact]
    public void TwoDestructiveButtons_AreChargedOnce_NotTwice()
    {
        var b = LayoutBuilder.Screen();
        b.Add(ElementType.Button, 300, 1020, 120, 44, "검사 취소", el => el.ButtonKind = ButtonKind.Danger);
        b.Add(ElementType.Button, 436, 1020, 120, 44, "판정 취소", el => el.Semantics.IsDestructiveAction = true);

        var (status, findings) = TestData.Run("C3-05", b.Build());
        Assert.Equal(RuleStatus.Violation, status);
        Assert.Single(findings);
    }

    [Fact]
    public void DangerElementIsNamedFirstSoTheMessageMatchesTheHighlight()
    {
        var b = LayoutBuilder.Screen();
        b.Add(ElementType.Button, 436, 1020, 120, 44, "임시 저장");
        b.Add(ElementType.Button, 300, 1020, 120, 44, "검사 취소", el => el.ButtonKind = ButtonKind.Danger);

        var finding = TestData.Run("C3-05", b.Build()).Findings.Single();
        var danger = finding.ElementIds[0];
        Assert.Equal("e2", danger); // the Danger button, regardless of document order
    }

    [Fact]
    public void RedGreenAdjacency_IsOneFindingPerMistake_NotOnePerPair()
    {
        var b = LayoutBuilder.Screen();
        for (var i = 0; i < 4; i++)
            b.Add(ElementType.Image, 300 + i * 30, 200, 20, 20, cfg: el => el.Style.BgColor = "#2E7D32");
        for (var i = 0; i < 4; i++)
            b.Add(ElementType.Image, 300 + i * 30, 230, 20, 20, cfg: el => el.Style.BgColor = "#C62828");

        var (status, findings) = TestData.Run("C1-06", b.Build());
        Assert.Equal(RuleStatus.Violation, status);
        Assert.Single(findings);
        Assert.Equal(8, findings[0].ElementIds.Count); // every involved element is still highlighted
    }
}
