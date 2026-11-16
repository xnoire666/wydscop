using System.Windows.Interop;
using Wydscop.Interop;
using Wydscop.Services;

namespace Wydscop;

public partial class SearchWindow
{
    // ---------- Theme (light gray / dark / match Windows) ----------

    private bool _acrylic;
    private BarTheme _themeMode = ThemeSettings.Load();

    public BarTheme BarTheme => _themeMode;

    /// <summary>
    /// Asks Windows 11 (22H2+) to draw an acrylic blur behind the window, with rounded corners,
    /// a hairline border and a shadow. Falls back to the solid background when that isn't available.
    /// </summary>
    private void EnableAcrylic()
    {
        int round = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));

        bool supported = Environment.OSVersion.Version.Build >= 22621;
        if (supported)
        {
            // Let the backdrop show through: transparent WPF surface + glass over the whole window.
            var source = HwndSource.FromHwnd(_hwnd);
            source.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;
            var margins = new NativeMethods.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            NativeMethods.DwmExtendFrameIntoClientArea(_hwnd, ref margins);

            int backdrop = NativeMethods.DWMSBT_TRANSIENTWINDOW;
            _acrylic = NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0;
        }

        Log.Write($"Acrylic background: {_acrylic} (Windows build {Environment.OSVersion.Version.Build})");
        ApplyTheme();
    }

    public void SetTheme(BarTheme mode)
    {
        _themeMode = mode;
        ThemeSettings.Save(mode);
        ApplyTheme();
        Log.Write($"Theme: {mode}");
    }

    /// <summary>Recolors the bar, and tells Windows whether to draw a light or dark acrylic base and which border color.</summary>
    private void ApplyTheme()
    {
        var window = Themes.Apply(Resources, ThemeSettings.IsDark(_themeMode));

        var background = (System.Windows.Media.Brush)Resources[_acrylic ? "AcrylicTint" : "SolidBackground"];
        BarBorder.Background = background;
        Background = _acrylic ? System.Windows.Media.Brushes.Transparent : background;

        if (_hwnd == IntPtr.Zero) return;
        int dark = window.DarkFrame ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        int border = window.Border.R | (window.Border.G << 8) | (window.Border.B << 16); // COLORREF is 0x00BBGGRR
        NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    /// <summary>"Match Windows": follow light/dark app mode changes while Wydscop runs.</summary>
    private void OnWindowsSettingChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (_themeMode == BarTheme.System && e.Category == Microsoft.Win32.UserPreferenceCategory.General)
            Dispatcher.InvokeAsync(ApplyTheme);
    }
}
