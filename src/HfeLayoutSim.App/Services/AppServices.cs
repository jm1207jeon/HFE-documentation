using System.IO;
using System.Reflection;
using HfeLayoutSim.Core.Advisor;
using HfeLayoutSim.Core.Engine;
using HfeLayoutSim.Core.Presets;
using HfeLayoutSim.Core.Rules;

namespace HfeLayoutSim.App.Services;

/// <summary>How the knowledge base was obtained — surfaced to the user, because scoring depends on it.</summary>
public enum KnowledgeSource
{
    /// <summary>Loaded from the editable file next to the exe (the normal case).</summary>
    File,
    /// <summary>The file was missing, so the copy compiled into the exe is in use.</summary>
    EmbeddedBecauseMissing,
    /// <summary>The file was unusable and the user chose to continue with the built-in copy.</summary>
    EmbeddedBecauseInvalid,
}

public sealed record KnowledgeLoad(KnowledgeSource Source, string Path, string? Problem);

/// <summary>
/// Loads the rule knowledge base and preset library once at start-up and exposes the shared
/// engine services. The app must always be able to start: the JSON files ship both next to the
/// exe (so a QC manager can tune thresholds) and embedded in the exe (so a lone exe still runs).
/// A file that exists but is invalid is never silently ignored — that would score layouts against
/// rules the user did not write.
/// </summary>
public static class AppServices
{
    public static RuleSet Rules { get; private set; } = null!;
    public static PresetLibrary Presets { get; private set; } = null!;
    public static HfeEngine Engine { get; private set; } = null!;
    public static PlacementAdvisor Advisor { get; private set; } = null!;
    public static AppSettings Settings { get; private set; } = new();

    public static KnowledgeLoad RulesLoad { get; private set; } = new(KnowledgeSource.File, "", null);
    public static KnowledgeLoad PresetsLoad { get; private set; } = new(KnowledgeSource.File, "", null);

    public const string RulesRelativePath = "rules/hfe_rules.json";
    public const string PresetsRelativePath = "rules/element_presets.json";

    /// <summary>
    /// Asked before falling back to the built-in copy of a file that exists but cannot be used.
    /// Returns true to continue with the built-in rules, false to abort start-up.
    /// </summary>
    public delegate bool FallbackPrompt(string fileDescription, string path, string problem);

    public static void Initialize(FallbackPrompt confirmFallback)
    {
        Settings = SettingsService.Load();

        Rules = LoadKnowledge(
            RulesRelativePath, "규칙 지식베이스",
            text => ValidateRules(RuleLoader.Load(text, RulesRelativePath)),
            confirmFallback, load => RulesLoad = load);

        Presets = LoadKnowledge(
            PresetsRelativePath, "요소 프리셋",
            text => PresetLibrary.Load(text, PresetsRelativePath),
            confirmFallback, load => PresetsLoad = load);

        Engine = new HfeEngine();
        Advisor = new PlacementAdvisor(Rules, Presets);

        AppLog.Info($"지식베이스 로드 완료 — 규칙 {Rules.Rules.Count}개({RulesLoad.Source}), " +
                    $"프리셋 {Presets.All.Count}개({PresetsLoad.Source})");
    }

    /// <summary>Rules must be usable by the evaluators, checked here so a bad file fails at start-up
    /// rather than when the user first presses [평가 실행].</summary>
    private static RuleSet ValidateRules(RuleSet rules)
    {
        new EvaluatorRegistry().EnsureCoverage(rules);
        return rules;
    }

    private static T LoadKnowledge<T>(string relativePath, string description,
        Func<string, T> parse, FallbackPrompt confirmFallback, Action<KnowledgeLoad> record)
    {
        var path = TryFindFile(relativePath);

        if (path is not null)
        {
            try
            {
                var value = parse(File.ReadAllText(path));
                record(new KnowledgeLoad(KnowledgeSource.File, path, null));
                return value;
            }
            catch (Exception ex)
            {
                AppLog.Error($"{description} 파일 사용 불가: {path}", ex);
                if (!confirmFallback(description, path, ex.Message))
                    throw new KnowledgeAbortException(
                        $"{description} 파일에 오류가 있어 시작을 중단했습니다:\n{path}\n\n{ex.Message}");

                var embedded = parse(ReadEmbedded(relativePath));
                record(new KnowledgeLoad(KnowledgeSource.EmbeddedBecauseInvalid, path, ex.Message));
                return embedded;
            }
        }

        AppLog.Warn($"{description} 파일을 찾을 수 없어 내장본을 사용합니다 ({relativePath})");
        var fromResource = parse(ReadEmbedded(relativePath));
        record(new KnowledgeLoad(KnowledgeSource.EmbeddedBecauseMissing, relativePath, null));
        return fromResource;
    }

    private static string ReadEmbedded(string relativePath)
    {
        var name = "HfeLayoutSim.Embedded." + relativePath.Replace('/', '.');
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(name)
            ?? throw new FileNotFoundException(
                $"내장 리소스를 찾을 수 없습니다: {name} (빌드 구성 오류)");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Looks next to the exe first (the published layout), then walks up the tree so the app also
    /// runs from a development checkout where rules/ sits at the repository root.
    /// </summary>
    public static string? TryFindFile(string relativePath)
    {
        var relative = relativePath.Replace('/', Path.DirectorySeparatorChar);
        try
        {
            var direct = Path.Combine(AppContext.BaseDirectory, relative);
            if (File.Exists(direct)) return direct;

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var depth = 0; dir is not null && depth < 8; depth++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"파일 탐색 실패 ({relativePath}): {ex.Message}");
        }
        return null;
    }

    /// <summary>Directory to open file dialogs in, remembered across runs.</summary>
    public static string? DirectoryOf(string relativePath)
    {
        var found = TryFindFile(relativePath);
        return found is null ? null : Path.GetDirectoryName(found);
    }
}

/// <summary>Start-up was abandoned on the user's instruction (not a crash).</summary>
public sealed class KnowledgeAbortException : Exception
{
    public KnowledgeAbortException(string message) : base(message) { }
}
