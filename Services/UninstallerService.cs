using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using NODR.Models;

namespace NODR.Services;

public sealed class UninstallerService
{
    private readonly FileFinderService _fileService = new();

    public async Task RunStockUninstallerAsync(InstalledAppInfo app, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(app.UninstallString))
            return;

        var launch = BuildInteractiveUninstallLaunch(app.UninstallString);
        var startInfo = new ProcessStartInfo
        {
            FileName = launch.FileName,
            Arguments = launch.Arguments,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };

        if (Path.IsPathFullyQualified(launch.FileName))
        {
            if (!File.Exists(launch.FileName))
                throw new FileNotFoundException("The registered uninstaller no longer exists.", launch.FileName);

            var workingDirectory = Path.GetDirectoryName(launch.FileName);
            if (!string.IsNullOrWhiteSpace(workingDirectory))
                startInfo.WorkingDirectory = workingDirectory;
        }

        // Process.Start with UseShellExecute can synchronously invoke shell/DDE/UAC work.
        // Keep that work off WPF's dispatcher so clicking “Open uninstaller” never freezes NODR.
        var process = await Task.Run(() => Process.Start(startInfo), cancellationToken);
        if (process is null)
            throw new InvalidOperationException("Windows could not start the registered uninstaller.");

        using (process)
        {
            // Some vendor uninstallers keep a bootstrap/background process alive after their
            // visible window has already closed. Waiting only for Process.Exit can therefore
            // leave NODR stuck forever. Observe both the process UI and real uninstall evidence.
            var evidence = CaptureUninstallEvidence(app);
            var sawWindow = false;
            DateTimeOffset? windowGoneSince = null;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (process.HasExited)
                        break;

                    process.Refresh();
                    var hasWindow = process.MainWindowHandle != IntPtr.Zero;
                    if (hasWindow)
                    {
                        sawWindow = true;
                        windowGoneSince = null;
                    }
                    else if (sawWindow)
                    {
                        windowGoneSince ??= DateTimeOffset.UtcNow;
                        if (DateTimeOffset.UtcNow - windowGoneSince >= TimeSpan.FromSeconds(2))
                            break;
                    }
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                // Successful uninstall is also a completion signal even when a vendor leaves
                // a helper/bootstrap process running in the background.
                if (HasUninstallStateChanged(app, evidence))
                    break;

                await Task.Delay(300, cancellationToken);
            }
        }
    }

    private static bool HasUninstallStateChanged(InstalledAppInfo app, UninstallEvidenceSnapshot evidence)
    {
        if (!RegistryEntryStillExists(app))
            return true;

        try
        {
            if (evidence.DisplayIconExecutableExisted
                && !string.IsNullOrWhiteSpace(evidence.DisplayIconExecutable)
                && !File.Exists(evidence.DisplayIconExecutable))
                return true;

            if (evidence.InstallLocationExisted
                && !string.IsNullOrWhiteSpace(evidence.InstallLocation)
                && !Directory.Exists(evidence.InstallLocation))
                return true;
        }
        catch
        {
            // Evidence checks are best-effort; registry state remains the fail-closed signal.
        }

        return false;
    }

    public UninstallEvidenceSnapshot CaptureUninstallEvidence(InstalledAppInfo app)
    {
        string? installLocation = null;
        var installLocationExisted = false;
        if (TryGetInstallLocation(app, out var installPath))
        {
            installLocation = installPath;
            try { installLocationExisted = Directory.Exists(installPath); } catch { }
        }

        string? displayIconExecutable = null;
        var displayIconExecutableExisted = false;
        if (TryGetDisplayIconExecutable(app.DisplayIcon, out var iconPath))
        {
            displayIconExecutable = iconPath;
            try { displayIconExecutableExisted = File.Exists(iconPath); } catch { }
        }

        return new UninstallEvidenceSnapshot(
            installLocation,
            installLocationExisted,
            displayIconExecutable,
            displayIconExecutableExisted);
    }

    public async Task<UninstallResidualScanResult> ScanResidualsAsync(InstalledAppInfo app, UninstallEvidenceSnapshot? evidence = null, CancellationToken cancellationToken = default)
    {
        // Vendor uninstallers can finish their visible flow before background cleanup has
        // removed the last folder/registry entry. Scan twice and trust the later filesystem
        // state so the modal never presents a stale snapshot as a real leftover.
        await Task.Run(async () => await ScanResidualsCoreAsync(app, evidence, cancellationToken), cancellationToken);
        await Task.Delay(1200, cancellationToken);
        return await Task.Run(async () => await ScanResidualsCoreAsync(app, evidence, cancellationToken), cancellationToken);
    }

    // Recycle Bin operations use the Windows shell and must originate from the WPF STA thread.
    // Running Microsoft.VisualBasic.FileIO on Task.Run (MTA) made folder cleanup silently skip.
    public Task<ResidualCleanupResult> CleanResidualsAsync(IEnumerable<UninstallResidual> residuals, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CleanResiduals(residuals, cancellationToken));
    }

    private async Task<UninstallResidualScanResult> ScanResidualsCoreAsync(InstalledAppInfo app, UninstallEvidenceSnapshot? evidence, CancellationToken cancellationToken)
    {
        var registryEntryRemains = RegistryEntryStillExists(app);
        var originalRegistryEntryCanClean = registryEntryRemains && CanSafelyRemoveOriginalRegistryEntry(evidence);
        var uninstallConfirmed = !registryEntryRemains || originalRegistryEntryCanClean || CanSafelyRemoveOriginalRegistryEntry(evidence);
        var items = new List<UninstallResidual>();

        if (uninstallConfirmed)
        {
            foreach (var candidate in BuildResidualFolderCandidates(app).DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(candidate.Path) || IsUnsafeFolder(candidate.Path)) continue;
                try
                {
                    var summary = await _fileService.GetFolderSummaryAsync(candidate.Path, cancellationToken);
                    items.Add(new UninstallResidual(UninstallResidualKind.Folder, summary.Path, summary.Path, summary.SizeBytes, summary.FileCount, summary.SkippedCount, true, candidate.Confidence));
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or System.Security.SecurityException) { }
            }

            foreach (var candidate in FindProductRegistryResiduals(app))
                items.Add(new UninstallResidual(UninstallResidualKind.RegistryKey, candidate.Path, null, 0, 0, 0, true, candidate.Confidence));
        }

        if (registryEntryRemains && originalRegistryEntryCanClean)
            items.Add(new UninstallResidual(UninstallResidualKind.RegistryKey, FormatOriginalRegistryPath(app), null, 0, 0, 0, true, ResidualConfidence.Safe));

        var deduped = items.GroupBy(x => x.DisplayPath, StringComparer.OrdinalIgnoreCase).Select(g => g.OrderBy(x => x.Confidence).First()).ToArray();
        return new UninstallResidualScanResult(deduped, deduped.Sum(x => x.SizeBytes), deduped.Sum(x => x.FileCount), registryEntryRemains, originalRegistryEntryCanClean);
    }

    private ResidualCleanupResult CleanResiduals(IEnumerable<UninstallResidual> residuals, CancellationToken cancellationToken)
    {
        var removed = 0;
        var skipped = 0;
        var removedDisplayPaths = new List<string>();
        foreach (var residual in residuals.Where(item => item.CanClean))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The filesystem/registry is the source of truth. A vendor uninstaller may have
            // removed this item after NODR scanned it; treat an already-gone item as resolved.
            if (!ResidualStillExists(residual))
            {
                removed++;
                removedDisplayPaths.Add(residual.DisplayPath);
                continue;
            }

            try
            {
                if (residual.Kind == UninstallResidualKind.Folder && !string.IsNullOrWhiteSpace(residual.FileSystemPath))
                    _fileService.MoveFolderToRecycleBin(residual.FileSystemPath!);
                else if (residual.Kind == UninstallResidualKind.RegistryKey)
                    DeleteRegistryResidual(residual.DisplayPath);
                else
                    continue;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or System.Security.SecurityException)
            {
                // Do not trust the API exception/result alone: verify the real post-condition.
            }

            if (!ResidualStillExists(residual))
            {
                removed++;
                removedDisplayPaths.Add(residual.DisplayPath);
            }
            else
            {
                skipped++;
            }
        }
        return new ResidualCleanupResult(removed, skipped, removedDisplayPaths);
    }

    private static bool ResidualStillExists(UninstallResidual residual)
    {
        try
        {
            if (residual.Kind == UninstallResidualKind.Folder)
                return !string.IsNullOrWhiteSpace(residual.FileSystemPath) && Directory.Exists(residual.FileSystemPath);

            if (residual.Kind == UninstallResidualKind.RegistryKey)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    residual.DisplayPath,
                    @"^(HKCU|HKLM)(?: \((32|64)-bit\))?\\(.+)$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!match.Success) return true;
                var hive = match.Groups[1].Value.Equals("HKCU", StringComparison.OrdinalIgnoreCase) ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
                var view = match.Groups[2].Value == "32" ? RegistryView.Registry32 : RegistryView.Registry64;
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(match.Groups[3].Value);
                return key is not null;
            }
        }
        catch
        {
            return true; // fail closed
        }

        return true;
    }

    private static string FormatOriginalRegistryPath(InstalledAppInfo app)
    {
        var prefix = app.RegistryHive + "\\";
        var path = app.RegistryKeyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? app.RegistryKeyPath[prefix.Length..] : app.RegistryKeyPath;
        return $"{app.RegistryHive} ({app.RegistryView})\\{path}";
    }

    private static UninstallLaunch BuildInteractiveUninstallLaunch(string command)
    {
        var (fileName, arguments) = SplitExecutableAndArguments(command);
        var executableName = Path.GetFileName(fileName);

        if (executableName.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            // Keep Windows Installer interactive: convert maintenance/install mode to
            // uninstall mode and remove UI-suppressing switches that may be stored in the
            // registry by third-party installers.
            arguments = System.Text.RegularExpressions.Regex.Replace(arguments, @"(?i)(^|\s)/I(?=\s|\{)", "$1/X");
            arguments = System.Text.RegularExpressions.Regex.Replace(
                arguments,
                @"(?i)(^|\s)/(?:quiet|passive|q(?:n|b!?|r|f)?)(?=\s|$)",
                "$1");
        }
        else if (System.Text.RegularExpressions.Regex.IsMatch(executableName, @"(?i)^unins\d*\.exe$"))
        {
            // Inno Setup uninstallers commonly use unins000.exe. Strip only the switches
            // whose documented purpose is to suppress the normal interactive UI.
            arguments = System.Text.RegularExpressions.Regex.Replace(
                arguments,
                @"(?i)(^|\s)/(?:VERYSILENT|SILENT|SUPPRESSMSGBOXES)(?=\s|$)",
                "$1");
        }

        arguments = System.Text.RegularExpressions.Regex.Replace(arguments, @"\s{2,}", " ").Trim();
        return new UninstallLaunch(fileName, arguments);
    }

    private static (string FileName, string Arguments) SplitExecutableAndArguments(string command)
    {
        var expanded = Environment.ExpandEnvironmentVariables(command.Trim());
        if (expanded.Length == 0)
            throw new InvalidOperationException("The registered uninstall command is empty.");

        if (expanded[0] == '"')
        {
            var closingQuote = expanded.IndexOf('"', 1);
            if (closingQuote <= 1)
                throw new InvalidOperationException("The registered uninstall command has invalid quotes.");

            var quotedFile = expanded[1..closingQuote].Trim();
            if (quotedFile.Length == 0)
                throw new InvalidOperationException("The registered uninstall command has no executable.");

            return (quotedFile, expanded[(closingQuote + 1)..].Trim());
        }

        // Many legacy uninstall records incorrectly leave a Program Files path unquoted.
        // Prefer the first executable-style extension so a path with spaces still launches
        // directly instead of being delegated to a hidden cmd.exe parser.
        foreach (var extension in new[] { ".exe", ".com", ".cmd", ".bat" })
        {
            var extensionIndex = expanded.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
            if (extensionIndex < 0)
                continue;

            var end = extensionIndex + extension.Length;
            var fileName = expanded[..end].Trim().Trim('"');
            var arguments = expanded[end..].Trim();
            if (fileName.Length > 0)
                return (fileName, arguments);
        }

        var firstSpace = expanded.IndexOfAny(new[] { ' ', '\t' });
        if (firstSpace < 0)
            return (expanded.Trim('"'), string.Empty);

        var fallbackFile = expanded[..firstSpace].Trim().Trim('"');
        if (fallbackFile.Length == 0)
            throw new InvalidOperationException("The registered uninstall command has no executable.");
        return (fallbackFile, expanded[(firstSpace + 1)..].Trim());
    }

    private sealed record UninstallLaunch(string FileName, string Arguments);

    private static bool CanSafelyRemoveOriginalRegistryEntry(UninstallEvidenceSnapshot? evidence)
    {
        if (evidence is null)
            return false;

        // Evidence must represent a real before -> after transition caused by the just-closed
        // uninstaller. A path that was already missing before launch proves nothing.
        if (evidence.InstallLocationExisted && !string.IsNullOrWhiteSpace(evidence.InstallLocation))
        {
            try
            {
                if (!Directory.Exists(evidence.InstallLocation))
                    return true;
            }
            catch
            {
                // Keep looking for the more specific executable transition below.
            }
        }

        if (evidence.DisplayIconExecutableExisted && !string.IsNullOrWhiteSpace(evidence.DisplayIconExecutable))
        {
            try { return !File.Exists(evidence.DisplayIconExecutable); }
            catch { return false; }
        }

        return false;
    }

    private static bool TryGetInstallLocation(InstalledAppInfo app, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(app.InstallLocation)) return false;
        try
        {
            path = Environment.ExpandEnvironmentVariables(app.InstallLocation.Trim().Trim('"'));
            return Path.IsPathFullyQualified(path);
        }
        catch { return false; }
    }

    private static bool TryGetDisplayIconExecutable(string? displayIcon, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(displayIcon)) return false;
        try
        {
            var value = Environment.ExpandEnvironmentVariables(displayIcon.Trim());
            if (value.StartsWith('"'))
            {
                var end = value.IndexOf('"', 1);
                if (end > 1) value = value[1..end];
            }
            else
            {
                var comma = value.LastIndexOf(',');
                if (comma > 2 && int.TryParse(value[(comma + 1)..].Trim(), out _)) value = value[..comma];
                value = value.Trim().Trim('"');
            }
            if (!value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !Path.IsPathFullyQualified(value)) return false;
            path = value;
            return true;
        }
        catch { return false; }
    }

    private static void DeleteRegistryResidual(string displayPath)
    {
        var match = System.Text.RegularExpressions.Regex.Match(displayPath, @"^(HKCU|HKLM)(?: \((32|64)-bit\))?\\(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) throw new InvalidOperationException("Unsupported registry residual path.");
        var hive = match.Groups[1].Value.Equals("HKCU", StringComparison.OrdinalIgnoreCase) ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
        var view = match.Groups[2].Value == "32" ? RegistryView.Registry32 : RegistryView.Registry64;
        var path = match.Groups[3].Value;
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("SOFTWARE\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe registry residual path.");
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        baseKey.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
    }

    private sealed record ResidualCandidate(string Path, ResidualConfidence Confidence);
    private sealed record RegistryResidualCandidate(string Path, ResidualConfidence Confidence);

    private static IEnumerable<ResidualCandidate> BuildResidualFolderCandidates(InstalledAppInfo app)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(x => !string.IsNullOrWhiteSpace(x) && Directory.Exists(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        if (!string.IsNullOrWhiteSpace(app.InstallLocation))
        {
            var install = TryNormalizeDirectoryPath(app.InstallLocation);
            if (!string.IsNullOrWhiteSpace(install)) yield return new(install, ResidualConfidence.Safe);
        }

        var names = BuildExactFolderNames(app.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var root in roots)
            foreach (var name in names)
                yield return new(Path.Combine(root, name), ResidualConfidence.Safe);

        var publisher = NormalizePublisher(app.Publisher);
        if (!string.IsNullOrWhiteSpace(publisher) && IsSafeLeafName(publisher))
        {
            foreach (var root in roots)
            {
                var vendor = Path.Combine(root, publisher);
                foreach (var name in names)
                    yield return new(Path.Combine(vendor, name), ResidualConfidence.Safe);
                // Never auto-select a vendor root: it may contain other products. Surface only for review.
                if (Directory.Exists(vendor) && DirectoryNameStronglyMatches(vendor, app))
                    yield return new(vendor, ResidualConfidence.Review);
            }
        }
    }

    private static IEnumerable<RegistryResidualCandidate> FindProductRegistryResiduals(InstalledAppInfo app)
    {
        var names = BuildExactFolderNames(app.Name).ToArray();
        var publisher = NormalizePublisher(app.Publisher);
        if (string.IsNullOrWhiteSpace(publisher) || names.Length == 0) yield break;
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            RegistryKey? software = null;
            try { software = RegistryKey.OpenBaseKey(hive, view).OpenSubKey("SOFTWARE"); } catch { }
            using (software)
            {
                if (software is null) continue;
                var vendorName = software.GetSubKeyNames().FirstOrDefault(x => string.Equals(NormalizePublisher(x), publisher, StringComparison.OrdinalIgnoreCase));
                if (vendorName is null) continue;
                using var vendor = software.OpenSubKey(vendorName);
                if (vendor is null) continue;
                foreach (var sub in vendor.GetSubKeyNames())
                {
                    if (!names.Any(n => StrongNameMatch(sub, n))) continue;
                    var prefix = hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
                    var bits = view == RegistryView.Registry32 ? "32-bit" : "64-bit";
                    yield return new($@"{prefix} ({bits})\SOFTWARE\{vendorName}\{sub}", ResidualConfidence.Safe);
                }
            }
        }
    }

    private static string NormalizePublisher(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var s = System.Text.RegularExpressions.Regex.Replace(value, @"(?i)\b(incorporated|inc\.?|llc|ltd\.?|limited|corp\.?|corporation|software|technologies)\b", " ");
        return System.Text.RegularExpressions.Regex.Replace(s, @"[^\p{L}\p{N}]+", " ").Trim();
    }

    private static bool StrongNameMatch(string candidate, string product)
    {
        static string N(string x) => System.Text.RegularExpressions.Regex.Replace(x, @"[^\p{L}\p{N}]+", "").ToLowerInvariant();
        var a=N(candidate); var b=N(product);
        return a.Length >= 4 && b.Length >= 4 && (a == b || (a.Length >= 6 && b.Length >= 6 && (a.Contains(b) || b.Contains(a))));
    }

    private static bool DirectoryNameStronglyMatches(string path, InstalledAppInfo app)
    {
        var leaf = Path.GetFileName(path);
        return BuildExactFolderNames(app.Name).Any(n => StrongNameMatch(leaf, n));
    }

    private static IEnumerable<string> BuildExactFolderNames(string displayName)
    {
        var original = displayName.Trim();
        if (IsSafeLeafName(original))
            yield return original;

        var withoutParen = original;
        var paren = withoutParen.LastIndexOf(" (", StringComparison.Ordinal);
        if (paren > 2)
        {
            withoutParen = withoutParen[..paren].Trim();
            if (!string.Equals(withoutParen, original, StringComparison.OrdinalIgnoreCase) && IsSafeLeafName(withoutParen))
                yield return withoutParen;
        }

        var parts = withoutParen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1 && LooksLikeVersion(parts[^1]))
        {
            var withoutVersion = string.Join(' ', parts[..^1]).Trim();
            if (withoutVersion.Length >= 3 && IsSafeLeafName(withoutVersion))
                yield return withoutVersion;
        }
    }

    private static bool IsSafeLeafName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || Path.IsPathRooted(value))
            return false;
        if (value.IndexOf(Path.DirectorySeparatorChar) >= 0 || value.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            return false;
        return value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static bool LooksLikeVersion(string value)
    {
        var hasDigit = false;
        foreach (var ch in value)
        {
            if (char.IsDigit(ch)) { hasDigit = true; continue; }
            if (ch is '.' or '-' or '_' or 'v' or 'V') continue;
            return false;
        }
        return hasDigit;
    }

    private static string? TryNormalizeDirectoryPath(string value)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
            return Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return null;
        }
    }

    private static bool RegistryEntryStillExists(InstalledAppInfo app)
    {
        try
        {
            var hive = app.RegistryHive == "HKCU" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
            var view = app.RegistryView == "32-bit" ? RegistryView.Registry32 : RegistryView.Registry64;
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            var prefix = app.RegistryHive + "\\";
            var path = app.RegistryKeyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? app.RegistryKeyPath[prefix.Length..]
                : app.RegistryKeyPath;
            using var key = baseKey.OpenSubKey(path);
            return key is not null;
        }
        catch
        {
            // Fail closed: if we cannot verify that the uninstall record disappeared, do not offer folders for deletion.
            return true;
        }
    }

    private static bool IsUnsafeFolder(string folder)
    {
        try
        {
            var full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                return true;
            return (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true;
        }
    }
}
