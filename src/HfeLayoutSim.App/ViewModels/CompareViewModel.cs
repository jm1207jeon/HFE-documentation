using System.Collections.ObjectModel;
using HfeLayoutSim.Core.Compare;
using HfeLayoutSim.Core.Report;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.App.ViewModels;

/// <summary>One variant column in the comparison.</summary>
public sealed class VariantColumnViewModel
{
    public VariantColumnViewModel(VariantSummary summary, string accentHex)
    {
        Summary = summary;
        AccentHex = accentHex;
    }

    public VariantSummary Summary { get; }
    public string AccentHex { get; }

    public string Name => Summary.Name;
    // The engine already decided this against the rules file's passGrade; re-deriving it from the
    // letter here would put a second, hard-coded threshold in the product.
    public string Verdict => Summary.Pass ? "PASS ✓" : "FAIL ✕";
    public string VerdictHex => Summary.Pass ? "#2E7D32" : "#C62828";
    public string ScoreLabel => $"{Summary.Total:0.0}";
    public string GradeLabel => Summary.Grade;
    public string FindingLabel => Summary.CriticalCount > 0
        ? $"지적 {Summary.FindingCount}건 (치명 {Summary.CriticalCount})"
        : $"지적 {Summary.FindingCount}건";
    public string CapNote => Summary.GradeCapped ? "Critical 위반 → 등급 상한" : "";
}

/// <summary>A category row across all variants (the radar chart's axes, as a readable table).</summary>
public sealed class CategoryCompareRow
{
    public string Category { get; init; } = "";
    public string Name { get; init; } = "";
    public IReadOnlyList<double> Scores { get; init; } = Array.Empty<double>();

    public string Score0 => Format(0);
    public string Score1 => Format(1);
    public string Score2 => Format(2);
    public bool HasThird => Scores.Count > 2;

    private string Format(int i) => i < Scores.Count ? $"{Scores[i]:0.#}" : "—";
}

/// <summary>A rule that changed between the baseline and a variant.</summary>
public sealed class DiffRowViewModel
{
    public DiffRowViewModel(DiffItem item, string bucket)
    {
        var style = SeverityDisplay.Of(item.Severity);
        RuleId = item.RuleId;
        Title = item.Title;
        Category = item.Category;
        SeverityLabel = $"{style.Glyph} {style.Label}";
        SeverityHex = style.Hex;
        Bucket = bucket;
    }

    public string RuleId { get; }
    public string Title { get; }
    public string Category { get; }
    public string SeverityLabel { get; }
    public string SeverityHex { get; }
    public string Bucket { get; }
}

public sealed class DiffGroupViewModel
{
    public string Header { get; init; } = "";
    public string Hex { get; init; } = "#495057";
    public ObservableCollection<DiffRowViewModel> Rows { get; } = new();
    public bool IsEmpty => Rows.Count == 0;
    public string EmptyNote { get; init; } = "해당 없음";
}

/// <summary>
/// Side-by-side comparison of up to three variants (SPEC §6.5): scores, per-category grades and
/// the rule-level diff that says what an edit actually fixed or broke.
/// </summary>
public sealed class CompareViewModel
{
    /// <summary>Verified palette entries, so each variant is distinguishable without relying on hue alone
    /// (the table also labels every column).</summary>
    private static readonly string[] Accents = { "#1565C0", "#B45309", "#2E7D32" };

    public CompareViewModel(ComparisonResult result)
    {
        Result = result;

        for (var i = 0; i < result.Variants.Count; i++)
            Variants.Add(new VariantColumnViewModel(result.Variants[i], Accents[i % Accents.Length]));

        var categories = result.Variants[0].CategoryScores.Keys.OrderBy(k => k).ToList();
        foreach (var category in categories)
            Categories.Add(new CategoryCompareRow
            {
                Category = category,
                Name = MainViewModel.CategoryName(category),
                Scores = result.Variants.Select(v => v.CategoryScores.GetValueOrDefault(category)).ToList(),
            });

        foreach (var diff in result.Diffs)
        {
            var resolved = new DiffGroupViewModel
            {
                Header = $"해소됨 — {diff.BaselineName} → {diff.VariantName}",
                Hex = "#2E7D32",
                EmptyNote = "해소된 지적 없음",
            };
            foreach (var item in diff.Resolved) resolved.Rows.Add(new DiffRowViewModel(item, "해소"));

            var introduced = new DiffGroupViewModel
            {
                Header = $"신규 발생 — {diff.VariantName}",
                Hex = "#C62828",
                EmptyNote = "새로 생긴 지적 없음",
            };
            foreach (var item in diff.NewlyIntroduced) introduced.Rows.Add(new DiffRowViewModel(item, "신규"));

            var remaining = new DiffGroupViewModel
            {
                Header = "공통 잔존",
                Hex = "#B45309",
                EmptyNote = "공통으로 남은 지적 없음",
            };
            foreach (var item in diff.Remaining) remaining.Rows.Add(new DiffRowViewModel(item, "잔존"));

            DiffGroups.Add(resolved);
            DiffGroups.Add(introduced);
            DiffGroups.Add(remaining);
        }

        Summary = string.Join("   ·   ",
            result.Variants.Select(v => $"{v.Name}: {v.Total:0.0}({v.Grade})"));
    }

    public ComparisonResult Result { get; }

    public ObservableCollection<VariantColumnViewModel> Variants { get; } = new();
    public ObservableCollection<CategoryCompareRow> Categories { get; } = new();
    public ObservableCollection<DiffGroupViewModel> DiffGroups { get; } = new();

    public string Summary { get; }

    public string Title => $"배치안 비교 ({Variants.Count}개)";

    public bool HasThirdVariant => Variants.Count > 2;

    public string Variant0Name => Variants.Count > 0 ? Variants[0].Name : "";
    public string Variant1Name => Variants.Count > 1 ? Variants[1].Name : "";
    public string Variant2Name => Variants.Count > 2 ? Variants[2].Name : "";
}
