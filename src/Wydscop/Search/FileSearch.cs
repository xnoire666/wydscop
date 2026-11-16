using System.Data.OleDb;
using System.Diagnostics;
using System.IO;
using Wydscop.Services;

namespace Wydscop.Search;

/// <summary>A file or folder found by the Windows Search index, Wydscop's own index, or a folder listing.</summary>
/// <param name="Modified">Last modified (UTC), or DateTime.MinValue when unknown.</param>
/// <param name="Created">Created (UTC), or DateTime.MinValue when unknown.</param>
/// <param name="Size">File size in bytes, or -1 when unknown (and for folders).</param>
internal sealed record FileHit(string Path, string Name, bool IsFolder, DateTime Modified, DateTime Created = default, long Size = -1);

/// <param name="MayHaveMore">True when the index returned as many rows as asked for, so a bigger request may find more.</param>
internal sealed record FileBatch(IReadOnlyList<FileHit> Hits, bool MayHaveMore)
{
    public static readonly FileBatch Empty = new([], false);
}

/// <summary>
/// Finds files and folders by name using the Windows Search index (the same index File Explorer uses),
/// so results come back in milliseconds without Wydscop keeping its own index.
/// Only covers locations Windows indexes (by default: your user folders, Start menu);
/// <see cref="LocalIndex"/> covers the rest.
/// </summary>
internal static class FileSearch
{
    private const string ConnectionString = "Provider=Search.CollatorDSO;Extended Properties='Application=Windows';";

    /// <summary>Rows asked for on the first search; more are fetched when you scroll (see NextBatchSize).</summary>
    public const int FirstBatchSize = 60;

    /// <summary>Hard ceiling so a one-letter search can't pull the whole index.</summary>
    public const int MaxBatchSize = 2000;

    private const int FileAttributeDirectory = 0x10;

    /// <summary>The index has no OFFSET, so "load more" re-asks with a bigger TOP (60 → 300 → 1500 → 2000).</summary>
    public static int NextBatchSize(int current) => Math.Min(current * 5, MaxBatchSize);

    /// <summary>True when the Windows Search service is running and its index can be queried.</summary>
    public static bool IsAvailable()
    {
        try
        {
            using var connection = new OleDbConnection(ConnectionString);
            connection.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static Task<FileBatch> SearchAsync(string query, int top, CancellationToken cancel) =>
        Task.Run(() => Search(query, top, cancel), cancel);

    private static FileBatch Search(string query, int top, CancellationToken cancel)
    {
        string? contains = BuildContains(query);
        if (contains is null) return FileBatch.Empty;

        string sql =
            $"SELECT TOP {top} System.ItemPathDisplay, System.FileName, System.FileAttributes, System.DateModified, System.DateCreated " +
            "FROM SystemIndex " +
            $"WHERE SCOPE='file:' AND CONTAINS(System.FileName, '{contains}') " +
            "ORDER BY System.Search.Rank DESC";

        var sw = Stopwatch.StartNew();
        var hits = new List<FileHit>();
        int rows = 0;
        try
        {
            using var connection = new OleDbConnection(ConnectionString);
            connection.Open();
            using var command = new OleDbCommand(sql, connection);
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                cancel.ThrowIfCancellationRequested();
                rows++;

                if (reader.GetValue(0) is not string path || reader.GetValue(1) is not string name) continue;
                if (SearchFilters.IsExcludedFileName(name) || SearchFilters.IsNoisePath(path)) continue;

                long attributes = reader.GetValue(2) is DBNull ? 0 : Convert.ToInt64(reader.GetValue(2));
                DateTime modified = reader.GetValue(3) is DateTime m ? DateTime.SpecifyKind(m, DateTimeKind.Utc) : DateTime.MinValue;
                DateTime created = reader.GetValue(4) is DateTime c ? DateTime.SpecifyKind(c, DateTimeKind.Utc) : DateTime.MinValue;
                hits.Add(new FileHit(path, name, (attributes & FileAttributeDirectory) != 0, modified, created));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Windows Search turned off, index rebuilding, etc. Apps still work.
            Log.Write("File search failed: " + ex.Message);
            return FileBatch.Empty;
        }

        Log.Write($"File search \"{query}\" (top {top}): {hits.Count} hits of {rows} rows in {sw.ElapsedMilliseconds} ms");
        return new FileBatch(hits, MayHaveMore: rows >= top && top < MaxBatchSize);
    }

    /// <summary>
    /// Turns "proj notes" into "proj*" AND "notes*" (each word must start a word in the file name).
    /// Returns null when nothing searchable is left.
    /// </summary>
    private static string? BuildContains(string query)
    {
        var words = query
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(c => c != '"' && c != '*' && c != '\'' && c != '(' && c != ')').ToArray()))
            .Where(w => w.Length > 0)
            .Take(5)
            .ToList();

        return words.Count == 0 ? null : string.Join(" AND ", words.Select(w => $"\"{w}*\""));
    }
}
