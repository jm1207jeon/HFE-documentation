using HfeLayoutSim.Core.Engine;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Tests;

public class RuleLoaderTests
{
    [Fact]
    public void LoadsRealKnowledgeBase()
    {
        var rules = TestData.Rules;
        Assert.Equal("1.0", rules.Version);
        Assert.Equal(53, rules.Rules.Count);
        Assert.Equal(3, rules.Penalties.Count);
        Assert.Equal(20, rules.PenaltyOf(RuleSeverity.Critical));
        Assert.True(rules.CriticalElementSeverityEscalation);
        Assert.Equal("C", rules.CriticalViolationGradeCap);
        Assert.Equal("B", rules.PassGrade);
        Assert.Equal(1.0, rules.Weights.Values.Sum(), 3);

        var categories = rules.Rules.Select(r => r.Category).Distinct().OrderBy(c => c).ToList();
        Assert.Equal(new[] { "C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9" }, categories);
    }

    [Fact]
    public void PaletteTokensAreParsed()
    {
        Assert.True(TestData.Rules.Palette.ContainsKey("text"));
        Assert.Equal("#212529", TestData.Rules.DefaultTextColor);
        Assert.Equal("#EAF4EB", TestData.Rules.Palette["pass"].Bg);
    }

    [Fact]
    public void EveryRuleCheckHasAnEvaluator()
        => new EvaluatorRegistry().EnsureCoverage(TestData.Rules); // throws on gaps

    [Fact]
    public void MalformedJson_ReportsPosition()
    {
        var ex = Assert.Throws<RuleLoadException>(() => RuleLoader.Load("{ \"rules\": [ }", "broken.json"));
        Assert.Contains("구문 오류", ex.Message);
        Assert.Contains("broken.json", ex.Message);
    }

    [Fact]
    public void DuplicateRuleId_Rejected()
    {
        const string json = """
        {
          "penalties": { "Minor": 3, "Major": 8, "Critical": 20 },
          "weights": { "C1": 1.0 },
          "rules": [
            { "id": "X-01", "category": "C1", "severity": "Minor", "check": "textContrast" },
            { "id": "X-01", "category": "C1", "severity": "Minor", "check": "textContrast" }
          ]
        }
        """;
        var ex = Assert.Throws<RuleLoadException>(() => RuleLoader.Load(json));
        Assert.Contains("중복", ex.Message);
    }

    [Fact]
    public void UnknownCheck_FailsCoverage()
    {
        const string json = """
        {
          "penalties": { "Minor": 3, "Major": 8, "Critical": 20 },
          "weights": { "C1": 1.0 },
          "rules": [
            { "id": "X-01", "category": "C1", "severity": "Minor", "check": "noSuchCheck" }
          ]
        }
        """;
        var rules = RuleLoader.Load(json);
        var ex = Assert.Throws<RuleLoadException>(() => new EvaluatorRegistry().EnsureCoverage(rules));
        Assert.Contains("noSuchCheck", ex.Message);
    }

    [Fact]
    public void WeightsMustSumToOne()
    {
        const string json = """
        {
          "penalties": { "Minor": 3, "Major": 8, "Critical": 20 },
          "weights": { "C1": 0.5 },
          "rules": []
        }
        """;
        var ex = Assert.Throws<RuleLoadException>(() => RuleLoader.Load(json));
        Assert.Contains("weights", ex.Message);
    }
}
