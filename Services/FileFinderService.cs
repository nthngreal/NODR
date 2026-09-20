using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using NODR.Models;

namespace NODR.Services;

public sealed class FileFinderService
{
    public const long DefaultMinimumSizeBytes = 100L * 1024 * 1024;
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(200);

    public string? PickFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.Instance["Files.ChooseFolder"],
            Multiselect = false
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public Task<FileScanResult> ScanAsync(
        string rootPath,
        CancellationToken cancellationToken = default,
        IProgress<FileScanProgress>? progress = null) =>
        Task.Run(() => Scan(rootPath, cancellationToken, progress), cancellationToken);

    public void OpenLocation(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The file no longer exists.", path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    public void OpenParentFolder(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("The file no longer exists.", filePath);
        var folder = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            throw new DirectoryNotFoundException("The containing folder is no longer available.");
        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }

    public Task<FolderSummary> GetFolderSummaryAsync(string folderPath, CancellationToken cancellationToken = default) =>
        Task.Run(() => GetFolderSummary(folderPath, cancellationToken), cancellationToken);

    public void MoveFolderToRecycleBin(string folderPath)
    {
        var folder = ValidateDeletableFolder(folderPath);
        FileSystem.DeleteDirectory(folder, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    public void MoveToRecycleBin(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The file no longer exists.", path);
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
    }

    private static FolderSummary GetFolderSummary(string folderPath, CancellationToken cancellationToken)
    {
        var root = ValidateDeletableFolder(folderPath);
        long size = 0;
        long files = 0;
        long skipped = 0;
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            try
            {
                foreach (var child in Directory.EnumerateDirectories(current))
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
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                        size += info.Length; files++;
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException) { skipped++; }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException) { skipped++; }
        }
        return new FolderSummary(root, size, files, skipped);
    }

    private static string ValidateDeletableFolder(string folderPath)
    {
        var full = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException("The containing folder is no longer available.");
        var root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("NODR will never move a drive root to the Recycle Bin.");
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("NODR will not remove a reparse-point folder directly.");
        return full;
    }

    private static FileScanResult Scan(
        string rootPath,
        CancellationToken cancellationToken,
        IProgress<FileScanProgress>? progress)
    {
        var fullRoot = Path.GetFullPath(rootPath);
        var pathRoot = Path.GetPathRoot(fullRoot);
        var root = string.Equals(fullRoot, pathRoot, StringComparison.OrdinalIgnoreCase)
            ? fullRoot
            : fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("The selected folder is no longer available.");

        var files = new List<FoundFile>();
        var pending = new Stack<string>();
        long filesChecked = 0;
        long directoriesChecked = 0;
        var skipped = 0;
        var progressClock = Stopwatch.StartNew();
        pending.Push(root);

        void Report(string currentDirectory, bool force = false)
        {
            if (progress is null || (!force && progressClock.Elapsed < ProgressInterval))
                return;

            progress.Report(new FileScanProgress(
                filesChecked,
                directoriesChecked,
                files.Count,
                skipped,
                currentDirectory));
            progressClock.Restart();
        }

        Report(root, force: true);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            directoriesChecked++;
            Report(directory);

            try
            {
                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                        {
                            skipped++;
                            continue;
                        }
                        pending.Push(child);
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
                    {
                        skipped++;
                    }
                    Report(directory);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
            {
                skipped++;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    filesChecked++;
                    try
                    {
                        var info = new FileInfo(file);
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            skipped++;
                            continue;
                        }
                        if (info.Length >= DefaultMinimumSizeBytes)
                            files.Add(new FoundFile(info.FullName, info.Length, info.LastWriteTimeUtc));
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
                    {
                        skipped++;
                    }
                    Report(directory);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
            {
                skipped++;
            }
        }

        Report(root, force: true);
        return new FileScanResult(root, files.OrderByDescending(file => file.SizeBytes).ToList(), skipped);
    }
}

public sealed record FolderSummary(string Path, long SizeBytes, long FileCount, long SkippedCount);
