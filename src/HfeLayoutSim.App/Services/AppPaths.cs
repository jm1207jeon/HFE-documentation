using System.IO;

namespace HfeLayoutSim.App.Services;

/// <summary>
/// Per-user data folder (%APPDATA%\HfeLayoutSim): settings, diagnostic log and autosave live here,
/// never next to the exe — the exe may sit in Program Files or a read-only share.
/// </summary>
public static class AppPaths
{
    public const string AppName = "HfeLayoutSim";
    public const string ProductTitle = "HFE Layout Simulator";

    public static string DataDir { get; }
    public static string AutosaveDir => Path.Combine(DataDir, "autosave");

    static AppPaths()
    {
        string root;
        try
        {
            root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(root)) root = Path.GetTempPath();
        }
        catch
        {
            root = Path.GetTempPath();
        }
        DataDir = Path.Combine(root, AppName);
    }

    /// <summary>Creates the data folder; returns false when it is not usable (locked-down profile).</summary>
    public static bool EnsureDataDir()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
