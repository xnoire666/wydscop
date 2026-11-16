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
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wydscop", "usage.json");

    private readonly Dictionary<string, int> _counts;

    private UsageStore(Dictionary<string, int> counts) => _counts = counts;

    public static UsageStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var counts = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(FilePath));
                if (counts is not null)
                    return new UsageStore(new Dictionary<string, int>(counts, StringComparer.OrdinalIgnoreCase));
            }
        }
        catch (Exception ex)
        {
            Log.Write("Could not read usage.json: " + ex.Message);
        }
        return new UsageStore(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Extra score: grows quickly for the first few launches, then levels off (max 200).</summary>
    public double Boost(string id) =>
        _counts.TryGetValue(id, out int count) ? Math.Min(200, 60 * Math.Log2(1 + count)) : 0;

    public void Record(string id)
    {
        _counts[id] = _counts.TryGetValue(id, out int count) ? count + 1 : 1;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_counts));
        }
        catch (Exception ex)
        {
            Log.Write("Could not save usage.json: " + ex.Message);
        }
    }
}
