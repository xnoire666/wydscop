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

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wydscop", "recent.json");

    private List<RecentItem> _items;

    private RecentStore(List<RecentItem> items) => _items = items;

    public IReadOnlyList<RecentItem> Items => _items;

    public static RecentStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var items = JsonSerializer.Deserialize<List<RecentItem>>(File.ReadAllText(FilePath));
                if (items is not null) return new RecentStore(items);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Could not read recent.json: " + ex.Message);
        }
        return new RecentStore(new List<RecentItem>());
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
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items));
        }
        catch (Exception ex)
        {
            Log.Write("Could not save recent.json: " + ex.Message);
        }
    }
}
