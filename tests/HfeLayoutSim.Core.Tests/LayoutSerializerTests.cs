using HfeLayoutSim.Core.Model;

namespace HfeLayoutSim.Core.Tests;

public class LayoutSerializerTests
{
    [Fact]
    public void SampleFile_LoadsWithSemantics()
    {
        var layout = TestData.LoadSample("incoming_inspection_paper.hfelayout.json");
        Assert.Equal(Medium.Paper, layout.Medium);
        Assert.Equal(210, layout.Canvas.Width);

        var lot = layout.Elements.Single(e => e.Id == "in-lot");
        Assert.Equal(ElementType.TextInput, lot.Type);
        Assert.True(lot.Semantics.IsCritical);
        Assert.Equal(InputKind.Transcribe, lot.Semantics.InputKind);
        Assert.Equal(SemanticRole.Identification, lot.Semantics.Role);
        Assert.Equal("id", lot.EffectiveGroupId);
    }

    [Fact]
    public void Roundtrip_PreservesEverything()
    {
        var original = TestData.LoadSample("mes_inspection_screen.hfelayout.json");
        var json = LayoutSerializer.Save(original);
        var reloaded = LayoutSerializer.Load(json);

        Assert.Equal(original.Elements.Count, reloaded.Elements.Count);
        Assert.Equal(original.Meta.Name, reloaded.Meta.Name);
        var badge = reloaded.Elements.Single(e => e.Id == "badge-verdict");
        Assert.True(badge.Semantics.Judgment!.HasIcon);
        Assert.Equal("#EAF4EB", badge.Style.BgColor);
    }

    [Fact]
    public void DuplicateElementId_Rejected()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Label, 20, 20, 30, 5, "a", el => el.Id = "dup");
        b.Add(ElementType.Label, 20, 30, 30, 5, "b", el => el.Id = "dup");
        var ex = Assert.Throws<LayoutFormatException>(() => LayoutSerializer.Validate(b.Build()));
        Assert.Contains("중복", ex.Message);
    }

    [Fact]
    public void ScreenOnlyElementOnPaper_Rejected()
    {
        var b = LayoutBuilder.Paper();
        b.Add(ElementType.Button, 20, 20, 30, 10, "확인");
        var ex = Assert.Throws<LayoutFormatException>(() => LayoutSerializer.Validate(b.Build()));
        Assert.Contains("Screen 전용", ex.Message);
    }

    [Fact]
    public void SyntaxError_ReportsLine()
    {
        var ex = Assert.Throws<LayoutFormatException>(() => LayoutSerializer.Load("{\n  \"meta\": [ }"));
        Assert.Contains("구문 오류", ex.Message);
    }
}
