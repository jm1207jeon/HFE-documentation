using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Report;

namespace HfeLayoutSim.Core.Tests;

public class C1_ColorContrastRuleTests
{
    [Fact]
    public void C1_01_LowContrastNormalText_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 20, 30, 60, 5, "주의 문구", el => el.Style.FgColor = "#F57C00");
        var (status, findings) = TestData.Run("C1-01", b.Build());
        Assert.Equal(RuleStatus.Violation, status);
        Assert.StartsWith("2.70", findings.Single().Measured);
    }

    [Fact]
    public void C1_01_DefaultTextOnWhite_Passes()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 20, 30, 60, 5, "본문");
        Assert.Equal(RuleStatus.Pass, TestData.Run("C1-01", b.Build()).Status);
    }

    [Fact]
    public void C1_01_NoText_NotApplicable()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.TextInput, 20, 30, 60, 8);
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C1-01", b.Build()).Status);
    }

    [Fact]
    public void C1_02_LargeText_UsesRelaxedThreshold()
    {
        var b = LayoutBuilder.Paper();
        // 2.70:1 fails even the 3:1 large-text bar
        b.Add(ElementType.Header, 20, 15, 100, 10, "제목", el =>
        {
            el.Style.FontSize = 18;
            el.Style.FgColor = "#F57C00";
        });
        Assert.Equal(RuleStatus.Violation, TestData.Run("C1-02", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        // 3.79:1 passes as large text (warn-accent is large-only by design)
        ok.Add(ElementType.Header, 20, 15, 100, 10, "제목", el =>
        {
            el.Style.FontSize = 18;
            el.Style.FgColor = "#E65100";
        });
        Assert.Equal(RuleStatus.Pass, TestData.Run("C1-02", ok.Build()).Status);
    }

    [Fact]
    public void C1_03_WeakBorder_ViolatesOnScreen()
    {
        var b = LayoutBuilder.Screen();
        b.Add(ElementType.TextInput, 300, 200, 200, 40, cfg: el => el.Style.BorderColor = "#DEE2E6");
        Assert.Equal(RuleStatus.Violation, TestData.Run("C1-03", b.Build()).Status);

        var ok = LayoutBuilder.Screen();
        ok.Add(ElementType.TextInput, 300, 200, 200, 40, cfg: el => el.Style.BorderColor = "#868E96");
        Assert.Equal(RuleStatus.Pass, TestData.Run("C1-03", ok.Build()).Status);
    }

    [Fact]
    public void C1_03_NotApplicableOnPaper()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.TextInput, 20, 30, 60, 8, cfg: el => el.Style.BorderColor = "#DEE2E6");
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C1-03", b.Build()).Status);
    }

    [Fact]
    public void C1_04_ColorOnlyJudgment_ViolatesAsCritical()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.JudgmentBadge, 120, 250, 30, 8, cfg: el =>
            el.Semantics.Judgment = new JudgmentCoding { HasColor = true });
        var (status, findings) = TestData.Run("C1-04", b.Build());
        Assert.Equal(RuleStatus.Violation, status);
        Assert.Equal(Core.Rules.RuleSeverity.Critical, findings.Single().Severity);
    }

    [Fact]
    public void C1_04_TripleCoded_Passes_And_NoJudgment_NA()
    {
        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.JudgmentBadge, 120, 250, 30, 8, "적합", el =>
            el.Semantics.Judgment = new JudgmentCoding { HasColor = true, HasIcon = true, HasText = true });
        Assert.Equal(RuleStatus.Pass, TestData.Run("C1-04", ok.Build()).Status);

        var na = LayoutBuilder.Paper();
        na.Add(ElementType.Label, 20, 30, 30, 5, "라벨");
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C1-04", na.Build()).Status);
    }

    [Fact]
    public void C1_05_LargeSemanticBackground_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.WarningBox, 20, 20, 178, 140, cfg: el => el.Style.BgColor = "#2E7D32"); // ~52%
        Assert.Equal(RuleStatus.Violation, TestData.Run("C1-05", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.JudgmentBadge, 120, 250, 20, 6, cfg: el => el.Style.BgColor = "#2E7D32");
        Assert.Equal(RuleStatus.Pass, TestData.Run("C1-05", ok.Build()).Status);
    }

    [Fact]
    public void C1_06_AdjacentColorOnlyRedGreen_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Image, 20, 30, 10, 10, cfg: el => el.Style.BgColor = "#C62828");
        b.Add(ElementType.Image, 35, 30, 10, 10, cfg: el => el.Style.BgColor = "#2E7D32"); // 5mm apart
        Assert.Equal(RuleStatus.Violation, TestData.Run("C1-06", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.Image, 20, 30, 10, 10, cfg: el => el.Style.BgColor = "#C62828");
        ok.Add(ElementType.Image, 20, 100, 10, 10, cfg: el => el.Style.BgColor = "#2E7D32"); // far apart
        Assert.Equal(RuleStatus.Pass, TestData.Run("C1-06", ok.Build()).Status);
    }

    [Fact]
    public void C1_06_TextDifferentiatorNeutralizes()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.JudgmentBadge, 20, 30, 15, 6, "적합", el => el.Style.BgColor = "#2E7D32");
        b.Add(ElementType.JudgmentBadge, 40, 30, 15, 6, "부적합", el => el.Style.BgColor = "#C62828");
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C1-06", b.Build()).Status);
    }

    [Fact]
    public void C1_07_UnverifiedChromaticColor_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 20, 30, 40, 5, "튀는 색", el => el.Style.FgColor = "#FF00FF");
        var (status, findings) = TestData.Run("C1-07", b.Build());
        Assert.Equal(RuleStatus.Violation, status);
        Assert.Equal("#FF00FF", findings.Single().Measured);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.Label, 20, 30, 40, 5, "파랑", el => el.Style.FgColor = "#1565C0");
        Assert.Equal(RuleStatus.Pass, TestData.Run("C1-07", ok.Build()).Status);
    }
}

public class C2_TypographyRuleTests
{
    [Fact]
    public void C2_01_TinyFont_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 20, 30, 40, 4, "작은 글씨", el => el.Style.FontSize = 7);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C2-01", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.Label, 20, 30, 40, 4, "글씨", el => el.Style.FontSize = 8);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C2-01", ok.Build()).Status);
    }

    [Fact]
    public void C2_02_ThreeFontFamilies_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 20, 30, 40, 5, "a", el => el.Style.FontFamily = "Pretendard");
        b.Add(ElementType.Label, 20, 40, 40, 5, "b", el => el.Style.FontFamily = "D2Coding");
        b.Add(ElementType.Label, 20, 50, 40, 5, "c", el => el.Style.FontFamily = "Batang");
        Assert.Equal(RuleStatus.Violation, TestData.Run("C2-02", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.Label, 20, 30, 40, 5, "a", el => el.Style.FontFamily = "Pretendard");
        ok.Add(ElementType.Label, 20, 40, 40, 5, "b", el => el.Style.FontFamily = "D2Coding");
        Assert.Equal(RuleStatus.Pass, TestData.Run("C2-02", ok.Build()).Status);
    }

    [Fact]
    public void C2_03_SixSizeLevels_Violates()
    {
        var b = LayoutBuilder.Paper();
        double[] sizes = { 8, 9, 10, 11, 12, 14 };
        for (var i = 0; i < sizes.Length; i++)
            b.Add(ElementType.Label, 20, 30 + i * 10, 40, 5, $"t{i}", el => el.Style.FontSize = sizes[i]);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C2-03", b.Build()).Status);
    }

    [Fact]
    public void C2_04_NumInputWithoutMonoFont_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.NumInput, 20, 30, 15, 8);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C2-04", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.NumInput, 20, 30, 15, 8, cfg: el => el.Style.FontFamily = "D2Coding");
        Assert.Equal(RuleStatus.Pass, TestData.Run("C2-04", ok.Build()).Status);

        var na = LayoutBuilder.Paper();
        na.Add(ElementType.Label, 20, 30, 30, 5, "라벨");
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C2-04", na.Build()).Status);
    }

    [Fact]
    public void C2_05_ItalicEmphasis_Violates_UnderlinedLinkAllowed()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 20, 30, 40, 5, "강조", el => el.Style.Italic = true);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C2-05", b.Build()).Status);

        var ok = LayoutBuilder.Screen();
        ok.Add(ElementType.Label, 300, 200, 100, 20, "링크", el =>
        {
            el.Style.Underline = true;
            el.Semantics.Role = SemanticRole.Navigation;
        });
        Assert.Equal(RuleStatus.Pass, TestData.Run("C2-05", ok.Build()).Status);
    }

    [Fact]
    public void C2_06_WeakTitle_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Header, 20, 15, 170, 10, "제목", el => el.Style.FontSize = 10);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C2-06", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.Header, 20, 15, 170, 10, "제목", el =>
        {
            el.Style.FontSize = 16;
            el.Style.Bold = true;
        });
        Assert.Equal(RuleStatus.Pass, TestData.Run("C2-06", ok.Build()).Status);
    }
}

public class C3_SizeTargetRuleTests
{
    [Fact]
    public void C3_01_TinyCheckbox_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Checkbox, 20, 30, 3, 3, "확인");
        Assert.Equal(RuleStatus.Violation, TestData.Run("C3-01", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.Checkbox, 20, 30, 5, 5, "확인");
        Assert.Equal(RuleStatus.Pass, TestData.Run("C3-01", ok.Build()).Status);
    }

    [Fact]
    public void C3_02_SmallClickTarget_ViolatesOnScreen()
    {
        var b = LayoutBuilder.Screen();
        b.Add(ElementType.Checkbox, 300, 200, 18, 18, "확인");
        Assert.Equal(RuleStatus.Violation, TestData.Run("C3-02", b.Build()).Status);

        var ok = LayoutBuilder.Screen();
        ok.Add(ElementType.Checkbox, 300, 200, 24, 24, "확인");
        Assert.Equal(RuleStatus.Pass, TestData.Run("C3-02", ok.Build()).Status);
    }

    [Fact]
    public void C3_03_ShortPrimaryButton_Violates()
    {
        var b = LayoutBuilder.Screen();
        b.Add(ElementType.Button, 1700, 1020, 120, 32, "저장하기", el => el.ButtonKind = ButtonKind.Primary);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C3-03", b.Build()).Status);

        var na = LayoutBuilder.Screen();
        na.Add(ElementType.Button, 1700, 1020, 120, 44, "저장하기", el => el.ButtonKind = ButtonKind.Secondary);
        Assert.Equal(RuleStatus.NotApplicable, TestData.Run("C3-03", na.Build()).Status);
    }

    [Fact]
    public void C3_04_CrowdedButtons_Violate()
    {
        var b = LayoutBuilder.Screen();
        b.Add(ElementType.Button, 100, 100, 120, 44, "버튼 하나");
        b.Add(ElementType.Button, 224, 100, 120, 44, "버튼 둘"); // 4px gap
        Assert.Equal(RuleStatus.Violation, TestData.Run("C3-04", b.Build()).Status);

        var ok = LayoutBuilder.Screen();
        ok.Add(ElementType.Button, 100, 100, 120, 44, "버튼 하나");
        ok.Add(ElementType.Button, 232, 100, 120, 44, "버튼 둘"); // 12px gap
        Assert.Equal(RuleStatus.Pass, TestData.Run("C3-04", ok.Build()).Status);
    }

    [Fact]
    public void C3_05_DangerButtonTooClose_Violates()
    {
        var b = LayoutBuilder.Screen();
        b.Add(ElementType.Button, 100, 100, 120, 44, "삭제 실행", el => el.ButtonKind = ButtonKind.Danger);
        b.Add(ElementType.Button, 230, 100, 120, 44, "저장하기"); // 10px gap < 24
        Assert.Equal(RuleStatus.Violation, TestData.Run("C3-05", b.Build()).Status);

        var ok = LayoutBuilder.Screen();
        ok.Add(ElementType.Button, 100, 100, 120, 44, "삭제 실행", el => el.ButtonKind = ButtonKind.Danger);
        ok.Add(ElementType.Button, 300, 100, 120, 44, "저장하기"); // 80px gap
        Assert.Equal(RuleStatus.Pass, TestData.Run("C3-05", ok.Build()).Status);
    }

    [Fact]
    public void C3_06_SmallSignatureBox_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.SignatureBox, 124, 260, 25, 6, cfg: el => el.Semantics.Role = SemanticRole.Signature);
        Assert.Equal(RuleStatus.Violation, TestData.Run("C3-06", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.SignatureBox, 124, 260, 50, 10, cfg: el => el.Semantics.Role = SemanticRole.Signature);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C3-06", ok.Build()).Status);
    }

    [Fact]
    public void C3_07_FarLabel_Violates()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 20, 30, 25, 5, "측정값 *");
        b.Add(ElementType.TextInput, 20, 100, 60, 8); // 65mm below
        Assert.Equal(RuleStatus.Violation, TestData.Run("C3-07", b.Build()).Status);

        var ok = LayoutBuilder.Paper();
        ok.Add(ElementType.Label, 20, 30, 25, 5, "측정값 *");
        ok.Add(ElementType.TextInput, 46, 30, 60, 8);
        Assert.Equal(RuleStatus.Pass, TestData.Run("C3-07", ok.Build()).Status);
    }
}
