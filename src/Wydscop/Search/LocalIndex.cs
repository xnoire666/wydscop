using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Text.RegularExpressions;
using Wydscop.Services;
using Microsoft.Win32;

namespace Wydscop.Search;

/// <summary>
/// Wydscop's own file and folder name index for everything the Windows Search index does NOT cover
/// (for example C:\xampp, other drives). Together with <see cref="FileSearch"/> this covers all fixed drives.
///
/// - Built by scanning in the background (low priority) a few seconds after startup, then every hour.
/// - Kept current between scans with FileSystemWatcher (new, deleted and renamed items show up right away).
/// - Names only, stored compactly in memory: roughly 50–100 bytes per item.
/// </summary>
internal sealed partial class LocalIndex
{
    private static readonly TimeSpan FirstScanDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RescanInterval = TimeSpan.FromHours(1);

    /// <summary>After a folder is deleted or renamed, rescan this soon so its contents are corrected.</summary>
    private static readonly TimeSpan RescanAfterChange = TimeSpan.FromMinutes(2);

    /// <summary>Safety cap so a huge disk can't use unbounded memory.</summary>
    private const int MaxItems = 5_000_000;

    private static readonly EnumerationOptions ListOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint, // junctions/symlinks: avoid loops and duplicates
    };

    private volatile Snapshot _snapshot = Snapshot.Empty;

    // Changes seen by the watchers since the last scan (value = when, to drop them once a newer scan covers them).
    private readonly ConcurrentDictionary<string, (FileHit Hit, long Stamp)> _added = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _removed = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Timer _rescanTimer;
    private int _scanning;
    private volatile bool _changedDuringScan;

    private string[] _roots = [];

    /// <summary>What the Windows index already covers; skipped here, except the folders it excludes.</summary>
    private WindowsIndexScope _windowsScope = WindowsIndexScope.None;

    public LocalIndex()
    {
        _rescanTimer = new Timer(_ => StartScanThread(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public int Count => _snapshot.Count;

    public void Start()
    {
        _roots = DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => d.RootDirectory.FullName)
            .ToArray();
        _windowsScope = ReadWindowsIndexScope();

        Log.Write($"Local index roots: {string.Join(", ", _roots)}; skipping Windows-indexed: {string.Join(", ", _windowsScope.IncludedFolders)}" +
                  $"; but indexing what Windows excludes there: {string.Join(", ", _windowsScope.Gaps)}");

        foreach (var root in _roots) StartWatcher(root);
        _rescanTimer.Change(FirstScanDelay, Timeout.InfiniteTimeSpan);
    }

    // ---------- Search ----------

    /// <summary>Best <paramref name="top"/> name matches, best first.</summary>
    public FileBatch Search(string query, int top, CancellationToken cancel)
    {
        query = query.Trim();
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return FileBatch.Empty;

        // Very short queries match almost everything by "contains"; require a word start instead.
        double minScore = query.Length <= 2 ? 700 : 1;

        var sw = Stopwatch.StartNew();
        var snap = _snapshot;
        bool checkRemoved = !_removed.IsEmpty;
        var best = new PriorityQueue<int, double>(); // min-heap: lowest score on top, dropped first
        var hits = new List<(FileHit Hit, double Score)>();
        int matched = 0;

        // Pass 1: normal matching (fast: names must contain every typed word).
        // Pass 2, only if pass 1 found nothing: allow typos ("chorme"). Slower, since every name is scored.
        bool typos = false;
        for (int pass = 0; pass < 2 && matched == 0; pass++)
        {
            typos = pass == 1;
            if (typos && query.Length < 4) break;

            for (int i = 0; i < snap.Count; i++)
            {
                if ((i & 0xFFF) == 0) cancel.ThrowIfCancellationRequested();

                string name = snap.Names[i];
                if (!typos && !ContainsAll(name, tokens)) continue;

                double score = Matcher.Score(name, query, allowTypos: typos);
                if (score < (typos ? 1 : minScore)) continue;
                if (checkRemoved && IsRemoved(snap.FullPath(i))) continue;

                matched++;
                // Equal matches: keep the newest (the tiny fraction from the date never outweighs a better match).
                best.Enqueue(i, score + snap.Modified[i] / 1e19);
                if (best.Count > top) best.Dequeue();
            }

            // Items created since the last scan.
            foreach (var (hit, _) in _added.Values)
            {
                if (!typos && !ContainsAll(hit.Name, tokens)) continue;
                double score = Matcher.Score(hit.Name, query, allowTypos: typos);
                if (score >= (typos ? 1 : minScore)) { hits.Add((hit, score)); matched++; }
            }
        }

        while (best.TryDequeue(out int i, out double score))
            hits.Add((new FileHit(snap.FullPath(i), snap.Names[i], snap.IsDir[i], new DateTime(snap.Modified[i], DateTimeKind.Utc)), score));

        var result = hits
            .OrderByDescending(h => h.Score)
            .Select(h => h.Hit)
            .DistinctBy(h => h.Path, StringComparer.OrdinalIgnoreCase)
            .Take(top)
            .ToList();

        Log.Write($"Local search \"{query}\" (top {top}){(typos ? " with typos" : "")}: {result.Count} of {matched} matches in {snap.Count:N0} items, {sw.ElapsedMilliseconds} ms");
        return new FileBatch(result, MayHaveMore: matched > top);
    }

    private static bool ContainsAll(string name, string[] tokens)
    {
        foreach (var token in tokens)
            if (name.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) return false;
        return true;
    }

    /// <summary>True when the item, or a folder it is in, was deleted since the last scan.</summary>
    private bool IsRemoved(string path)
    {
        for (string? p = path; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if (_removed.ContainsKey(p)) return true;
        return false;
    }

    // ---------- Scanning ----------

    private void StartScanThread()
    {
        if (Interlocked.Exchange(ref _scanning, 1) == 1) return;
        var thread = new Thread(Scan) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Wydscop local index" };
        thread.Start();
    }

    private void Scan()
    {
        long scanStart = Stopwatch.GetTimestamp();
        _changedDuringScan = false;
        try
        {
            var sw = Stopwatch.StartNew();
            var builder = new Builder();
            foreach (var root in _roots) ScanRoot(root, builder);

            _snapshot = builder.ToSnapshot();

            // The new snapshot already reflects changes that happened before the scan started.
            foreach (var (path, value) in _added)
                if (value.Stamp < scanStart) _added.TryRemove(path, out _);
            foreach (var (path, stamp) in _removed)
                if (stamp < scanStart) _removed.TryRemove(path, out _);

            long memoryMb = GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024);
            Log.Write($"Local index: {_snapshot.Count:N0} items in {_snapshot.Dirs.Length:N0} folders, {sw.ElapsedMilliseconds:N0} ms (process managed memory {memoryMb} MB)");
        }
        catch (Exception ex)
        {
            Log.Write("Local index scan failed: " + ex);
        }
        finally
        {
            Volatile.Write(ref _scanning, 0);
            _rescanTimer.Change(_changedDuringScan ? RescanAfterChange : RescanInterval, Timeout.InfiniteTimeSpan);
        }
    }

    private void ScanRoot(string root, Builder builder)
    {
        var stack = new Stack<int>();
        stack.Push(builder.AddDir(root));

        while (stack.Count > 0 && builder.Count < MaxItems)
        {
            int dirId = stack.Pop();
            string dirPath = builder.Dirs[dirId];
            bool atDriveRoot = dirId >= 0 && dirPath.Length <= 3; // "C:\"
            string parentName = Path.GetFileName(dirPath.TrimEnd('\\'));

            // A folder Windows indexes is only walked through to reach a part it excludes; its own items are left to Windows.
            bool dirCovered = _windowsScope.Covers(dirPath);

            try
            {
                var entries = new FileSystemEnumerable<(string Name, bool IsDir, long Ticks, FileAttributes Attributes)>(
                    dirPath,
                    (ref FileSystemEntry e) => (e.FileName.ToString(), e.IsDirectory, e.LastWriteTimeUtc.UtcTicks, e.Attributes),
                    ListOptions);

                foreach (var (name, isDir, ticks, attributes) in entries)
                {
                    if ((attributes & FileAttributes.Hidden) != 0) continue; // like File Explorer's default

                    if (isDir)
                    {
                        string full = Path.Join(dirPath, name);
                        if (IsNoiseFolder(name, parentName, atDriveRoot)) continue;
                        if (!_windowsScope.Covers(full))
                        {
                            builder.AddItem(name, dirId, isDir: true, ticks);
                            stack.Push(builder.AddDir(full));
                        }
                        else if (_windowsScope.HasGapsInside(full))
                        {
                            stack.Push(builder.AddDir(full)); // walk through it without listing it
                        }
                    }
                    else if (!dirCovered && !SearchFilters.IsExcludedFileName(name))
                    {
                        builder.AddItem(name, dirId, isDir: false, ticks);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Folder vanished or is locked; skip it.
            }
        }
    }

    private static bool IsNoiseFolder(string name, string parentName, bool atDriveRoot) =>
        SearchFilters.NoiseFolderNames.Contains(name)
        || (atDriveRoot && SearchFilters.DriveRootFolderNames.Contains(name))
        || SearchFilters.IsBuildOutput(name, parentName);

    // ---------- Live updates ----------

    private void StartWatcher(string root)
    {
        try
        {
            var watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Created += (_, e) => OnCreated(e.FullPath);
            watcher.Deleted += (_, e) => OnDeleted(e.FullPath);
            watcher.Renamed += (_, e) =>
            {
                OnDeleted(e.OldFullPath);
                OnCreated(e.FullPath);
            };
            watcher.Error += (_, e) =>
            {
                // Too many changes at once (buffer overflow): just rescan.
                Log.Write($"Watcher error on {root}: {e.GetException().Message}; rescanning soon");
                RequestRescan();
            };
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
        catch (Exception ex)
        {
            Log.Write($"Could not watch {root}: {ex.Message}");
        }
    }

    private bool IsOutOfScope(string path) =>
        SearchFilters.IsNoisePath(path) || _windowsScope.Covers(path);

    private void OnCreated(string path)
    {
        try
        {
            if (IsOutOfScope(path)) return;
            string name = Path.GetFileName(path);
            bool isDir = Directory.Exists(path);
            if (!isDir && SearchFilters.IsExcludedFileName(name)) return;

            var hit = new FileHit(path, name, isDir, DateTime.UtcNow);
            _added[path] = (hit, Stopwatch.GetTimestamp());
            _removed.TryRemove(path, out _);
        }
        catch
        {
            // Watcher events must never crash the app.
        }
    }

    private void OnDeleted(string path)
    {
        try
        {
            if (IsOutOfScope(path)) return;
            _removed[path] = Stopwatch.GetTimestamp();
            bool wasAdded = _added.TryRemove(path, out _);

            // If it was a folder, its contents changed too; a rescan fixes them up.
            if (!wasAdded && string.IsNullOrEmpty(Path.GetExtension(path))) RequestRescan();
        }
        catch
        {
        }
    }

    private void RequestRescan()
    {
        if (Volatile.Read(ref _scanning) == 1)
            _changedDuringScan = true;
        else
            _rescanTimer.Change(RescanAfterChange, Timeout.InfiniteTimeSpan);
    }

    // ---------- Which folders Windows already indexes ----------

    /// <summary>
    /// Reads the Windows Search rules: "include" folders (e.g. C:\Users\) so we don't index them twice, and
    /// "exclude" folders inside them (e.g. a code folder removed in Indexing Options) so we index those ourselves.
    /// If Windows Search isn't running, nothing is covered by it and we index everything ourselves.
    /// </summary>
    private static WindowsIndexScope ReadWindowsIndexScope()
    {
        try
        {
            if (!FileSearch.IsAvailable()) return WindowsIndexScope.None;

            using var rules = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows Search\CrawlScopeManager\Windows\SystemIndex\WorkingSetRules");
            if (rules is null) return WindowsIndexScope.None;

            var list = new List<WindowsIndexScope.Rule>();
            foreach (var name in rules.GetSubKeyNames())
            {
                using var rule = rules.OpenSubKey(name);
                if (rule?.GetValue("URL") is not string url || !url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) continue;
                if (rule.GetValue("Include") is not int include) continue;

                // "file:///C:\[b4b9847c-...]\Users\" → "C:\Users\"
                string path = VolumeGuid().Replace(url["file:///".Length..], "");

                // Rules with wildcards are Windows' built-in junk lists (Users\*\AppData\, Users\me\.*\, temp folders).
                // Leaving them out keeps those folders counted as covered, so we skip them too.
                if (path.Contains('*')) continue;

                list.Add(new WindowsIndexScope.Rule(path, include == 1));
            }
            return new WindowsIndexScope(list);
        }
        catch (Exception ex)
        {
            Log.Write("Could not read Windows Search scopes: " + ex.Message);
            return WindowsIndexScope.None;
        }
    }

    [GeneratedRegex(@"\[[^\]]+\]\\")]
    private static partial Regex VolumeGuid();

    // ---------- Storage ----------

    /// <summary>Immutable index: item i is Names[i] inside folder Dirs[Parents[i]].</summary>
    private sealed class Snapshot(string[] dirs, string[] names, int[] parents, long[] modified, bool[] isDir)
    {
        public static readonly Snapshot Empty = new([], [], [], [], []);

        public string[] Dirs { get; } = dirs;
        public string[] Names { get; } = names;
        public int[] Parents { get; } = parents;
        public long[] Modified { get; } = modified;
        public bool[] IsDir { get; } = isDir;
        public int Count => Names.Length;

        public string FullPath(int i) => Path.Join(Dirs[Parents[i]], Names[i]);
    }

    private sealed class Builder
    {
        public readonly List<string> Dirs = new();
        private readonly List<string> _names = new();
        private readonly List<int> _parents = new();
        private readonly List<long> _modified = new();
        private readonly List<bool> _isDir = new();

        public int Count => _names.Count;

        public int AddDir(string fullPath)
        {
            Dirs.Add(fullPath);
            return Dirs.Count - 1;
        }

        public void AddItem(string name, int parent, bool isDir, long ticks)
        {
            _names.Add(name);
            _parents.Add(parent);
            _modified.Add(ticks);
            _isDir.Add(isDir);
        }

        public Snapshot ToSnapshot() =>
            new(Dirs.ToArray(), _names.ToArray(), _parents.ToArray(), _modified.ToArray(), _isDir.ToArray());
    }
}
