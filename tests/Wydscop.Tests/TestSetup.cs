using System.IO;
using System.Runtime.CompilerServices;
using Wydscop.Services;

namespace Wydscop.Tests;

internal static class TestSetup
{
    /// <summary>Runs before any test: keeps the tests out of the real app's log.</summary>
    [ModuleInitializer]
    internal static void Init() => Log.Enabled = false;

    /// <summary>A row as the stores see it; opening it does nothing.</summary>
    internal static SearchResult Result(string id, ResultKind kind = ResultKind.File, string? title = null) =>
        new(id, kind, title ?? Path.GetFileName(id), "", () => { });
}

/// <summary>
/// A fresh temporary folder for each test, deleted afterwards, so store tests never touch
/// the real files in %LocalAppData%\Wydscop.
/// </summary>
public abstract class TempFolderTest : IDisposable
{
    protected readonly string Folder = Path.Combine(Path.GetTempPath(), "Wydscop.Tests", Guid.NewGuid().ToString("N"));

    protected string FileIn(string name) => Path.Combine(Folder, name);

    public void Dispose()
    {
        try { Directory.Delete(Folder, recursive: true); } catch { /* already gone, or never made */ }
        GC.SuppressFinalize(this);
    }
}
