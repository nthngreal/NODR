using LibreHardwareMonitor.Hardware;
using NODR.Models;

namespace NODR.Services;

internal sealed class LowLevelSensorReader : IDisposable
{
    private readonly Computer _computer;
    private bool _disposed;

    public LowLevelSensorReader()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true
        };
        _computer.Open();
    }

    public LowLevelSensorSnapshot Read()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var hardware in _computer.Hardware)
            UpdateRecursive(hardware);

        var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        var gpu = _computer.Hardware.Where(IsGpu).OrderBy(GpuPreference).FirstOrDefault();

        return new LowLevelSensorSnapshot(
            DateTime.UtcNow,
            cpu is null ? null : SelectCpuTemperature(cpu),
            cpu?.Name,
            gpu is null ? null : SelectGpuLoad(gpu),
            gpu is null ? null : SelectGpuTemperature(gpu),
            gpu is null ? null : FindSensorValue(gpu, SensorType.SmallData, "GPU Memory Used"),
            gpu is null ? null : FindSensorValue(gpu, SensorType.SmallData, "GPU Memory Total"),
            gpu?.Name);
    }

    private static bool IsGpu(IHardware hardware) =>
        hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    private static int GpuPreference(IHardware hardware) => hardware.HardwareType switch
    {
        HardwareType.GpuNvidia => 0,
        HardwareType.GpuAmd => 1,
        HardwareType.GpuIntel => 2,
        _ => 3
    };

    private static void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (var subHardware in hardware.SubHardware)
            UpdateRecursive(subHardware);
    }

    private static IEnumerable<ISensor> SensorsRecursive(IHardware hardware)
    {
        foreach (var sensor in hardware.Sensors)
            yield return sensor;

        foreach (var subHardware in hardware.SubHardware)
        foreach (var sensor in SensorsRecursive(subHardware))
            yield return sensor;
    }

    private static double? SelectCpuTemperature(IHardware cpu)
    {
        var sensors = SensorsRecursive(cpu)
            .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue)
            .ToList();

        var preferred = sensors.FirstOrDefault(s =>
            s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains("Tctl/Tdie", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains("Core Average", StringComparison.OrdinalIgnoreCase));

        return preferred?.Value ?? sensors.Select(s => (double?)s.Value).Max();
    }

    private static double? SelectGpuLoad(IHardware gpu)
    {
        var sensors = SensorsRecursive(gpu).Where(s => s.SensorType == SensorType.Load && s.Value.HasValue).ToList();
        var preferred = sensors.FirstOrDefault(s =>
            s.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Equals("GPU Total", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains("D3D 3D", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains("Render/Compute", StringComparison.OrdinalIgnoreCase));

        if (preferred?.Value is { } preferredValue)
            return Math.Clamp(preferredValue, 0, 100);

        var fallback = sensors.Where(s => !s.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
            .Select(s => (double?)s.Value).Max();
        return fallback.HasValue ? Math.Clamp(fallback.Value, 0, 100) : null;
    }

    private static double? SelectGpuTemperature(IHardware gpu)
    {
        var sensors = SensorsRecursive(gpu).Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue).ToList();
        var preferred = sensors.FirstOrDefault(s =>
            s.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Equals("GPU", StringComparison.OrdinalIgnoreCase));
        return preferred?.Value ?? sensors.Select(s => (double?)s.Value).FirstOrDefault();
    }

    private static double? FindSensorValue(IHardware hardware, SensorType type, string name) =>
        SensorsRecursive(hardware).FirstOrDefault(s => s.SensorType == type &&
            s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && s.Value.HasValue)?.Value;

    public void Dispose()
    {
        if (_disposed) return;
        _computer.Close();
        _disposed = true;
    }
}
