using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wydscop.Interop;
using Wydscop.Search;
using Wydscop.Services;

namespace Wydscop;

public partial class SearchWindow : Window
{
    private static readonly TimeSpan IndexMaxAge = TimeSpan.FromMinutes(10);

    private readonly AppIndex _apps = new();
    private readonly LocalIndex _local = new();
    private readonly IconLoader _icons = new();
    private readonly UsageStore _usage = UsageStore.Load();
    private readonly RecentStore _recent = RecentStore.Load();

    private IntPtr _hwnd;

    /// <summary>Set to true only when the app is exiting; otherwise closing just hides the bar.</summary>
    public bool AllowClose { get; set; }

    public SearchWindow()
    {
        InitializeComponent();
        ApplyTheme(); // colors exist before the window is first shown
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnWindowsSettingChanged;
        ResultsList.ItemsSource = _results;

        _fileTimer = new DispatcherTimer { Interval = FileSearchDelay };
        _fileTimer.Tick += (_, _) =>
        {
            _fileTimer.Stop();
            _ = SearchFilesAsync(SearchBox.Text.Trim(), FileSearch.FirstBatchSize);
        };

        _taglineTimer.Tick += (_, _) => RotateTagline();
        Placeholder.Text = _taglines.Next();

        _apps.Updated += (_, _) => Dispatcher.InvokeAsync(RunSearch);
        _apps.RefreshInBackground();
        _local.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // Tool window: never shows in Alt+Tab or the taskbar.
        int exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        exStyle = (exStyle | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

        EnableAcrylic();
    }

    /// <summary>Pinned: the bar stays open when you click elsewhere or open a result, and stays where it is.</summary>
    public bool IsPinned => PinButton.IsChecked == true;

    public void SetPinned(bool pinned) => PinButton.IsChecked = pinned;

    /// <summary>Alt+Space. A pinned bar that's open in the background is brought to the front instead of hidden.</summary>
    public void Toggle()
    {
        if (!IsVisible) ShowBar();
        else if (IsPinned && !IsActive) FocusBar();
        else HideBar();
    }

    private void FocusBar()
    {
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
        SearchBox.SelectAll();
    }

    private void PinButton_Changed(object sender, RoutedEventArgs e)
    {
        bool pinned = IsPinned;
        PinButton.ToolTip = pinned ? "Unpin (hide when you click elsewhere)" : "Pin (keep open)";
        BarPositionStore.SavePinned(pinned);

        // Pinning keeps the bar where it is now, also after a restart.
        if (pinned && IsVisible && NativeMethods.GetWindowRect(_hwnd, out var rect)) SaveBarPosition(rect);

        Log.Write(pinned ? "Pinned" : "Unpinned");
        if (IsVisible) SearchBox.Focus();
    }

    public void ShowBar()
    {
        if (_hwnd == IntPtr.Zero) _hwnd = new WindowInteropHelper(this).EnsureHandle();

        if (DateTime.Now - _apps.LastRefresh > IndexMaxAge)
            _apps.RefreshInBackground(); // picks up newly installed / removed apps

        _lastMousePosition = null;

        // A pinned bar keeps its text and its place; otherwise start fresh on the monitor you're using.
        if (!IsPinned || !_positioned)
        {
            ResetFolders();
            SearchBox.Text = string.Empty;
            PositionOnCursorMonitor();
        }

        // Empty bar: refresh the recent list (TextChanged doesn't fire if the text was already empty).
        if (ShowingRecent)
        {
            _userMovedSelection = false;
            _recentExpanded = false; // back to the 10 newest
            ClearMarks();
            RunSearch();
        }

        // A new line each time the bar opens, then a new one every few seconds.
        if (!InFolder) ShowFreshTagline();
        else _taglineTimer.Start();

        BarGrid.Opacity = 0;
        Show();
        Topmost = true;
        Activate();
        NativeMethods.SetForegroundWindow(_hwnd);

        SearchBox.Focus();
        Keyboard.Focus(SearchBox);

        // Fade the contents in (window opacity doesn't apply to a non-layered acrylic window).
        BarGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110)));
        Log.Write($"Shown at {Left},{Top}; active={IsActive}");
    }

    public void HideBar()
    {
        if (!IsVisible) return;
        _taglineTimer.Stop();
        BarGrid.BeginAnimation(OpacityProperty, null);
        Hide();
    }

    // Clicking anywhere outside the bar (or switching apps) hides it, unless it's pinned.
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (IsPinned) return;
        Log.Write("Deactivated -> hide");
        HideBar();
    }

    // Alt+F4 hides instead of closing, so the hotkey keeps working.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            HideBar();
        }
        base.OnClosing(e);
    }
}
