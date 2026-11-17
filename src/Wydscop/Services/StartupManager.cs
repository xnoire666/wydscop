using Microsoft.Win32;
using Windows.ApplicationModel;
using Wydscop.Interop;

namespace Wydscop.Services;

/// <summary>
/// Turns "start with Windows" on or off.
///
/// Run as a plain exe it uses the current user's Run key (no admin needed). Installed from the
/// Microsoft Store (MSIX) that key doesn't work, because Windows gives packaged apps their own private
/// copy of the registry. There it uses the package's startup task instead (declared in
/// packaging/Package.appxmanifest), which also shows up in Task Manager's Startup apps.
/// </summary>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppKeyPath = @"Software\Wydscop";
    private const string ValueName = "Wydscop";

    /// <summary>Must match TaskId of the startup task in packaging/Package.appxmanifest.</summary>
    private const string StartupTaskId = "WydscopStartup";

    private static string Command => $"\"{Environment.ProcessPath}\"";

    /// <summary>True when running from an installed MSIX package (e.g. from the Store).</summary>
    public static bool IsPackaged { get; } = NativeMethods.IsPackaged();

    private static StartupTask? _task;

    /// <summary>The package's startup task, looked up once. Null if it can't be found.</summary>
    private static StartupTask? Task
    {
        get
        {
            if (_task is null)
            {
                try
                {
                    _task = StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log.Write("Startup task not found: " + ex.Message);
                }
            }
            return _task;
        }
    }

    public static bool IsEnabled
    {
        get
        {
            if (IsPackaged)
                return Task?.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (IsPackaged)
        {
            SetTaskEnabled(enabled);
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
            key.SetValue(ValueName, Command);
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static void SetTaskEnabled(bool enabled)
    {
        var task = Task;
        if (task is null) return;
        try
        {
            if (!enabled)
            {
                task.Disable();
                return;
            }

            // Windows won't let an app turn this back on after the user switched it off in
            // Task Manager or Settings > Apps > Startup; then only the user can turn it on there.
            var state = task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
            if (state is not (StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy))
                Log.Write($"Start with Windows not turned on: {state}");
        }
        catch (Exception ex)
        {
            Log.Write("Could not change the startup task: " + ex.Message);
        }
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
        else if (IsEnabled && !IsPackaged)
        {
            SetEnabled(true); // refresh path (a package's startup task always points at the right exe)
        }

        return firstRun;
    }
}
