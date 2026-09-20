using System.IO;
using System.Security;
using NODR.Models;

namespace NODR.Services;

public sealed class DriveBreakdownService
{
    public Task<DriveBreakdownResult> ScanAsync(string rootPath, CancellationToken cancellationToken = default, IProgress<DriveBreakdownProgress>? progress = null) =>
        Task.Run(() => Scan(rootPath, cancellationToken, progress), cancellationToken);

    private static DriveBreakdownResult Scan(string rootPath, CancellationToken cancellationToken, IProgress<DriveBreakdownProgress>? progress)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(rootPath)) ?? rootPath;
        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["System"] = 0,
            ["Programs"] = 0,
            ["Cache & development"] = 0,
            ["Media"] = 0,
            ["Documents & archives"] = 0,
            ["Virtual disks & VM data"] = 0,
            ["Installers & disk images"] = 0,
            ["Other"] = 0
        };
        var pending = new Stack<string>();
        pending.Push(root);
        var skipped = 0;
        long bytesScanned = 0;
        long filesChecked = 0;
        long directoriesChecked = 0;
        long usedBytes = 0;
        try
        {
            var drive = new DriveInfo(root);
            usedBytes = Math.Max(0, drive.TotalSize - drive.AvailableFreeSpace);
        }
        catch { }

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            directoriesChecked++;
            if (directoriesChecked == 1 || directoriesChecked % 24 == 0)
                progress?.Report(new DriveBreakdownProgress(bytesScanned, usedBytes, filesChecked, directoriesChecked, skipped, directory));
            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                        pending.Push(child);
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException) { skipped++; }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException) { skipped++; }

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        totals[ClassifyPath(root, info.FullName, info.Extension)] += info.Length;
                        bytesScanned += info.Length;
                        filesChecked++;
                        if (filesChecked % 256 == 0)
                            progress?.Report(new DriveBreakdownProgress(bytesScanned, usedBytes, filesChecked, directoriesChecked, skipped, directory));
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException) { skipped++; }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException) { skipped++; }
        }

        var colors = new Dictionary<string, string>
        {
            ["System"] = "#10B981",
            ["Programs"] = "#34D399",
            ["Cache & development"] = "#E0B66A",
            ["Media"] = "#6EE7B7",
            ["Documents & archives"] = "#B48BE0",
            ["Virtual disks & VM data"] = "#D18A72",
            ["Installers & disk images"] = "#9E8BD8",
            ["Other"] = "#667486"
        };
        progress?.Report(new DriveBreakdownProgress(bytesScanned, usedBytes, filesChecked, directoriesChecked, skipped, root));
        return new DriveBreakdownResult(totals.Select(x => new DriveCategoryUsage(x.Key, x.Value, colors[x.Key])).ToList(), skipped);
    }

    public static string ClassifyPath(string root, string path, string extension)
    {
        var relative = path[root.Length..].TrimStart(Path.DirectorySeparatorChar).Replace('/', '\\');
        var lower = relative.ToLowerInvariant();
        var ext = extension.ToLowerInvariant();

        if (lower.StartsWith("windows\\") || lower.StartsWith("$recycle.bin\\") || lower.StartsWith("system volume information\\") || lower.StartsWith("recovery\\"))
            return "System";
        if (lower.StartsWith("program files\\") || lower.StartsWith("program files (x86)\\") || lower.StartsWith("programdata\\"))
            return "Programs";
        if (lower.Contains("\\appdata\\local\\temp\\") || lower.Contains("\\cache\\") || lower.Contains("\\caches\\") || lower.Contains("\\.cache\\") || lower.Contains("\\node_modules\\") || lower.Contains("\\.nuget\\") || lower.Contains("\\.gradle\\") || lower.Contains("\\docker\\") || lower.Contains("\\android\\") || lower.Contains("\\npm-cache\\") || lower.Contains("\\pip\\cache\\"))
            return "Cache & development";
        if (ext is ".vhd" or ".vhdx" or ".vmdk" or ".qcow" or ".qcow2" or ".img")
            return "Virtual disks & VM data";
        if (ext is ".iso" or ".msi" or ".msix" or ".appx" or ".appxbundle" || lower.Contains("\\updater\\") || lower.Contains("\\installer\\"))
            return "Installers & disk images";
        if (ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".mp3" or ".flac" or ".wav" or ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".heic")
            return "Media";
        if (ext is ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".md" or ".zip" or ".7z" or ".rar")
            return "Documents & archives";
        return "Other";
    }
}
