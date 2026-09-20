using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using NODR.Models;

namespace NODR.Services;

internal sealed class SensorWorkerClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _snapshotPath;
    private readonly string _errorPath;
    private Process? _process;
    private bool _started;
    private bool _disposed;

    public SensorWorkerClient()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NODR");
        Directory.CreateDirectory(directory);
        _snapshotPath = Path.Combine(directory, $"sensors-{Environment.ProcessId}.json");
        _errorPath = _snapshotPath + ".error.txt";
    }

    public void Start()
    {
        if (_started || _disposed)
            return;

        _started = true;

        try
        {
            TryDelete(_snapshotPath);
            TryDelete(_errorPath);

            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            var processPath = Environment.ProcessPath;
            var runningViaDotnetHost = !string.IsNullOrWhiteSpace(processPath) &&
                                        Path.GetFileNameWithoutExtension(processPath)
                                            .Equals("dotnet", StringComparison.OrdinalIgnoreCase);

            var startInfo = new ProcessStartInfo
            {
                FileName = runningViaDotnetHost ? processPath! : processPath ?? "dotnet",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = AppContext.BaseDirectory,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            if (runningViaDotnetHost || string.IsNullOrWhiteSpace(processPath))
                startInfo.ArgumentList.Add(assemblyPath);

            startInfo.ArgumentList.Add(SensorWorker.WorkerArg);
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(_snapshotPath);
            startInfo.ArgumentList.Add("--parent");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());

            _process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _process = null;
            WritePersistentError("Sensor worker could not start.", ex.ToString());
        }
    }

    public LowLevelSensorSnapshot? ReadLatest()
    {
        if (_disposed || !_started)
            return null;

        try
        {
            if (_process is { HasExited: true } && !File.Exists(_snapshotPath))
            {
                var details = File.Exists(_errorPath)
                    ? File.ReadAllText(_errorPath)
                    : $"Sensor worker exited with code {_process.ExitCode} before producing data.";
                WritePersistentError("Sensor worker stopped.", details);
                return null;
            }

            if (!File.Exists(_snapshotPath))
                return null;

            var lastWriteUtc = File.GetLastWriteTimeUtc(_snapshotPath);
            if (DateTime.UtcNow - lastWriteUtc > TimeSpan.FromSeconds(5))
                return null;

            var json = File.ReadAllText(_snapshotPath);
            return JsonSerializer.Deserialize<LowLevelSensorSnapshot>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(1000);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
        finally
        {
            _process?.Dispose();
            TryDelete(_snapshotPath);
            TryDelete(_errorPath);
            _disposed = true;
        }
    }

    private static void WritePersistentError(string heading, string details)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NODR");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "sensor-worker-error.txt"),
                $"{heading}{Environment.NewLine}{DateTimeOffset.Now:O}{Environment.NewLine}{details}");
        }
        catch
        {
            // Diagnostics must never affect monitoring.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Temp cleanup is best-effort.
        }
    }
}
