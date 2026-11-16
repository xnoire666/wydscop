using System.IO;
using Microsoft.Win32;

namespace Wydscop.Services;

/// <summary>
/// The app used to be called EasyFind. On the first run under the new name, carry everything over:
/// settings (HKCU\Software\EasyFind), recents, pins and usage (%LocalAppData%\EasyFind), and the
/// start-with-Windows entry (now pointing at Wydscop.exe). The old data is left in place, untouched.
/// </summary>
internal static class LegacyMigration
{
    private const string OldName = "EasyFind";
    private const string NewName = "Wydscop";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static readonly string[] DataFiles = ["recent.json", "pinned.json", "usage.json"];

    /// <summary>Runs before anything reads settings. Safe to call every launch; it only acts once.</summary>
    public static void Run()
    {
        try
        {
            CopySettings();
            CopyData();
            MoveStartup();
        }
        catch (Exception ex)
        {
            Log.Write("Carrying over EasyFind settings failed: " + ex.Message);
        }
    }

    private static void CopySettings()
    {
        using var oldKey = Registry.CurrentUser.OpenSubKey($@"Software\{OldName}");
        if (oldKey is null) return;
        using var existing = Registry.CurrentUser.OpenSubKey($@"Software\{NewName}");
        if (existing is not null) return; // already migrated (or set up fresh)

        using var newKey = Registry.CurrentUser.CreateSubKey($@"Software\{NewName}");
        foreach (string name in oldKey.GetValueNames())
            newKey.SetValue(name, oldKey.GetValue(name)!, oldKey.GetValueKind(name));
        Log.Write($"Carried over {oldKey.ValueCount} settings from {OldName}");
    }

    private static void CopyData()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string oldDir = Path.Combine(local, OldName);
        string newDir = Path.Combine(local, NewName);
        if (!Directory.Exists(oldDir)) return;

        Directory.CreateDirectory(newDir);
        foreach (string file in DataFiles)
        {
            string from = Path.Combine(oldDir, file), to = Path.Combine(newDir, file);
            if (File.Exists(from) && !File.Exists(to))
            {
                File.Copy(from, to);
                Log.Write($"Carried over {file} from {OldName}");
            }
        }
    }

    /// <summary>The old "start with Windows" entry points at EasyFind.exe; replace it with one for this exe.</summary>
    private static void MoveStartup()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (run?.GetValue(OldName) is null) return;

        run.DeleteValue(OldName, throwOnMissingValue: false);
        StartupManager.SetEnabled(true);
        Log.Write("Start with Windows moved from EasyFind.exe to Wydscop.exe");
    }
}
