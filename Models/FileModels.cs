namespace NODR.Models;

public sealed record FoundFile(string Path, long SizeBytes, DateTime LastWriteTimeUtc);
public sealed record FileScanResult(string RootPath, IReadOnlyList<FoundFile> Files, int SkippedEntries);

public sealed record FileScanProgress(
    long FilesChecked,
    long DirectoriesChecked,
    int LargeFilesFound,
    int SkippedEntries,
    string CurrentDirectory);
