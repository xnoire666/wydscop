using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Wydscop.Search;
using Wydscop.Services;

namespace Wydscop;

public partial class SearchWindow
{
    // ---------- Search ----------

    /// <summary>Wait this long after the last keystroke before asking the Windows Search index.</summary>
    private static readonly TimeSpan FileSearchDelay = TimeSpan.FromMilliseconds(120);

    private static readonly string UserProfile =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\') + "\\";

    private readonly DispatcherTimer _fileTimer;
    private CancellationTokenSource? _fileCancel;
    private string _fileQuery = string.Empty;
    private List<SearchResult> _fileResults = new();
    private int _fileBatchSize = FileSearch.FirstBatchSize;
    private bool _fileMayHaveMore;
    private bool _loadingMoreFiles;

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
}
