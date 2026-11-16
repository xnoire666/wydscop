using System.Windows;
using Wydscop.Search;
using Wydscop.Services;

namespace Wydscop;

public partial class SearchWindow
{
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

    /// <summary>Scroll position (in rows) and loaded row count of each level, saved when you step into a folder.</summary>
    private readonly Stack<(double Offset, int Shown)> _scrollStack = new();

    /// <summary>Saved scroll position to restore once the level you stepped back to is shown.</summary>
    private (double Offset, int Shown)? _pendingScroll;

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
}
