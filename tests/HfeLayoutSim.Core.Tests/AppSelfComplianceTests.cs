using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HfeLayoutSim.Core.Engine;

namespace HfeLayoutSim.Core.Tests;

/// <summary>
/// SPEC §8: "앱 자체도 매뉴얼 표준을 따를 것 … 앱 스스로가 규칙 위반이면 안 됨."
/// A tool that flags 11px text and 3.32:1 foregrounds in the layouts it judges must not ship them
/// in its own chrome. These tests read the app's XAML as data (no WPF needed) and apply the same
/// thresholds the rule knowledge base applies to a Screen layout.
/// </summary>
public class AppSelfComplianceTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HfeLayoutSim.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("저장소 루트를 찾을 수 없습니다.");
        }
    }

    private static string AppDir => Path.Combine(RepoRoot, "src", "HfeLayoutSim.App");

    public static IEnumerable<object[]> AppXamlFiles()
        => Directory.GetFiles(AppDir, "*.xaml").Select(f => new object[] { Path.GetFileName(f) });

    private static XDocument Load(string fileName) => XDocument.Load(Path.Combine(AppDir, fileName));

    private static string ReadText(string fileName) => File.ReadAllText(Path.Combine(AppDir, fileName));

    // ---------------------------------------------------------------- C2-01: minimum font size

    [Theory]
    [MemberData(nameof(AppXamlFiles))]
    public void NoChromeTextIsBelowTheScreenMinimumItEnforces(string fileName)
    {
        var minPx = TestData.Rules.Rules.Single(r => r.Id == "C2-01").GetDouble("screenMinPx");

        var offenders = new List<string>();
        foreach (var match in Regex.Matches(ReadText(fileName), @"FontSize\s*=\s*""(?<v>[0-9.]+)""").Cast<Match>())
        {
            var size = double.Parse(match.Groups["v"].Value);
            if (size < minPx) offenders.Add($"{size}px");
        }

        Assert.True(offenders.Count == 0,
            $"{fileName}: C2-01 위반 — 화면 최소 {minPx}px 미만 글자 {offenders.Count}건 ({string.Join(", ", offenders)})");
    }

    // ---------------------------------------------------------------- C1: verified palette only

    [Fact]
    public void EveryChromeColourComesFromTheVerifiedPalette()
    {
        // Colours live in App.xaml as named brushes; any hex used there must be a token the rule
        // knowledge base knows, so the chrome and the rules cannot drift apart.
        var palette = TestData.Rules.Palette.Values
            .SelectMany(p => new[] { p.Hex, p.Bg })
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h!.ToUpperInvariant())
            .ToHashSet();

        // neutrals the manual permits for non-informational chrome
        palette.Add("#FFFFFF");
        palette.Add("#E8F1FB"); // primary tint, listed in C1-07's allowed semantic colours

        var unknown = new List<string>();
        foreach (var brush in Load("App.xaml").Descendants()
                     .Where(e => e.Name.LocalName == "SolidColorBrush"))
        {
            var hex = ((string?)brush.Attribute("Color") ?? "").ToUpperInvariant();
            if (hex.Length > 0 && !palette.Contains(hex))
                unknown.Add($"{(string?)brush.Attribute(X + "Key")}={hex}");
        }

        Assert.True(unknown.Count == 0,
            "App.xaml: 검증 팔레트에 없는 색 " + string.Join(", ", unknown) +
            " — rules/hfe_rules.json palette 에 등재하거나 검증된 토큰으로 교체하십시오.");
    }

    [Theory]
    [MemberData(nameof(AppXamlFiles))]
    public void WindowsUseColourTokensRatherThanInlineHexLiterals(string fileName)
    {
        if (fileName == "App.xaml") return; // the token definitions themselves

        var inline = Regex.Matches(ReadText(fileName), @"=""#[0-9A-Fa-f]{6,8}""")
            .Select(m => m.Value).Distinct().ToList();

        Assert.True(inline.Count == 0,
            $"{fileName}: 색을 직접 적었습니다 ({string.Join(", ", inline)}) — App.xaml 토큰을 사용하십시오.");
    }

    /// <summary>Text foregrounds must clear 4.5:1 on the surfaces the app actually paints.</summary>
    [Fact]
    public void TextTokensClearTheContrastThresholdOnAppSurfaces()
    {
        var minRatio = TestData.Rules.Rules.Single(r => r.Id == "C1-01").GetDouble("minRatio");
        var brushes = Load("App.xaml").Descendants()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => (string)e.Attribute("Color")!);

        // (foreground token, background token) pairs the window actually puts together
        var pairs = new[]
        {
            ("TextBrush", "WhiteBrush"), ("TextBrush", "SurfaceBrush"),
            ("TextSubBrush", "WhiteBrush"), ("TextSubBrush", "SurfaceBrush"),
            ("PrimaryBrush", "WhiteBrush"), ("PrimaryDarkBrush", "WhiteBrush"),
            ("PassBrush", "WhiteBrush"), ("PassBrush", "PassBgBrush"),
            ("FailBrush", "WhiteBrush"), ("FailBrush", "FailBgBrush"),
            ("WarnBrush", "WhiteBrush"), ("WarnBrush", "WarnBgBrush"),
            ("WhiteBrush", "PrimaryBrush"), ("WhiteBrush", "PrimaryDarkBrush"),
        };

        var failures = new List<string>();
        foreach (var (fg, bg) in pairs)
        {
            var ratio = ContrastCalculator.Ratio(brushes[fg], brushes[bg]);
            Assert.NotNull(ratio);
            if (ratio < minRatio)
                failures.Add($"{fg}({brushes[fg]}) on {bg}({brushes[bg]}) = {ratio:0.00}:1");
        }

        Assert.True(failures.Count == 0,
            $"C1-01 위반 — 기준 {minRatio}:1 미달: " + string.Join("; ", failures));
    }

    /// <summary>
    /// A button is not only its resting colours. Hover and press darken the fill with a translucent
    /// veil, and the label has to stay readable in those states too — the previous template replaced
    /// the fill instead, which put the primary button's white label on a near-white background.
    /// </summary>
    [Fact]
    public void ButtonLabelsStayReadable_WhileHoveredAndPressed()
    {
        var minRatio = TestData.Rules.Rules.Single(r => r.Id == "C1-01").GetDouble("minRatio");
        var app = Load("App.xaml");
        var brushes = app.Descendants()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => (string)e.Attribute("Color")!);

        // the veil opacities the template actually applies, read from the XAML rather than assumed
        var veilOpacities = app.Descendants()
            .Where(e => e.Name.LocalName == "Setter"
                        && (string?)e.Attribute("TargetName") == "Veil"
                        && (string?)e.Attribute("Property") == "Opacity")
            .Select(e => double.Parse((string)e.Attribute("Value")!, CultureInfo.InvariantCulture))
            .Where(o => o > 0)
            .ToList();
        Assert.NotEmpty(veilOpacities);

        // (label token, fill token) as each button style paints itself WHILE hovered or pressed
        var buttons = new[]
        {
            ("TextBrush", "WhiteBrush"),          // ToolbarButton
            ("WhiteBrush", "PrimaryBrush"),       // PrimaryButton
            ("WhiteBrush", "PrimaryDarkBrush"),   // PrimaryButton, hover fill
            ("WhiteBrush", "FailBrush"),          // DangerButton, hover fill
        };

        var failures = new List<string>();
        foreach (var (fg, bg) in buttons)
            foreach (var opacity in veilOpacities)
            {
                var veiled = Composite(brushes["TextBrush"], brushes[bg], opacity);
                var ratio = ContrastCalculator.Ratio(brushes[fg], veiled);
                Assert.NotNull(ratio);
                if (ratio < minRatio)
                    failures.Add($"{fg} on {bg}+veil {opacity:0.##} ({veiled}) = {ratio:0.00}:1");
            }

        Assert.True(failures.Count == 0,
            $"C1-01 위반 — 마우스 오버/누름 상태에서 라벨이 읽히지 않습니다: " + string.Join("; ", failures));
    }

    /// <summary>Alpha-composites <paramref name="over"/> onto <paramref name="under"/>, as WPF paints it.</summary>
    private static string Composite(string over, string under, double alpha)
    {
        static (int R, int G, int B) Parse(string hex) => (
            Convert.ToInt32(hex.Substring(1, 2), 16),
            Convert.ToInt32(hex.Substring(3, 2), 16),
            Convert.ToInt32(hex.Substring(5, 2), 16));

        var (r1, g1, b1) = Parse(over);
        var (r2, g2, b2) = Parse(under);
        int Mix(int a, int b) => (int)Math.Round(a * alpha + b * (1 - alpha));
        return $"#{Mix(r1, r2):X2}{Mix(g1, g2):X2}{Mix(b1, b2):X2}";
    }

    // ---------------------------------------------------------------- C8-06: button labels

    [Theory]
    [MemberData(nameof(AppXamlFiles))]
    public void NoButtonIsLabelledWithABareConfirmation(string fileName)
    {
        var forbidden = TestData.Rules.Rules.Single(r => r.Id == "C8-06").GetStringList("forbiddenAlone");

        var offenders = Load(fileName).Descendants()
            .Where(e => e.Name.LocalName == "Button")
            .Select(e => ((string?)e.Attribute("Content") ?? "").Trim())
            .Where(label => forbidden.Contains(label, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{fileName}: C8-06 위반 — 행위를 기술하지 않는 버튼 라벨 {string.Join(", ", offenders)}");
    }

    // ---------------------------------------------------------------- C3: target sizes

    [Fact]
    public void ButtonStylesMeetTheTargetSizeTheyEnforce()
    {
        var minTarget = TestData.Rules.Rules.Single(r => r.Id == "C3-02").GetDouble("minPx");
        var primaryMin = TestData.Rules.Rules.Single(r => r.Id == "C3-03").GetDouble("minHeightPx");

        var styles = Load("App.xaml").Descendants()
            .Where(e => e.Name.LocalName == "Style" && e.Attribute(X + "Key") is not null)
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => e);

        double MinHeightOf(string key)
        {
            var setter = styles[key].Elements()
                .FirstOrDefault(e => e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "MinHeight");
            return setter is null ? 0 : double.Parse((string)setter.Attribute("Value")!);
        }

        Assert.True(MinHeightOf("BaseButton") >= primaryMin,
            $"C3-03 위반 — 기본 버튼 높이 {MinHeightOf("BaseButton")}px, 기준 {primaryMin}px");
        Assert.True(MinHeightOf("PrimaryButton") >= 44,
            $"주 행위 버튼은 44px 권장 — 현재 {MinHeightOf("PrimaryButton")}px");
        Assert.True(MinHeightOf("AppCheckBox") >= minTarget,
            $"C3-02 위반 — 체크박스 타겟 {MinHeightOf("AppCheckBox")}px, 기준 {minTarget}px");

        // the redrawn bullet itself must be a real 24px target, not a stretched 13px one
        var bullet = styles["AppCheckBox"].Descendants()
            .First(e => e.Name.LocalName == "Border" && (string?)e.Attribute(X + "Name") == "Box");
        Assert.True(double.Parse((string)bullet.Attribute("Width")!) >= minTarget);
        Assert.True(double.Parse((string)bullet.Attribute("Height")!) >= minTarget);
    }

    // ---------------------------------------------------------------- HFE affordances

    [Fact]
    public void MainWindowDeclaresAMinimumSizeThatFitsAQcWorkstation()
    {
        var window = Load("MainWindow.xaml").Root!;
        var minWidth = double.Parse((string)window.Attribute("MinWidth")!);
        var minHeight = double.Parse((string)window.Attribute("MinHeight")!);

        Assert.True(minWidth <= 1366, $"MinWidth {minWidth} — 1366×768 검사 PC에서 잘립니다.");
        Assert.True(minHeight <= 768, $"MinHeight {minHeight} — 1366×768 검사 PC에서 잘립니다.");
    }

    [Fact]
    public void DestructiveActionUsesTheDangerStyleAndNotTheToolbar()
    {
        var document = Load("MainWindow.xaml");
        var deleteButtons = document.Descendants()
            .Where(e => e.Name.LocalName == "Button" &&
                        ((string?)e.Attribute("Content") ?? "").Contains("삭제"))
            .ToList();

        Assert.NotEmpty(deleteButtons);
        foreach (var button in deleteButtons)
            Assert.Contains("DangerButton", (string?)button.Attribute("Style") ?? "");
    }

    [Fact]
    public void EveryToolbarButtonExplainsItselfWithATooltip()
    {
        var toolbar = Load("MainWindow.xaml").Descendants()
            .Where(e => e.Name.LocalName == "Button" && (string?)e.Attribute("Content") is not null)
            .ToList();

        var missing = toolbar
            .Where(e => e.Attribute("ToolTip") is null && e.Element(XName.Get("Button.ToolTip", e.Name.NamespaceName)) is null)
            .Select(e => (string?)e.Attribute("Content"))
            .ToList();

        Assert.True(missing.Count == 0, "툴팁 없는 버튼: " + string.Join(", ", missing));
    }

    [Fact]
    public void SeverityIsTripleCodedInTheFindingsList()
    {
        // C1-04 applies to the app itself: severity must not be colour alone.
        var text = ReadText("MainWindow.xaml");
        Assert.Contains("SeverityLabel", text);   // glyph + Korean word, from SeverityDisplay
        Assert.Contains("SeverityHex", text);     // colour
        Assert.DoesNotContain("{Binding Severity}", text); // never the raw enum
    }

    [Fact]
    public void KeyboardPathsExistForEveryFrequentAction()
    {
        var bindings = Load("MainWindow.xaml").Descendants()
            .Where(e => e.Name.LocalName == "KeyBinding")
            .Select(e => $"{(string?)e.Attribute("Modifiers")}+{(string?)e.Attribute("Key")}")
            .ToList();

        Assert.Contains(bindings, b => b.Contains("Ctrl") && b.EndsWith("+S"));
        Assert.Contains(bindings, b => b.Contains("Ctrl") && b.EndsWith("+Z"));
        Assert.Contains(bindings, b => b.EndsWith("+Delete"));
        Assert.Contains(bindings, b => b.EndsWith("+F5"));
    }
}
