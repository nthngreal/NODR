namespace NODR.Models;

public sealed record DriveCategoryUsage(string Name, long Bytes, string ColorHex);

public sealed record DriveBreakdownProgress(
    long BytesScanned,
    long UsedBytes,
    long FilesChecked,
    long DirectoriesChecked,
    int SkippedEntries,
    string CurrentDirectory)
{
    public double Percent => UsedBytes <= 0 ? 0 : Math.Min(99, (double)BytesScanned / UsedBytes * 100.0);
}

public sealed record DriveBreakdownResult(
    IReadOnlyList<DriveCategoryUsage> Categories,
    int SkippedEntries);
