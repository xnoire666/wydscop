using System.IO;

namespace Wydscop.Search;

/// <summary>
/// Which folders the Windows Search index covers, from its include and exclude rules
/// (for example "C:\Users\" included but "C:\Users\me\code\" excluded in Indexing Options).
/// Wydscop's own index covers everything this says Windows does not.
/// </summary>
internal sealed class WindowsIndexScope
{
    /// <summary>One rule: a folder (ending with '\') and whether Windows indexes what is inside it.</summary>
    public readonly record struct Rule(string Folder, bool Include);

    public static readonly WindowsIndexScope None = new([]);

    // Longest folder first, so the first rule that matches is the most specific one, like Windows decides.
    private readonly Rule[] _rules;

    public WindowsIndexScope(IEnumerable<Rule> rules)
    {
        _rules = rules
            .Select(r => r with { Folder = WithSlash(r.Folder) })
            .DistinctBy(r => (r.Folder.ToUpperInvariant(), r.Include))
            .OrderByDescending(r => r.Folder.Length)
            .ThenBy(r => r.Include) // same folder both ways: exclude wins, so we index it ourselves
            .ToArray();
    }

    public IEnumerable<string> IncludedFolders => _rules.Where(r => r.Include).Select(r => r.Folder);
    /// <summary>Excluded folders inside an included one: the gaps Wydscop fills (others are never covered anyway).</summary>
    public IEnumerable<string> Gaps => _rules.Where(r => !r.Include && Covers(Path.GetDirectoryName(r.Folder.TrimEnd('\\')) ?? ""))
                                             .Select(r => r.Folder);

    /// <summary>True when Windows indexes this file or folder, so Wydscop doesn't need to.</summary>
    public bool Covers(string path)
    {
        string p = WithSlash(path);
        foreach (var rule in _rules)
            if (p.StartsWith(rule.Folder, StringComparison.OrdinalIgnoreCase)) return rule.Include;
        return false;
    }

    /// <summary>
    /// True when somewhere inside this folder Windows stops indexing (an exclude rule below it),
    /// so a scan has to walk through the folder to reach that part.
    /// </summary>
    public bool HasGapsInside(string folder)
    {
        string p = WithSlash(folder);
        foreach (var rule in _rules)
            if (!rule.Include && rule.Folder.Length > p.Length && rule.Folder.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string WithSlash(string path) => path.EndsWith('\\') ? path : path + "\\";
}
