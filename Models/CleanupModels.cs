namespace NODR.Models;

public enum CleanupCategoryKind
{
    UserTemp,
    WindowsTemp,
    WindowsErrorReports,
    SystemCrashDumps,
    TelegramMediaCache,
    SteamClientCache,
    EpicClientCache,
    DeliveryOptimization,
    ThumbnailCache,
    BrowserWebCache,
    BrowserCodeGpuCache,
    BrowserDownloadTempCache,
    DirectXShaderCache,
    NvidiaShaderCache,
    AmdShaderCache,
    GradleCache,
    NpmCache,
    YarnCache,
    PnpmCache,
    JetBrainsCache,
    VsCodeCache,
    NuGetCache,
    PipCache,
    DockerBuildCache,
    CapCutCache,
    ComfyUiTemp,
    Automatic1111Temp,
    MavenRepository,
    RecycleBin
}

public enum CleanupRisk
{
    Safe,
    PerformanceImpact,
    ReviewFirst
}

public sealed record CleanupCandidate(string Path, long SizeBytes);

public sealed record CleanupCategoryScan(
    CleanupCategoryKind Kind,
    string Name,
    string Description,
    long SizeBytes,
    int FileCount,
    IReadOnlyList<CleanupCandidate> Candidates,
    IReadOnlyList<string>? AllowedRoots = null,
    bool SelectedByDefault = true,
    CleanupRisk Risk = CleanupRisk.Safe,
    bool IsCleanable = true,
    IReadOnlyList<string>? DetectedApps = null,
    IReadOnlyList<string>? RunningApps = null);

public sealed record CleanupScanResult(IReadOnlyList<CleanupCategoryScan> Categories);

public sealed record CleanupResult(long FreedBytes, int DeletedFiles, int SkippedFiles);

public sealed record CleanupProgress(int ProcessedFiles, int TotalFiles, string Status)
{
    public double Percent => TotalFiles <= 0 ? 0 : Math.Clamp((double)ProcessedFiles / TotalFiles * 100.0, 0, 100);
}
