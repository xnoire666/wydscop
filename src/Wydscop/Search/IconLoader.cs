using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using Wydscop.Services;

namespace Wydscop.Search;

/// <summary>
/// Loads shell icons on one background STA thread (shell icon APIs expect STA) and caches them.
/// Ordinary files share one icon per extension; folders, programs and shortcuts get their own.
/// </summary>
internal sealed class IconLoader
{
    private const int IconSizePx = 64;

    private static readonly HashSet<string> PerFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".ico", ".lnk", ".url", ".msc", ".appref-ms", ".scr", ".cpl",
    };

    private readonly BlockingCollection<SearchResult> _queue = new();
    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public IconLoader()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "Wydscop icons" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>Sets result.Icon now if cached, otherwise soon (the row updates by itself).</summary>
    public void Request(SearchResult result, string path, bool isFolder)
    {
        result.IconPath = path;
        result.IconKey = CacheKey(path, isFolder);
        if (_cache.TryGetValue(result.IconKey, out var cached))
            result.Icon = cached;
        else
            _queue.Add(result);
    }

    private void Run()
    {
        foreach (var result in _queue.GetConsumingEnumerable())
        {
            try
            {
                string key = result.IconKey!;
                if (!_cache.TryGetValue(key, out var icon))
                {
                    icon = ShellIcons.Get(result.IconPath!, IconSizePx);
                    _cache[key] = icon;
                }
                result.Icon = icon;
            }
            catch (Exception ex)
            {
                Log.Write("Icon failed: " + ex.Message);
            }
        }
    }

    private static string CacheKey(string path, bool isFolder)
    {
        if (isFolder) return path;
        string ext = Path.GetExtension(path);
        return ext.Length == 0 || PerFileExtensions.Contains(ext) ? path : "ext:" + ext;
    }
}
