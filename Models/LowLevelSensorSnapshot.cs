namespace NODR.Models;

public sealed record LowLevelSensorSnapshot(
    DateTime TimestampUtc,
    double? CpuTemperatureC,
    string? CpuName,
    double? GpuUsagePercent,
    double? GpuTemperatureC,
    double? GpuMemoryUsedMb,
    double? GpuMemoryTotalMb,
    string? GpuName);
