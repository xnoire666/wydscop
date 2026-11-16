using Microsoft.Win32;

namespace Wydscop.Services;

public enum BarTheme
{
    /// <summary>Soft gray (the default).</summary>
    Light,
    Dark,

    /// <summary>Follows Windows' app mode (Settings > Personalization > Colors).</summary>
    System,
}

/// <summary>Which theme the bar uses, saved in HKCU\Software\Wydscop ("Theme").</summary>
internal static class ThemeSettings
{
    private const string KeyPath = @"Software\Wydscop";

    public static BarTheme Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key?.GetValue("Theme") is string s && Enum.TryParse(s, out BarTheme mode)) return mode;
        }
        catch
        {
        }
        return BarTheme.Light;
    }

    public static void Save(BarTheme mode)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue("Theme", mode.ToString());
        }
        catch (Exception ex)
        {
            Log.Write("Could not save theme: " + ex.Message);
        }
    }

    public static bool IsDark(BarTheme mode) => mode switch
    {
        BarTheme.Dark => true,
        BarTheme.Light => false,
        _ => WindowsUsesDarkApps(),
    };

    /// <summary>Windows' "Choose your default app mode": Dark when AppsUseLightTheme is 0.</summary>
    private static bool WindowsUsesDarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }
}
