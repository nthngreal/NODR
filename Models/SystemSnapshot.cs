namespace NODR.Models;

public sealed record SystemSnapshot(
    DateTime Timestamp,
    double CpuUsagePercent,
    double? CpuTemperatureC,
    string CpuName,
    double? GpuUsagePercent,
    double? GpuTemperatureC,
    double? GpuMemoryUsedMb,
    double? GpuMemoryTotalMb,
    string? GpuName,
    ulong MemoryUsedBytes,
    ulong MemoryTotalBytes,
    IReadOnlyList<DiskSnapshot> Disks,
    bool HardwareSensorsAvailable);

public sealed record DiskSnapshot(
    string Name,
    string VolumeLabel,
    long UsedBytes,
    long TotalBytes)
{
    public double UsedPercent => TotalBytes <= 0 ? 0 : (double)UsedBytes / TotalBytes * 100.0;
}
