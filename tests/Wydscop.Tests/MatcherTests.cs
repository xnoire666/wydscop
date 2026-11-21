namespace Wydscop.Tests;

/// <summary>
/// How well a name matches what was typed. The exact numbers matter: results are ordered by them, and
/// AGENTS.md lists them (exact 1000, starts with 900, word start 800, initials 700, contains 500,
/// letters in order ≤300, typos ≤150).
/// </summary>
public class MatcherTests
{
    [Theory]
    [InlineData("Notepad", "notepad", 1000)]            // exact, any case
    [InlineData("Notepad", "note", 900)]                // starts with
    [InlineData("Visual Studio Code", "studio", 790)]   // second word starts with it
    [InlineData("Visual Studio Code", "code", 780)]     // later words score a little lower
    [InlineData("PowerShell", "shell", 790)]            // camelCase counts as a new word
    [InlineData("Visual Studio Code", "vsc", 700)]      // initials
    [InlineData("Notepad", "pad", 496)]                 // contains, earlier is better
    [InlineData("Notepad", "ntpd", 276)]                // letters in order, 3 skipped
    public void Scores_each_kind_of_match(string text, string query, double expected) =>
        Assert.Equal(expected, Matcher.Score(text, query));

    [Fact]
    public void Kinds_of_match_rank_in_order()
    {
        double[] scores =
        [
            Matcher.Score("Notepad", "notepad"),
            Matcher.Score("Notepad", "note"),
            Matcher.Score("Visual Studio Code", "studio"),
            Matcher.Score("Visual Studio Code", "vsc"),
            Matcher.Score("Notepad", "pad"),
            Matcher.Score("Notepad", "ntpd"),
            Matcher.Score("Chrome", "chrime", allowTypos: true),
        ];
        Assert.Equal(scores.OrderByDescending(s => s), scores);
        Assert.All(scores, s => Assert.True(s > 0));
    }

    [Theory]
    [InlineData("Notepad", "")]
    [InlineData("Notepad", "   ")]
    [InlineData("", "note")]
    [InlineData("Notepad", "xyz")]
    [InlineData("Realtek Audio", "read")] // letters scattered across the name are noise
    public void No_match_scores_zero(string text, string query) =>
        Assert.Equal(0, Matcher.Score(text, query));

    [Fact]
    public void Surrounding_spaces_are_ignored() =>
        Assert.Equal(900, Matcher.Score("Notepad", "  note "));

    [Fact]
    public void Every_word_must_match_somewhere()
    {
        // Average of "code" (780) and "studio" (790), minus 50 for being split up.
        Assert.Equal(735, Matcher.Score("Visual Studio Code", "code studio"));
        Assert.Equal(0, Matcher.Score("Visual Studio Code", "code xyz"));
    }

    [Theory]
    [InlineData("Google Chrome", "chorme")] // two neighbors swapped
    [InlineData("Chrome", "chrime")]        // one wrong letter
    public void Typos_only_match_when_asked(string text, string query)
    {
        Assert.Equal(0, Matcher.Score(text, query));
        double score = Matcher.Score(text, query, allowTypos: true);
        Assert.InRange(score, 1, 150);
    }

    [Fact]
    public void Short_words_get_no_typo_tolerance() =>
        Assert.Equal(0, Matcher.Score("Paint", "pbi", allowTypos: true));

    [Fact]
    public void Typos_never_outrank_a_real_match() =>
        Assert.True(Matcher.Score("Chrome", "chrime", allowTypos: true) < Matcher.Score("Notepad", "ntpd"));

    [Theory]
    [InlineData("kitten", "sitting", 3, 3)]
    [InlineData("chorme", "chrome", 2, 1)] // a swap is one typo, not two
    [InlineData("abc", "abc", 1, 0)]
    [InlineData("abc", "xyzxyz", 1, 2)]   // gives up early: returns max + 1
    [InlineData("abcd", "wxyz", 1, 2)]
    public void Distance_counts_typos(string a, string b, int max, int expected) =>
        Assert.Equal(expected, Matcher.Distance(a, b, max));
}
