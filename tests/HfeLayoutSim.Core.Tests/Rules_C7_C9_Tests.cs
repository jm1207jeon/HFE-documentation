using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Report;

namespace HfeLayoutSim.Core.Tests;

public class C7_WorkflowRuleTests
{
    [Fact]
    public void C7_01_BackwardSequence_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.TextInput, 20, 100, 60, 8, cfg: el => el.Semantics.Sequence = 1);
        b.Add(ElementType.TextInput, 20, 40, 60, 8, cfg: el => el.Semantics.Sequence = 2); // above #1
        Assert.Equal(RuleStatus.Violation, TestData.Run("C7-01", b.Build()).Status);
    }

    [Fact]
    public void C7_01_ForwardSequence_Passes_SingleInput_NA()
    {
        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.TextInput, 20, 40, 60, 8, cfg: el => el.Semantics.Sequence = 1);
        ok.Add(ElementType.TextInput, 110, 40, 60, 8, cfg: el => el.Semantics.Sequence = 2); // same row, right
        ok.Add(ElementType.TextInput, 20, 60, 60, 8, cfg: el => el.Semantics.Sequence = 3);  // next row
        Assert.Equal(RuleStatus.Pass, TestData.Run("C7-01", ok.Build()).Status);

        var na = LayoutBuilder.Paper();
        na.Add(ElementType.TextInput, 20, 40, 60, 8, cfg: el => el.Semantics.Sequence = 1);
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C7-01", na.Build()).Status);
    }

    [Fact]
    public void C7_02_PreconditionAfterItems_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Checkbox, 20, 40, 5, 5, "항목 확인", el => el.Semantics.Role = SemanticRole.InspectionItem);
        b.Add(ElementType.InfoBox, 20, 200, 170, 8, "교정 확인", el => el.Semantics.Role = SemanticRole.Precondition);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C7-02", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.InfoBox, 20, 40, 170, 8, "교정 확인", el => el.Semantics.Role = SemanticRole.Precondition);
        ok.Add(ElementType.Checkbox, 20, 200, 5, 5, "항목 확인", el => el.Semantics.Role = SemanticRole.InspectionItem);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C7-02", ok.Build()).Status);

        var na = LayoutBuilder.Paper();
        na.Add(ElementType.Checkbox, 20, 40, 5, 5, "항목 확인", el => el.Semantics.Role = SemanticRole.InspectionItem);
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C7-02", na.Build()).Status);
    }

    [Fact]
    public void C7_03_VerdictAboveIdentification_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Header, 20, 15, 178, 10, "제목");
        b.Add(ElementType.JudgmentBadge, 130, 28, 40, 6, "적합", el => el.Semantics.Role = SemanticRole.FinalVerdict);
        b.Add(ElementType.TextInput, 20, 100, 60, 8, cfg: el => el.Semantics.Role = SemanticRole.Identification);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C7-03", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.Header, 20, 15, 178, 10, "제목");
        ok.Add(ElementType.TextInput, 20, 35, 60, 8, cfg: el => el.Semantics.Role = SemanticRole.Identification);
        ok.Add(ElementType.JudgmentBadge, 130, 250, 40, 6, "적합", el => el.Semantics.Role = SemanticRole.FinalVerdict);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C7-03", ok.Build()).Status);
    }

    [Fact]
    public void C7_04_MissingCompletenessGate_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.JudgmentBadge, 130, 250, 40, 10, "적합", el => el.Semantics.Role = SemanticRole.FinalVerdict);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C7-04", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.Checkbox, 20, 230, 5, 5, "전 항목 기입 완료 확인");
        ok.Add(ElementType.JudgmentBadge, 130, 250, 40, 10, "적합", el => el.Semantics.Role = SemanticRole.FinalVerdict);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C7-04", ok.Build()).Status);

        var na = LayoutBuilder.Paper();
        na.Add(ElementType.Label, 20, 30, 30, 5, "라벨");
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C7-04", na.Build()).Status);
    }

    [Fact]
    public void C7_05_MissingNonconformanceBlock_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 20, 30, 30, 5, "라벨");
        Assert.Equal(RuleStatus.Violation, TestData.Run("C7-05", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.TextInput, 20, 200, 170, 12, cfg: el =>
            el.Semantics.Role = SemanticRole.NonconformanceRecord);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C7-05", ok.Build()).Status);
    }

    [Fact]
    public void C7_06_InputAfterSignature_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.SignatureBox, 124, 240, 50, 10, cfg: el => el.Semantics.Role = SemanticRole.Signature);
        b.Add(ElementType.TextInput, 20, 260, 60, 8);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C7-06", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.TextInput, 20, 100, 60, 8);
        ok.Add(ElementType.SignatureBox, 124, 260, 50, 10, cfg: el => el.Semantics.Role = SemanticRole.Signature);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C7-06", ok.Build()).Status);

        var na = LayoutBuilder.Paper();
        na.Add(ElementType.TextInput, 20, 100, 60, 8);
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C7-06", na.Build()).Status);
    }
}

public class C8_ErrorPreventionRuleTests
{
    [Fact]
    public void C8_01_CriticalCheckbox_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Checkbox, 20, 40, 5, 5, "LOT 확인", el =>
        {
            el.Semantics.IsCritical = true;
            el.Semantics.InputKind = InputKind.Check;
        });
        Assert.Equal(RuleStatus.Violation, TestData.Run("C8-01", b.Build()).Status);
    }

    [Fact]
    public void C8_01_KeywordCheckbox_CaughtEvenWithoutCriticalFlag()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Checkbox, 20, 40, 5, 5, "유효기간 확인");
        Assert.Equal(RuleStatus.Violation, TestData.Run("C8-01", b.Build()).Status);
    }

    [Fact]
    public void C8_01_Transcription_Passes_NoCandidates_NA()
    {
        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.TextInput, 20, 40, 60, 8, cfg: el =>
        {
            el.Semantics.IsCritical = true;
            el.Semantics.InputKind = InputKind.Transcribe;
        });
        Assert.Equal(RuleStatus.Pass, TestData.Run("C8-01", ok.Build()).Status);

        var na = LayoutBuilder.Paper();
        na.Add(ElementType.Checkbox, 20, 40, 5, 5, "기준서 확인");
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C8-01", na.Build()).Status);
    }

    [Fact]
    public void C8_02_SpecLimitMissing_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.NumInput, 20, 40, 15, 8, cfg: el => el.Unit = "mm");
        Assert.Equal(RuleStatus.Violation, TestData.Run("C8-02", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.NumInput, 20, 40, 15, 8, cfg: el =>
        {
            el.Unit = "mm";
            el.Semantics.SpecLimitShown = true;
        });
        Assert.Equal(RuleStatus.Pass, TestData.Run("C8-02", ok.Build()).Status);
    }

    [Fact]
    public void C8_03_RequiredWithoutMarker_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 20, 30, 25, 5, "품명");
        b.Add(ElementType.TextInput, 46, 30, 60, 8, cfg: el => el.Semantics.IsRequired = true);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C8-03", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.Label, 20, 30, 25, 5, "품명 *");
        ok.Add(ElementType.TextInput, 46, 30, 60, 8, cfg: el => el.Semantics.IsRequired = true);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C8-03", ok.Build()).Status);
    }

    [Fact]
    public void C8_04_PreselectedPassJudgment_Violates()
    {
        var b = LayoutBuilder.Screen();
        b.Add(ElementType.RadioGroup, 1300, 880, 300, 32, "판정 *", el =>
        {
            el.Semantics.Role = SemanticRole.FinalVerdict;
            el.DefaultValue = "적합";
        });
        Assert.Equal(RuleStatus.Violation, TestData.Run("C8-04", b.Build()).Status);

        var ok = LayoutBuilder.Screen();
        ok.Add(ElementType.RadioGroup, 1300, 880, 300, 32, "판정 *", el =>
            el.Semantics.Role = SemanticRole.FinalVerdict);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C8-04", ok.Build()).Status);
    }

    [Fact]
    public void C8_05_MissingUnit_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.NumInput, 20, 40, 15, 8);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C8-05", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.NumInput, 20, 40, 15, 8, cfg: el => el.Unit = "N");
        Assert.Equal(RuleStatus.Pass, TestData.Run("C8-05", ok.Build()).Status);
    }

    [Fact]
    public void C8_06_VagueButtonLabel_Violates()
    {
        var b = LayoutBuilder.Screen();
        b.Add(ElementType.Button, 1700, 1020, 120, 44, "확인");
        Assert.Equal(RuleStatus.Violation, TestData.Run("C8-06", b.Build()).Status);

        var ok = LayoutBuilder.Screen();
        ok.Add(ElementType.Button, 1700, 1020, 120, 44, "검사 완료 처리");
        Assert.Equal(RuleStatus.Pass, TestData.Run("C8-06", ok.Build()).Status);
    }
}

public class C9_VigilanceRuleTests
{
    [Fact]
    public void C9_01_LongMeasurementRun_Violates()
    {
        var b = LayoutBuilder.Paper();
        for (var i = 0; i < 21; i++)
            b.Add(ElementType.NumInput, 20, 20 + i * 10, 15, 6, cfg: el => el.Unit = "mm");
        Assert.Equal(RuleStatus.Violation, TestData.Run("C9-01", b.Build()).Status);
    }

    [Fact]
    public void C9_01_SectionedRun_Passes()
    {
        var b = LayoutBuilder.Paper();
        for (var i = 0; i < 10; i++)
            b.Add(ElementType.NumInput, 20, 20 + i * 10, 15, 6, cfg: el => el.Unit = "mm");
        b.Add(ElementType.Divider, 20, 122, 170, 1);
        for (var i = 0; i < 11; i++)
            b.Add(ElementType.NumInput, 20, 126 + i * 10, 15, 6, cfg: el => el.Unit = "mm");
        Assert.Equal(RuleStatus.Pass, TestData.Run("C9-01", b.Build()).Status);
    }

    [Fact]
    public void C9_01_OversizedTable_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Table, 20, 20, 178, 250, cfg: el => { el.Rows = 25; el.Columns = 4; });
        Assert.Equal(RuleStatus.Violation, TestData.Run("C9-01", b.Build()).Status);
    }

    [Fact]
    public void C9_02_MissingPageTotal_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 178, 275, 20, 5, "1");
        Assert.Equal(RuleStatus.Violation, TestData.Run("C9-02", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.Label, 178, 275, 20, 5, "Page 1/2", el => el.Semantics.Role = SemanticRole.Decoration);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C9-02", ok.Build()).Status);
    }

    [Fact]
    public void C9_03_ManyInputsWithoutStepper_Violates()
    {
        var b = LayoutBuilder.Screen();
        for (var i = 0; i < 11; i++)
            b.Add(ElementType.TextInput, 300, 130 + i * 60, 200, 40);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C9-03", b.Build()).Status);

        var ok = LayoutBuilder.Screen();
        ok.Add(ElementType.Stepper, 264, 130, 400, 32, "1·2·3");
        for (var i = 0; i < 11; i++)
            ok.Add(ElementType.TextInput, 300, 180 + i * 60, 200, 40);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C9-03", ok.Build()).Status);

        var na = LayoutBuilder.Screen();
        for (var i = 0; i < 5; i++)
            na.Add(ElementType.TextInput, 300, 130 + i * 60, 200, 40);
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C9-03", na.Build()).Status);
    }
}
