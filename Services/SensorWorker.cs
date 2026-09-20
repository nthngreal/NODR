using System.IO;
using System.Diagnostics;
using System.Text.Json;

namespace NODR.Services;

internal static class SensorWorker
{
    public const string WorkerArg = "--sensor-worker";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool IsWorker(string[] args) =>
        args.Any(a => string.Equals(a, WorkerArg, StringComparison.OrdinalIgnoreCase));

    public static int Run(string[] args)
    {
        if (!TryGetArgument(args, "--output", out var outputPath) ||
            !TryGetArgument(args, "--parent", out var parentText) ||
            !int.TryParse(parentText, out var parentPid))
            return 2;

        try
        {
            using var parent = Process.GetProcessById(parentPid);
            using var reader = new LowLevelSensorReader();
            while (!parent.HasExited)
            {
                var snapshot = reader.Read();
                WriteAtomically(outputPath, JsonSerializer.Serialize(snapshot, JsonOptions));
                Thread.Sleep(1000);
            }

            return 0;
        }
        catch (ArgumentException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            TryWriteWorkerError(outputPath, ex);
            return 1;
        }
    }


    private static bool TryGetArgument(string[] args, string name, out string value)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                continue;

            value = args[i + 1];
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static void WriteAtomically(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, content);
        File.Move(tempPath, path, true);
    }

    private static void TryWriteWorkerError(string outputPath, Exception ex)
    {
        try
        {
            var errorPath = outputPath + ".error.txt";
            File.WriteAllText(errorPath, ex.ToString());
        }
        catch
        {
            // Worker logging must never create another failure path.
        }
    }
}
