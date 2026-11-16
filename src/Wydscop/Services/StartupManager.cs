using Microsoft.Win32;

namespace Wydscop.Services;

/// <summary>
/// Turns "start with Windows" on or off through the current user's Run key (no admin needed).
/// </summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppKeyPath = @"Software\Wydscop";
    private const string ValueName = "Wydscop";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
            key.SetValue(ValueName, Command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// On the first launch, turns start-with-Windows on. Afterwards it respects the user's choice,
    /// and keeps the saved path current if the exe moved.
    /// </summary>
    /// <returns>True when this is the first launch.</returns>
    public static bool EnsureDefault()
    {
        using var appKey = Registry.CurrentUser.CreateSubKey(AppKeyPath);
        bool firstRun = appKey.GetValue("Initialized") is null;

        if (firstRun)
        {
            SetEnabled(true);
            appKey.SetValue("Initialized", 1, RegistryValueKind.DWord);
        }
        else if (IsEnabled)
        {
            SetEnabled(true); // refresh path
        }

        return firstRun;
    }
}
