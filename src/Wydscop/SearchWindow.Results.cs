using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Wydscop.Search;
using Wydscop.Services;

namespace Wydscop;

public partial class SearchWindow
{
    // ---------- The results list: paging, highlight, opening, mouse ----------

    /// <summary>Rows visible without scrolling (the list's MaxHeight in XAML fits exactly this many).</summary>
    private const int VisibleRows = 10;

    /// <summary>Rows added to the list at a time; more are added as you scroll near the end.</summary>
    private const int PageSize = 20;

    /// <summary>Start loading the next page when the highlight or scroll position is this close to the end.</summary>
    private const int LoadMoreThreshold = 3;

    /// <summary>Section heading row height (matches the XAML trigger).</summary>
    private const double HeadingHeight = 30;

    /// <summary>Result row height and the list's normal max height (10 rows + padding), matching the XAML.</summary>
    private const double RowHeight = 44;
    private const double ListMaxHeight = VisibleRows * RowHeight + 12;

    /// <summary>What the list shows: a growing prefix of <see cref="_ordered"/>.</summary>
    private readonly ObservableCollection<SearchResult> _results = new();

    /// <summary>Every result for the current query, best first.</summary>
    private List<SearchResult> _ordered = new();

    private bool _userMovedSelection;
    private Point? _lastMousePosition;

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
        SyncMarks();

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

    // ---------- Lazy loading ----------

    /// <summary>Adds the next page to the list; when everything we have is showing, asks the index for more files.</summary>
    private void LoadMore()
    {
        if (_results.Count < _ordered.Count)
        {
            int end = Math.Min(_ordered.Count, _results.Count + PageSize);
            for (int i = _results.Count; i < end; i++) _results.Add(_ordered[i]);
            SyncMarks();
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

    /// <summary>
    /// Opens the highlighted result, or, when some are marked with Ctrl+click, all the marked ones.
    /// </summary>
    /// <param name="reveal">Ctrl+Enter: show the file or folder in File Explorer instead of opening it.</param>
    private void OpenSelected(bool reveal = false)
    {
        if (ResultsList.SelectedItem is SearchResult { IsCommand: true } command)
        {
            // Action rows ("Show more") run in place: the bar stays open and nothing is recorded.
            command.Execute();
            return;
        }

        if (_marked.Count > 0)
        {
            OpenResults(_marked.ToList(), reveal);
            return;
        }

        if (ResultsList.SelectedItem is not SearchResult result || result.IsHeader) return;
        OpenResults([result], reveal);
    }

    private void OpenResults(IReadOnlyList<SearchResult> results, bool reveal)
    {
        if (!IsPinned) HideBar();
        ClearMarks();

        // Opened in the order you marked them; the last one ends up on top of Recent.
        foreach (var result in results)
        {
            Action action = reveal && result.Reveal is not null ? result.Reveal : result.Execute;
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
    }

    // ---------- Marking several results (Ctrl+click / Ctrl+Space), opened together with Enter ----------

    /// <summary>Marked results in the order you marked them. Kept while you type, so you can mark from several searches.</summary>
    private readonly List<SearchResult> _marked = new();

    /// <summary>Ctrl+click or Ctrl+Space: mark the row, or unmark it. Apps, files and folders only.</summary>
    private void ToggleMark(SearchResult result)
    {
        if (!result.CanPin) return; // headings, "Show more"
        int index = _marked.FindIndex(m => string.Equals(m.Id, result.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) _marked.RemoveAt(index);
        else _marked.Add(result);
        SyncMarks();
    }

    private void ClearMarks()
    {
        if (_marked.Count == 0) return;
        foreach (var m in _marked) m.IsMarked = false;
        _marked.Clear();
        SyncMarks();
    }

    /// <summary>
    /// Rows are rebuilt for every search (a file found again is a new object), so marks are matched by id.
    /// </summary>
    private void SyncMarks()
    {
        foreach (var r in _results)
            r.IsMarked = _marked.Count > 0 && _marked.Exists(m => string.Equals(m.Id, r.Id, StringComparison.OrdinalIgnoreCase));
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

    /// <summary>A heading with "Clear all" takes clicks, but the press must not highlight the heading itself.</summary>
    private void ResultsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemFromEvent(e) is SearchResult { IsHeader: true }) e.Handled = true;
    }

    private void ResultsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // "Clear all" on the Recent heading.
        if (ItemFromEvent(e) is SearchResult { IsHeader: true })
        {
            e.Handled = true;
            if (HasTag(e.OriginalSource, "HeaderAction")) ClearAllRecentClicked();
            SearchBox.Focus();
            return;
        }

        if (ItemFromEvent(e) is not SearchResult { IsHeader: false } item) return; // e.g. the scrollbar
        ResultsList.SelectedItem = item;
        e.Handled = true;

        // Ctrl+click marks the row (or unmarks it) instead of opening it; Enter then opens all marked rows.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && item.CanPin
            && !HasTag(e.OriginalSource, "TogglePin") && !HasTag(e.OriginalSource, "RemoveRecent"))
        {
            ToggleMark(item);
            _userMovedSelection = true;
            SearchBox.Focus();
            return;
        }

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

        // A plain click on a marked row opens all marked rows; on any other row it opens just that one.
        if (_marked.Count > 0 && !item.IsMarked) ClearMarks();
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
}
