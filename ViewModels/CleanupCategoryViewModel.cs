using NODR.Models;
using NODR.Services;

namespace NODR.ViewModels;

public sealed class CleanupCategoryViewModel : ViewModelBase
{
    private bool _isSelected;
    private readonly LocalizationService _loc = LocalizationService.Instance;

    public CleanupCategoryViewModel(CleanupCategoryScan scan)
    {
        Scan = scan;
        _isSelected = scan.SelectedByDefault && scan.SizeBytes > 0;
    }

    public CleanupCategoryScan Scan { get; }
    public string Name => BuildName();
    public string SizeText => ByteFormatter.Format(Scan.SizeBytes);
    public string FileCountText => Scan.FileCount == 1 ? _loc["Cleanup.FileCount.One"] : _loc.Format("Cleanup.FileCount.Many", Scan.FileCount);
    public bool HasData => Scan.SizeBytes > 0;
    public bool CanSelect => HasData && Scan.IsCleanable;
    public bool IsRecommended => Scan.SelectedByDefault && Scan.Risk == CleanupRisk.Safe;
    public bool IsOptional => !IsRecommended;

    public string SafetyBadge => !Scan.IsCleanable ? _loc["Cleanup.Badge.Detected"] : Scan.Risk switch
    {
        CleanupRisk.PerformanceImpact => _loc["Cleanup.Badge.Performance"],
        CleanupRisk.ReviewFirst => _loc["Cleanup.Badge.Review"],
        _ => _loc["Cleanup.Badge.Safe"]
    };

    public string SafetyColor => !Scan.IsCleanable ? "#9CA3AF" : Scan.Risk switch
    {
        CleanupRisk.PerformanceImpact => "#E9B95E",
        CleanupRisk.ReviewFirst => "#F0A15A",
        _ => "#10B981"
    };

    public string ImpactDescription => BuildDescription();

    public string IconGlyph => Scan.Kind switch
    {
        CleanupCategoryKind.UserTemp => "",
        CleanupCategoryKind.WindowsTemp => "",
        CleanupCategoryKind.WindowsErrorReports or CleanupCategoryKind.SystemCrashDumps => "",
        CleanupCategoryKind.TelegramMediaCache => "",
        CleanupCategoryKind.SteamClientCache or CleanupCategoryKind.EpicClientCache => "",
        CleanupCategoryKind.DeliveryOptimization => "",
        CleanupCategoryKind.ThumbnailCache => "",
        CleanupCategoryKind.BrowserWebCache or CleanupCategoryKind.BrowserCodeGpuCache or CleanupCategoryKind.BrowserDownloadTempCache => "",
        CleanupCategoryKind.DirectXShaderCache or CleanupCategoryKind.NvidiaShaderCache or CleanupCategoryKind.AmdShaderCache => "",
        CleanupCategoryKind.GradleCache or CleanupCategoryKind.NpmCache or CleanupCategoryKind.YarnCache or CleanupCategoryKind.PnpmCache or CleanupCategoryKind.NuGetCache or CleanupCategoryKind.PipCache or CleanupCategoryKind.DockerBuildCache or CleanupCategoryKind.MavenRepository => "",
        CleanupCategoryKind.JetBrainsCache or CleanupCategoryKind.VsCodeCache => "",
        CleanupCategoryKind.CapCutCache => "",
        CleanupCategoryKind.ComfyUiTemp or CleanupCategoryKind.Automatic1111Temp => "",
        CleanupCategoryKind.RecycleBin => "",
        _ => ""
    };

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(FileCountText));
        OnPropertyChanged(nameof(SafetyBadge));
        OnPropertyChanged(nameof(ImpactDescription));
    }

    private string BuildName()
    {
        var key = Scan.Kind switch
        {
            CleanupCategoryKind.UserTemp => "Cleanup.Category.UserTemp.Name",
            CleanupCategoryKind.WindowsTemp => "Cleanup.Category.WindowsTemp.Name",
            CleanupCategoryKind.WindowsErrorReports or CleanupCategoryKind.SystemCrashDumps => "Cleanup.Category.SystemCrash.Name",
            CleanupCategoryKind.TelegramMediaCache => "Cleanup.Category.Telegram.Name",
            CleanupCategoryKind.SteamClientCache => "Cleanup.Category.Steam.Name",
            CleanupCategoryKind.EpicClientCache => "Cleanup.Category.Epic.Name",
            CleanupCategoryKind.DeliveryOptimization => "Cleanup.Category.Delivery.Name",
            CleanupCategoryKind.ThumbnailCache => "Cleanup.Category.Thumbnail.Name",
            CleanupCategoryKind.BrowserWebCache => "Cleanup.Category.BrowserWeb.Name",
            CleanupCategoryKind.BrowserCodeGpuCache => "Cleanup.Category.BrowserCode.Name",
            CleanupCategoryKind.BrowserDownloadTempCache => "Cleanup.Category.BrowserDownload.Name",
            CleanupCategoryKind.DirectXShaderCache => "Cleanup.Category.DirectX.Name",
            CleanupCategoryKind.NvidiaShaderCache => "Cleanup.Category.Nvidia.Name",
            CleanupCategoryKind.AmdShaderCache => "Cleanup.Category.Amd.Name",
            CleanupCategoryKind.GradleCache => "Cleanup.Category.Gradle.Name",
            CleanupCategoryKind.NpmCache => "Cleanup.Category.Npm.Name",
            CleanupCategoryKind.YarnCache => "Cleanup.Category.Yarn.Name",
            CleanupCategoryKind.PnpmCache => "Cleanup.Category.Pnpm.Name",
            CleanupCategoryKind.JetBrainsCache => "Cleanup.Category.JetBrains.Name",
            CleanupCategoryKind.VsCodeCache => "Cleanup.Category.VsCode.Name",
            CleanupCategoryKind.NuGetCache => "Cleanup.Category.NuGet.Name",
            CleanupCategoryKind.PipCache => "Cleanup.Category.Pip.Name",
            CleanupCategoryKind.DockerBuildCache => "Cleanup.Category.Docker.Name",
            CleanupCategoryKind.CapCutCache => "Cleanup.Category.CapCut.Name",
            CleanupCategoryKind.ComfyUiTemp => "Cleanup.Category.Comfy.Name",
            CleanupCategoryKind.Automatic1111Temp => "Cleanup.Category.A1111.Name",
            CleanupCategoryKind.MavenRepository => "Cleanup.Category.Maven.Name",
            CleanupCategoryKind.RecycleBin => "Cleanup.Category.Recycle.Name",
            _ => null
        };

        if (key is null)
            return Scan.Name;

        if (Scan.Kind is CleanupCategoryKind.BrowserWebCache or CleanupCategoryKind.BrowserCodeGpuCache && Scan.DetectedApps is { Count: > 0 })
        {
            var formatKey = Scan.Kind == CleanupCategoryKind.BrowserWebCache
                ? "Cleanup.Category.BrowserWeb.NameApps"
                : "Cleanup.Category.BrowserCode.NameApps";
            return _loc.Format(formatKey, string.Join(", ", Scan.DetectedApps));
        }

        return _loc[key];
    }

    private string BuildDescription()
    {
        var key = Scan.Kind switch
        {
            CleanupCategoryKind.UserTemp => "Cleanup.Category.UserTemp.Description",
            CleanupCategoryKind.WindowsTemp => "Cleanup.Category.WindowsTemp.Description",
            CleanupCategoryKind.WindowsErrorReports or CleanupCategoryKind.SystemCrashDumps => "Cleanup.Category.SystemCrash.Description",
            CleanupCategoryKind.TelegramMediaCache => "Cleanup.Category.Telegram.Description",
            CleanupCategoryKind.SteamClientCache => "Cleanup.Category.Steam.Description",
            CleanupCategoryKind.EpicClientCache => "Cleanup.Category.Epic.Description",
            CleanupCategoryKind.DeliveryOptimization => "Cleanup.Category.Delivery.Description",
            CleanupCategoryKind.ThumbnailCache => "Cleanup.Category.Thumbnail.Description",
            CleanupCategoryKind.BrowserWebCache => "Cleanup.Category.BrowserWeb.Description",
            CleanupCategoryKind.BrowserCodeGpuCache => "Cleanup.Category.BrowserCode.Description",
            CleanupCategoryKind.BrowserDownloadTempCache => "Cleanup.Category.BrowserDownload.Description",
            CleanupCategoryKind.DirectXShaderCache => "Cleanup.Category.DirectX.Description",
            CleanupCategoryKind.NvidiaShaderCache => "Cleanup.Category.Nvidia.Description",
            CleanupCategoryKind.AmdShaderCache => "Cleanup.Category.Amd.Description",
            CleanupCategoryKind.GradleCache => "Cleanup.Category.Gradle.Description",
            CleanupCategoryKind.NpmCache => "Cleanup.Category.Npm.Description",
            CleanupCategoryKind.YarnCache => "Cleanup.Category.Yarn.Description",
            CleanupCategoryKind.PnpmCache => "Cleanup.Category.Pnpm.Description",
            CleanupCategoryKind.JetBrainsCache => "Cleanup.Category.JetBrains.Description",
            CleanupCategoryKind.VsCodeCache => "Cleanup.Category.VsCode.Description",
            CleanupCategoryKind.NuGetCache => "Cleanup.Category.NuGet.Description",
            CleanupCategoryKind.PipCache => "Cleanup.Category.Pip.Description",
            CleanupCategoryKind.DockerBuildCache => "Cleanup.Category.Docker.Description",
            CleanupCategoryKind.CapCutCache => "Cleanup.Category.CapCut.Description",
            CleanupCategoryKind.ComfyUiTemp => "Cleanup.Category.Comfy.Description",
            CleanupCategoryKind.Automatic1111Temp => "Cleanup.Category.A1111.Description",
            CleanupCategoryKind.MavenRepository => "Cleanup.Category.Maven.Description",
            CleanupCategoryKind.RecycleBin => "Cleanup.Category.Recycle.Description",
            _ => null
        };

        var description = key is null ? Scan.Description : _loc[key];
        if (Scan.Kind is not (CleanupCategoryKind.BrowserWebCache or CleanupCategoryKind.BrowserCodeGpuCache or CleanupCategoryKind.BrowserDownloadTempCache))
            return description;

        var detected = Scan.DetectedApps is { Count: > 0 }
            ? _loc.Format("Browser.Detected", string.Join(", ", Scan.DetectedApps))
            : _loc["Browser.DetectedNone"];
        var running = Scan.RunningApps is { Count: > 0 }
            ? _loc.Format("Browser.Running", string.Join(", ", Scan.RunningApps))
            : string.Empty;
        return $"{description} {detected}{running}";
    }
}
