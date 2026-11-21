namespace Wydscop.Tests;

/// <summary>What file search leaves out: dependency, build and system folders, and junk file types.</summary>
public class SearchFiltersTests
{
    [Theory]
    [InlineData("Notepad.lnk", true)]   // Start menu shortcuts already show as apps
    [InlineData("site.url", true)]
    [InlineData("server.LOG", true)]    // any case
    [InlineData("notes.txt", false)]
    [InlineData("README", false)]
    public void Skips_junk_file_types(string name, bool excluded) =>
        Assert.Equal(excluded, SearchFilters.IsExcludedFileName(name));

    [Theory]
    [InlineData("Debug", "bin", true)]
    [InlineData("release", "BIN", true)]
    [InlineData("Debug", "src", false)]
    [InlineData("bin", "app", false)]
    public void Spots_build_output(string name, string parent, bool expected) =>
        Assert.Equal(expected, SearchFilters.IsBuildOutput(name, parent));

    [Theory]
    [InlineData(@"C:\xampp\htdocs\index.php", false)]
    [InlineData(@"C:\xampp\htdocs\node_modules\react\index.js", true)]
    [InlineData(@"C:\xampp\htdocs\node_modules", false)]      // the folder itself can still be found
    [InlineData(@"C:\xampp\.git\config", true)]
    [InlineData(@"C:\Windows\notepad.exe", true)]             // system folder at the top of a drive
    [InlineData(@"C:\Backup\Windows\notes.txt", false)]       // ...but not deeper down
    [InlineData(@"D:\Program Files\tool.exe", true)]
    [InlineData(@"C:\code\app\bin\Debug\app.exe", true)]
    [InlineData(@"C:\code\app\bin\Debug", false)]
    [InlineData(@"C:\code\app\bin\tool.exe", false)]
    public void Spots_noise_paths(string path, bool expected) =>
        Assert.Equal(expected, SearchFilters.IsNoisePath(path));
}
