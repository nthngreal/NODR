namespace NODR.Models;

public sealed record InstalledAppInfo(
    string Id,
    string Name,
    string Publisher,
    string Version,
    DateTime? InstallDate,
    long? EstimatedSizeBytes,
    string? UninstallString,
    string? InstallLocation,
    string? DisplayIcon,
    string RegistryHive,
    string RegistryView,
    string RegistryKeyPath);


public sealed record UninstallEvidenceSnapshot(
    string? InstallLocation,
    bool InstallLocationExisted,
    string? DisplayIconExecutable,
    bool DisplayIconExecutableExisted);

public enum UninstallResidualKind
{
    Folder,
    RegistryKey
}

public enum ResidualConfidence
{
    Safe,
    Review
}

public sealed record UninstallResidual(
    UninstallResidualKind Kind,
    string DisplayPath,
    string? FileSystemPath,
    long SizeBytes,
    long FileCount,
    long SkippedEntries,
    bool CanClean,
    ResidualConfidence Confidence = ResidualConfidence.Safe);

public sealed record UninstallResidualScanResult(
    IReadOnlyList<UninstallResidual> Items,
    long CleanableBytes,
    long CleanableFiles,
    bool OriginalUninstallEntryRemains,
    bool OriginalUninstallEntryCanClean);

public sealed record ResidualCleanupResult(int RemovedItems, int SkippedItems, IReadOnlyList<string> RemovedDisplayPaths);
