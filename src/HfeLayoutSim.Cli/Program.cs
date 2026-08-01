using System.Text;
using System.Text.Json;
using HfeLayoutSim.Core.Compare;
using HfeLayoutSim.Core.Engine;
using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Report;
using HfeLayoutSim.Core.Rules;

Console.OutputEncoding = Encoding.UTF8;

var command = args.Length > 0 ? args[0] : "";
if (command is not ("evaluate" or "compare"))
{
    Console.WriteLine("""
        사용법:
          evaluate <layout.hfelayout.json> [--rules <hfe_rules.json>] [--out <report.json>] [--html <report.html>]
          compare  <a.hfelayout.json> <b.hfelayout.json> [c.hfelayout.json] [--rules <hfe_rules.json>]
        """);
    return 1;
}

var files = new List<string>();
string? rulesPath = null;
string? outPath = null;
string? htmlPath = null;
for (var i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--rules" when i + 1 < args.Length: rulesPath = args[++i]; break;
        case "--out" when i + 1 < args.Length: outPath = args[++i]; break;
        case "--html" when i + 1 < args.Length: htmlPath = args[++i]; break;
        default: files.Add(args[i]); break;
    }
}

rulesPath ??= FindUp("rules/hfe_rules.json");
if (rulesPath is null)
{
    Console.Error.WriteLine("rules/hfe_rules.json을 찾을 수 없습니다. --rules로 경로를 지정하십시오.");
    return 2;
}

try
{
    var rules = RuleLoader.LoadFile(rulesPath);
    var engine = new HfeEngine();

    if (command == "evaluate")
    {
        if (files.Count != 1)
        {
            Console.Error.WriteLine("evaluate에는 레이아웃 파일 1개가 필요합니다.");
            return 1;
        }
        var report = engine.Evaluate(LayoutSerializer.LoadFile(files[0]), rules);
        PrintReport(report);

        if (outPath is not null)
        {
            File.WriteAllText(outPath, JsonSerializer.Serialize(report, JsonDefaults.Options));
            Console.WriteLine($"\nJSON 리포트 저장: {outPath}");
        }
        if (htmlPath is not null)
        {
            HtmlReportExporter.ExportFile(report, htmlPath);
            Console.WriteLine($"HTML 리포트 저장: {htmlPath}");
        }
        return 0;
    }

    // ---- compare ----
    if (files.Count is < 2 or > VariantComparer.MaxVariants)
    {
        Console.Error.WriteLine($"compare에는 레이아웃 파일 2~{VariantComparer.MaxVariants}개가 필요합니다.");
        return 1;
    }
    var reports = files.Select(f => engine.Evaluate(LayoutSerializer.LoadFile(f), rules)).ToList();
    PrintComparison(VariantComparer.Compare(reports));
    return 0;
}
catch (Exception ex) when (ex is RuleLoadException or LayoutFormatException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static string? FindUp(string relative)
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        var candidate = Path.Combine(dir.FullName, relative);
        if (File.Exists(candidate)) return candidate;
        dir = dir.Parent;
    }
    return null;
}

static void PrintReport(EvaluationReport report)
{
    Console.WriteLine("══════════════════════════════════════════════════");
    Console.WriteLine($"  HFE 평가 리포트 — {report.LayoutName} [{(report.Medium == Medium.Paper ? "Paper" : "Screen")}]");
    Console.WriteLine($"  규칙 버전 {report.RulesVersion} · {report.EvaluatedAt:yyyy-MM-dd HH:mm}");
    Console.WriteLine("══════════════════════════════════════════════════");
    var cap = report.ScoreCard.GradeCapped ? " (Critical 위반 → 등급 상한 적용, 배포 부적합)" : "";
    Console.WriteLine($"  총점 {report.ScoreCard.Total:0.0} / 100   등급 {report.ScoreCard.Grade}{cap}");
    Console.WriteLine("──────────────────────────────────────────────────");
    foreach (var c in report.ScoreCard.Categories)
    {
        var bar = new string('█', (int)Math.Round(c.Score / 5));
        Console.WriteLine($"  {c.Category}  {c.Score,5:0.#}  {bar,-20}  (통과 {c.PassCount} / 위반 {c.ViolationCount} / N.A. {c.NotApplicableCount})");
    }

    if (report.Findings.Count > 0)
    {
        Console.WriteLine("──────────────────────────────────────────────────");
        Console.WriteLine($"  지적 사항 {report.Findings.Count}건 (심각도순)");
        foreach (var f in report.Findings)
        {
            var esc = f.Escalated ? " ↑Critical요소" : "";
            Console.WriteLine($"\n  [{f.Severity}{esc}] {f.RuleId} {f.Title}  (-{f.Penalty})");
            Console.WriteLine($"    {f.Message}");
            Console.WriteLine($"    실측: {f.Measured}  |  기준: {f.Target}");
            if (f.ElementIds.Count > 0)
                Console.WriteLine($"    요소: {string.Join(", ", f.ElementIds.Take(8))}{(f.ElementIds.Count > 8 ? " …" : "")}");
            Console.WriteLine($"    권고: {f.Recommendation}");
            Console.WriteLine($"    근거: {f.StandardRef}");
        }
    }

    if (report.Strengths.Count > 0)
    {
        Console.WriteLine("──────────────────────────────────────────────────");
        Console.WriteLine($"  강점 {report.Strengths.Count}건");
        foreach (var s in report.Strengths)
            Console.WriteLine($"  ✓ {s.RuleId} {s.Title}  ({s.StandardRef})");
    }
    Console.WriteLine("══════════════════════════════════════════════════");
}

static void PrintComparison(ComparisonResult result)
{
    Console.WriteLine("══════════════════════════════════════════════════");
    Console.WriteLine("  Variant 비교");
    Console.WriteLine("══════════════════════════════════════════════════");
    foreach (var v in result.Variants)
    {
        var cap = v.GradeCapped ? " (상한 적용)" : "";
        Console.WriteLine($"  {v.Name}: 총점 {v.Total:0.0} · 등급 {v.Grade}{cap} · 지적 {v.FindingCount}건 (Critical {v.CriticalCount})");
    }

    Console.WriteLine("──────────────────────────────────────────────────");
    Console.WriteLine("  카테고리 점수");
    var categories = result.Variants[0].CategoryScores.Keys.OrderBy(k => k).ToList();
    Console.Write("        ");
    foreach (var v in result.Variants) Console.Write($"{Truncate(v.Name, 14),16}");
    Console.WriteLine();
    foreach (var cat in categories)
    {
        Console.Write($"  {cat,-6}");
        foreach (var v in result.Variants)
            Console.Write($"{v.CategoryScores.GetValueOrDefault(cat),16:0.#}");
        Console.WriteLine();
    }

    foreach (var diff in result.Diffs)
    {
        Console.WriteLine("──────────────────────────────────────────────────");
        Console.WriteLine($"  Finding diff: {diff.BaselineName} → {diff.VariantName}");
        PrintBucket("해소됨", diff.Resolved);
        PrintBucket("신규 발생", diff.NewlyIntroduced);
        PrintBucket("공통 잔존", diff.Remaining);
    }
    Console.WriteLine("══════════════════════════════════════════════════");

    static void PrintBucket(string name, IReadOnlyList<DiffItem> items)
    {
        Console.WriteLine($"  ▸ {name} ({items.Count}건)");
        foreach (var item in items)
            Console.WriteLine($"      [{item.Severity}] {item.RuleId} {item.Title}");
    }

    static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";
}
