using Wydscop.Search;
using Wydscop.Services;

namespace Wydscop;

public partial class SearchWindow
{
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
            var heading = SearchResult.Heading("Recent");
            heading.HeaderAction = ClearAllText;
            _ordered.Add(heading);
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

    private const string ClearAllText = "Clear all";

    /// <summary>"Clear all" on the Recent heading: empties the Recent list in one click. Pins stay.</summary>
    private void ClearAllRecentClicked()
    {
        Log.Write("Cleared all recent items from the bar");
        ClearRecent();
    }

    public void ClearRecent()
    {
        _recent.Clear();
        if (IsVisible) RunSearch();
    }

    private bool ShowingRecent => !InFolder && SearchBox.Text.Trim().Length == 0;
}
