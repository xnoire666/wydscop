namespace Wydscop.Tests;

/// <summary>Which folders Windows Search covers, so Wydscop's own index can fill in the rest.</summary>
public class WindowsIndexScopeTests
{
    // Windows indexes C:\Users\ but the user excluded a code folder, and Windows' own media folder is back in.
    private static readonly WindowsIndexScope Scope = new([
        new(@"C:\Users\", true),
        new(@"C:\Users\me\code\", false),
        new(@"C:\Users\me\code\media", true), // no trailing slash: still read as a folder
    ]);

    [Theory]
    [InlineData(@"C:\Users\me\Documents\notes.txt", true)]
    [InlineData(@"C:\Users", true)]                        // the rule folder itself
    [InlineData(@"C:\Users\me\code", false)]               // excluded folder: Wydscop indexes it
    [InlineData(@"C:\Users\me\code\app\readme.md", false)]
    [InlineData(@"C:\Users\me\codex\notes.txt", true)]     // only whole folder names match
    [InlineData(@"C:\Users\me\code\media\clip.mp4", true)] // the most specific rule wins
    [InlineData(@"C:\xampp\htdocs", false)]                // no rule at all
    [InlineData(@"c:\users\ME\CODE\app", false)]           // any case
    public void Covers_follows_the_most_specific_rule(string path, bool covered) =>
        Assert.Equal(covered, Scope.Covers(path));

    [Theory]
    [InlineData(@"C:\", true)]
    [InlineData(@"C:\Users", true)]
    [InlineData(@"C:\Users\me", true)]
    [InlineData(@"C:\Users\me\code", false)] // the gap itself, not something inside it
    [InlineData(@"C:\Users\other", false)]
    [InlineData(@"C:\Users\me\Documents", false)]
    public void Finds_excluded_folders_inside(string folder, bool expected) =>
        Assert.Equal(expected, Scope.HasGapsInside(folder));

    [Fact]
    public void Exclude_wins_when_a_folder_has_both_rules()
    {
        var scope = new WindowsIndexScope([new(@"C:\Users\me\code\", true), new(@"C:\Users\me\code\", false)]);
        Assert.False(scope.Covers(@"C:\Users\me\code\app"));
    }

    [Fact]
    public void Gaps_are_only_exclusions_inside_an_included_folder()
    {
        var scope = new WindowsIndexScope([new(@"C:\Users\", true), new(@"C:\Users\me\code\", false), new(@"C:\Windows\", false)]);
        Assert.Equal([@"C:\Users\me\code\"], scope.Gaps);
    }

    [Fact]
    public void Nothing_is_covered_without_rules() =>
        Assert.False(WindowsIndexScope.None.Covers(@"C:\Users\me\notes.txt"));
}
