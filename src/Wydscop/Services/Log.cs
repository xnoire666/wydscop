using System.IO;

namespace Wydscop.Services;

/// <summary>Tiny file log at %LocalAppData%\Wydscop\wydscop.log (overwritten each launch).</summary>
internal static class Log
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wydscop", "wydscop.log");

    public static void Reset()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, string.Empty);
        }
        catch { /* logging must never crash the app */ }
    }

    /// <summary>Off in the unit tests, so they don't write into the running app's log.</summary>
    internal static bool Enabled { get; set; } = true;

    public static void Write(string message)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
                File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
