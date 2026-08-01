using System.IO;
using HfeLayoutSim.Core.Advisor;
using HfeLayoutSim.Core.Engine;
using HfeLayoutSim.Core.Presets;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.App.Services;

/// <summary>Loads the knowledge base once at startup and exposes shared engine services.</summary>
public static class AppServices
{
    public static RuleSet Rules { get; private set; } = null!;
    public static PresetLibrary Presets { get; private set; } = null!;
    public static HfeEngine Engine { get; private set; } = null!;
    public static PlacementAdvisor Advisor { get; private set; } = null!;

    public static void Initialize()
    {
        Rules = RuleLoader.LoadFile(FindFile(Path.Combine("rules", "hfe_rules.json")));
        Presets = PresetLibrary.LoadFile(FindFile(Path.Combine("rules", "element_presets.json")));
        Engine = new HfeEngine();
        Advisor = new PlacementAdvisor(Rules, Presets);
    }

    /// <summary>Looks next to the exe first, then walks up (development tree layout).</summary>
    public static string FindFile(string relative)
    {
        var direct = Path.Combine(AppContext.BaseDirectory, relative);
        if (File.Exists(direct)) return direct;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"필수 파일을 찾을 수 없습니다: {relative} (exe 옆의 rules 폴더를 확인하십시오)");
    }

    public static string? TryFindFile(string relative)
    {
        try { return FindFile(relative); }
        catch (FileNotFoundException) { return null; }
    }
}
