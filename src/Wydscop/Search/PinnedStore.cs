using System.IO;
using System.Text.Json;
using Wydscop.Services;

namespace Wydscop.Search;

/// <summary>An app, file or folder you pinned to the empty bar.</summary>
internal sealed record PinnedItem(string Id, ResultKind Kind, string Title, DateTime PinnedUtc);

/// <summary>
/// Results you pinned (pin on a row, or Ctrl+P). Shown under "Pinned" when the bar is empty, in the order
/// you pinned them. Stored at %LocalAppData%\Wydscop\pinned.json.
/// </summary>
internal sealed class PinnedStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wydscop", "pinned.json");

    private readonly List<PinnedItem> _items;
    private readonly HashSet<string> _ids;

    private PinnedStore(List<PinnedItem> items)
    {
        _items = items;
        _ids = new HashSet<string>(items.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<PinnedItem> Items => _items;

    public bool Contains(string id) => _ids.Contains(id);

    public static PinnedStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var items = JsonSerializer.Deserialize<List<PinnedItem>>(File.ReadAllText(FilePath));
                if (items is not null) return new PinnedStore(items);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Could not read pinned.json: " + ex.Message);
        }
        return new PinnedStore(new List<PinnedItem>());
    }

    /// <summary>Pins it (added at the end) or unpins it. Returns true when it's now pinned.</summary>
    public bool Toggle(SearchResult result)
    {
        bool pinned;
        if (_ids.Remove(result.Id))
        {
            _items.RemoveAll(i => string.Equals(i.Id, result.Id, StringComparison.OrdinalIgnoreCase));
            pinned = false;
        }
        else
        {
            _ids.Add(result.Id);
            _items.Add(new PinnedItem(result.Id, result.Kind, result.Title, DateTime.UtcNow));
            pinned = true;
        }
        Save();
        return pinned;
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
            Log.Write("Could not save pinned.json: " + ex.Message);
        }
    }
}
