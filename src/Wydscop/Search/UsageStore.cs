using System.IO;
using System.Text.Json;
using Wydscop.Services;

namespace Wydscop.Search;

/// <summary>
/// Remembers how often each result was opened, so things you use often rank higher.
/// Stored at %LocalAppData%\Wydscop\usage.json.
/// </summary>
internal sealed class UsageStore
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wydscop", "usage.json");

    private readonly Dictionary<string, int> _counts;

    private UsageStore(Dictionary<string, int> counts, string path)
    {
        _counts = counts;
        _path = path;
    }

    /// <summary>The file this store reads and saves.</summary>
    private readonly string _path;

    /// <param name="path">Where to keep the file; tests pass a temporary one. Defaults to usage.json in %LocalAppData%\Wydscop.</param>
    public static UsageStore Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var counts = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path));
                if (counts is not null)
                    return new UsageStore(new Dictionary<string, int>(counts, StringComparer.OrdinalIgnoreCase), path);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Could not read usage.json: " + ex.Message);
        }
        return new UsageStore(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), path);
    }

    /// <summary>Extra score: grows quickly for the first few launches, then levels off (max 200).</summary>
    public double Boost(string id) =>
        _counts.TryGetValue(id, out int count) ? Math.Min(200, 60 * Math.Log2(1 + count)) : 0;

    public void Record(string id)
    {
        _counts[id] = _counts.TryGetValue(id, out int count) ? count + 1 : 1;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_counts));
        }
        catch (Exception ex)
        {
            Log.Write("Could not save usage.json: " + ex.Message);
        }
    }
}
