using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Text.Json;
using NODR.Models;

namespace NODR.Services;

public sealed class CleanupService
{
    private static readonly TimeSpan MinimumFileAge = TimeSpan.FromHours(24);

    public Task<CleanupScanResult> ScanAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(cancellationToken), cancellationToken);

    public Task<CleanupResult> CleanAsync(
        IReadOnlyList<CleanupCategoryScan> categories,
        CancellationToken cancellationToken = default,
        IProgress<CleanupProgress>? progress = null) =>
        Task.Run(() => Clean(categories, cancellationToken, progress), cancellationToken);

    private static CleanupScanResult Scan(CancellationToken cancellationToken)
    {
        var categories = new List<CleanupCategoryScan>();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var windowsRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        var userTemp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        categories.Add(ScanRoots(CleanupCategoryKind.UserTemp, "Temporary files",
            "Safe. Old app and user temporary files are removed; apps recreate temporary data when needed.",
            new[] { userTemp }, cancellationToken, MinimumFileAge));

        var windowsTemp = Path.GetFullPath(Path.Combine(windowsRoot, "Temp")).TrimEnd(Path.DirectorySeparatorChar);
        if (!windowsTemp.Equals(userTemp, StringComparison.OrdinalIgnoreCase))
            categories.Add(ScanRoots(CleanupCategoryKind.WindowsTemp, "Windows temp",
                "Safe. Windows temporary files older than 24 hours are removed when accessible.",
                new[] { windowsTemp }, cancellationToken, MinimumFileAge));

        var systemWerRoot = Path.Combine(programData, "Microsoft", "Windows", "WER");
        var userWerRoot = Path.Combine(localAppData, "Microsoft", "Windows", "WER");
        categories.Add(ScanRoots(CleanupCategoryKind.SystemCrashDumps, "System Crash Dumps & Error Logs",
            "Safe. Old application crash dumps and archived Windows Error Reporting data are diagnostic leftovers; personal documents are not included.",
            new[] { Path.Combine(localAppData, "CrashDumps"), Path.Combine(systemWerRoot, "ReportArchive"), Path.Combine(systemWerRoot, "ReportQueue"), Path.Combine(userWerRoot, "ReportArchive"), Path.Combine(userWerRoot, "ReportQueue") },
            cancellationToken, MinimumFileAge));

        AddAppClientCaches(categories, localAppData, appData, cancellationToken);

        var systemDrive = Path.GetPathRoot(windowsRoot) ?? "C:\\";
        var deliveryOptimization = Path.Combine(systemDrive, "Windows", "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache");
        categories.Add(ScanRoots(CleanupCategoryKind.DeliveryOptimization, "Delivery Optimization",
            "Safe. Cached Windows delivery files can be downloaded again when needed.",
            new[] { deliveryOptimization }, cancellationToken, TimeSpan.Zero));

        var explorerCache = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer");
        categories.Add(ScanFilesByPattern(CleanupCategoryKind.ThumbnailCache, "Thumbnail cache",
            "Safe. File Explorer preview thumbnails are rebuilt automatically.", explorerCache, "thumbcache_*.db", cancellationToken));

        AddBrowserCleanup(categories, localAppData, appData, cancellationToken);

        categories.Add(ScanRoots(CleanupCategoryKind.DirectXShaderCache, "DirectX shader cache",
            "Safe to regenerate. Games and graphics apps may briefly stutter while shaders are rebuilt.",
            new[] { Path.Combine(localAppData, "D3DSCache") }, cancellationToken, TimeSpan.Zero,
            selectedByDefault: false, risk: CleanupRisk.PerformanceImpact));

        categories.Add(ScanRoots(CleanupCategoryKind.NvidiaShaderCache, "NVIDIA shader cache",
            "Safe to regenerate. NVIDIA apps and games may briefly stutter while shaders are rebuilt.",
            new[] { Path.Combine(localAppData, "NVIDIA", "DXCache"), Path.Combine(localAppData, "NVIDIA", "GLCache"), Path.Combine(localAppData, "NVIDIA Corporation", "NV_Cache") },
            cancellationToken, TimeSpan.Zero, selectedByDefault: false, risk: CleanupRisk.PerformanceImpact));

        categories.Add(ScanRoots(CleanupCategoryKind.AmdShaderCache, "AMD shader cache",
            "Safe to regenerate. AMD graphics apps and games may rebuild shaders after cleanup.",
            new[] { Path.Combine(localAppData, "AMD", "DxCache"), Path.Combine(localAppData, "AMD", "GLCache"), Path.Combine(localAppData, "AMD", "VkCache") },
            cancellationToken, TimeSpan.Zero, selectedByDefault: false, risk: CleanupRisk.PerformanceImpact));

        AddCreativeCleanup(categories, userProfile, localAppData, cancellationToken);
        AddDeveloperCleanup(categories, userProfile, localAppData, appData, cancellationToken);

        categories.Add(ScanRecycleBin());
        return new CleanupScanResult(categories);
    }


    private static void AddAppClientCaches(List<CleanupCategoryScan> categories, string localAppData, string appData, CancellationToken cancellationToken)
    {
        // Telegram Desktop keeps cloud-backed media cache under tdata\user_data.
        // Deliberately exclude tdata root/database/session files and only allow known cache subfolders.
        var telegramBase = Path.Combine(appData, "Telegram Desktop", "tdata", "user_data");
        categories.Add(ScanRoots(CleanupCategoryKind.TelegramMediaCache, "Telegram Media Cache",
            "Safe cache only. Cached cloud media can be downloaded again; chats, sessions, settings and downloaded files outside Telegram's cache are not touched.",
            new[] { Path.Combine(telegramBase, "cache"), Path.Combine(telegramBase, "media_cache") },
            cancellationToken, TimeSpan.Zero, selectedByDefault: true, risk: CleanupRisk.Safe));

        categories.Add(ScanRoots(CleanupCategoryKind.SteamClientCache, "Steam Client Cache",
            "Safe client web cache only. Installed games, saves, Workshop content and active game downloads are not included.",
            new[] { Path.Combine(localAppData, "Steam", "htmlcache") },
            cancellationToken, TimeSpan.Zero, selectedByDefault: true, risk: CleanupRisk.Safe));

        var epicSaved = Path.Combine(localAppData, "EpicGamesLauncher", "Saved");
        var epicRoots = SafeEnumerateDirectories(epicSaved)
            .Where(path => Path.GetFileName(path).StartsWith("webcache", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        categories.Add(ScanRoots(CleanupCategoryKind.EpicClientCache, "Epic Games Client Cache",
            "Safe launcher web cache only. Installed games, saves and download/install data are not included.",
            epicRoots, cancellationToken, TimeSpan.Zero, selectedByDefault: true, risk: CleanupRisk.Safe));
    }

    private static void AddBrowserCleanup(List<CleanupCategoryScan> categories, string localAppData, string appData, CancellationToken cancellationToken)
    {
        var webRoots = new List<string>();
        var codeGpuRoots = new List<string>();
        var downloadTempRoots = new List<string>();
        var detected = new List<string>();
        var running = new List<string>();

        AddChromiumBrowser("Chrome", "chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data"), webRoots, codeGpuRoots, downloadTempRoots, detected, running);
        AddChromiumBrowser("Edge", "msedge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data"), webRoots, codeGpuRoots, downloadTempRoots, detected, running);
        AddChromiumBrowser("Brave", "brave", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"), webRoots, codeGpuRoots, downloadTempRoots, detected, running);
        AddChromiumBrowser("Opera", "opera", Path.Combine(localAppData, "Opera Software", "Opera Stable"), webRoots, codeGpuRoots, downloadTempRoots, detected, running, singleProfile: true);
        AddChromiumBrowser("Opera GX", "opera", Path.Combine(localAppData, "Opera Software", "Opera GX Stable"), webRoots, codeGpuRoots, downloadTempRoots, detected, running, singleProfile: true);

        var firefoxRoaming = Path.Combine(appData, "Mozilla", "Firefox", "Profiles");
        var firefoxLocal = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefoxRoaming) || Directory.Exists(firefoxLocal))
        {
            detected.Add("Firefox");
            if (IsProcessRunning("firefox")) running.Add("Firefox");
            if (Directory.Exists(firefoxLocal))
            {
                foreach (var profile in SafeEnumerateDirectories(firefoxLocal))
                {
                    webRoots.Add(Path.Combine(profile, "cache2"));
                    codeGpuRoots.Add(Path.Combine(profile, "startupCache"));
                }
            }
        }

        var detectedApps = detected.Distinct().ToArray();
        var runningApps = running.Distinct().ToArray();

        categories.Add(ScanRoots(CleanupCategoryKind.BrowserWebCache, "Browser web cache",
            "Browser web cache", webRoots, cancellationToken, TimeSpan.Zero, selectedByDefault: true, risk: CleanupRisk.Safe)
            with { DetectedApps = detectedApps, RunningApps = runningApps });

        categories.Add(ScanRoots(CleanupCategoryKind.BrowserCodeGpuCache, "Browser GPU & code cache",
            "Browser GPU & code cache", codeGpuRoots, cancellationToken, TimeSpan.Zero, selectedByDefault: false, risk: CleanupRisk.PerformanceImpact)
            with { DetectedApps = detectedApps, RunningApps = runningApps });

        categories.Add(ScanRoots(CleanupCategoryKind.BrowserDownloadTempCache, "Browser download temp cache",
            "Browser download temp cache", downloadTempRoots, cancellationToken, TimeSpan.Zero, selectedByDefault: false, risk: CleanupRisk.ReviewFirst)
            with { DetectedApps = detectedApps, RunningApps = runningApps });
    }

    private static void AddChromiumBrowser(string name, string processName, string userDataRoot, List<string> webRoots, List<string> codeGpuRoots, List<string> downloadTempRoots, List<string> detected, List<string> running, bool singleProfile = false)
    {
        if (!Directory.Exists(userDataRoot)) return;
        detected.Add(name);
        if (IsProcessRunning(processName)) running.Add(name);

        var profiles = singleProfile
            ? new[] { userDataRoot }
            : SafeEnumerateDirectories(userDataRoot).Where(path =>
                string.Equals(Path.GetFileName(path), "Default", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(path).StartsWith("Profile ", StringComparison.OrdinalIgnoreCase)).ToArray();

        foreach (var profile in profiles)
        {
            webRoots.Add(Path.Combine(profile, "Cache"));
            webRoots.Add(Path.Combine(profile, "Media Cache"));
            codeGpuRoots.Add(Path.Combine(profile, "Code Cache"));
            codeGpuRoots.Add(Path.Combine(profile, "GPUCache"));
            downloadTempRoots.Add(Path.Combine(profile, "Download Service"));
        }
    }

    private static void AddCreativeCleanup(List<CleanupCategoryScan> categories, string userProfile, string localAppData, CancellationToken cancellationToken)
    {
        categories.Add(ScanRoots(CleanupCategoryKind.CapCutCache, "CapCut Cache",
            "Optional creative cache. Only CapCut User Data\\Cache is included. Drafts, projects and source media remain untouched.",
            new[] { Path.Combine(localAppData, "CapCut", "User Data", "Cache") },
            cancellationToken, TimeSpan.Zero, selectedByDefault: false, risk: CleanupRisk.PerformanceImpact));

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var comfyRoots = new[]
        {
            Path.Combine(userProfile, "ComfyUI", "temp"),
            Path.Combine(desktop, "ComfyUI", "temp"),
            Path.Combine(documents, "ComfyUI", "temp"),
            Path.Combine(Path.GetTempPath(), "ComfyUI")
        };
        categories.Add(ScanRoots(CleanupCategoryKind.ComfyUiTemp, "ComfyUI Temp",
            "Optional generated temp only. Models, checkpoints, workflows and normal output folders are excluded.",
            comfyRoots, cancellationToken, TimeSpan.Zero, selectedByDefault: false, risk: CleanupRisk.PerformanceImpact));

        var a1111Roots = new[]
        {
            Path.Combine(userProfile, "stable-diffusion-webui", "tmp"),
            Path.Combine(userProfile, "stable-diffusion-webui", "temp"),
            Path.Combine(desktop, "stable-diffusion-webui", "tmp"),
            Path.Combine(desktop, "stable-diffusion-webui", "temp"),
            Path.Combine(documents, "stable-diffusion-webui", "tmp"),
            Path.Combine(documents, "stable-diffusion-webui", "temp")
        };
        categories.Add(ScanRoots(CleanupCategoryKind.Automatic1111Temp, "Automatic1111 Temp",
            "Optional generated temp only. Models, checkpoints, workflows and saved outputs are excluded.",
            a1111Roots, cancellationToken, TimeSpan.Zero, selectedByDefault: false, risk: CleanupRisk.PerformanceImpact));
    }

    private static void AddDeveloperCleanup(List<CleanupCategoryScan> categories, string userProfile, string localAppData, string appData, CancellationToken cancellationToken)
    {
        categories.Add(ScanRoots(CleanupCategoryKind.GradleCache, "Gradle cache",
            "Developer cache. Safe to regenerate, but the next Gradle build may download dependencies again.",
            new[] { Path.Combine(userProfile, ".gradle", "caches") }, cancellationToken, TimeSpan.Zero, false, CleanupRisk.PerformanceImpact));

        categories.Add(ScanRoots(CleanupCategoryKind.NpmCache, "npm cache",
            "Developer package cache. Safe to regenerate; future installs may need to download packages again.",
            new[] { Path.Combine(appData, "npm-cache") }, cancellationToken, TimeSpan.Zero, false, CleanupRisk.PerformanceImpact));

        categories.Add(ScanRoots(CleanupCategoryKind.YarnCache, "Yarn cache",
            "Developer package cache. Safe to regenerate; future installs may need to download packages again.",
            new[] { Path.Combine(localAppData, "Yarn", "Cache") }, cancellationToken, TimeSpan.Zero, false, CleanupRisk.PerformanceImpact));

        categories.Add(ScanRoots(CleanupCategoryKind.PnpmCache, "pnpm store",
            "Developer package store. Safe to regenerate, but projects may need package downloads again.",
            new[] { Path.Combine(localAppData, "pnpm", "store"), Path.Combine(localAppData, "pnpm-store") }, cancellationToken, TimeSpan.Zero, false, CleanupRisk.PerformanceImpact));

        var jetBrainsRoots = new List<string>();
        var jetBrainsBase = Path.Combine(localAppData, "JetBrains");
        foreach (var product in SafeEnumerateDirectories(jetBrainsBase))
            jetBrainsRoots.Add(Path.Combine(product, "caches"));
        categories.Add(ScanRoots(CleanupCategoryKind.JetBrainsCache, "JetBrains IDE caches",
            "IDE cache only. Safe to regenerate; the IDE may re-index projects after cleanup.",
            jetBrainsRoots, cancellationToken, TimeSpan.Zero, false, CleanupRisk.PerformanceImpact));

        var vsCodeRoots = new List<string>();
        foreach (var codeRoot in new[] { Path.Combine(appData, "Code"), Path.Combine(appData, "Code - Insiders") })
        {
            vsCodeRoots.Add(Path.Combine(codeRoot, "Cache"));
            vsCodeRoots.Add(Path.Combine(codeRoot, "CachedData"));
            vsCodeRoots.Add(Path.Combine(codeRoot, "Code Cache"));
            vsCodeRoots.Add(Path.Combine(codeRoot, "GPUCache"));
        }
        categories.Add(ScanRoots(CleanupCategoryKind.VsCodeCache, "VS Code caches",
            "Editor cache only. Workspace files, settings and extensions are not included.",
            vsCodeRoots, cancellationToken, TimeSpan.Zero, false, CleanupRisk.PerformanceImpact));

        categories.Add(ScanRoots(CleanupCategoryKind.NuGetCache, "NuGet / .NET caches",
            "Developer package/build cache. Packages can be restored; the next .NET build may take longer.",
            new[] { Path.Combine(userProfile, ".nuget", "packages"), Path.Combine(Path.GetTempPath(), "NuGetScratchroot") },
            cancellationToken, TimeSpan.Zero, false, CleanupRisk.PerformanceImpact));

        categories.Add(ScanRoots(CleanupCategoryKind.PipCache, "pip cache",
            "Developer package cache only. Installed Python environments and project files stay untouched; pip can download packages again.",
            new[] { Path.Combine(localAppData, "pip", "Cache") }, cancellationToken, TimeSpan.Zero, false, CleanupRisk.PerformanceImpact));

        var dockerBytes = TryGetDockerBuildCacheBytes();
        if (dockerBytes > 0)
        {
            categories.Add(new CleanupCategoryScan(
                CleanupCategoryKind.DockerBuildCache,
                "Docker build cache",
                "Docker-managed unused build cache. Images, containers, volumes and project files are not removed; future builds may rebuild layers.",
                dockerBytes,
                1,
                Array.Empty<CleanupCandidate>(),
                null,
                false,
                CleanupRisk.PerformanceImpact));
        }

        var maven = ScanRoots(CleanupCategoryKind.MavenRepository, "Maven local repository",
            "Detected only. NODR does not delete the whole .m2 repository because it can contain locally installed artifacts that are not safely recoverable from a remote registry.",
            new[] { Path.Combine(userProfile, ".m2", "repository") }, cancellationToken, TimeSpan.Zero, false, CleanupRisk.ReviewFirst);
        categories.Add(maven with { IsCleanable = false });
    }

    private static CleanupCategoryScan ScanRoots(CleanupCategoryKind kind, string name, string description, IEnumerable<string> roots,
        CancellationToken cancellationToken, TimeSpan minimumAge, bool selectedByDefault = true, CleanupRisk risk = CleanupRisk.Safe)
    {
        var candidates = new List<CleanupCandidate>();
        var allowedRoots = roots.Where(root => !string.IsNullOrWhiteSpace(root)).Select(NormalizeRoot).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var root in allowedRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidates.AddRange(ScanDirectoryCandidates(root, cancellationToken, minimumAge));
        }
        return new CleanupCategoryScan(kind, name, description, candidates.Sum(x => x.SizeBytes), candidates.Count, candidates, allowedRoots, selectedByDefault, risk);
    }

    private static IReadOnlyList<CleanupCandidate> ScanDirectoryCandidates(string root, CancellationToken cancellationToken, TimeSpan minimumAge)
    {
        var candidates = new List<CleanupCandidate>();
        var cutoff = DateTime.UtcNow - minimumAge;
        if (!Directory.Exists(root)) return candidates;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTimeUtc <= cutoff) candidates.Add(new CleanupCandidate(info.FullName, info.Length));
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
        }
        return candidates;
    }

    private static CleanupCategoryScan ScanFilesByPattern(CleanupCategoryKind kind, string name, string description, string root, string pattern, CancellationToken cancellationToken)
    {
        var candidates = new List<CleanupCandidate>();
        var normalized = NormalizeRoot(root);
        if (Directory.Exists(normalized))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(normalized, pattern, SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { var info = new FileInfo(file); candidates.Add(new CleanupCandidate(info.FullName, info.Length)); }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
        }
        return new CleanupCategoryScan(kind, name, description, candidates.Sum(x => x.SizeBytes), candidates.Count, candidates, new[] { normalized });
    }

    private static CleanupCategoryScan ScanRecycleBin()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        var result = SHQueryRecycleBin(null, ref info);
        var size = result == 0 ? Math.Max(0, info.i64Size) : 0;
        var count = result == 0 ? (int)Math.Min(int.MaxValue, Math.Max(0, info.i64NumItems)) : 0;
        return new CleanupCategoryScan(CleanupCategoryKind.RecycleBin, "Recycle Bin",
            "Review first. These are previously deleted files; emptying the Bin removes its normal restore option.",
            size, count, Array.Empty<CleanupCandidate>(), null, false, CleanupRisk.ReviewFirst);
    }

    private static CleanupResult Clean(IReadOnlyList<CleanupCategoryScan> categories, CancellationToken cancellationToken, IProgress<CleanupProgress>? progress)
    {
        long freedBytes = 0; var deletedFiles = 0; var skippedFiles = 0;
        var totalFiles = categories.Sum(category => Math.Max(0, category.FileCount));
        var processedFiles = 0;
        progress?.Report(new CleanupProgress(0, totalFiles, LocalizationService.Instance["Cleanup.Preparing"]));

        foreach (var category in categories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new CleanupProgress(processedFiles, totalFiles, GetCleaningStatus(category.Kind)));
            if (category.Kind == CleanupCategoryKind.DeliveryOptimization)
            {
                if (category.SizeBytes > 0 && TryClearDeliveryOptimizationCache()) { freedBytes += category.SizeBytes; deletedFiles += category.FileCount; }
                else if (category.SizeBytes > 0) skippedFiles += category.FileCount;
                processedFiles += category.FileCount;
                progress?.Report(new CleanupProgress(processedFiles, totalFiles, GetCleaningStatus(category.Kind)));
                continue;
            }
            if (category.Kind == CleanupCategoryKind.DockerBuildCache)
            {
                var before = Math.Max(0, TryGetDockerBuildCacheBytes());
                if (TryPruneDockerBuildCache())
                {
                    var after = Math.Max(0, TryGetDockerBuildCacheBytes());
                    var delta = before > 0 ? Math.Max(0, before - after) : category.SizeBytes;
                    freedBytes += delta;
                    deletedFiles += 1;
                }
                else
                {
                    skippedFiles += 1;
                }
                processedFiles += 1;
                progress?.Report(new CleanupProgress(processedFiles, totalFiles, GetCleaningStatus(category.Kind)));
                continue;
            }
            if (category.Kind == CleanupCategoryKind.RecycleBin)
            {
                if (category.SizeBytes > 0 && SHEmptyRecycleBin(IntPtr.Zero, null, RecycleBinFlags.NoConfirmation | RecycleBinFlags.NoProgressUI | RecycleBinFlags.NoSound) == 0)
                { freedBytes += category.SizeBytes; deletedFiles += category.FileCount; }
                else if (category.SizeBytes > 0) skippedFiles += category.FileCount;
                processedFiles += category.FileCount;
                progress?.Report(new CleanupProgress(processedFiles, totalFiles, GetCleaningStatus(category.Kind)));
                continue;
            }

            var allowedRoots = category.AllowedRoots ?? Array.Empty<string>();
            foreach (var candidate in category.Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!allowedRoots.Any(root => IsPathInsideRoot(candidate.Path, root))) { skippedFiles++; continue; }
                    if (!File.Exists(candidate.Path)) continue;
                    if ((File.GetAttributes(candidate.Path) & FileAttributes.ReparsePoint) != 0) { skippedFiles++; continue; }
                    File.Delete(candidate.Path); freedBytes += candidate.SizeBytes; deletedFiles++;
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { skippedFiles++; }
                finally { processedFiles++; progress?.Report(new CleanupProgress(processedFiles, totalFiles, GetCleaningStatus(category.Kind))); }
            }
            foreach (var root in allowedRoots) DeleteEmptyDirectories(category.Candidates, root);
        }
        return new CleanupResult(freedBytes, deletedFiles, skippedFiles);
    }

    private static string GetCleaningStatus(CleanupCategoryKind kind)
    {
        var key = kind switch
        {
            CleanupCategoryKind.UserTemp or CleanupCategoryKind.WindowsTemp => "Cleanup.Progress.Temp",
            CleanupCategoryKind.WindowsErrorReports or CleanupCategoryKind.SystemCrashDumps => "Cleanup.Progress.Crash",
            CleanupCategoryKind.TelegramMediaCache => "Cleanup.Progress.Telegram",
            CleanupCategoryKind.SteamClientCache or CleanupCategoryKind.EpicClientCache => "Cleanup.Progress.Game",
            CleanupCategoryKind.DeliveryOptimization => "Cleanup.Progress.Delivery",
            CleanupCategoryKind.ThumbnailCache => "Cleanup.Progress.Thumbnail",
            CleanupCategoryKind.BrowserWebCache or CleanupCategoryKind.BrowserCodeGpuCache or CleanupCategoryKind.BrowserDownloadTempCache => "Cleanup.Progress.Browser",
            CleanupCategoryKind.DirectXShaderCache or CleanupCategoryKind.NvidiaShaderCache or CleanupCategoryKind.AmdShaderCache => "Cleanup.Progress.Shader",
            CleanupCategoryKind.GradleCache or CleanupCategoryKind.NpmCache or CleanupCategoryKind.YarnCache or CleanupCategoryKind.PnpmCache or CleanupCategoryKind.NuGetCache or CleanupCategoryKind.PipCache or CleanupCategoryKind.DockerBuildCache => "Cleanup.Progress.Dev",
            CleanupCategoryKind.CapCutCache or CleanupCategoryKind.ComfyUiTemp or CleanupCategoryKind.Automatic1111Temp => "Cleanup.Progress.Creative",
            CleanupCategoryKind.JetBrainsCache or CleanupCategoryKind.VsCodeCache => "Cleanup.Progress.IDE",
            CleanupCategoryKind.RecycleBin => "Cleanup.Progress.Recycle",
            _ => "Cleanup.Progress.Default"
        };
        return LocalizationService.Instance[key];
    }

    private static long TryGetDockerBuildCacheBytes()
    {
        try
        {
            // Cleanup scans run while the NODR page is visible. Keep CLI probes fully
            // detached from console UI so navigation never flashes a terminal window.
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("system");
            startInfo.ArgumentList.Add("df");
            startInfo.ArgumentList.Add("--format");
            startInfo.ArgumentList.Add("json");
            using var process = Process.Start(startInfo);
            if (process is null) return 0;
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(8_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return 0;
            }
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!root.TryGetProperty("Type", out var type) || !string.Equals(type.GetString(), "Build Cache", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!root.TryGetProperty("Reclaimable", out var reclaimable))
                    return 0;
                return ParseHumanBytes(reclaimable.GetString());
            }
        }
        catch
        {
            // Docker is optional and can be stopped. Do not surface this as a cleanup error.
        }
        return 0;
    }

    private static bool TryPruneDockerBuildCache()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("builder");
            startInfo.ArgumentList.Add("prune");
            startInfo.ArgumentList.Add("--force");
            using var process = Process.Start(startInfo);
            if (process is null) return false;
            if (!process.WaitForExit(60_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static long ParseHumanBytes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var value = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        var numberEnd = 0;
        while (numberEnd < value.Length && (char.IsDigit(value[numberEnd]) || value[numberEnd] == '.' || value[numberEnd] == ',')) numberEnd++;
        if (numberEnd == 0) return 0;
        if (!double.TryParse(value[..numberEnd].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return 0;
        var unit = value[numberEnd..].Trim().ToUpperInvariant();
        var multiplier = unit switch
        {
            "B" => 1d,
            "KB" => 1000d,
            "KIB" => 1024d,
            "MB" => 1000d * 1000d,
            "MIB" => 1024d * 1024d,
            "GB" => 1000d * 1000d * 1000d,
            "GIB" => 1024d * 1024d * 1024d,
            "TB" => 1000d * 1000d * 1000d * 1000d,
            "TIB" => 1024d * 1024d * 1024d * 1024d,
            _ => 1d
        };
        return (long)Math.Max(0, Math.Round(number * multiplier));
    }

    private static bool IsProcessRunning(string processName)
    {
        try { return Process.GetProcessesByName(processName).Length > 0; }
        catch { return false; }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();
        try { return Directory.EnumerateDirectories(root).ToArray(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return Array.Empty<string>(); }
    }

    private static string NormalizeRoot(string root) => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);

    private static bool TryClearDeliveryOptimizationCache()
    {
        try
        {
            var startInfo = new ProcessStartInfo { FileName = "powershell.exe", UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            startInfo.ArgumentList.Add("-NoProfile"); startInfo.ArgumentList.Add("-NonInteractive"); startInfo.ArgumentList.Add("-WindowStyle"); startInfo.ArgumentList.Add("Hidden"); startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("Delete-DeliveryOptimizationCache -Force -ErrorAction Stop");
            using var process = Process.Start(startInfo);
            if (process is null) return false;
            if (!process.WaitForExit(30_000)) { try { process.Kill(entireProcessTree: true); } catch { } return false; }
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    private static bool IsPathInsideRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void DeleteEmptyDirectories(IReadOnlyList<CleanupCandidate> candidates, string allowedRoot)
    {
        var directories = candidates.Select(candidate => Path.GetDirectoryName(candidate.Path)).Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(directory => directory!.Length);
        foreach (var directory in directories)
        {
            try
            {
                if (directory is not null && IsPathInsideRoot(directory, allowedRoot) && Directory.Exists(directory) &&
                    (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0 && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory, false);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }
    [Flags]
    private enum RecycleBinFlags : uint { NoConfirmation = 0x00000001, NoProgressUI = 0x00000002, NoSound = 0x00000004 }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, RecycleBinFlags dwFlags);
}
