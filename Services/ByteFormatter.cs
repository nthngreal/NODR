namespace NODR.Services;

public static class ByteFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes) => Format(bytes < 0 ? 0UL : (ulong)bytes);

    public static string Format(ulong bytes)
    {
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < Units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        var decimals = unitIndex == 0 ? 0 : value >= 100 ? 0 : value >= 10 ? 1 : 2;
        return $"{value.ToString($"F{decimals}")} {Units[unitIndex]}";
    }
}
