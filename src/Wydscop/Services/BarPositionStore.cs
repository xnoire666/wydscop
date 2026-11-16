using System.Globalization;
using Microsoft.Win32;

namespace Wydscop.Services;

/// <summary>
/// Where you dragged the bar, saved as fractions of the monitor (so it lands in the same spot on any
/// monitor or resolution): the bar's horizontal center and its top edge. Stored in HKCU\Software\Wydscop.
/// </summary>
internal static class BarPositionStore
{
    private const string KeyPath = @"Software\Wydscop";
    private const string CenterXName = "BarCenterX";
    private const string TopYName = "BarTopY";
    private const string WidthName = "BarWidth";

    public static (double CenterX, double TopY)? Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key?.GetValue(CenterXName) is string x && key.GetValue(TopYName) is string y
                && double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out double cx)
                && double.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out double ty))
            {
                return (cx, ty);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Could not read bar position: " + ex.Message);
        }
        return null;
    }

    public static void Save(double centerX, double topY)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue(CenterXName, centerX.ToString("R", CultureInfo.InvariantCulture));
            key.SetValue(TopYName, topY.ToString("R", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Log.Write("Could not save bar position: " + ex.Message);
        }
    }

    /// <summary>The bar width you chose by dragging its edge (in DIP), or null for the default.</summary>
    public static double? LoadWidth()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key?.GetValue(WidthName) is string s
                && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double width))
            {
                return width;
            }
        }
        catch
        {
        }
        return null;
    }

    public static void SaveWidth(double width)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue(WidthName, width.ToString("R", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Log.Write("Could not save bar width: " + ex.Message);
        }
    }

    public static void ClearWidth()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            key?.DeleteValue(WidthName, throwOnMissingValue: false);
        }
        catch
        {
        }
    }

    /// <summary>Whether the bar is pinned (stays open). Remembered across restarts.</summary>
    public static bool LoadPinned()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue("Pinned") is int pinned && pinned == 1;
        }
        catch
        {
            return false;
        }
    }

    public static void SavePinned(bool pinned)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue("Pinned", pinned ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            Log.Write("Could not save pin state: " + ex.Message);
        }
    }

    /// <summary>Back to the default spot (centered, near the top) and default width.</summary>
    public static void Clear()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            key?.DeleteValue(CenterXName, throwOnMissingValue: false);
            key?.DeleteValue(TopYName, throwOnMissingValue: false);
            key?.DeleteValue(WidthName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Log.Write("Could not reset bar position: " + ex.Message);
        }
    }
}
