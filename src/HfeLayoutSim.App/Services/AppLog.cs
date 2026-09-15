using System.IO;

namespace HfeLayoutSim.App.Services;

/// <summary>
/// Diagnostic log (%APPDATA%\HfeLayoutSim\app.log). A status-bar message disappears on the next
/// action; this is what remains afterwards to explain a failure. Logging never throws and never
/// blocks the app — if the log itself cannot be written the app carries on.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private const long MaxBytes = 1024 * 1024;

    public static string FilePath { get; } = Path.Combine(AppPaths.DataDir, "app.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null
            ? message
            : $"{message} :: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                if (!AppPaths.EnsureDataDir()) return;
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                    File.Move(FilePath, Path.Combine(AppPaths.DataDir, "app.prev.log"), overwrite: true);
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // diagnostics must never become the failure
        }
    }
}
