using System.IO;
using System.Text.Json;
using Wydscop.Services;

namespace Wydscop.Search;

/// <summary>Something you opened from Wydscop (an app, file or folder).</summary>
internal sealed record RecentItem(string Id, ResultKind Kind, string Title, DateTime OpenedUtc);

/// <summary>
/// The last <see cref="MaxItems"/> things you opened, newest first; shown when the bar is empty.
/// Stored at %LocalAppData%\Wydscop\recent.json.
/// </summary>
internal sealed class RecentStore
{
    public const int MaxItems = 50;

    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wydscop", "recent.json");

    private List<RecentItem> _items;

    private RecentStore(List<RecentItem> items, string path)
    {
        _items = items;
        _path = path;
    }

    public IReadOnlyList<RecentItem> Items => _items;

    /// <summary>The file this store reads and saves.</summary>
    private readonly string _path;

    /// <param name="path">Where to keep the file; tests pass a temporary one. Defaults to recent.json in %LocalAppData%\Wydscop.</param>
    public static RecentStore Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var items = JsonSerializer.Deserialize<List<RecentItem>>(File.ReadAllText(path));
                if (items is not null) return new RecentStore(items, path);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Could not read recent.json: " + ex.Message);
        }
        return new RecentStore(new List<RecentItem>(), path);
    }

    /// <summary>Moves (or adds) this item to the top.</summary>
    public void Add(SearchResult result)
    {
        _items.RemoveAll(i => string.Equals(i.Id, result.Id, StringComparison.OrdinalIgnoreCase));
        _items.Insert(0, new RecentItem(result.Id, result.Kind, result.Title, DateTime.UtcNow));
        if (_items.Count > MaxItems) _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        Save();
    }

    public void Remove(string id)
    {
        if (_items.RemoveAll(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase)) > 0) Save();
    }

    public void Clear()
    {
        _items = new List<RecentItem>();
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_items));
        }
        catch (Exception ex)
        {
            Log.Write("Could not save recent.json: " + ex.Message);
        }
    }
}
