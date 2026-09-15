using System.IO;
using System.Text.Json;
using HfeLayoutSim.Core.Model;

namespace HfeLayoutSim.App.Services;

/// <summary>What an interrupted previous session left behind.</summary>
public sealed record AutosaveRecovery(string SlotPath, string? OriginalPath, DateTimeOffset SavedAt, string LayoutName);

/// <summary>
/// Periodic snapshot of the open layout into %APPDATA% (SPEC §9). The slot is deleted on a clean
/// exit, so a slot still present at start-up means the previous run was killed — power loss, a
/// forced quit, a driver crash — and the work is offered back instead of being lost.
/// </summary>
public static class AutosaveService
{
    private static string SlotPath => Path.Combine(AppPaths.AutosaveDir, "session.hfelayout.json");
    private static string MetaPath => Path.Combine(AppPaths.AutosaveDir, "session.meta.json");

    private sealed class Meta
    {
        public string? OriginalPath { get; set; }
        public DateTimeOffset SavedAt { get; set; }
        public string LayoutName { get; set; } = "";
    }

    /// <summary>Best-effort write; autosave must never interrupt editing, so failures only log.</summary>
    public static bool TryWrite(Layout layout, string? originalPath)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AutosaveDir);
            AtomicFile.WriteAllText(SlotPath, LayoutSerializer.Save(layout));
            AtomicFile.WriteAllText(MetaPath, JsonSerializer.Serialize(new Meta
            {
                OriginalPath = originalPath,
                SavedAt = DateTimeOffset.Now,
                LayoutName = layout.Meta.Name,
            }));
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("자동 저장 실패", ex);
            return false;
        }
    }

    /// <summary>Called after a clean save or a clean exit — the work is safe elsewhere.</summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(SlotPath)) File.Delete(SlotPath);
            if (File.Exists(MetaPath)) File.Delete(MetaPath);
        }
        catch (Exception ex)
        {
            AppLog.Warn("자동 저장본 정리 실패: " + ex.Message);
        }
    }

    /// <summary>A recoverable slot from an interrupted run, or null.</summary>
    public static AutosaveRecovery? FindPending()
    {
        try
        {
            if (!File.Exists(SlotPath)) return null;

            var meta = File.Exists(MetaPath)
                ? JsonSerializer.Deserialize<Meta>(File.ReadAllText(MetaPath))
                : null;

            // Only offer a slot that still parses — a truncated slot would fail on load anyway.
            var layout = LayoutSerializer.LoadFile(SlotPath);
            return new AutosaveRecovery(
                SlotPath,
                meta?.OriginalPath,
                meta?.SavedAt ?? File.GetLastWriteTime(SlotPath),
                string.IsNullOrWhiteSpace(meta?.LayoutName) ? layout.Meta.Name : meta!.LayoutName);
        }
        catch (Exception ex)
        {
            AppLog.Warn("복구 가능한 자동 저장본 없음: " + ex.Message);
            Clear();
            return null;
        }
    }
}
