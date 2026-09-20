using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NODR.Models;

namespace NODR.Services;

public sealed class HardwareMonitorService : IDisposable
{
    private readonly object _sync = new();
    private readonly CpuUsageSampler _cpuSampler = new();
    private readonly SensorWorkerClient _sensorWorker = new();
    private readonly string _cpuFallbackName;
    private bool _disposed;

    public HardwareMonitorService()
    {
        _cpuFallbackName = ReadCpuNameFromRegistry() ?? "CPU";
    }

    public void StartLowLevelSensors()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _sensorWorker.Start();
        }
    }

    public SystemSnapshot ReadSnapshot()
    {
        lock (_sync)
        {
            ThrowIfDisposed();

            var cpuUsage = _cpuSampler.Sample();
            GetMemoryUsage(out var memoryUsed, out var memoryTotal);
            var disks = ReadFixedDisks();
            var lowLevel = _sensorWorker.ReadLatest();

            return new SystemSnapshot(
                DateTime.Now,
                cpuUsage,
                lowLevel?.CpuTemperatureC,
                lowLevel?.CpuName ?? _cpuFallbackName,
                lowLevel?.GpuUsagePercent,
                lowLevel?.GpuTemperatureC,
                lowLevel?.GpuMemoryUsedMb,
                lowLevel?.GpuMemoryTotalMb,
                lowLevel?.GpuName,
                memoryUsed,
                memoryTotal,
                disks,
                lowLevel is not null);
        }
    }

    private static IReadOnlyList<DiskSnapshot> ReadFixedDisks()
    {
        var result = new List<DiskSnapshot>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                    continue;

                var used = drive.TotalSize - drive.AvailableFreeSpace;
                result.Add(new DiskSnapshot(drive.Name, drive.VolumeLabel, used, drive.TotalSize));
            }
            catch
            {
                // A disappearing/removable volume should not break the dashboard.
            }
        }

        return result;
    }

    private static void GetMemoryUsage(out ulong usedBytes, out ulong totalBytes)
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status))
        {
            usedBytes = 0;
            totalBytes = 0;
            return;
        }

        totalBytes = status.TotalPhys;
        usedBytes = status.TotalPhys >= status.AvailPhys ? status.TotalPhys - status.AvailPhys : 0;
    }

    private static string? ReadCpuNameFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString")?.ToString()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_sync)
        {
            if (_disposed)
                return;

            _sensorWorker.Dispose();
            _disposed = true;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    private sealed class CpuUsageSampler
    {
        private ulong _previousIdle;
        private ulong _previousKernel;
        private ulong _previousUser;
        private bool _hasPrevious;

        public double Sample()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user))
                return 0;

            var idleTicks = ToUInt64(idle);
            var kernelTicks = ToUInt64(kernel);
            var userTicks = ToUInt64(user);

            if (!_hasPrevious)
            {
                _previousIdle = idleTicks;
                _previousKernel = kernelTicks;
                _previousUser = userTicks;
                _hasPrevious = true;
                return 0;
            }

            var idleDelta = idleTicks - _previousIdle;
            var kernelDelta = kernelTicks - _previousKernel;
            var userDelta = userTicks - _previousUser;
            var total = kernelDelta + userDelta;

            _previousIdle = idleTicks;
            _previousKernel = kernelTicks;
            _previousUser = userTicks;

            if (total == 0)
                return 0;

            return Math.Clamp((1.0 - (double)idleDelta / total) * 100.0, 0, 100);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

        private static ulong ToUInt64(FileTime time) => ((ulong)time.High << 32) | time.Low;

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct FileTime
        {
            public readonly uint Low;
            public readonly uint High;
        }
    }
}
