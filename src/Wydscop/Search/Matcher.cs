namespace Wydscop.Search;

/// <summary>
/// Scores how well a name matches what the user typed. 0 means no match; higher is better.
/// Order: exact, starts with, a word starts with, initials ("vsc" → Visual Studio Code),
/// contains, loose in-order letters ("ntpd" → Notepad), and, only when asked for, typos
/// ("chorme" → Chrome).
/// </summary>
internal static class Matcher
{
    private const StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;

    /// <summary>Most letters a loose in-order match may skip in total ("ntpd" → Notepad skips 3).</summary>
    private const int MaxFuzzyGaps = 3;

    /// <summary>Shorter words get no typo tolerance: nearly everything is one letter away from them.</summary>
    private const int MinTypoLength = 4;

    /// <summary>Typo matches always score below this, under every other kind of match.</summary>
    private const double TypoMaxScore = 150;

    /// <param name="allowTypos">
    /// Also accept names a typo or two away. Callers use this as a fallback only when nothing matched
    /// normally, so near-misses never crowd out real matches.
    /// </param>
    public static double Score(string text, string query, bool allowTypos = false)
    {
        query = query.Trim();
        if (query.Length == 0 || text.Length == 0) return 0;

        double whole = ScoreOne(text, query, allowTypos);
        if (whole > 0) return whole;

        // "studio code": every word must match somewhere.
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return 0;

        double total = 0;
        foreach (var token in tokens)
        {
            double s = ScoreOne(text, token, allowTypos);
            if (s <= 0) return 0;
            total += s;
        }
        return Math.Max(1, total / tokens.Length - 50);
    }

    private static double ScoreOne(string text, string q, bool allowTypos)
    {
        if (text.Equals(q, IgnoreCase)) return 1000;
        if (text.StartsWith(q, IgnoreCase)) return 900;

        var words = SplitWords(text);
        for (int i = 0; i < words.Count; i++)
            if (words[i].StartsWith(q, IgnoreCase))
                return 800 - i * 10;

        if (q.Length >= 2 && words.Count > 0)
        {
            string initials = string.Concat(words.Select(w => w[0]));
            if (initials.StartsWith(q, IgnoreCase)) return 700;
        }

        int index = text.IndexOf(q, IgnoreCase);
        if (index >= 0) return 500 - index;

        double inOrder = InOrderScore(text, q);
        if (inOrder > 0) return inOrder;

        return allowTypos ? TypoScore(q, words) : 0;
    }

    /// <summary>All letters appear in order with only a few skipped ("ntpd" → NoTePaD).</summary>
    private static double InOrderScore(string text, string q)
    {
        if (q.Length < 3) return 0;

        int gaps = 0, last = -1, from = 0;
        foreach (char c in q)
        {
            int found = IndexOfIgnoreCase(text, c, from);
            if (found < 0) return 0;
            if (last >= 0) gaps += found - last - 1;
            last = found;
            from = found + 1;
        }

        // Letters scattered across the name ("read" in "REaltek AuDio") are noise, not a match.
        if (gaps > MaxFuzzyGaps) return 0;
        return Math.Max(1, 300 - gaps * 8);
    }

    /// <summary>
    /// The query is within 1 typo (words of 4–7 letters) or 2 typos (8+) of a word in the name, of the whole
    /// name without spaces ("powershel" → PowerShell), or of the start of a word ("notpa" → Notepad).
    /// A typo is a missing, extra or wrong letter, or two neighbors swapped.
    /// </summary>
    private static double TypoScore(string q, List<string> words)
    {
        if (q.Length < MinTypoLength || words.Count == 0) return 0;

        int maxTypos = q.Length <= 7 ? 1 : 2;
        string query = q.ToLowerInvariant();
        int best = int.MaxValue;

        var candidates = new List<string>(words.Count + 1);
        candidates.AddRange(words);
        if (words.Count > 1) candidates.Add(string.Concat(words));

        foreach (var candidate in candidates)
        {
            if (candidate.Length < MinTypoLength - 1) continue;
            string word = candidate.ToLowerInvariant();

            best = Math.Min(best, Distance(query, word, maxTypos));

            // Still typing a longer word: compare with its beginning ("notpa" vs "notep").
            foreach (int length in new[] { query.Length, query.Length + 1 })
                if (word.Length > length)
                    best = Math.Min(best, Distance(query, word[..length], maxTypos));

            if (best == 1 && maxTypos == 1) break; // can't do better than one typo here
        }

        if (best > maxTypos) return 0;
        return Math.Max(1, TypoMaxScore - best * 40);
    }

    /// <summary>
    /// Number of typos between two words (Damerau–Levenshtein, "optimal string alignment" variant):
    /// insertions, deletions, substitutions and swaps of neighboring letters each count as one.
    /// Returns max + 1 as soon as it's clear the answer is above <paramref name="max"/>.
    /// </summary>
    internal static int Distance(string a, string b, int max)
    {
        if (Math.Abs(a.Length - b.Length) > max) return max + 1;

        int n = a.Length, m = b.Length;
        var previous2 = new int[m + 1];
        var previous = new int[m + 1];
        var current = new int[m + 1];
        for (int j = 0; j <= m; j++) previous[j] = j;

        for (int i = 1; i <= n; i++)
        {
            current[0] = i;
            int rowMin = current[0];
            for (int j = 1; j <= m; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                int value = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    value = Math.Min(value, previous2[j - 2] + 1);
                current[j] = value;
                rowMin = Math.Min(rowMin, value);
            }
            if (rowMin > max) return max + 1;

            (previous2, previous, current) = (previous, current, previous2);
        }
        return previous[m];
    }

    /// <summary>Splits on spaces and punctuation, and on camelCase ("PowerShell" → Power, Shell).</summary>
    private static List<string> SplitWords(string text)
    {
        var words = new List<string>();
        int start = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool end = i == text.Length || !char.IsLetterOrDigit(text[i]);
            bool camel = !end && start >= 0 && char.IsUpper(text[i]) && char.IsLower(text[i - 1]);

            if ((end || camel) && start >= 0)
            {
                words.Add(text[start..i]);
                start = -1;
            }
            if (!end && start < 0) start = i;
        }
        return words;
    }

    private static int IndexOfIgnoreCase(string text, char c, int from)
    {
        char lower = char.ToLowerInvariant(c);
        for (int i = from; i < text.Length; i++)
            if (char.ToLowerInvariant(text[i]) == lower) return i;
        return -1;
    }
}
