using System.Text;
using System.Text.Json;
using HfeLayoutSim.Core.Advisor;
using HfeLayoutSim.Core.Catalog;
using HfeLayoutSim.Core.Compare;
using HfeLayoutSim.Core.Engine;
using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Presets;
using HfeLayoutSim.Core.Report;
using HfeLayoutSim.Core.Rules;
using HfeLayoutSim.Core.Tuning;

Console.OutputEncoding = Encoding.UTF8;

var command = args.Length > 0 ? args[0] : "";
string[] knownCommands = { "evaluate", "compare", "advise", "generate", "regress" };
if (!knownCommands.Contains(command))
{
    Console.WriteLine("""
        사용법:
          evaluate <layout.hfelayout.json> [--rules R] [--out report.json] [--html report.html]
          compare  <a.hfelayout.json> <b.hfelayout.json> [c] [--rules R]
          advise   <layout.hfelayout.json> [elementId] [--rules R] [--presets P]
          generate <catalog.json> --medium paper|screen --out layout.hfelayout.json [--rules R] [--presets P]
          regress  <modified_rules.json> <layout...> [--rules 기준규칙]
        """);
    return 1;
}

var files = new List<string>();
string? rulesPath = null, outPath = null, htmlPath = null, presetsPath = null, mediumArg = null;
for (var i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--rules" when i + 1 < args.Length: rulesPath = args[++i]; break;
        case "--out" when i + 1 < args.Length: outPath = args[++i]; break;
        case "--html" when i + 1 < args.Length: htmlPath = args[++i]; break;
        case "--presets" when i + 1 < args.Length: presetsPath = args[++i]; break;
        case "--medium" when i + 1 < args.Length: mediumArg = args[++i]; break;
        default: files.Add(args[i]); break;
    }
}

rulesPath ??= FindUp("rules/hfe_rules.json");
presetsPath ??= FindUp("rules/element_presets.json");
if (rulesPath is null)
{
    Console.Error.WriteLine("rules/hfe_rules.json을 찾을 수 없습니다. --rules로 경로를 지정하십시오.");
    return 2;
}

try
{
    var rules = RuleLoader.LoadFile(rulesPath);
    var engine = new HfeEngine();
    var presets = presetsPath is not null ? PresetLibrary.LoadFile(presetsPath) : null;

    switch (command)
    {
        case "evaluate":
        {
            if (files.Count != 1) return Fail("evaluate에는 레이아웃 파일 1개가 필요합니다.");
            var report = engine.Evaluate(LayoutSerializer.LoadFile(files[0]), rules);
            PrintReport(report);
            if (outPath is not null)
            {
                AtomicFile.WriteAllText(outPath, JsonSerializer.Serialize(report, JsonDefaults.Options));
                Console.WriteLine($"\nJSON 리포트 저장: {outPath}");
            }
            if (htmlPath is not null)
            {
                HtmlReportExporter.ExportFile(report, htmlPath);
                Console.WriteLine($"HTML 리포트 저장: {htmlPath}");
            }
            return 0;
        }

        case "compare":
        {
            if (files.Count is < 2 or > VariantComparer.MaxVariants)
                return Fail($"compare에는 레이아웃 파일 2~{VariantComparer.MaxVariants}개가 필요합니다.");
            var reports = files.Select(f => engine.Evaluate(LayoutSerializer.LoadFile(f), rules)).ToList();
            PrintComparison(VariantComparer.Compare(reports));
            return 0;
        }

        case "advise":
        {
            if (files.Count is < 1 or > 2) return Fail("advise: <layout> [elementId]");
            var layout = LayoutSerializer.LoadFile(files[0]);
            var advisor = new PlacementAdvisor(rules, presets);
            if (files.Count == 2)
                PrintAdvice(layout.Elements.First(e => e.Id == files[1]), advisor.Advise(layout, files[1]));
            else
                foreach (var (element, advice) in advisor.AdviseAll(layout))
                    PrintAdvice(element, advice);
            return 0;
        }

        case "generate":
        {
            if (files.Count != 1) return Fail("generate: <catalog.json> --medium paper|screen --out <layout>");
            if (mediumArg is not ("paper" or "screen")) return Fail("--medium paper|screen 을 지정하십시오.");
            if (outPath is null) return Fail("--out <layout.hfelayout.json> 을 지정하십시오.");
            if (presets is null) return Fail("rules/element_presets.json을 찾을 수 없습니다 (--presets).");

            var catalog = InspectionCatalog.LoadFile(files[0]);
            var medium = mediumArg == "paper" ? Medium.Paper : Medium.Screen;
            var layout = new LayoutComposer(presets, rules).Compose(catalog, medium);
            LayoutSerializer.SaveFile(layout, outPath);
            Console.WriteLine($"레이아웃 생성 완료: {outPath} (요소 {layout.Elements.Count}개)");

            var report = engine.Evaluate(layout, rules);
            Console.WriteLine($"자동 평가: 총점 {report.ScoreCard.Total:0.0} · 등급 {report.ScoreCard.Grade} · " +
                              $"{(report.ScoreCard.Pass ? "PASS" : "FAIL")} · 지적 {report.Findings.Count}건");
            return 0;
        }

        case "regress":
        {
            if (files.Count < 2) return Fail("regress: <modified_rules.json> <layout...>");
            var modified = RuleLoader.LoadFile(files[0]);
            var layouts = files.Skip(1)
                .Select(f => (Path.GetFileNameWithoutExtension(f), LayoutSerializer.LoadFile(f)))
                .ToList();
            PrintRegression(RuleRegression.Compare(rules, modified, layouts));
            return 0;
        }
    }
    return 0;
}
catch (Exception ex) when (ex is RuleLoadException or LayoutFormatException or PresetLoadException or CatalogLoadException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
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
    var sc = report.ScoreCard;
    Console.WriteLine("══════════════════════════════════════════════════");
    Console.WriteLine($"  HFE 평가 리포트 — {report.LayoutName} [{(report.Medium == Medium.Paper ? "Paper" : "Screen")}]");
    Console.WriteLine($"  규칙 버전 {report.RulesVersion} · {report.EvaluatedAt:yyyy-MM-dd HH:mm}");
    Console.WriteLine("══════════════════════════════════════════════════");
    // verifier-style verdict banner
    var verdict = sc.Pass ? "PASS ✓" : "FAIL ✕";
    Console.WriteLine($"  판정 {verdict}   종합 등급 {sc.Grade}   총점 {sc.Total:0.0}/100   (합격 기준: {sc.PassGrade} 이상)");
    if (sc.GradeCapped)
        Console.WriteLine("  ⚠ Critical 위반 → 등급 상한 적용, 배포 부적합");
    Console.WriteLine("  " + string.Join("  ", sc.Categories.Select(c => $"{c.Category}:{c.Grade}")));
    Console.WriteLine("──────────────────────────────────────────────────");
    foreach (var c in sc.Categories)
    {
        var bar = new string('█', (int)Math.Round(c.Score / 5));
        Console.WriteLine($"  {c.Category} [{c.Grade}] {c.Score,5:0.#}  {bar,-20}  (통과 {c.PassCount} / 위반 {c.ViolationCount} / N.A. {c.NotApplicableCount})");
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

static void PrintAdvice(LayoutElement element, IReadOnlyList<AdviceItem> advice)
{
    var role = element.Semantics.Role == SemanticRole.None ? "" : $" · {element.Semantics.Role}";
    Console.WriteLine($"\n▸ [{element.Id}] {element.Type}{role} @ ({element.X:0.#}, {element.Y:0.#})");
    foreach (var a in advice)
    {
        var mark = a.Level switch
        {
            AdviceLevel.Ok => "✓",
            AdviceLevel.Info => "ⓘ",
            AdviceLevel.Warning => "!",
            _ => "✕"
        };
        var rule = a.RuleId is null ? "" : $" [{a.RuleId}]";
        Console.WriteLine($"   {mark} ({a.Topic}){rule} {a.Message}");
        if (!string.IsNullOrWhiteSpace(a.Suggestion))
            Console.WriteLine($"      → {a.Suggestion}");
    }
}

static void PrintRegression(RegressionReport report)
{
    Console.WriteLine("══════════════════════════════════════════════════");
    Console.WriteLine($"  규칙 튜닝 회귀: v{report.RulesVersionA} (기준) → v{report.RulesVersionB} (수정)");
    Console.WriteLine("══════════════════════════════════════════════════");
    foreach (var row in report.Rows)
    {
        var delta = row.Delta switch { > 0 => $"+{row.Delta:0.0}", < 0 => $"{row.Delta:0.0}", _ => "±0" };
        var pass = row.PassA == row.PassB
            ? (row.PassB ? "PASS 유지" : "FAIL 유지")
            : (row.PassB ? "FAIL→PASS ▲" : "PASS→FAIL ▼");
        Console.WriteLine($"\n  {row.Name}: {row.TotalA:0.0}({row.GradeA}) → {row.TotalB:0.0}({row.GradeB})  Δ{delta}  {pass}");
        if (row.Changes.Count == 0)
        {
            Console.WriteLine("    변화한 규칙 없음");
            continue;
        }
        foreach (var c in row.Changes)
            Console.WriteLine($"    {c.RuleId} {c.Title}: {c.StatusA}({c.FindingsA}) → {c.StatusB}({c.FindingsB})");
    }
    Console.WriteLine($"\n  종합: {(report.AnyChange ? "변화 있음 — 위 목록 검토 후 반영 결정" : "변화 없음")}");
    Console.WriteLine("══════════════════════════════════════════════════");
}
