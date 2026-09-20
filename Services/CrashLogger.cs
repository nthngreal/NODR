using System.IO;
namespace NODR.Services;

internal static class CrashLogger
{
    private static readonly object Sync = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NODR",
        "nodr.log");

    public static void WriteStage(string stage)
    {
        try
        {
            lock (Sync)
            {
                var directory = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] STARTUP {stage}{Environment.NewLine}");
            }
        }
        catch
        {
            // Diagnostics must never be able to crash the app.
        }
    }

    public static void Write(string source, Exception exception)
    {
        try
        {
            lock (Sync)
            {
                var directory = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never be able to crash the app.
        }
    }
}
