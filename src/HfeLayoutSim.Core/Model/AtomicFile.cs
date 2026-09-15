using System.Text;

namespace HfeLayoutSim.Core.Model;

/// <summary>
/// Write-then-replace file writing. A plain WriteAllText truncates the target first, so a crash,
/// a full disk or a dropped network share mid-write destroys the previous contents — which for a
/// layout the user has been editing for an hour is unacceptable.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var temp = full + ".tmp";
        File.WriteAllText(temp, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try
        {
            // Replace keeps the original in place until the new file is complete.
            File.Move(temp, full, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
