using System.IO;
using static Wydscop.Tests.TestSetup;

namespace Wydscop.Tests;

/// <summary>Recent items on the empty bar: newest first, no duplicates, at most 50, kept between launches.</summary>
public class RecentStoreTests : TempFolderTest
{
    private string Path => FileIn("recent.json");

    [Fact]
    public void Starts_empty_without_a_file() => Assert.Empty(RecentStore.Load(Path).Items);

    [Fact]
    public void Newest_is_first()
    {
        var store = RecentStore.Load(Path);
        store.Add(Result(@"C:\xampp\a.txt"));
        store.Add(Result(@"C:\xampp\b.txt"));
        Assert.Equal([@"C:\xampp\b.txt", @"C:\xampp\a.txt"], store.Items.Select(i => i.Id));
    }

    [Fact]
    public void Opening_again_moves_it_to_the_top_without_a_duplicate()
    {
        var store = RecentStore.Load(Path);
        store.Add(Result(@"C:\xampp\a.txt"));
        store.Add(Result(@"C:\xampp\b.txt"));
        store.Add(Result(@"C:\XAMPP\A.TXT")); // paths are compared ignoring case
        Assert.Equal(2, store.Items.Count);
        Assert.Equal(@"C:\XAMPP\A.TXT", store.Items[0].Id);
    }

    [Fact]
    public void Keeps_only_the_latest_50()
    {
        var store = RecentStore.Load(Path);
        for (int i = 0; i < RecentStore.MaxItems + 5; i++) store.Add(Result($@"C:\xampp\{i}.txt"));
        Assert.Equal(RecentStore.MaxItems, store.Items.Count);
        Assert.Equal(@"C:\xampp\54.txt", store.Items[0].Id);
        Assert.Equal(@"C:\xampp\5.txt", store.Items[^1].Id);
    }

    [Fact]
    public void Remove_and_clear()
    {
        var store = RecentStore.Load(Path);
        store.Add(Result(@"C:\xampp\a.txt"));
        store.Add(Result(@"C:\xampp\b.txt"));
        store.Remove(@"c:\xampp\A.txt");
        Assert.Equal([@"C:\xampp\b.txt"], store.Items.Select(i => i.Id));
        store.Clear();
        Assert.Empty(RecentStore.Load(Path).Items);
    }

    [Fact]
    public void Survives_a_restart()
    {
        RecentStore.Load(Path).Add(Result("Microsoft.WindowsNotepad", ResultKind.App, "Notepad"));
        var item = Assert.Single(RecentStore.Load(Path).Items);
        Assert.Equal(("Microsoft.WindowsNotepad", ResultKind.App, "Notepad"), (item.Id, item.Kind, item.Title));
    }

    [Fact]
    public void A_damaged_file_starts_over_instead_of_crashing()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path, "{ not json");
        var store = RecentStore.Load(Path);
        Assert.Empty(store.Items);
        store.Add(Result(@"C:\xampp\a.txt")); // and saving still works
        Assert.Single(RecentStore.Load(Path).Items);
    }
}

/// <summary>Pinned items: toggled on and off, kept in the order pinned, kept between launches.</summary>
public class PinnedStoreTests : TempFolderTest
{
    private string Path => FileIn("pinned.json");

    [Fact]
    public void Toggle_pins_then_unpins()
    {
        var store = PinnedStore.Load(Path);
        Assert.True(store.Toggle(Result(@"C:\xampp")));
        Assert.True(store.Contains(@"c:\XAMPP"));
        Assert.False(store.Toggle(Result(@"C:\xampp")));
        Assert.False(store.Contains(@"C:\xampp"));
        Assert.Empty(store.Items);
    }

    [Fact]
    public void Keeps_the_order_you_pinned_in_after_a_restart()
    {
        var store = PinnedStore.Load(Path);
        store.Toggle(Result(@"C:\xampp\b.txt"));
        store.Toggle(Result(@"C:\xampp\a.txt"));
        store.Toggle(Result(@"C:\xampp\c.txt"));

        var reloaded = PinnedStore.Load(Path);
        Assert.Equal([@"C:\xampp\b.txt", @"C:\xampp\a.txt", @"C:\xampp\c.txt"], reloaded.Items.Select(i => i.Id));
        Assert.True(reloaded.Contains(@"C:\xampp\a.txt"));
    }

    [Fact]
    public void A_damaged_file_starts_over_instead_of_crashing()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path, "[1, 2");
        Assert.Empty(PinnedStore.Load(Path).Items);
    }
}

/// <summary>Open counts: things you open often get a boost that grows fast, then levels off at 200.</summary>
public class UsageStoreTests : TempFolderTest
{
    private string Path => FileIn("usage.json");

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 60)]
    [InlineData(3, 120)]
    [InlineData(7, 180)]
    [InlineData(50, 200)] // capped
    public void Boost_grows_then_levels_off(int opens, double expected)
    {
        var store = UsageStore.Load(Path);
        for (int i = 0; i < opens; i++) store.Record("Microsoft.WindowsNotepad");
        Assert.Equal(expected, store.Boost("Microsoft.WindowsNotepad"), precision: 6);
    }

    [Fact]
    public void Counts_survive_a_restart_and_ignore_case()
    {
        var store = UsageStore.Load(Path);
        store.Record(@"C:\xampp\a.txt");
        store.Record(@"C:\XAMPP\A.TXT");
        Assert.Equal(60 * Math.Log2(3), UsageStore.Load(Path).Boost(@"c:\xampp\a.txt"), precision: 6);
    }

    [Fact]
    public void A_damaged_file_starts_over_instead_of_crashing()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path, "nope");
        Assert.Equal(0, UsageStore.Load(Path).Boost("anything"));
    }
}
