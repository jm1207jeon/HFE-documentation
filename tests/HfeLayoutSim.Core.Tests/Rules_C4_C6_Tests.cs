using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Report;

namespace HfeLayoutSim.Core.Tests;

public class C4_PlacementRuleTests
{
    [Fact]
    public void C4_01_IdentificationAtBottom_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.TextInput, 20, 260, 60, 8, cfg: el => el.Semantics.Role = SemanticRole.Identification);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C4-01", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.TextInput, 20, 20, 60, 8, cfg: el => el.Semantics.Role = SemanticRole.Identification);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C4-01", ok.Build()).Status);

        var na = LayoutBuilder.Paper();
        na.Add(ElementType.Label, 20, 20, 30, 5, "무관");
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C4-01", na.Build()).Status);
    }

    [Fact]
    public void C4_02_WarningInBlindSpot_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.WarningBox, 25, 220, 60, 15, "주의!", el => el.Semantics.Role = SemanticRole.Warning);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C4-02", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.WarningBox, 25, 30, 60, 15, "주의!", el => el.Semantics.Role = SemanticRole.Warning);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C4-02", ok.Build()).Status);
    }

    [Fact]
    public void C4_02_CriticalElementInBlindSpot_AlsoCaught()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.TextInput, 25, 220, 60, 8, cfg: el => el.Semantics.IsCritical = true);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C4-02", b.Build()).Status);
    }

    [Fact]
    public void C4_03_VerdictOutsideTerminalArea_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.JudgmentBadge, 30, 30, 40, 10, "적합", el => el.Semantics.Role = SemanticRole.FinalVerdict);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C4-03", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.JudgmentBadge, 130, 250, 40, 10, "적합", el => el.Semantics.Role = SemanticRole.FinalVerdict);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C4-03", ok.Build()).Status);
    }

    [Fact]
    public void C4_04_PrimaryButtonTopLeft_Violates()
    {
        var b = LayoutBuilder.Screen();
        b.Add(ElementType.Button, 300, 130, 128, 44, "검사 완료 처리", el => el.ButtonKind = ButtonKind.Primary);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C4-04", b.Build()).Status);

        var ok = LayoutBuilder.Screen();
        ok.Add(ElementType.Button, 1768, 1027, 128, 44, "검사 완료 처리", el => el.ButtonKind = ButtonKind.Primary);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C4-04", ok.Build()).Status);
    }

    [Fact]
    public void C4_05_HeaderMidPage_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Header, 20, 140, 178, 10, "제목");
        Assert.Equal(RuleStatus.Violation, TestData.Run("C4-05", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.Header, 20, 15, 178, 10, "제목");
        Assert.Equal(RuleStatus.Pass, TestData.Run("C4-05", ok.Build()).Status);
    }

    [Fact]
    public void C4_06_ScrollableWithoutStickyIdentification_Violates()
    {
        var b = LayoutBuilder.Screen(scrollable: true);
        b.Add(ElementType.Label, 300, 300, 300, 24, "LOT: X-1", el => el.Semantics.Role = SemanticRole.Identification);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C4-06", b.Build()).Status);

        var ok = LayoutBuilder.Screen(scrollable: true);
        ok.Add(ElementType.Label, 264, 64, 300, 32, "LOT: X-1", el => el.Semantics.Role = SemanticRole.Identification);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C4-06", ok.Build()).Status);

        var na = LayoutBuilder.Screen(scrollable: false);
        na.Add(ElementType.Label, 300, 300, 300, 24, "LOT: X-1", el => el.Semantics.Role = SemanticRole.Identification);
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C4-06", na.Build()).Status);
    }
}

public class C5_GroupingRuleTests
{
    [Fact]
    public void C5_01_BlurryGroupBoundary_Violates()
    {
        var b = LayoutBuilder.Paper();
        // group A: intra 2mm, group B starts only 4mm below A → ratio 2 < 2.5
        b.Add(ElementType.Label, 20, 30, 30, 5, "a1", el => el.GroupId = "A");
        b.Add(ElementType.Label, 20, 37, 30, 5, "a2", el => el.GroupId = "A");
        b.Add(ElementType.Label, 20, 46, 30, 5, "b1", el => el.GroupId = "B");
        b.Add(ElementType.Label, 20, 53, 30, 5, "b2", el => el.GroupId = "B");
        Assert.Equal(RuleStatus.Violation, TestData.Run("C5-01", b.Build()).Status);
    }

    [Fact]
    public void C5_01_ClearSeparation_Passes_TwoGroupsRequired()
    {
        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.Label, 20, 30, 30, 5, "a1", el => el.GroupId = "A");
        ok.Add(ElementType.Label, 20, 37, 30, 5, "a2", el => el.GroupId = "A");
        ok.Add(ElementType.Label, 20, 52, 30, 5, "b1", el => el.GroupId = "B");
        ok.Add(ElementType.Label, 20, 59, 30, 5, "b2", el => el.GroupId = "B");
        Assert.Equal(RuleStatus.Pass, TestData.Run("C5-01", ok.Build()).Status);

        var na = LayoutBuilder.Paper();
        na.Add(ElementType.Label, 20, 30, 30, 5, "a1", el => el.GroupId = "A");
        na.Add(ElementType.Label, 20, 37, 30, 5, "a2", el => el.GroupId = "A");
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C5-01", na.Build()).Status);
    }

    [Fact]
    public void C5_02_LabelDriftedFromInput_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 20, 30, 25, 5, "품명");
        b.Add(ElementType.TextInput, 52, 30, 60, 8); // 7mm gap > 3mm
        Assert.Equal(RuleStatus.Violation, TestData.Run("C5-02", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.Label, 20, 30, 25, 5, "품명");
        ok.Add(ElementType.TextInput, 46, 30, 60, 8); // 1mm gap
        Assert.Equal(RuleStatus.Pass, TestData.Run("C5-02", ok.Build()).Status);
    }

    [Fact]
    public void C5_03_OversizedGroup_Violates()
    {
        var b = LayoutBuilder.Paper();
        for (var i = 0; i < 9; i++)
            b.Add(ElementType.Checkbox, 20, 30 + i * 8, 5, 5, $"항목{i}", el =>
            {
                el.GroupId = "dim";
                el.Semantics.Role = SemanticRole.InspectionItem;
            });
        Assert.Equal(RuleStatus.Violation, TestData.Run("C5-03", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        for (var i = 0; i < 5; i++)
            ok.Add(ElementType.Checkbox, 20, 30 + i * 8, 5, 5, $"항목{i}", el =>
            {
                el.GroupId = "dim";
                el.Semantics.Role = SemanticRole.InspectionItem;
            });
        Assert.Equal(RuleStatus.Pass, TestData.Run("C5-03", ok.Build()).Status);
    }

    [Fact]
    public void C5_04_FiveConsecutiveCheckboxes_Violate()
    {
        var b = LayoutBuilder.Paper();
        for (var i = 0; i < 5; i++)
            b.Add(ElementType.Checkbox, 20, 40 + i * 6, 5, 5, $"확인{i}");
        var (status, findings) = TestData.Run("C5-04", b.Build());
        Assert.Equal(RuleStatus.Violation, status);
        Assert.Equal("5", findings.Single().Measured);
    }

    [Fact]
    public void C5_04_DividerBreaksTheRun()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Checkbox, 20, 40, 5, 5, "확인0");
        b.Add(ElementType.Checkbox, 20, 46, 5, 5, "확인1");
        b.Add(ElementType.Checkbox, 20, 52, 5, 5, "확인2");
        b.Add(ElementType.Divider, 20, 57, 100, 1);
        b.Add(ElementType.Checkbox, 20, 58, 5, 5, "확인3");
        b.Add(ElementType.Checkbox, 20, 64, 5, 5, "확인4");
        Assert.Equal(RuleStatus.Pass, TestData.Run("C5-04", b.Build()).Status);
    }

    [Fact]
    public void C5_05_ManyUngroupedItems_Violate()
    {
        var b = LayoutBuilder.Paper();
        for (var i = 0; i < 9; i++)
            b.Add(ElementType.TextInput, 20, 30 + i * 10, 60, 8, cfg: el =>
                el.Semantics.Role = SemanticRole.InspectionItem);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C5-05", b.Build()).Status);

        var na = LayoutBuilder.Paper();
        for (var i = 0; i < 5; i++)
            na.Add(ElementType.TextInput, 20, 30 + i * 10, 60, 8, cfg: el =>
                el.Semantics.Role = SemanticRole.InspectionItem);
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C5-05", na.Build()).Status);
    }
}

public class C6_DensityRuleTests
{
    [Fact]
    public void C6_01_SparsePage_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 20, 30, 30, 5, "달랑 하나");
        Assert.Equal(RuleStatus.Violation, TestData.Run("C6-01", b.Build()).Status);
    }

    [Fact]
    public void C6_01_OvercrowdedPage_ViolatesWithSplitAdvice()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Table, 20, 15, 178, 267); // 100% of printable area
        var (status, findings) = TestData.Run("C6-01", b.Build());
        Assert.Equal(RuleStatus.Violation, status);
        Assert.Contains("페이지 분할", findings.Single().Target);
    }

    [Fact]
    public void C6_02_TooManyInputs_Violate()
    {
        var b = LayoutBuilder.Paper();
        for (var i = 0; i < 26; i++)
            b.Add(ElementType.TextInput, 20 + i % 2 * 90, 25 + i / 2 * 18, 60, 8);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C6-02", b.Build()).Status);
    }

    [Fact]
    public void C6_03_WideTable_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Table, 20, 30, 178, 100, cfg: el => { el.Rows = 5; el.Columns = 8; });
        Assert.Equal(RuleStatus.Violation, TestData.Run("C6-03", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.Table, 20, 30, 178, 100, cfg: el => { el.Rows = 5; el.Columns = 7; });
        Assert.Equal(RuleStatus.Pass, TestData.Run("C6-03", ok.Build()).Status);
    }

    [Fact]
    public void C6_04_TooManyTopLevelBlocks_Violate()
    {
        var b = LayoutBuilder.Screen();
        for (var i = 0; i < 6; i++)
            b.Add(ElementType.Section, 264, 130 + i * 140, 800, 120, $"블록 {i}");
        Assert.Equal(RuleStatus.Violation, TestData.Run("C6-04", b.Build()).Status);

        var ok = LayoutBuilder.Screen();
        for (var i = 0; i < 4; i++)
            ok.Add(ElementType.Section, 264, 130 + i * 200, 800, 150, $"블록 {i}");
        Assert.Equal(RuleStatus.Pass, TestData.Run("C6-04", ok.Build()).Status);
    }

    [Fact]
    public void C6_05_ElementInPrintMargin_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 5, 100, 30, 5, "여백 침범");
        Assert.Equal(RuleStatus.Violation, TestData.Run("C6-05", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.Label, 20, 100, 30, 5, "안전");
        Assert.Equal(RuleStatus.Pass, TestData.Run("C6-05", ok.Build()).Status);
    }
}
