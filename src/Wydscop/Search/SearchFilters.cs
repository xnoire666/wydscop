using System.IO;

namespace Wydscop.Search;

/// <summary>
/// What file search leaves out, shared by the Windows index search and Wydscop's own index:
/// dependency, build and cache folders (thousands of files nobody launches) and junk file types.
/// </summary>
internal static class SearchFilters
{
    /// <summary>Folders skipped wherever they appear.</summary>
    public static readonly HashSet<string> NoiseFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "vendor", ".git", ".svn", ".hg", ".vs", ".idea", "obj", "__pycache__", ".venv",
        "site-packages", ".cache", ".gradle", ".nuget", "AppData", "$Recycle.Bin", "System Volume Information",
    };

    /// <summary>System folders skipped at the top of a drive (apps there are already found via the Start menu).</summary>
    public static readonly HashSet<string> DriveRootFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "Program Files", "Program Files (x86)", "ProgramData", "Recovery", "Windows.old",
        "$WinREAgent", "$SysReset", "$Windows.~BT", "$Windows.~WS", "Config.Msi", "MSOCache", "PerfLogs",
        "OneDriveTemp", "Intel", "AMD", "NVIDIA",
    };

    public static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk", ".url", // Start menu shortcuts are already shown as apps
        ".tmp", ".log", ".etl", ".cache",
    };

    public static bool IsExcludedFileName(string name) => ExcludedExtensions.Contains(Path.GetExtension(name));

    /// <summary>bin\Debug and bin\Release are build output.</summary>
    public static bool IsBuildOutput(string name, string parentName) =>
        parentName.Equals("bin", StringComparison.OrdinalIgnoreCase)
        && (name.Equals("Debug", StringComparison.OrdinalIgnoreCase) || name.Equals("Release", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when any folder in the path is noise (or a system folder at the top of a drive).</summary>
    public static bool IsNoisePath(string path)
    {
        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < parts.Length; i++) // parts[0] is the drive ("C:")
        {
            // The last part is the item itself: only judge it as a folder name if something is inside it.
            bool isLast = i == parts.Length - 1;
            if (!isLast && NoiseFolderNames.Contains(parts[i])) return true;
            if (!isLast && i == 1 && DriveRootFolderNames.Contains(parts[i])) return true;
            if (!isLast && IsBuildOutput(parts[i], parts[i - 1])) return true;
        }
        return false;
    }
}
