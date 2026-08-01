using System.Text;
using System.Text.Json;
using HfeLayoutSim.Core.Engine;
using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Report;
using HfeLayoutSim.Core.Rules;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 2 || args[0] != "evaluate")
{
    Console.WriteLine("사용법: dotnet run --project src/HfeLayoutSim.Cli -- evaluate <layout.hfelayout.json> [--rules <hfe_rules.json>] [--out <report.json>]");
    return 1;
}

var layoutPath = args[1];
string? rulesPath = null;
string? outPath = null;
for (var i = 2; i < args.Length - 1; i++)
{
    if (args[i] == "--rules") rulesPath = args[i + 1];
    if (args[i] == "--out") outPath = args[i + 1];
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
    var layout = LayoutSerializer.LoadFile(layoutPath);
    var report = new HfeEngine().Evaluate(layout, rules);
    Print(report);

    if (outPath is not null)
    {
        File.WriteAllText(outPath, JsonSerializer.Serialize(report, JsonDefaults.Options));
        Console.WriteLine($"\n리포트 저장: {outPath}");
    }
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

static void Print(EvaluationReport report)
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
