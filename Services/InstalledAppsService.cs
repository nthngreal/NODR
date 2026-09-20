using System.IO;
using Microsoft.Win32;
using NODR.Models;

namespace NODR.Services;

public sealed class InstalledAppsService
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public Task<IReadOnlyList<InstalledAppInfo>> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Load(cancellationToken), cancellationToken);

    private static IReadOnlyList<InstalledAppInfo> Load(CancellationToken cancellationToken)
    {
        var apps = new List<InstalledAppInfo>();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                cancellationToken.ThrowIfCancellationRequested();
                ReadHive(apps, hive, view, cancellationToken);
            }
        }

        return apps
            .Where(app => !string.IsNullOrWhiteSpace(app.Name))
            .GroupBy(app => BuildDeduplicationKey(app), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(app => !string.IsNullOrWhiteSpace(app.UninstallString))
                .ThenByDescending(app => app.EstimatedSizeBytes ?? 0)
                .First())
            .OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static void ReadHive(List<InstalledAppInfo> apps, RegistryHive hive, RegistryView view, CancellationToken cancellationToken)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstallRoot = baseKey.OpenSubKey(UninstallPath);
            if (uninstallRoot is null)
                return;

            foreach (var subKeyName in uninstallRoot.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var key = uninstallRoot.OpenSubKey(subKeyName);
                    if (key is null)
                        continue;

                    var displayName = ReadString(key, "DisplayName");
                    if (string.IsNullOrWhiteSpace(displayName))
                        continue;

                    if (ReadInt(key, "SystemComponent") == 1 || !string.IsNullOrWhiteSpace(ReadString(key, "ParentKeyName")))
                        continue;

                    var releaseType = ReadString(key, "ReleaseType");
                    if (releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
                        releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase) ||
                        releaseType.Contains("Security", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var uninstallString = ReadString(key, "UninstallString");
                    var estimatedKilobytes = ReadLong(key, "EstimatedSize");
                    var estimatedBytes = estimatedKilobytes > 0 ? estimatedKilobytes * 1024L : (long?)null;
                    var registryHiveName = hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
                    var registryViewName = view == RegistryView.Registry64 ? "64-bit" : "32-bit";
                    var registryKeyPath = $@"{registryHiveName}\{UninstallPath}\{subKeyName}";

                    var installLocation = EmptyToNull(ReadString(key, "InstallLocation"));
                    var displayIcon = EmptyToNull(ReadString(key, "DisplayIcon"));
                    var fallbackFolder = ResolveFallbackFolder(installLocation, displayIcon)
                        ?? FindKnownInstallFolder(displayName, ReadString(key, "Publisher"));
                    var registryInstallDate = ParseInstallDate(ReadString(key, "InstallDate"));
                    var folderActivityDate = TryGetFolderDate(fallbackFolder);
                    // Windows uninstall entries usually store only yyyyMMdd. Prefer the local folder timestamp
                    // when available so the UI can show a meaningful time; otherwise keep the registry date.
                    var installDate = folderActivityDate ?? registryInstallDate;
                    estimatedBytes ??= TryGetFolderSize(fallbackFolder, cancellationToken);

                    apps.Add(new InstalledAppInfo(
                        $"{registryHiveName}|{registryViewName}|{subKeyName}",
                        displayName.Trim(),
                        ReadString(key, "Publisher").Trim(),
                        ReadString(key, "DisplayVersion").Trim(),
                        installDate,
                        estimatedBytes,
                        string.IsNullOrWhiteSpace(uninstallString) ? null : uninstallString.Trim(),
                        installLocation,
                        displayIcon,
                        registryHiveName,
                        registryViewName,
                        registryKeyPath));
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                {
                    // Skip unreadable entries; a partial list is better than blocking the page.
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // One registry view may be unavailable; keep reading the others.
        }
    }

    private static string BuildDeduplicationKey(InstalledAppInfo app) =>
        $"{app.Name}\u001f{app.Publisher}\u001f{app.Version}\u001f{app.InstallLocation}";

    private static string ReadString(RegistryKey key, string name) => key.GetValue(name)?.ToString() ?? string.Empty;

    private static int ReadInt(RegistryKey key, string name)
    {
        var value = key.GetValue(name);
        return value switch
        {
            int number => number,
            long number => (int)Math.Clamp(number, int.MinValue, int.MaxValue),
            _ => int.TryParse(value?.ToString(), out var parsed) ? parsed : 0
        };
    }

    private static long ReadLong(RegistryKey key, string name)
    {
        var value = key.GetValue(name);
        return value switch
        {
            int number => Math.Max(0, number),
            long number => Math.Max(0, number),
            _ => long.TryParse(value?.ToString(), out var parsed) ? Math.Max(0, parsed) : 0
        };
    }

    private static DateTime? ParseInstallDate(string raw)
    {
        if (raw.Length == 8 && DateTime.TryParseExact(raw, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var parsed))
            return parsed;
        return null;
    }

    private static string? ResolveFallbackFolder(string? installLocation, string? displayIcon)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
                return Path.GetFullPath(Environment.ExpandEnvironmentVariables(installLocation.Trim().Trim('"')));
            if (string.IsNullOrWhiteSpace(displayIcon)) return null;
            var icon = Environment.ExpandEnvironmentVariables(displayIcon.Trim());
            if (icon.StartsWith('"')) { var q = icon.IndexOf('"', 1); if (q > 1) icon = icon[1..q]; }
            else { var comma = icon.LastIndexOf(','); if (comma > 2 && int.TryParse(icon[(comma + 1)..].Trim(), out _)) icon = icon[..comma]; }
            icon = icon.Trim().Trim('"');
            var folder = File.Exists(icon) ? Path.GetDirectoryName(icon) : null;
            return !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) ? folder : null;
        }
        catch { return null; }
    }


    // Conservative local fallback for registry entries that omit InstallLocation/DisplayIcon.
    // Only immediate child folders with an exact normalized app-name match are accepted;
    // fuzzy matches are intentionally rejected to avoid attributing another app's files.
    private static string? FindKnownInstallFolder(string displayName, string publisher)
    {
        var target = NormalizeFolderIdentity(displayName);
        if (string.IsNullOrWhiteSpace(target)) return null;

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        }.Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)).Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    try
                    {
                        if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
                        if (NormalizeFolderIdentity(Path.GetFileName(dir)) == target) return dir;
                    }
                    catch { }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
        }
        return null;
    }

    private static string NormalizeFolderIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var text = value.Trim();
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s*\((?:User|x64|x86|64-bit|32-bit)\)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+v?\d+(?:[._-]\d+)+\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return new string(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    }

    private static DateTime? TryGetFolderDate(string? folder)
    {
        try { return !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) ? Directory.GetLastWriteTime(folder) : null; }
        catch { return null; }
    }

    private static long? TryGetFolderSize(string? folder, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        try
        {
            long total = 0; var pending = new Stack<string>(); pending.Push(folder); var seen = 0;
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = pending.Pop();
                try
                {
                    foreach (var file in Directory.EnumerateFiles(current)) { cancellationToken.ThrowIfCancellationRequested(); try { total += new FileInfo(file).Length; } catch { } if (++seen > 250000) return total; }
                    foreach (var dir in Directory.EnumerateDirectories(current)) { try { if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) == 0) pending.Push(dir); } catch { } }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
            }
            return total > 0 ? total : null;
        }
        catch { return null; }
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
