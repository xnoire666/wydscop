using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Wydscop.Interop;
using Wydscop.Services;

namespace Wydscop;

public partial class SearchWindow
{
    // ---------- Window behavior ----------

    /// <summary>
    /// Space around the bar inside the window. 0 now that Windows draws the shadow (acrylic window);
    /// kept so the position math still reads clearly.
    /// </summary>
    private const double ShadowMargin = 0;

    /// <summary>Default: the bar's top edge sits this far down the monitor (Claude's Quick Entry uses 18.6%).</summary>
    private const double BarTopFraction = 0.14;

    private const double BarHeight = 56;

    /// <summary>Bar width in DIP (same as Claude's Quick Entry), and the limits when you drag an edge.</summary>
    private const double DefaultBarWidth = 614;
    private const double MinBarWidth = 420;
    private const double MaxBarWidth = 1600;

    private bool _positioned;

    /// <summary>
    /// Centers the bar horizontally on the monitor under the mouse, with its top edge 14% down (or where you last dragged it).
    /// Works in physical pixels so mixed-DPI setups line up.
    /// </summary>
    private void PositionOnCursorMonitor()
    {
        NativeMethods.GetCursorPos(out var cursor);
        IntPtr monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);

        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;

        double scale = 1.0;
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0)
            scale = dpiX / 96.0;

        var work = info.rcWork;
        var screen = info.rcMonitor;

        // The width you chose (default 614), limited to what fits on this monitor.
        ApplyBarWidth(ClampBarWidth(BarPositionStore.LoadWidth() ?? DefaultBarWidth, work.Width / scale));

        int width = (int)Math.Round(Width * scale);
        int shadow = (int)Math.Round(ShadowMargin * scale);
        int barHeight = (int)Math.Round(BarHeight * scale);

        // Where you last dragged it, or centered near the top.
        var saved = BarPositionStore.Load();
        int barCenterX = screen.Left + (int)Math.Round((saved?.CenterX ?? 0.5) * screen.Width);
        int barTop = screen.Top + (int)Math.Round((saved?.TopY ?? BarTopFraction) * screen.Height);

        // Keep the bar itself fully on screen (the transparent shadow area may hang off the edge).
        barCenterX = Math.Clamp(barCenterX, work.Left + width / 2 - shadow, work.Right - width / 2 + shadow);
        barTop = Math.Clamp(barTop, work.Top, work.Bottom - barHeight);

        int x = barCenterX - width / 2;
        int y = barTop - shadow;

        // Height follows the content (SizeToContent), so keep whatever height the window has now.
        NativeMethods.GetWindowRect(_hwnd, out var current);
        int height = Math.Max(current.Height, barHeight);
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, width, height, NativeMethods.SWP_NOACTIVATE);
        _positioned = true;
    }

    // ---------- Resizing (width) ----------

    private bool _resizing;
    private bool _resizingRightEdge;
    private int _resizeStartCursorX;
    private NativeMethods.RECT _resizeStartRect;
    private double _resizeStartWidth;
    private double _resizeScale;
    private double _resizeMaxWidth;

    private static double ClampBarWidth(double width, double workAreaWidthDip) =>
        Math.Clamp(width, MinBarWidth, Math.Max(MinBarWidth, Math.Min(MaxBarWidth, workAreaWidthDip - 2 * ShadowMargin)));

    /// <summary>Sets the bar (and the transparent window around it) to this width in DIP.</summary>
    private void ApplyBarWidth(double width)
    {
        BarGrid.Width = width;
        Width = width + 2 * ShadowMargin;
        Resources["SubtitleMaxWidth"] = Math.Round(width * 0.45); // folder paths (with the date below) get more room on a wider bar
    }

    private double CurrentScale() => System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;

    private double WorkAreaWidthDip(double scale)
    {
        IntPtr monitor = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        return NativeMethods.GetMonitorInfo(monitor, ref info) ? info.rcWork.Width / scale : MaxBarWidth;
    }

    private void Resize_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        NativeMethods.GetWindowRect(_hwnd, out var rect);
        double scale = CurrentScale();

        if (e.ClickCount == 2)
        {
            // Double-click an edge: back to the default width, keeping the bar's center where it is.
            BarPositionStore.ClearWidth();
            ApplyBarWidth(ClampBarWidth(DefaultBarWidth, WorkAreaWidthDip(scale)));
            int widthPx = (int)Math.Round(Width * scale);
            int x = (rect.Left + rect.Right) / 2 - widthPx / 2;
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, rect.Top, widthPx, rect.Height, NativeMethods.SWP_NOACTIVATE);
            if (NativeMethods.GetWindowRect(_hwnd, out var after)) SaveBarPosition(after);
            Log.Write("Bar width reset");
            return;
        }

        NativeMethods.GetCursorPos(out var cursor);
        _resizing = true;
        _resizingRightEdge = sender == ResizeRight;
        _resizeStartCursorX = cursor.X;
        _resizeStartRect = rect;
        _resizeStartWidth = BarGrid.Width;
        _resizeScale = scale;
        _resizeMaxWidth = WorkAreaWidthDip(scale);
        ((UIElement)sender).CaptureMouse();
    }

    private void Resize_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing) return;

        // Physical cursor movement → DIP. The opposite edge stays put.
        NativeMethods.GetCursorPos(out var cursor);
        double delta = (cursor.X - _resizeStartCursorX) / _resizeScale;
        double width = ClampBarWidth(_resizeStartWidth + (_resizingRightEdge ? delta : -delta), _resizeMaxWidth);
        if (Math.Abs(width - BarGrid.Width) < 0.5) return;

        ApplyBarWidth(width);
        int widthPx = (int)Math.Round(Width * _resizeScale);
        int x = _resizingRightEdge ? _resizeStartRect.Left : _resizeStartRect.Right - widthPx;
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, _resizeStartRect.Top, widthPx,
            _resizeStartRect.Height, NativeMethods.SWP_NOACTIVATE);
    }

    private void Resize_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        ((UIElement)sender).ReleaseMouseCapture();
        e.Handled = true;

        BarPositionStore.SaveWidth(BarGrid.Width);
        if (NativeMethods.GetWindowRect(_hwnd, out var rect)) SaveBarPosition(rect); // its center may have moved
        Log.Write($"Bar width saved: {BarGrid.Width:0}");
        SearchBox.Focus();
    }

    // ---------- Dragging ----------

    /// <summary>
    /// The empty part of the bar (search icon, space right of the text) drags the window;
    /// the text itself keeps normal caret placement and selection. Double-click the empty part to reset.
    /// </summary>
    private void BarRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsOverText(e) || IsInside(e.OriginalSource, PinButton)) return;
        e.Handled = true;

        if (e.ClickCount == 2)
        {
            BarPositionStore.Clear();
            PositionOnCursorMonitor();
            Log.Write("Bar position reset");
        }
        else
        {
            NativeMethods.GetWindowRect(_hwnd, out var before);
            DragMove(); // returns when the button is released
            NativeMethods.GetWindowRect(_hwnd, out var after);
            if (before.Left != after.Left || before.Top != after.Top) SaveBarPosition(after);
        }

        SearchBox.Focus();
    }

    private static bool IsInside(object source, DependencyObject container)
    {
        for (var d = source as DependencyObject; d is not null;
             d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d == container) return true;
        }
        return false;
    }

    private void BarRow_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        // I-beam over the text, arrow over the draggable empty part.
        SearchBox.Cursor = IsOverText(e) ? null : Cursors.Arrow;
    }

    /// <summary>True when the mouse is over typed text (plus a little slack), where clicks should edit text.</summary>
    private bool IsOverText(MouseEventArgs e)
    {
        Point p = e.GetPosition(SearchBox);
        if (p.X < 0 || p.Y < 0 || p.X > SearchBox.ActualWidth || p.Y > SearchBox.ActualHeight) return false;
        if (SearchBox.Text.Length == 0) return false;

        double textEnd = SearchBox.GetRectFromCharacterIndex(SearchBox.Text.Length).X;
        return p.X <= textEnd + 8;
    }

    /// <summary>Saves the bar's center and top edge as fractions of the monitor it was dropped on.</summary>
    private void SaveBarPosition(NativeMethods.RECT window)
    {
        IntPtr monitor = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;

        double scale = 1.0;
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0)
            scale = dpiX / 96.0;

        var screen = info.rcMonitor;
        double centerX = ((window.Left + window.Right) / 2.0 - screen.Left) / screen.Width;
        double topY = (window.Top + ShadowMargin * scale - screen.Top) / screen.Height;
        BarPositionStore.Save(centerX, topY);
        Log.Write($"Bar position saved: center {centerX:P1}, top {topY:P1}");
    }
}
