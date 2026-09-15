using System.IO;
using System.Text.Json;
using HfeLayoutSim.Core.Model;

namespace HfeLayoutSim.App.Services;

/// <summary>User preferences that must survive a restart. Every field has a safe default so a
/// missing or truncated settings file degrades to defaults instead of blocking start-up.</summary>
public sealed class AppSettings
{
    /// <summary>Schema version, so an older file can be migrated instead of discarded.</summary>
    public int Version { get; set; } = 1;

    public double WindowWidth { get; set; } = 1500;
    public double WindowHeight { get; set; } = 950;
    public bool WindowMaximized { get; set; }

    public double Zoom { get; set; } = 1.0;
    public bool ShowZones { get; set; }
    public bool ShowGuides { get; set; } = true;

    public string? LastLayoutDirectory { get; set; }
    public string? LastReportDirectory { get; set; }
    public string? LastCatalogDirectory { get; set; }

    /// <summary>Most-recently-opened layouts, newest first.</summary>
    public List<string> RecentFiles { get; set; } = new();

    public bool AutosaveEnabled { get; set; } = true;

    /// <summary>Autosave period in seconds (SPEC §9 asks for 30).</summary>
    public int AutosaveSeconds { get; set; } = 30;

    public const int MaxRecentFiles = 8;

    /// <summary>Clamp every value into a usable range — a hand-edited or partly-written file must not
    /// produce a zero-size window, a 0x zoom, or an autosave timer that fires continuously.</summary>
    public AppSettings Normalize()
    {
        WindowWidth = Clamp(WindowWidth, 1000, 8000, 1500);
        WindowHeight = Clamp(WindowHeight, 640, 5000, 950);
        Zoom = Clamp(Zoom, 0.25, 4.0, 1.0);
        AutosaveSeconds = (int)Clamp(AutosaveSeconds, 10, 600, 30);

        RecentFiles = (RecentFiles ?? new List<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxRecentFiles)
            .ToList();

        LastLayoutDirectory = SafeDir(LastLayoutDirectory);
        LastReportDirectory = SafeDir(LastReportDirectory);
        LastCatalogDirectory = SafeDir(LastCatalogDirectory);
        return this;

        static double Clamp(double value, double min, double max, double fallback)
            => double.IsFinite(value) && value >= min && value <= max ? value : fallback;

        static string? SafeDir(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Directory.Exists(path) ? path : null; } catch { return null; }
        }
    }

    public void RememberRecent(string path)
    {
        try { path = Path.GetFullPath(path); } catch { return; }
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles) RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
    }
}

/// <summary>Loads/saves <see cref="AppSettings"/> atomically; failures are logged, never fatal.</summary>
public static class SettingsService
{
    private static readonly string FilePath = Path.Combine(AppPaths.DataDir, "settings.json");

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
                if (loaded is not null) return loaded.Normalize();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("설정 파일을 읽을 수 없어 기본값으로 시작합니다", ex);
        }
        return new AppSettings();
    }

    /// <summary>Returns false when the settings could not be persisted (the caller warns once).</summary>
    public static bool TrySave(AppSettings settings)
    {
        try
        {
            if (!AppPaths.EnsureDataDir()) return false;
            AtomicFile.WriteAllText(FilePath, JsonSerializer.Serialize(settings.Normalize(), Options));
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("설정 저장 실패", ex);
            return false;
        }
    }
}
