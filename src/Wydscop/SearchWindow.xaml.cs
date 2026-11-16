using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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

    /// <summary>Rows visible without scrolling (the list's MaxHeight in XAML fits exactly this many).</summary>
    private const int VisibleRows = 10;

    /// <summary>Rows added to the list at a time; more are added as you scroll near the end.</summary>
    private const int PageSize = 20;

    /// <summary>Start loading the next page when the highlight or scroll position is this close to the end.</summary>
    private const int LoadMoreThreshold = 3;

    private static readonly TimeSpan IndexMaxAge = TimeSpan.FromMinutes(10);

    /// <summary>Wait this long after the last keystroke before asking the Windows Search index.</summary>
    private static readonly TimeSpan FileSearchDelay = TimeSpan.FromMilliseconds(120);

    private static readonly string UserProfile =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\') + "\\";

    private readonly AppIndex _apps = new();
    private readonly LocalIndex _local = new();
    private readonly IconLoader _icons = new();
    private readonly UsageStore _usage = UsageStore.Load();
    private readonly RecentStore _recent = RecentStore.Load();

    // Rotating placeholder lines in the empty bar.
    private static readonly TimeSpan TaglineInterval = TimeSpan.FromSeconds(30);
    private readonly Taglines _taglines = new();
    private readonly DispatcherTimer _taglineTimer = new() { Interval = TaglineInterval };

    /// <summary>What the list shows: a growing prefix of <see cref="_ordered"/>.</summary>
    private readonly ObservableCollection<SearchResult> _results = new();

    /// <summary>Every result for the current query, best first.</summary>
    private List<SearchResult> _ordered = new();

    private readonly DispatcherTimer _fileTimer;
    private CancellationTokenSource? _fileCancel;
    private string _fileQuery = string.Empty;
    private List<SearchResult> _fileResults = new();
    private int _fileBatchSize = FileSearch.FirstBatchSize;
    private bool _fileMayHaveMore;
    private bool _loadingMoreFiles;

    private bool _userMovedSelection;
    private bool _positioned;
    private Point? _lastMousePosition;
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

    // ---------- Theme (light gray / dark / match Windows) ----------

    private bool _acrylic;
    private BarTheme _themeMode = ThemeSettings.Load();

    public BarTheme BarTheme => _themeMode;

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

    // ---------- Rotating placeholder ----------

    /// <summary>The line to show in the empty bar right now (not inside a folder).</summary>
    private void ShowFreshTagline()
    {
        Placeholder.BeginAnimation(OpacityProperty, null);
        PlaceholderShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        Placeholder.Text = _taglines.Next();
        _taglineTimer.Stop();
        _taglineTimer.Start();
    }

    /// <summary>Every few seconds: fade the line out, swap it, and slide the next one up into place.</summary>
    private void RotateTagline()
    {
        // Only while the empty bar is showing its placeholder.
        if (!IsVisible || InFolder || SearchBox.Text.Length > 0) return;

        string next = _taglines.Next();
        if (!SystemParameters.ClientAreaAnimation) // Windows animations turned off: just swap
        {
            Placeholder.Text = next;
            return;
        }

        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(170));
        fadeOut.Completed += (_, _) =>
        {
            if (InFolder) // you stepped into a folder meanwhile; its "Search in …" stays
            {
                Placeholder.BeginAnimation(OpacityProperty, null);
                return;
            }

            Placeholder.Text = next;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Placeholder.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
            PlaceholderShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(7, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        };
        Placeholder.BeginAnimation(OpacityProperty, fadeOut);
    }

    // ---------- Search ----------

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Hidden;
        _userMovedSelection = false;
        if (!_restoringSearch) _pendingSelectId = null;

        // Apps show instantly; files follow once typing pauses. Inside a folder, the text only filters it.
        _fileCancel?.Cancel();
        _fileTimer.Stop();
        // (Stepping back out of a folder restores your text; its file results are still here, so don't search
        // again: a fresh search would rebuild the list and lose your scroll position.)
        string text = SearchBox.Text.Trim();
        bool resultsStillValid = _restoringSearch && string.Equals(_fileQuery, text, StringComparison.OrdinalIgnoreCase);
        if (!InFolder && text.Length > 0 && !resultsStillValid) _fileTimer.Start();

        RunSearch();
    }

    /// <summary>Rebuilds the list from apps plus the latest file results for this exact query.</summary>
    private void RunSearch()
    {
        if (InFolder)
        {
            ShowFolderContents();
            return;
        }

        string query = SearchBox.Text.Trim();
        string? selectedId = _userMovedSelection ? (ResultsList.SelectedItem as SearchResult)?.Id : null;
        int shownBefore = _results.Count;

        if (query.Length == 0)
        {
            ShowRecent(selectedId);
            return;
        }
        ResultsList.MaxHeight = ListMaxHeight;

        _ordered = new List<SearchResult>();
        {
            // Typos ("chorme") are only tried when nothing matches normally, so they never add noise.
            var apps = ScoreApps(query, allowTypos: false);
            if (apps.Count == 0) apps = ScoreApps(query, allowTypos: true);

            var files = string.Equals(_fileQuery, query, StringComparison.OrdinalIgnoreCase)
                ? _fileResults // already scored
                : new List<SearchResult>();

            _ordered = OrderResults(apps, files);
        }

        ShowOrdered(selectedId, shownBefore, emptyMessage: null);
    }

    // ---------- Recently opened (shown when the bar is empty) ----------

    /// <summary>
    /// The empty bar: "Recent" (what you opened, newest first, 10 then "Show more"), then "Pinned" (what you
    /// pinned, in the order you pinned it). Pinned items aren't repeated under Recent. Uninstalled apps and
    /// deleted files are skipped.
    /// </summary>
    private void ShowRecent(string? selectedId)
    {
        var apps = _apps.Items.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

        var pinned = _pinned.Items
            .Select(p => StoredResult(p.Id, p.Kind, p.Title, apps))
            .OfType<SearchResult>()
            .ToList();

        var recent = _recent.Items
            .Where(r => !_pinned.Contains(r.Id))
            .Select(r => StoredResult(r.Id, r.Kind, r.Title, apps)?.AsRecent())
            .OfType<SearchResult>()
            .ToList();

        _ordered = new List<SearchResult>();
        int headings = 0;
        bool hasShowMore = false;

        if (recent.Count > 0)
        {
            _ordered.Add(SearchResult.Heading("Recent"));
            headings++;
            if (_recentExpanded || recent.Count <= RecentShownAtFirst)
            {
                _ordered.AddRange(recent);
            }
            else
            {
                // The 10 newest, then a "Show more (N)" row for the rest.
                _ordered.AddRange(recent.Take(RecentShownAtFirst));
                int more = recent.Count - RecentShownAtFirst;
                _ordered.Add(new SearchResult(ShowMoreId, ResultKind.Command, $"Show more ({more})", string.Empty, ExpandRecent));
                hasShowMore = true;
            }
        }

        if (pinned.Count > 0)
        {
            _ordered.Add(SearchResult.Heading("Pinned"));
            headings++;
            _ordered.AddRange(pinned);
        }

        // Room for the headings and the "Show more" row on top of 10 rows, so they don't push items out of view.
        ResultsList.MaxHeight = ListMaxHeight + headings * HeadingHeight + (hasShowMore ? RowHeight : 0) + 2; // +2: no scrollbar from rounding

        ShowOrdered(selectedId, shownBefore: 0, emptyMessage: null);

        // Nothing is highlighted until you press ↓ (or hover), so Enter on an empty bar does nothing.
        if (selectedId is null && _results.Count > 0)
        {
            ResultsList.SelectedIndex = -1;
            ResultsList.ScrollIntoView(_results[0]);
        }
    }

    /// <summary>
    /// A saved item (recent or pinned) as a row: the app from the app list, or the file/folder with its dates.
    /// Null when it's gone (uninstalled, deleted) or the app list hasn't loaded yet.
    /// </summary>
    private SearchResult? StoredResult(string id, ResultKind kind, string title, Dictionary<string, SearchResult> apps)
    {
        if (kind == ResultKind.App)
        {
            if (!apps.TryGetValue(id, out var app)) return null;
            app.IsPinned = _pinned.Contains(id);
            return app;
        }

        bool exists = kind == ResultKind.Folder ? System.IO.Directory.Exists(id) : System.IO.File.Exists(id);
        if (!exists) return null;

        string folder = System.IO.Path.GetDirectoryName(id) ?? string.Empty;
        string subtitle = folder.StartsWith(UserProfile, StringComparison.OrdinalIgnoreCase) ? folder[UserProfile.Length..] : folder;
        var file = CreateFileResult(id, title, kind == ResultKind.Folder, subtitle);
        try
        {
            file.SetDates(System.IO.File.GetLastWriteTimeUtc(id), System.IO.File.GetCreationTimeUtc(id)); // works for folders too
        }
        catch
        {
            // no date shown
        }
        return file;
    }

    // ---------- Pinning results ----------

    private readonly PinnedStore _pinned = PinnedStore.Load();

    /// <summary>Pin or unpin a result (its pin button, or Ctrl+P). On the empty bar it moves between Recent and Pinned.</summary>
    private void TogglePin(SearchResult result)
    {
        if (!result.CanPin) return;

        bool pinned = _pinned.Toggle(result);
        result.IsPinned = pinned;
        foreach (var app in _apps.Items)
            if (string.Equals(app.Id, result.Id, StringComparison.OrdinalIgnoreCase)) app.IsPinned = pinned;
        Log.Write($"{(pinned ? "Pinned" : "Unpinned")} result {result.Title} ({result.Id})");

        if (ShowingRecent)
        {
            // Rebuild the sections and keep the highlight on the same item in its new place.
            bool wasSelected = ResultsList.SelectedItem == result;
            RunSearch();
            if (wasSelected && _results.FirstOrDefault(r => r.Id == result.Id) is { } moved)
            {
                ResultsList.SelectedItem = moved;
                ResultsList.ScrollIntoView(moved);
                _userMovedSelection = true;
            }
        }
    }

    /// <summary>Section heading row height (matches the XAML trigger).</summary>
    private const double HeadingHeight = 30;

    /// <summary>Result row height and the list's normal max height (10 rows + padding), matching the XAML.</summary>
    private const double RowHeight = 44;
    private const double ListMaxHeight = VisibleRows * RowHeight + 12;

    /// <summary>Recent items shown before "Show more".</summary>
    private const int RecentShownAtFirst = 10;

    private const string ShowMoreId = "wydscop:show-more-recent";

    /// <summary>"Show more" was used; resets each time the bar opens.</summary>
    private bool _recentExpanded;

    /// <summary>"Show more": list every recent item and highlight the first one that was hidden.</summary>
    private void ExpandRecent()
    {
        _recentExpanded = true;
        RunSearch();
        if (_results.Count > RecentShownAtFirst + 1)
        {
            ResultsList.SelectedIndex = RecentShownAtFirst + 1; // +1: the "Recent" heading
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            _userMovedSelection = true;
        }
    }

    /// <summary>Delete key or ✕: takes the item off the Recent list. The highlight stays at the same position.</summary>
    private void RemoveRecent(SearchResult result, bool keepHighlight)
    {
        if (!result.IsRecent) return;
        int index = _results.IndexOf(result);

        _recent.Remove(result.Id);
        RunSearch();

        if (keepHighlight && _results.Count > 0 && index >= 0)
        {
            int target = Math.Min(index, _results.Count - 1);
            while (target < _results.Count - 1 && _results[target].IsHeader) target++; // not onto a heading
            while (target > 0 && _results[target].IsHeader) target--;
            ResultsList.SelectedIndex = target;
            _userMovedSelection = true;
        }
    }

    private void RemoveSelectedRecent()
    {
        if (ResultsList.SelectedItem is SearchResult result) RemoveRecent(result, keepHighlight: true);
    }

    public void ClearRecent()
    {
        _recent.Clear();
        if (IsVisible) RunSearch();
    }

    private bool ShowingRecent => !InFolder && SearchBox.Text.Trim().Length == 0;

    /// <summary>
    /// Puts the first page of <see cref="_ordered"/> on screen and picks the highlighted row:
    /// the item you came back from (after leaving a folder), the item you had highlighted, or the top one.
    /// </summary>
    private void ShowOrdered(string? selectedId, int shownBefore, string? emptyMessage, string? header = null)
    {
        SectionHeader.Text = header ?? string.Empty;
        SectionHeader.Visibility = header is null ? Visibility.Collapsed : Visibility.Visible;

        // Coming back out of a folder: highlight that folder again, even if it only shows up once
        // the file results arrive (scrolling further down if needed).
        int show = Math.Min(_ordered.Count, Math.Max(PageSize, _userMovedSelection ? shownBefore : 0));
        bool backFromFolder = false;
        if (_pendingSelectId is not null)
        {
            int index = _ordered.FindIndex(r => r.Id == _pendingSelectId);
            if (index >= 0)
            {
                show = Math.Min(_ordered.Count, Math.Max(show, index + 1));
                if (_pendingScroll is { } saved) show = Math.Min(_ordered.Count, Math.Max(show, saved.Shown)); // as many rows as were loaded
                selectedId = _pendingSelectId;
                _pendingSelectId = null;
                _userMovedSelection = true;
                backFromFolder = true;
            }
        }

        _results.Clear();
        for (int i = 0; i < show; i++) _results.Add(_ordered[i]);

        bool any = _results.Count > 0;
        EmptyMessage.Text = emptyMessage ?? string.Empty;
        EmptyMessage.Visibility = !any && emptyMessage is not null ? Visibility.Visible : Visibility.Collapsed;
        ResultsList.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        ResultsPanel.Visibility = any || emptyMessage is not null ? Visibility.Visible : Visibility.Collapsed;
        if (!any) return;

        // If you moved the highlight (arrows / mouse), keep it on the same item when file results
        // arrive and reshuffle the list; otherwise highlight the best match.
        var keep = selectedId is null ? null : _results.FirstOrDefault(r => r.Id == selectedId);
        ResultsList.SelectedItem = keep ?? _results[0];

        // Back out of a folder: put the list back exactly where it was scrolled, not just "somewhere the
        // folder is visible", so the rows don't jump around.
        if (backFromFolder && _pendingScroll is { } restore)
        {
            _pendingScroll = null;
            Dispatcher.InvokeAsync(() => ListScroll?.ScrollToVerticalOffset(restore.Offset), DispatcherPriority.Loaded);
            return;
        }
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    /// <summary>Scroll position (in rows) and loaded row count of each level, saved when you step into a folder.</summary>
    private readonly Stack<(double Offset, int Shown)> _scrollStack = new();

    /// <summary>Saved scroll position to restore once the level you stepped back to is shown.</summary>
    private (double Offset, int Shown)? _pendingScroll;

    private ScrollViewer? _listScroll;

    /// <summary>The results list's own ScrollViewer (inside its template).</summary>
    private ScrollViewer? ListScroll => _listScroll ??= FindDescendant<ScrollViewer>(ResultsList);

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } deeper) return deeper;
        }
        return null;
    }

    // ---------- Looking inside a folder (Right arrow / ›) ----------

    /// <summary>Folders you've stepped into, innermost on top. Empty = normal search.</summary>
    private readonly Stack<string> _folderStack = new();

    /// <summary>Everything in the current folder: subfolders first, then files, by name.</summary>
    private List<SearchResult> _folderItems = new();

    /// <summary>What you had typed before stepping into the first folder, restored when you step back out.</summary>
    private string _searchBeforeFolder = string.Empty;

    /// <summary>Item to highlight when it shows up (the folder you just stepped out of).</summary>
    private string? _pendingSelectId;

    private bool _restoringSearch;
    private bool _loadingFolder;
    private int _folderLoadVersion;

    private bool InFolder => _folderStack.Count > 0;

    private void EnterFolder(SearchResult folder)
    {
        if (!folder.IsFolder) return;

        if (!InFolder) _searchBeforeFolder = SearchBox.Text;
        _scrollStack.Push((ListScroll?.VerticalOffset ?? 0, _results.Count));
        _folderStack.Push(folder.Id);
        Log.Write($"Entered folder {folder.Id}");
        OpenFolderLevel(selectId: null);
    }

    /// <summary>Left arrow: one folder up; out of the first folder, back to your search results.</summary>
    private void LeaveFolder()
    {
        if (!InFolder) return;
        string left = _folderStack.Pop();
        _pendingSelectId = left; // highlight the folder we came out of
        _pendingScroll = _scrollStack.Count > 0 ? _scrollStack.Pop() : null; // and scroll back to where it was

        if (InFolder)
        {
            OpenFolderLevel(selectId: left);
            return;
        }

        // Back to the original search.
        FolderChip.Visibility = Visibility.Collapsed;
        ShowFreshTagline();
        _folderItems = new List<SearchResult>();
        _restoringSearch = true;
        try
        {
            SearchBox.Text = _searchBeforeFolder;
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }
        finally
        {
            _restoringSearch = false;
        }
        if (SearchBox.Text.Length == 0) RunSearch(); // TextChanged doesn't fire when it was already empty
    }

    /// <summary>Leaves every folder without restoring anything (the bar is starting fresh).</summary>
    private void ResetFolders()
    {
        _folderStack.Clear();
        _scrollStack.Clear();
        _pendingScroll = null;
        _folderItems = new List<SearchResult>();
        _pendingSelectId = null;
        FolderChip.Visibility = Visibility.Collapsed;
        Placeholder.Text = _taglines.Current;
    }

    /// <summary>Shows the folder on top of the stack: chip in the bar, empty filter text, its contents below.</summary>
    private void OpenFolderLevel(string? selectId)
    {
        string path = _folderStack.Peek();
        string name = System.IO.Path.GetFileName(path.TrimEnd('\\'));
        if (name.Length == 0) name = path; // a drive, e.g. "D:\"

        FolderChipText.Text = name;
        FolderChip.ToolTip = path;
        FolderChip.Visibility = Visibility.Visible;
        Placeholder.BeginAnimation(OpacityProperty, null); // stop a tagline fade in progress
        PlaceholderShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
        Placeholder.Text = $"Search in {name}";

        _fileCancel?.Cancel();
        _fileTimer.Stop();
        _pendingSelectId = selectId;
        _folderItems = new List<SearchResult>();

        _restoringSearch = true;
        try
        {
            SearchBox.Text = string.Empty; // the text now filters inside this folder
        }
        finally
        {
            _restoringSearch = false;
        }
        _pendingSelectId = selectId;

        _ = LoadFolderAsync(path);
    }

    private async Task LoadFolderAsync(string path)
    {
        int version = ++_folderLoadVersion;
        _loadingFolder = true;
        ShowFolderContents();

        List<FileHit> entries;
        try
        {
            entries = await Task.Run(() => ListFolder(path));
        }
        catch (Exception ex)
        {
            Log.Write($"Could not list {path}: {ex.Message}");
            entries = new List<FileHit>();
        }

        if (version != _folderLoadVersion || !InFolder || _folderStack.Peek() != path) return; // you moved on

        _loadingFolder = false;
        // Inside a folder the location is obvious, so the second column shows "Folder" or the file size; the date has its own column.
        _folderItems = entries.Select(e =>
            {
                var item = CreateFileResult(e.Path, e.Name, e.IsFolder, e.IsFolder ? "Folder" : FormatSize(e.Size));
                item.SetDates(e.Modified, e.Created);
                return item;
            })
            .ToList();
        ShowFolderContents();
    }

    /// <summary>Subfolders first, then files, alphabetically. Hidden and system items are left out, like File Explorer.</summary>
    private static List<FileHit> ListFolder(string path)
    {
        var options = new System.IO.EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = System.IO.FileAttributes.Hidden | System.IO.FileAttributes.System,
        };

        return new System.IO.DirectoryInfo(path)
            .EnumerateFileSystemInfos("*", options)
            .Take(50_000)
            .Select(i => new FileHit(i.FullName, i.Name, i is System.IO.DirectoryInfo, i.LastWriteTimeUtc, i.CreationTimeUtc,
                i is System.IO.FileInfo f ? f.Length : -1))
            .OrderByDescending(h => h.IsFolder)
            .ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>"512 bytes", "12 KB", "3.4 MB", "1.2 GB".</summary>
    private static string FormatSize(long bytes) => bytes switch
    {
        < 0 => string.Empty,
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{Math.Ceiling(bytes / 1024.0):0} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB",
    };

    /// <summary>The current folder's contents, filtered by what's typed (best matches first).</summary>
    private void ShowFolderContents()
    {
        ResultsList.MaxHeight = ListMaxHeight;
        string query = SearchBox.Text.Trim();
        string name = FolderChipText.Text;

        if (query.Length == 0)
        {
            _ordered = new List<SearchResult>(_folderItems);
        }
        else
        {
            foreach (var item in _folderItems) item.Score = Matcher.Score(item.Title, query);
            _ordered = _folderItems.Where(i => i.Score > 0).ToList();
            if (_ordered.Count == 0)
            {
                foreach (var item in _folderItems) item.Score = Matcher.Score(item.Title, query, allowTypos: true);
                _ordered = _folderItems.Where(i => i.Score > 0).ToList();
            }
            _ordered = SortByScore(_ordered);
        }

        string message = _loadingFolder ? "Loading…"
            : _folderItems.Count == 0 ? $"{name} is empty"
            : $"Nothing in {name} matches \"{query}\"";
        ShowOrdered(selectedId: null, shownBefore: 0, emptyMessage: message);
    }

    private List<SearchResult> ScoreApps(string query, bool allowTypos)
    {
        var apps = new List<SearchResult>();
        foreach (var app in _apps.Items)
        {
            double score = Matcher.Score(app.Title, query, allowTypos);
            if (score <= 0) continue;
            app.Score = score;
            app.IsPinned = _pinned.Contains(app.Id);
            apps.Add(app);
        }
        return apps;
    }

    /// <summary>
    /// Closest match first: exact name, then starts with, word start, initials, contains, letters in order,
    /// typos (see Matcher). When two results match equally well, the newest (modified, or created if it has
    /// no modified date) comes first, then the shorter name.
    /// </summary>
    private static List<SearchResult> SortByScore(IEnumerable<SearchResult> results) =>
        results
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Date ?? DateTime.MinValue) // equal matches: newest (modified, else created) first
            .ThenBy(r => r.Title.Length)
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>One list, apps and files together, ordered only by how closely each matches.</summary>
    private static List<SearchResult> OrderResults(List<SearchResult> apps, List<SearchResult> files) =>
        SortByScore(apps.Concat(files));

    // ---------- Lazy loading ----------

    /// <summary>Adds the next page to the list; when everything we have is showing, asks the index for more files.</summary>
    private void LoadMore()
    {
        if (_results.Count < _ordered.Count)
        {
            int end = Math.Min(_ordered.Count, _results.Count + PageSize);
            for (int i = _results.Count; i < end; i++) _results.Add(_ordered[i]);
            return;
        }

        string query = SearchBox.Text.Trim();
        if (!InFolder && _fileMayHaveMore && !_loadingMoreFiles && query.Length > 0
            && string.Equals(_fileQuery, query, StringComparison.OrdinalIgnoreCase))
        {
            _ = SearchFilesAsync(query, FileSearch.NextBatchSize(_fileBatchSize));
        }
    }

    private void ResultsList_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // CanContentScroll is on, so these are in rows.
        if (e.ExtentHeight > 0 && e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - LoadMoreThreshold)
            LoadMore();
    }

    /// <param name="batchSize">How many rows to ask the index for; bigger than the last batch means "load more".</param>
    private async Task SearchFilesAsync(string query, int batchSize)
    {
        if (query.Length == 0) return;
        bool isMore = batchSize > FileSearch.FirstBatchSize;

        CancellationTokenSource cancel;
        if (isMore)
        {
            _loadingMoreFiles = true;
            cancel = _fileCancel ??= new CancellationTokenSource();
        }
        else
        {
            _fileCancel?.Cancel();
            cancel = new CancellationTokenSource();
            _fileCancel = cancel;
        }

        FileBatch batch;
        try
        {
            batch = await SearchAllFilesAsync(query, batchSize, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (isMore) _loadingMoreFiles = false;
        }

        // A newer keystroke won; drop these.
        if (cancel.IsCancellationRequested || !string.Equals(SearchBox.Text.Trim(), query, StringComparison.Ordinal))
            return;

        _fileBatchSize = batchSize;
        _fileMayHaveMore = batch.MayHaveMore;

        if (!isMore)
        {
            _fileQuery = query;
            _fileResults = batch.Hits.Select(hit => ToResult(hit, query)).ToList();
            RunSearch();
            return;
        }

        // "Load more": the bigger batch repeats what we have, so keep only new files and add them
        // after everything already in the list (nothing on screen moves).
        var known = new HashSet<string>(_fileResults.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
        var added = SortByScore(batch.Hits.Where(h => known.Add(h.Path)).Select(h => ToResult(h, query)));
        _fileResults.AddRange(added);
        _ordered.AddRange(added);
        Log.Write($"Loaded {added.Count} more files for \"{query}\"");
        if (added.Count > 0) LoadMore();
    }

    /// <summary>
    /// Asks the Windows Search index (your user folders) and Wydscop's own index (everything else)
    /// at the same time and merges the answers.
    /// </summary>
    private async Task<FileBatch> SearchAllFilesAsync(string query, int top, CancellationToken cancel)
    {
        var windows = FileSearch.SearchAsync(query, top, cancel);
        var local = Task.Run(() => _local.Search(query, top, cancel), cancel);
        var batches = await Task.WhenAll(windows, local);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hits = batches.SelectMany(b => b.Hits).Where(h => seen.Add(h.Path)).ToList();
        return new FileBatch(hits, batches.Any(b => b.MayHaveMore));
    }

    /// <summary>A file or folder row: Enter opens it, Ctrl+Enter shows it in File Explorer. The icon loads in the background.</summary>
    private SearchResult CreateFileResult(string path, string name, bool isFolder, string subtitle)
    {
        var result = new SearchResult(
            path,
            isFolder ? ResultKind.Folder : ResultKind.File,
            name,
            subtitle,
            execute: () => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }),
            reveal: () => Process.Start("explorer.exe", $"/select,\"{path}\""))
        {
            IsPinned = _pinned.Contains(path),
        };
        _icons.Request(result, path, isFolder);
        return result;
    }

    private SearchResult ToResult(FileHit hit, string query)
    {
        string path = hit.Path;
        string folder = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
        string subtitle = folder.StartsWith(UserProfile, StringComparison.OrdinalIgnoreCase)
            ? folder[UserProfile.Length..]
            : folder;

        var result = CreateFileResult(path, hit.Name, hit.IsFolder, subtitle);
        result.SetDates(hit.Modified, hit.Created);

        // The index matched a word in the name, so never score 0 even if Matcher is stricter.
        result.Score = Math.Max(Matcher.Score(hit.Name, query), 200);
        return result;
    }

    // ---------- Selection and opening ----------

    private void MoveSelection(int delta)
    {
        if (_results.Count == 0) return;
        _userMovedSelection = true;

        // Nothing highlighted yet (empty bar showing recents): ↓ starts at the first row, ↑ at the last.
        int current = ResultsList.SelectedIndex;
        int target = current < 0
            ? (delta > 0 ? Math.Min(delta, _results.Count) - 1 : _results.Count - 1)
            : current + delta;

        if (target >= _results.Count - LoadMoreThreshold) LoadMore();

        if (target >= _results.Count)
            target = delta == 1 ? 0 : _results.Count - 1; // wrap from the bottom only on a single step
        else if (target < 0)
            target = delta == -1 ? _results.Count - 1 : 0;

        // Section headings ("Recent", "Pinned") can't be highlighted: step over them, wrapping at the ends.
        int step = delta >= 0 ? 1 : -1;
        for (int tries = 0; tries < _results.Count && _results[target].IsHeader; tries++)
            target = ((target + step) % _results.Count + _results.Count) % _results.Count;
        if (_results[target].IsHeader) return; // only headings (shouldn't happen)

        ResultsList.SelectedIndex = target;
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    /// <param name="reveal">Ctrl+Enter: show the file or folder in File Explorer instead of opening it.</param>
    private void OpenSelected(bool reveal = false)
    {
        if (ResultsList.SelectedItem is not SearchResult result || result.IsHeader) return;

        // Action rows ("Show more") run in place: the bar stays open and nothing is recorded.
        if (result.IsCommand)
        {
            result.Execute();
            return;
        }

        Action action = reveal && result.Reveal is not null ? result.Reveal : result.Execute;
        if (!IsPinned) HideBar();
        _usage.Record(result.Id);
        _recent.Add(result);
        try
        {
            action();
            Log.Write($"{(action == result.Execute ? "Opened" : "Revealed")} {result.Title} ({result.Id})");
        }
        catch (Exception ex)
        {
            Log.Write($"Could not open {result.Title}: {ex.Message}");
        }
    }

    // ---------- Mouse ----------

    private void ResultsList_MouseMove(object sender, MouseEventArgs e)
    {
        // WPF also raises MouseMove when the list changes or scrolls under a still cursor; only follow
        // real movement, otherwise a resting mouse would steal the highlight from the keyboard.
        Point position = e.GetPosition(this);
        bool moved = _lastMousePosition is { } last && (Math.Abs(position.X - last.X) > 1 || Math.Abs(position.Y - last.Y) > 1);
        _lastMousePosition = position;
        if (!moved) return;

        if (ItemFromEvent(e) is SearchResult { IsHeader: false } item)
        {
            ResultsList.SelectedItem = item;
            _userMovedSelection = true;
        }
    }

    private void ResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemFromEvent(e) is not SearchResult { IsHeader: false } item) return; // e.g. the scrollbar, a heading
        ResultsList.SelectedItem = item;
        e.Handled = true;

        // The pin on a row pins or unpins it.
        if (HasTag(e.OriginalSource, "TogglePin"))
        {
            TogglePin(item);
            SearchBox.Focus();
            return;
        }

        // The ✕ on a recent row removes it from the Recent list.
        if (item is SearchResult { IsRecent: true } recent && HasTag(e.OriginalSource, "RemoveRecent"))
        {
            RemoveRecent(recent, keepHighlight: false);
            SearchBox.Focus();
            return;
        }

        // The › on a folder row looks inside it; anywhere else on the row opens the item.
        if (item is SearchResult { IsFolder: true } folder && HasTag(e.OriginalSource, "EnterFolder"))
        {
            EnterFolder(folder);
            SearchBox.Focus();
            return;
        }
        OpenSelected();
    }

    private static bool HasTag(object source, string tag)
    {
        for (var d = source as DependencyObject; d is not null;
             d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement { Tag: string t } && t == tag) return true;
            if (d is ListBoxItem) return false;
        }
        return false;
    }

    private object? ItemFromEvent(RoutedEventArgs e) =>
        e.OriginalSource is DependencyObject source
        && ItemsControl.ContainerFromElement(ResultsList, source) is ListBoxItem container
            ? container.DataContext
            : null;

    // ---------- Keyboard ----------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                // Inside a folder: back out to your search results first.
                // Pinned: Esc clears the search instead of hiding (unpin, or Alt+Space, to hide).
                if (InFolder)
                {
                    while (_folderStack.Count > 1)
                    {
                        _folderStack.Pop();
                        if (_scrollStack.Count > 1) _scrollStack.Pop(); // keep the search's own scroll position
                    }
                    LeaveFolder();
                }
                else if (IsPinned) SearchBox.Clear();
                else HideBar();
                break;

            // Right on a highlighted folder (with the cursor at the end of the text): look inside it.
            case Key.Right when ResultsList.SelectedItem is SearchResult { IsFolder: true } folder
                                && SearchBox.SelectionLength == 0 && SearchBox.CaretIndex == SearchBox.Text.Length:
                EnterFolder(folder);
                break;

            // Ctrl+P: pin or unpin the highlighted result.
            case Key.P when Keyboard.Modifiers == ModifierKeys.Control:
                if (ResultsList.SelectedItem is SearchResult toPin) TogglePin(toPin);
                break;

            // Delete on a recent item (empty bar): remove it from the recent list.
            case Key.Delete when ShowingRecent && ResultsList.SelectedItem is not null:
                RemoveSelectedRecent();
                break;

            // Left (cursor at the start of the text), or Backspace with no text: back out one folder.
            case Key.Left when InFolder && SearchBox.SelectionLength == 0 && SearchBox.CaretIndex == 0:
            case Key.Back when InFolder && SearchBox.Text.Length == 0:
                LeaveFolder();
                break;
            case Key.Down:
                MoveSelection(+1);
                break;
            case Key.Up:
                MoveSelection(-1);
                break;
            case Key.PageDown:
                MoveSelection(+VisibleRows);
                break;
            case Key.PageUp:
                MoveSelection(-VisibleRows);
                break;
            case Key.Enter:
                OpenSelected(reveal: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                break;
            default:
                base.OnPreviewKeyDown(e);
                return;
        }
        e.Handled = true;
    }

    // ---------- Window behavior ----------

    /// <summary>
    /// Centers the bar horizontally on the monitor under the mouse, with its top edge 18.6% down.
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
