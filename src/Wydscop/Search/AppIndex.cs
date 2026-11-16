using System.Diagnostics;
using Wydscop.Services;

namespace Wydscop.Search;

/// <summary>
/// Every app in the Start menu "All apps" list (desktop and Store apps), read from shell:AppsFolder.
/// Refreshed on a background STA thread; icons are filled in after the names are available.
/// </summary>
internal sealed class AppIndex
{
    private const int IconSizePx = 64;

    private volatile IReadOnlyList<SearchResult> _items = Array.Empty<SearchResult>();
    private int _refreshing;

    public IReadOnlyList<SearchResult> Items => _items;

    public DateTime LastRefresh { get; private set; } = DateTime.MinValue;

    /// <summary>Raised (on a background thread) when the app list changes.</summary>
    public event EventHandler? Updated;

    public void RefreshInBackground()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;

        var thread = new Thread(Refresh) { IsBackground = true, Name = "Wydscop app index" };
        thread.SetApartmentState(ApartmentState.STA); // Shell COM objects want STA
        thread.Start();
    }

    private void Refresh()
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var items = Enumerate();

            int dated = items.Count(i => i.Date is not null); // dates are read in Enumerate

            // Keep icons we already have so a refresh doesn't make them blink.
            var previous = _items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
                if (previous.TryGetValue(item.Id, out var old) && old.Icon is not null)
                    item.Icon = old.Icon;

            _items = items;
            LastRefresh = DateTime.Now;
            Log.Write($"Indexed {items.Count} apps ({dated} with a date) in {sw.ElapsedMilliseconds} ms");
            Updated?.Invoke(this, EventArgs.Empty);

            int loaded = 0;
            foreach (var item in items)
            {
                if (item.Icon is not null) continue;
                item.Icon = ShellIcons.Get(@"shell:AppsFolder\" + item.Id, IconSizePx);
                if (item.Icon is not null) loaded++;
            }
            Log.Write($"Loaded {loaded} app icons in {sw.ElapsedMilliseconds} ms total");
        }
        catch (Exception ex)
        {
            Log.Write("App index failed: " + ex);
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    private static List<SearchResult> Enumerate()
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application")
            ?? throw new InvalidOperationException("Shell.Application is not available.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic folder = shell.NameSpace("shell:AppsFolder");

        var list = new List<SearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (dynamic item in folder.Items())
        {
            string name = item.Name;
            string id = item.Path; // parsing name: AUMID for Store apps, path or known-folder path for desktop apps
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                continue;

            var result = new SearchResult(id, ResultKind.App, name, "Application", () => Launch(id));

            // Date: from the program the entry starts (desktop apps) or the installed package (Store apps).
            string? target = null;
            try
            {
                target = item.ExtendedProperty("System.Link.TargetParsingPath") as string;
            }
            catch
            {
            }
            SetAppDates(result, id, target);

            list.Add(result);
        }
        return list;
    }

    /// <summary>
    /// The app's modified date (created if it has none): its program file, e.g. chrome.exe, for desktop apps,
    /// or its install folder for Store apps (found through the package's family name in the AUMID).
    /// </summary>
    private static void SetAppDates(SearchResult result, string id, string? target)
    {
        try
        {
            string? path = !string.IsNullOrEmpty(target) ? Environment.ExpandEnvironmentVariables(target)
                : System.IO.Path.IsPathFullyQualified(id) ? id
                : id.Contains('!') ? StorePackageFolder(id[..id.IndexOf('!')])
                : null;
            if (path is null) return;

            if (System.IO.File.Exists(path))
                result.SetDates(System.IO.File.GetLastWriteTimeUtc(path), System.IO.File.GetCreationTimeUtc(path));
            else if (System.IO.Directory.Exists(path))
                result.SetDates(System.IO.Directory.GetLastWriteTimeUtc(path), System.IO.Directory.GetCreationTimeUtc(path));
        }
        catch
        {
            // No date for this app; it's just shown without one.
        }
    }

    /// <summary>Install folder of the newest installed package of a Store app family, from the app model registry.</summary>
    private static string? StorePackageFolder(string familyName)
    {
        const string root = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository";
        using var family = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"{root}\Families\{familyName}");
        if (family is null) return null;

        foreach (string fullName in family.GetSubKeyNames().OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase))
        {
            using var package = Microsoft.Win32.Registry.CurrentUser.OpenSubKey($@"{root}\Packages\{fullName}");
            if (package?.GetValue("PackageRootFolder") is string folder && System.IO.Directory.Exists(folder))
                return folder;
        }
        return null;
    }

    private static void Launch(string id)
    {
        Process.Start(new ProcessStartInfo(@"shell:AppsFolder\" + id) { UseShellExecute = true });
    }
}
