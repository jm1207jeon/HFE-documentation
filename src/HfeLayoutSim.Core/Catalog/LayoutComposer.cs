using HfeLayoutSim.Core.Model;
using HfeLayoutSim.Core.Presets;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.Core.Catalog;

/// <summary>
/// Turns a user-authored <see cref="InspectionCatalog"/> into a layout that follows the
/// 8-block skeleton and the HFE rules by construction: identification top-left, precondition
/// gate ahead of items, critical confirmations as transcription inputs placed clear of the
/// WFA blind spot, checkbox runs broken, item groups chunked to Miller's bound,
/// verdict/signatures in the terminal area.
/// Rule thresholds (density band, run limits, group bound) are read from the RuleSet — not hardcoded.
/// </summary>
public sealed class LayoutComposer
{
    private readonly PresetLibrary _presets;
    private readonly RuleSet _rules;

    public LayoutComposer(PresetLibrary presets, RuleSet rules)
    {
        _presets = presets;
        _rules = rules;
    }

    private double RuleParam(string check, string name, double fallback)
        => _rules.Rules.FirstOrDefault(r => r.Check.Equals(check, StringComparison.OrdinalIgnoreCase))
               ?.GetDouble(name, fallback) ?? fallback;

    private int GroupMax => (int)RuleParam("groupItemCount", "max", 7);
    private int CheckboxMaxRun => (int)RuleParam("consecutiveSameInput", "maxRun", 4);
    private double DensityMin => RuleParam("paperDensity", "optimalMin", 0.40);
    private double DensityMax => RuleParam("paperDensity", "optimalMax", 0.60);

    public Layout Compose(InspectionCatalog catalog, Medium medium)
        => medium == Medium.Paper ? ComposePaper(catalog) : ComposeScreen(catalog);

    // ================================================================== Paper

    private Layout ComposePaper(InspectionCatalog catalog)
    {
        // First pass with the default measurement row height, then rescale toward the
        // optimal density band and recompose once (tables absorb the slack).
        var layout = ComposePaper(catalog, tableRowH: 8);
        var density = PaperDensity(layout);
        var target = (DensityMin + DensityMax) / 2;
        if (density < DensityMin && density > 0.01)
        {
            var scaled = Math.Min(16, 8 * target / density);
            try
            {
                layout = ComposePaper(catalog, scaled);
            }
            catch (CatalogLoadException)
            {
                // expanded tables no longer fit — keep the compact first pass
            }
        }
        return layout;
    }

    private static double PaperDensity(Layout layout)
    {
        var m = layout.Canvas.Margins!;
        var area = (layout.Canvas.Width - m.Left - m.Right) * (layout.Canvas.Height - m.Top - m.Bottom);
        var used = layout.Elements
            .Where(e => e.Type is not (ElementType.Section or ElementType.Divider))
            .Sum(e => e.W * e.H);
        return used / area;
    }

    private Layout ComposePaper(InspectionCatalog catalog, double tableRowH)
    {
        var layout = new Layout
        {
            Meta = new LayoutMeta { Name = catalog.ProcessName, Medium = Medium.Paper },
            Canvas = new CanvasSpec
            {
                Width = 210, Height = 297,
                Margins = new Margins { Top = 15, Bottom = 15, Left = 20, Right = 12 },
                PageCount = 1
            }
        };
        var els = layout.Elements;
        const double left = 20, width = 178;
        const double tableW = 118;      // leaves the 142.. right column for step alarms/images
        const double rightColX = 142, rightColW = 56;
        var seq = 0;
        var y = 15.0;

        // B1 header
        var title = catalog.DocumentNo is null ? catalog.ProcessName : $"{catalog.ProcessName} ({catalog.DocumentNo})";
        els.Add(_presets.Instantiate("header-title", Medium.Paper, left, y, "hdr", e => e.Text = title));
        y += 15;

        // B2 identification — key fields (first two) left column inside the POA band,
        // secondary fields on the right of the same rows (role-free metadata).
        var keyFields = catalog.IdentificationFields.Take(2).ToList();
        var extraFields = catalog.IdentificationFields.Skip(2).ToList();
        var idRows = Math.Max(keyFields.Count, extraFields.Count);
        for (var r = 0; r < idRows; r++)
        {
            if (r < keyFields.Count)
            {
                var field = keyFields[r];
                var isLot = field.Contains("LOT", StringComparison.OrdinalIgnoreCase);
                els.Add(_presets.Instantiate("label-field", Medium.Paper, left, y, $"lbl-id{r}", e =>
                {
                    e.Text = $"{field} *";
                    e.GroupId = "id";
                    e.Semantics.Role = SemanticRole.Identification;
                }));
                els.Add(_presets.Instantiate(isLot ? "input-lot" : "input-text", Medium.Paper, 46, y, $"in-id{r}", e =>
                {
                    e.GroupId = "id";
                    e.Semantics.Role = SemanticRole.Identification;
                    e.Semantics.IsRequired = true;
                    e.Semantics.Sequence = ++seq;
                }));
            }
            if (r < extraFields.Count)
            {
                els.Add(_presets.Instantiate("label-field", Medium.Paper, 110, y, $"lbl-idx{r}", e =>
                {
                    e.Text = extraFields[r];
                    e.GroupId = "id";
                }));
                els.Add(_presets.Instantiate("input-text", Medium.Paper, 136, y, $"in-idx{r}", e =>
                {
                    e.W = 50;
                    e.GroupId = "id";
                    e.Semantics.Sequence = ++seq;
                }));
            }
            y += 10;
        }
        y += 4;

        // document-level alarms (top area — never the WFA blind spot)
        var ai = 0;
        foreach (var alarm in catalog.Alarms)
        {
            els.Add(_presets.Instantiate("warning-box", Medium.Paper, left, y, $"alarm{ai}", e =>
            {
                e.Text = $"주의: {alarm.Text}";
                e.GroupId = "alerts";
            }));
            y += 10;
            ai++;
        }
        if (ai > 0) y += 2;

        // B3 precondition gate
        els.Add(_presets.Instantiate("info-precondition", Medium.Paper, left, y, "precond-info",
            e => e.GroupId = "precond"));
        y += 10;
        var pi = 0;
        var checkRun = 0;
        foreach (var p in catalog.Preconditions)
        {
            if (p.InputKind == InputKind.Transcribe)
            {
                els.Add(_presets.Instantiate("label-field", Medium.Paper, left, y, $"lbl-pre{pi}", e =>
                {
                    e.Text = $"{p.Label ?? p.Text} *";
                    e.W = 40;
                    e.GroupId = "precond";
                    e.Semantics.Role = SemanticRole.Precondition;
                }));
                els.Add(_presets.Instantiate("input-text", Medium.Paper, 61, y, $"in-pre{pi}", e =>
                {
                    e.W = 45;
                    e.GroupId = "precond";
                    e.Semantics.Role = SemanticRole.Precondition;
                    e.Semantics.IsRequired = true;
                    e.Semantics.InputKind = InputKind.Transcribe;
                    e.Semantics.Sequence = ++seq;
                }));
                y += 10;
                checkRun = 0;
            }
            else
            {
                els.Add(_presets.Instantiate("checkbox-confirm", Medium.Paper, left, y, $"cb-pre{pi}", e =>
                {
                    e.Text = p.Text;
                    e.GroupId = "precond";
                    e.Semantics.Role = SemanticRole.Precondition;
                    e.Semantics.Sequence = ++seq;
                }));
                y += 7;
                if (++checkRun >= CheckboxMaxRun)
                {
                    els.Add(_presets.Instantiate("divider-section", Medium.Paper, left, y, $"div-pre{pi}"));
                    y += 3;
                    checkRun = 0;
                }
            }
            pi++;
        }
        y += 5;

        // B4 sampling
        if (!string.IsNullOrWhiteSpace(catalog.SamplingNote))
        {
            els.Add(new LayoutElement
            {
                Id = "sampling", Type = ElementType.InfoBox,
                X = left, Y = y, W = width, H = 6,
                Text = $"샘플링: {catalog.SamplingNote}",
                GroupId = "sampling",
                Semantics = new Semantics { Role = SemanticRole.SamplingInfo }
            });
            y += 10;
        }

        // B5 inspection steps
        var si = 0;
        foreach (var step in catalog.Steps)
        {
            els.Add(new LayoutElement
            {
                Id = $"step{si}-cap", Type = ElementType.Label,
                X = left, Y = y, W = width, H = 6,
                Text = $"{step.Order}. {step.Name}",
                Style = new ElementStyle { Bold = true },
                GroupId = $"step{si}-0",
                Semantics = new Semantics { Role = SemanticRole.InspectionItem }
            });
            y += 8;
            var stepTop = y;

            var tableItems = step.Items.Where(it => it.Kind is "measure" or "text" && !it.Critical).ToList();
            var checkItems = step.Items.Where(it => it.Kind == "check" && !it.Critical).ToList();
            var criticalItems = step.Items.Where(it => it.Critical).ToList();

            var ci = 0;
            foreach (var chunk in Chunk(tableItems, GroupMax))
            {
                els.Add(_presets.Instantiate("table-inspection", Medium.Paper, left, y, $"step{si}-tbl{ci}", e =>
                {
                    e.W = tableW;
                    e.Rows = chunk.Count + 1;
                    e.H = (chunk.Count + 1) * tableRowH;
                    e.GroupId = ci == 0 ? $"step{si}-0" : $"step{si}-{ci}";
                }));
                y += (chunk.Count + 1) * tableRowH + 6;
                ci++;
            }

            checkRun = 0;
            var cci = 0;
            foreach (var item in checkItems)
            {
                var caption = item.Criterion is null ? $"{item.Name} 확인" : $"{item.Name} 확인 ({item.Criterion})";
                els.Add(_presets.Instantiate("checkbox-confirm", Medium.Paper, left, y, $"step{si}-cb{cci}", e =>
                {
                    e.Text = caption;
                    e.GroupId = $"step{si}-c";
                    e.Semantics.Role = SemanticRole.InspectionItem;
                    e.Semantics.Sequence = ++seq;
                }));
                y += 7;
                if (++checkRun >= CheckboxMaxRun)
                {
                    els.Add(_presets.Instantiate("divider-section", Medium.Paper, left, y, $"step{si}-div{cci}"));
                    y += 3;
                    checkRun = 0;
                }
                cci++;
            }

            // critical items: transcription inputs on the right half — out of the WFA blind spot (C4-02)
            var ki = 0;
            foreach (var item in criticalItems)
            {
                els.Add(_presets.Instantiate("label-field", Medium.Paper, 110, y, $"step{si}-klbl{ki}", e =>
                {
                    e.Text = item.Criterion is null ? $"{item.Name} 기입 *" : $"{item.Name} 기입 ({item.Criterion}) *";
                    e.W = 55;
                    e.GroupId = $"step{si}-c";
                    e.Semantics.Role = SemanticRole.InspectionItem;
                }));
                els.Add(_presets.Instantiate(item.Kind == "measure" ? "input-num" : "input-text",
                    Medium.Paper, 166, y, $"step{si}-kin{ki}", e =>
                {
                    e.W = 16;
                    e.Unit = item.Unit;
                    e.GroupId = $"step{si}-c";
                    e.Semantics.Role = SemanticRole.InspectionItem;
                    e.Semantics.IsCritical = true;
                    e.Semantics.IsRequired = true;
                    e.Semantics.InputKind = InputKind.Transcribe;
                    e.Semantics.SpecLimitShown = item.Criterion is not null;
                    e.Semantics.Sequence = ++seq;
                }));
                y += 10;
                ki++;
            }

            // step alarms and reference images live in the right column beside the tables
            var ry = stepTop;
            var stepAlarms = step.Items.Where(it => it.Alarm is not null).Select(it => it.Alarm!).ToList();
            if (stepAlarms.Count > 0)
            {
                els.Add(_presets.Instantiate("warning-box", Medium.Paper, rightColX, ry, $"step{si}-alarm", e =>
                {
                    e.W = rightColW; e.H = 10;
                    e.Text = $"주의: {string.Join(" / ", stepAlarms.Select(a => a.Text))}";
                    e.GroupId = $"step{si}-w";
                }));
                ry += 12;
            }
            var ii = 0;
            foreach (var item in step.Items.Where(it => !string.IsNullOrWhiteSpace(it.Image)))
            {
                els.Add(_presets.Instantiate("image-attach", Medium.Paper, rightColX, ry, $"step{si}-img{ii}", e =>
                {
                    e.W = rightColW; e.H = 28;
                    e.Text = $"{item.Name} 도해: {item.Image}";
                    e.GroupId = $"step{si}-img";
                }));
                ry += 30;
                ii++;
            }
            y = Math.Max(y, ry) + 6;
            si++;
        }

        // fixed bottom blocks (terminal area)
        var sigBlockH = catalog.Signers.Count * 13;
        var verdictY = 273 - sigBlockH - 14;

        // B6 nonconformance
        els.Add(new LayoutElement
        {
            Id = "nc-info", Type = ElementType.InfoBox,
            X = left, Y = y, W = width, H = 6,
            Text = "부적합 사항 기록 (부적합 없을 시 '해당 없음' 기재)",
            GroupId = "nc", Semantics = new Semantics { Role = SemanticRole.NonconformanceRecord }
        });
        y += 7;
        els.Add(_presets.Instantiate("input-text", Medium.Paper, left, y, "nc-in", e =>
        {
            e.W = width; e.H = 10;
            e.GroupId = "nc";
            e.Semantics.Role = SemanticRole.NonconformanceRecord;
            e.Semantics.Sequence = ++seq;
        }));
        y += 14;

        // completeness gate right before the verdict
        els.Add(_presets.Instantiate("info-gate", Medium.Paper, left, y, "gate", e =>
        {
            e.GroupId = "gate";
            e.Semantics.Sequence = ++seq;
        }));

        if (y + 5 > verdictY + 6)
            throw new CatalogLoadException(
                $"카탈로그 항목이 A4 1페이지 용량을 초과합니다 (필요 {y + 5:0}mm > 가용 {verdictY + 6:0}mm). " +
                "스텝을 나누거나 항목 수를 줄이십시오 (다중 페이지는 추후 지원).");

        // B7 verdict (TA)
        els.Add(new LayoutElement
        {
            Id = "verdict-lbl", Type = ElementType.Label,
            X = 98, Y = verdictY + 2, W = 25, H = 6, Text = "종합판정",
            GroupId = "verdict", Semantics = new Semantics { Role = SemanticRole.FinalVerdict }
        });
        els.Add(_presets.Instantiate("judgment-badge", Medium.Paper, 124, verdictY, "verdict-badge", e =>
        {
            e.GroupId = "verdict";
            e.Semantics.Sequence = ++seq;
        }));

        // B8 signatures (distinct signer roles)
        var sy = verdictY + 14;
        var sgi = 0;
        foreach (var signer in catalog.Signers)
        {
            els.Add(new LayoutElement
            {
                Id = $"sig-lbl{sgi}", Type = ElementType.Label,
                X = 98, Y = sy + 2, W = 25, H = 5, Text = $"{signer} (서명)",
                GroupId = "sign", Semantics = new Semantics { Role = SemanticRole.Signature }
            });
            els.Add(_presets.Instantiate(sgi == 0 ? "signature-inspector" : "signature-approver",
                Medium.Paper, 124, sy, $"sig{sgi}", e =>
            {
                e.SignerRole = signer;
                e.GroupId = "sign";
                e.Semantics.Sequence = ++seq;
            }));
            sy += 13;
            sgi++;
        }

        els.Add(_presets.Instantiate("page-marker", Medium.Paper, 178, 275, "pg"));
        return layout;
    }

    // ================================================================= Screen

    private Layout ComposeScreen(InspectionCatalog catalog)
    {
        var layout = new Layout
        {
            Meta = new LayoutMeta { Name = catalog.ProcessName, Medium = Medium.Screen },
            Canvas = new CanvasSpec
            {
                Width = 1920, Height = 1080,
                ContentPaddingPx = 24,
                FixedRegions = new FixedRegions(),
                IsScrollable = true
            }
        };
        var els = layout.Elements;
        const double left = 264;
        const double tableRowH = 36;
        var seq = 0;

        els.Add(_presets.Instantiate("header-title", Medium.Screen, 240, 0, "hdr", e =>
        {
            e.W = 1680; e.H = 56;
            e.Text = catalog.ProcessName;
        }));

        // context bar identification (sticky)
        var fx = left;
        var fi = 0;
        foreach (var field in catalog.IdentificationFields.Take(3))
        {
            var isLot = field.Contains("LOT", StringComparison.OrdinalIgnoreCase);
            els.Add(new LayoutElement
            {
                Id = $"ctx-id{fi}", Type = ElementType.Label,
                X = fx, Y = 64, W = 340, H = 32,
                Text = $"{field}: (작업 지시 연동)",
                Style = new ElementStyle { FontSize = 14, FontFamily = isLot ? "D2Coding" : null },
                Semantics = new Semantics { Role = SemanticRole.Identification, IsCritical = isLot }
            });
            fx += 364;
            fi++;
        }

        // GNB
        string[] nav = { "작업 지시", "검사 수행", "이력 조회" };
        for (var ni = 0; ni < nav.Length; ni++)
            els.Add(new LayoutElement
            {
                Id = $"nav{ni}", Type = ElementType.Label,
                X = 0, Y = 72 + ni * 48, W = 240, H = 40, Text = nav[ni],
                Style = new ElementStyle { FontSize = 14, Bold = ni == 1 },
                Semantics = new Semantics { Role = SemanticRole.Navigation }
            });

        // stepper reflects the user's step order
        var stepNames = string.Join(" · ", catalog.Steps.Select(s => $"{s.Order} {s.Name}"));
        els.Add(_presets.Instantiate("stepper-progress", Medium.Screen, left, 128, "stepper",
            e => e.Text = $"{stepNames} · {catalog.Steps.Count + 1} 판정"));

        var y = 176.0;

        // right column: alarms + images (SFA side, clear of the content flow)
        var ry = 176.0;
        var ai = 0;
        foreach (var alarm in catalog.Alarms.Concat(
                     catalog.Steps.SelectMany(s => s.Items).Where(i => i.Alarm != null).Select(i => i.Alarm!)))
        {
            els.Add(_presets.Instantiate("warning-box", Medium.Screen, 1288, ry, $"alarm{ai}", e =>
            {
                e.Text = $"주의: {alarm.Text}";
                e.GroupId = "alerts";
            }));
            ry += 56;
            ai++;
        }
        var imgi = 0;
        foreach (var item in catalog.Steps.SelectMany(s => s.Items).Where(i => !string.IsNullOrWhiteSpace(i.Image)))
        {
            els.Add(_presets.Instantiate("image-attach", Medium.Screen, 1288, ry, $"img{imgi}", e =>
            {
                e.Text = $"{item.Name} 도해: {item.Image}";
                e.GroupId = "refs";
            }));
            ry += 204;
            imgi++;
        }

        // precondition
        els.Add(_presets.Instantiate("info-precondition", Medium.Screen, left, y, "precond-info",
            e => e.GroupId = "precond"));
        y += 48;
        var pi = 0;
        foreach (var p in catalog.Preconditions)
        {
            if (p.InputKind == InputKind.Transcribe)
            {
                els.Add(_presets.Instantiate("label-field", Medium.Screen, left, y + 8, $"lbl-pre{pi}", e =>
                {
                    e.Text = $"{p.Label ?? p.Text} *";
                    e.GroupId = "precond";
                    e.Semantics.Role = SemanticRole.Precondition;
                }));
                els.Add(_presets.Instantiate("input-text", Medium.Screen, left + 146, y, $"in-pre{pi}", e =>
                {
                    e.GroupId = "precond";
                    e.Semantics.Role = SemanticRole.Precondition;
                    e.Semantics.IsRequired = true;
                    e.Semantics.InputKind = InputKind.Transcribe;
                    e.Semantics.Sequence = ++seq;
                }));
                y += 48;
            }
            else
            {
                els.Add(_presets.Instantiate("checkbox-confirm", Medium.Screen, left, y, $"cb-pre{pi}", e =>
                {
                    e.Text = p.Text;
                    e.GroupId = "precond";
                    e.Semantics.Role = SemanticRole.Precondition;
                    e.Semantics.Sequence = ++seq;
                }));
                y += 32;
            }
            pi++;
        }
        y += 16;

        // steps: one table per chunk; critical items become transcription inputs on the right (TA side)
        var si = 0;
        foreach (var step in catalog.Steps)
        {
            els.Add(new LayoutElement
            {
                Id = $"step{si}-cap", Type = ElementType.Label,
                X = left, Y = y, W = 400, H = 24,
                Text = $"{step.Order}. {step.Name}",
                Style = new ElementStyle { FontSize = 14, Bold = true },
                GroupId = $"step{si}-0",
                Semantics = new Semantics { Role = SemanticRole.InspectionItem }
            });
            y += 28;

            var tableItems = step.Items.Where(it => !it.Critical).ToList();
            var ci = 0;
            foreach (var chunk in Chunk(tableItems, GroupMax))
            {
                els.Add(_presets.Instantiate("table-inspection", Medium.Screen, left, y, $"step{si}-tbl{ci}", e =>
                {
                    e.Rows = chunk.Count + 1;
                    e.H = (chunk.Count + 1) * tableRowH;
                    e.GroupId = ci == 0 ? $"step{si}-0" : $"step{si}-{ci}";
                }));
                y += (chunk.Count + 1) * tableRowH + 16;
                ci++;
            }

            var ki = 0;
            foreach (var item in step.Items.Where(it => it.Critical))
            {
                // joins the step's main group so the logical item count stays in Miller's band
                els.Add(_presets.Instantiate("label-field", Medium.Screen, 844, y + 8, $"step{si}-klbl{ki}", e =>
                {
                    e.Text = item.Criterion is null ? $"{item.Name} 기입 *" : $"{item.Name} ({item.Criterion}) *";
                    e.W = 220;
                    e.GroupId = $"step{si}-0";
                    e.Semantics.Role = SemanticRole.InspectionItem;
                }));
                els.Add(_presets.Instantiate(item.Kind == "measure" ? "input-num" : "input-text",
                    Medium.Screen, 1070, y, $"step{si}-kin{ki}", e =>
                {
                    e.W = 160;
                    e.Unit = item.Unit;
                    e.GroupId = $"step{si}-0";
                    e.Semantics.Role = SemanticRole.InspectionItem;
                    e.Semantics.IsCritical = true;
                    e.Semantics.IsRequired = true;
                    e.Semantics.InputKind = InputKind.Transcribe;
                    e.Semantics.SpecLimitShown = item.Criterion is not null;
                    e.Semantics.Sequence = ++seq;
                }));
                y += 44;
                ki++;
            }
            y += 8;
            si++;
        }

        // nonconformance (placeholder text on the input itself keeps the flow compact)
        els.Add(_presets.Instantiate("input-text", Medium.Screen, left, y, "nc-in", e =>
        {
            e.W = 1000; e.H = 56;
            e.Text = "부적합 사항 (없으면 '해당 없음' 입력)";
            e.GroupId = "nc";
            e.Semantics.Role = SemanticRole.NonconformanceRecord;
            e.Semantics.Sequence = ++seq;
        }));
        y += 64;

        els.Add(_presets.Instantiate("info-gate", Medium.Screen, left, y, "gate", e =>
        {
            e.GroupId = "gate";
            e.Semantics.Sequence = ++seq;
        }));
        y += 24;

        const double verdictTop = 896;
        if (y > verdictTop - 16)
            throw new CatalogLoadException(
                $"카탈로그 항목이 한 화면 용량을 초과합니다 (필요 {y:0}px > 가용 {verdictTop - 16:0}px). " +
                "스텝을 나누어 다단계 화면으로 구성하십시오 (스텝 분할은 추후 지원).");

        // fixed verdict / signature / action bar (terminal area)
        els.Add(_presets.Instantiate("radio-judgment", Medium.Screen, 1300, verdictTop, "verdict-radio", e =>
        {
            e.GroupId = "verdict";
            e.Semantics.Sequence = ++seq;
        }));
        els.Add(_presets.Instantiate("judgment-badge", Medium.Screen, 1620, verdictTop, "verdict-badge", e =>
        {
            e.Text = "적합 ■";
            e.GroupId = "verdict";
        }));
        els.Add(_presets.Instantiate("signature-inspector", Medium.Screen, 1696, 940, "sig0", e =>
        {
            e.SignerRole = catalog.Signers[0];
            e.GroupId = "sign";
            e.Semantics.Sequence = ++seq;
        }));

        els.Add(_presets.Instantiate("button-danger", Medium.Screen, 264, 1027, "btn-cancel"));
        els.Add(_presets.Instantiate("button-secondary", Medium.Screen, 1620, 1027, "btn-save"));
        els.Add(_presets.Instantiate("button-primary", Medium.Screen, 1768, 1027, "btn-complete"));
        return layout;
    }

    /// <summary>Balanced chunks of at most <paramref name="max"/> (sizes differ by ≤1).</summary>
    public static List<List<T>> Chunk<T>(List<T> items, int max)
    {
        var result = new List<List<T>>();
        if (items.Count == 0) return result;
        var chunkCount = (int)Math.Ceiling(items.Count / (double)max);
        var baseSize = items.Count / chunkCount;
        var remainder = items.Count % chunkCount;
        var index = 0;
        for (var c = 0; c < chunkCount; c++)
        {
            var size = baseSize + (c < remainder ? 1 : 0);
            result.Add(items.GetRange(index, size));
            index += size;
        }
        return result;
    }
}
