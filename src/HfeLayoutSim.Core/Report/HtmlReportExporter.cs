using System.Net;
using System.Text;
using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Report;

/// <summary>
/// Renders an EvaluationReport as a self-contained, print-friendly HTML document (SPEC §6.4).
/// The template itself follows the manual: verified DESIGN_TOKENS colors only, and severity
/// is triple-coded (icon + text + color) so the report survives grayscale printing.
/// </summary>
public static class HtmlReportExporter
{
    public static string Export(EvaluationReport report)
    {
        var sb = new StringBuilder(32 * 1024);
        var medium = report.Medium == Medium.Paper ? "Paper (A4)" : "Screen (MES)";

        sb.Append("<!DOCTYPE html>\n<html lang=\"ko\">\n<head>\n<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append($"<title>HFE 평가 리포트 — {E(report.LayoutName)}</title>\n");
        sb.Append("<style>\n").Append(Css).Append("</style>\n</head>\n<body>\n");

        // ---- header ----
        sb.Append("<header>\n");
        sb.Append($"<h1>HFE 평가 리포트</h1>\n");
        sb.Append("<table class=\"meta\">\n");
        sb.Append($"<tr><th>레이아웃</th><td>{E(report.LayoutName)}</td>");
        sb.Append($"<th>매체</th><td>{medium}</td></tr>\n");
        sb.Append($"<tr><th>규칙 버전</th><td>{E(report.RulesVersion)}</td>");
        sb.Append($"<th>평가 일시</th><td>{report.EvaluatedAt:yyyy-MM-dd HH:mm}</td></tr>\n");
        sb.Append("</table>\n</header>\n");

        // ---- verifier-style verdict banner (barcode-verifier look: PASS/FAIL + letter grades) ----
        var sc = report.ScoreCard;
        var passCls = sc.Pass ? "pass" : "fail";
        sb.Append($"<section class=\"verdictbar {passCls}\">\n");
        sb.Append($"<span class=\"verdict\">{(sc.Pass ? "PASS ✓" : "FAIL ✕")}</span>");
        sb.Append($"<span class=\"biggrade\">{E(sc.Grade)}</span>");
        sb.Append($"<span class=\"score\">{sc.Total:0.0} <small>/ 100 · 합격 기준 {E(sc.PassGrade)} 이상</small></span>\n");
        sb.Append("<div class=\"paramgrades\">\n");
        foreach (var c in sc.Categories)
            sb.Append($"<span class=\"pgrade grade-{GradeClass(c.Grade)}\" title=\"{E(CategoryName(c.Category))}\">" +
                      $"{E(c.Category)}<b>{E(c.Grade)}</b></span>\n");
        sb.Append("</div>\n</section>\n");

        // ---- score card ----
        sb.Append("<section class=\"scorecard\">\n");
        if (sc.GradeCapped)
            sb.Append("<p class=\"capnote\">✕ Critical 위반 존재 — 등급 상한 적용, <strong>배포 부적합</strong> (MNL-QA-HFE-002 §7.2)</p>\n");

        sb.Append("<table class=\"categories\">\n<thead><tr><th>카테고리</th><th>등급</th><th>점수</th><th></th>" +
                  "<th>통과</th><th>위반</th><th>N.A.</th><th>가중치</th></tr></thead>\n<tbody>\n");
        foreach (var c in sc.Categories)
        {
            sb.Append($"<tr><td>{E(c.Category)} {E(CategoryName(c.Category))}</td>");
            sb.Append($"<td class=\"num\">{E(c.Grade)}</td>");
            sb.Append($"<td class=\"num\">{c.Score:0.#}</td>");
            sb.Append($"<td class=\"barcell\"><div class=\"bar\" style=\"width:{c.Score:0}%\"></div></td>");
            sb.Append($"<td class=\"num\">{c.PassCount}</td><td class=\"num\">{c.ViolationCount}</td>");
            sb.Append($"<td class=\"num\">{c.NotApplicableCount}</td><td class=\"num\">{c.Weight:0.00}</td></tr>\n");
        }
        sb.Append("</tbody>\n</table>\n</section>\n");

        // ---- findings ----
        sb.Append("<section>\n");
        sb.Append($"<h2>지적 사항 ({report.Findings.Count}건)</h2>\n");
        if (report.Findings.Count == 0)
        {
            sb.Append("<p class=\"empty\">지적 사항 없음 — 전 규칙 통과.</p>\n");
        }
        foreach (var f in report.Findings)
        {
            var style = SeverityDisplay.Of(f.Severity);
            var cls = SeverityClass(f.Severity);
            sb.Append($"<article class=\"finding {cls}\">\n");
            sb.Append($"<div class=\"head\"><span class=\"sev {cls}\">{E(style.Glyph)} {E(style.Label)}</span>");
            if (f.Escalated) sb.Append("<span class=\"esc\">Critical 요소 → 상향</span>");
            sb.Append($"<span class=\"rule\">{E(f.RuleId)}</span><strong>{E(f.Title)}</strong>");
            sb.Append($"<span class=\"penalty\">−{f.Penalty}</span></div>\n");
            sb.Append($"<p class=\"msg\">{E(f.Message)}</p>\n");
            sb.Append("<dl>\n");
            sb.Append($"<dt>실측</dt><dd>{E(f.Measured)}</dd>\n");
            sb.Append($"<dt>기준</dt><dd>{E(f.Target)}</dd>\n");
            if (f.ElementIds.Count > 0)
                sb.Append($"<dt>요소</dt><dd class=\"mono\">{E(string.Join(", ", f.ElementIds))}</dd>\n");
            sb.Append($"<dt>권고</dt><dd>{E(f.Recommendation)}</dd>\n");
            sb.Append($"<dt>근거</dt><dd>{E(f.StandardRef)}</dd>\n");
            sb.Append("</dl>\n</article>\n");
        }
        sb.Append("</section>\n");

        // ---- strengths ----
        sb.Append("<section>\n");
        sb.Append($"<h2>강점 ({report.Strengths.Count}건)</h2>\n<ul class=\"strengths\">\n");
        foreach (var s in report.Strengths)
            sb.Append($"<li><span class=\"ok\">✓</span> <span class=\"rule\">{E(s.RuleId)}</span> " +
                      $"{E(s.Title)} <span class=\"ref\">({E(s.StandardRef)})</span></li>\n");
        sb.Append("</ul>\n</section>\n");

        sb.Append("<footer>HFE Layout Simulator · MNL-QA-HFE-002 기반 자동 평가</footer>\n");
        sb.Append("</body>\n</html>\n");
        return sb.ToString();
    }

    public static void ExportFile(EvaluationReport report, string path)
        => AtomicFile.WriteAllText(path, Export(report));

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static string SeverityClass(RuleSeverity severity) => severity switch
    {
        RuleSeverity.Critical => "critical",
        RuleSeverity.Major => "major",
        _ => "minor"
    };

    private static string GradeClass(string grade) => grade switch
    {
        "A" or "B" => "good",
        "C" or "D" => "warn",
        _ => "bad"
    };

    private static string CategoryName(string id) => id switch
    {
        "C1" => "색상·대비",
        "C2" => "타이포그래피",
        "C3" => "크기·조작",
        "C4" => "배치·시선 흐름",
        "C5" => "그룹화·근접성",
        "C6" => "인지 부하·밀도",
        "C7" => "순서·업무 흐름",
        "C8" => "오류 방지·강건성",
        "C9" => "경계·부하 관리",
        _ => ""
    };

    // DESIGN_TOKENS §3 verified palette only. Severity triple-coded: icon + label + color.
    private const string Css = """
        :root {
          --text: #212529; --text-sub: #495057; --primary: #1565C0; --primary-dark: #0D47A1;
          --pass: #2E7D32; --pass-bg: #EAF4EB; --fail: #C62828; --fail-bg: #FDECEA;
          --warn: #B45309; --warn-bg: #FFF8E1; --border: #868E96; --border-light: #DEE2E6;
          --surface: #F1F3F5;
        }
        * { box-sizing: border-box; }
        body { font-family: 'Pretendard', 'Malgun Gothic', sans-serif; color: var(--text);
               margin: 0 auto; max-width: 900px; padding: 24px; line-height: 1.5; }
        h1 { color: var(--primary-dark); font-size: 24px; margin: 0 0 12px; }
        h2 { font-size: 18px; border-bottom: 2px solid var(--border-light); padding-bottom: 4px; }
        table.meta { border-collapse: collapse; width: 100%; margin-bottom: 16px; }
        table.meta th { text-align: left; color: var(--text-sub); padding: 2px 12px 2px 0; width: 90px; }
        table.meta td { padding: 2px 24px 2px 0; }
        .verdictbar { display: flex; align-items: center; gap: 16px; flex-wrap: wrap;
                      border-radius: 8px; padding: 14px 20px; margin-bottom: 12px;
                      border: 2px solid var(--border-light); }
        .verdictbar.pass { border-color: var(--pass); background: var(--pass-bg); }
        .verdictbar.fail { border-color: var(--fail); background: var(--fail-bg); }
        .verdictbar .verdict { font-size: 26px; font-weight: 800; letter-spacing: 1px; }
        .verdictbar.pass .verdict { color: var(--pass); }
        .verdictbar.fail .verdict { color: var(--fail); }
        .verdictbar .biggrade { font-size: 40px; font-weight: 800; }
        .verdictbar .score { font-size: 20px; font-weight: 700; }
        .verdictbar .score small { color: var(--text-sub); font-weight: 400; }
        .paramgrades { display: flex; gap: 6px; flex-wrap: wrap; margin-left: auto; }
        .pgrade { border: 1px solid var(--text-sub); border-radius: 4px; padding: 2px 6px;
                  font-size: 12px; background: #fff; font-family: Consolas, monospace; }
        .pgrade b { margin-left: 4px; font-size: 14px; }
        .scorecard { background: var(--surface); border: 1px solid var(--border-light);
                     border-radius: 8px; padding: 16px 20px; }
        .grade-good { color: var(--pass); }
        .grade-warn { color: var(--warn); }
        .grade-bad { color: var(--fail); }
        .capnote { color: var(--fail); background: var(--fail-bg); padding: 6px 10px; border-radius: 4px; }
        table.categories { border-collapse: collapse; width: 100%; margin-top: 12px; background: #fff; }
        table.categories th, table.categories td { border: 1px solid var(--border-light);
                                                   padding: 4px 8px; font-size: 13px; }
        table.categories th { background: var(--surface); }
        td.num { text-align: right; font-variant-numeric: tabular-nums; }
        td.barcell { width: 160px; }
        .bar { height: 10px; background: var(--primary); border: 1px solid var(--primary-dark);
               border-radius: 2px; min-width: 2px; }
        .finding { border: 1px solid var(--border-light); border-left-width: 4px;
                   border-radius: 4px; padding: 10px 14px; margin: 10px 0; page-break-inside: avoid; }
        .finding.critical { border-left-color: var(--fail); }
        .finding.major { border-left-color: var(--warn); }
        .finding.minor { border-left-color: var(--primary); }
        .finding .head { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
        .sev { font-weight: 700; padding: 1px 8px; border-radius: 4px; font-size: 13px; }
        .sev.critical { color: var(--fail); background: var(--fail-bg); }
        .sev.major { color: var(--warn); background: var(--warn-bg); }
        .sev.minor { color: var(--primary); background: #E8F1FB; }
        .esc { font-size: 12px; color: var(--fail); }
        .rule { font-family: 'D2Coding', Consolas, monospace; color: var(--text-sub); }
        .penalty { margin-left: auto; color: var(--fail); font-weight: 700; }
        .msg { margin: 6px 0; }
        dl { display: grid; grid-template-columns: 60px 1fr; gap: 2px 10px; margin: 0; font-size: 13px; }
        dt { color: var(--text-sub); }
        dd { margin: 0; }
        .mono { font-family: 'D2Coding', Consolas, monospace; }
        .strengths { list-style: none; padding: 0; }
        .strengths li { padding: 3px 0; }
        .ok { color: var(--pass); font-weight: 700; }
        .ref { color: var(--text-sub); font-size: 12px; }
        .empty { color: var(--pass); background: var(--pass-bg); padding: 8px 12px; border-radius: 4px; }
        footer { margin-top: 24px; color: var(--text-sub); font-size: 12px;
                 border-top: 1px solid var(--border-light); padding-top: 8px; }
        @media print {
          body { padding: 0; max-width: none; }
          .scorecard { break-inside: avoid; }
          /* Ask for the verdict/severity tints; the outlines above keep every signal readable
             even on a printer that ignores this. */
          .verdictbar, .sev, .capnote, .empty, .bar, .pgrade {
            -webkit-print-color-adjust: exact; print-color-adjust: exact;
          }
          .verdictbar { border-width: 3px; }
        }
        """;
}
