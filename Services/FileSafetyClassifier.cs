using System.IO;
using System.Text.RegularExpressions;
using NODR.Models;

namespace NODR.Services;

public sealed class FileSafetyClassifier
{
    // Lightweight local rule base: compiled regexes cover common cache path fragments, while
    // extension rules handle portable file types. unknown files remain Unknown and require user review or filename search.
    private static readonly Regex PipCacheRule = BuildPathRule(@"\appdata\local\pip\cache\", @"\.cache\pip\");
    private static readonly Regex NvidiaCacheRule = BuildPathRule(@"\appdata\local\nvidia\dxcache\", @"\appdata\local\nvidia\glcache\", @"\appdata\local\nvidia corporation\nv_cache\");
    private static readonly Regex NpmCacheRule = BuildPathRule(@"\npm-cache\", @"\appdata\local\npm-cache\", @"\.npm\_cacache\");
    private static readonly Regex NuGetRule = BuildPathRule(@"\nuget\packages\", @"\.nuget\packages\");
    private static readonly Regex TempRule = BuildPathRule(@"\appdata\local\temp\", @"\windows\temp\");
    private static readonly Regex GenericCacheRule = BuildPathRule(@"\cache\", @"\caches\", @"\code cache\", @"\gpucache\");

    private readonly LocalizationService _loc = LocalizationService.Instance;

    public FileSafetyAssessment Assess(string path)
    {
        var full = Path.GetFullPath(path);
        var normalized = full.Replace('/', '\\');
        var lower = normalized.ToLowerInvariant();
        var name = Path.GetFileName(lower);
        var ext = Path.GetExtension(lower);

        if (lower.Contains("\\appdata\\local\\obsidian-updater\\") &&
            (name is "installer.exe" || ext is ".exe" or ".zip" or ".7z"))
            return Safe("Safety.Obsidian.Category", "Safety.Obsidian.Effect", "Safety.Obsidian.Why");

        if (PipCacheRule.IsMatch(normalized))
            return Safe("Safety.Python.Category", "Safety.Python.Effect", "Safety.Python.Why");

        if (NvidiaCacheRule.IsMatch(normalized))
            return Safe("Safety.Nvidia.Category", "Safety.Nvidia.Effect", "Safety.Nvidia.Why");

        if (lower.Contains("\\android\\sdk\\system-images\\") || lower.Contains("\\android\\sdk\\system_images\\"))
            return Block("Safety.AndroidSdk.Category", "Safety.AndroidSdk.Effect", "Safety.AndroidSdk.Why");

        if (NpmCacheRule.IsMatch(normalized))
            return Safe("Safety.Npm.Category", "Safety.Npm.Effect", "Safety.Npm.Why");

        if (NuGetRule.IsMatch(normalized))
            return Review("Safety.NuGet.Category", "Safety.NuGet.Effect", "Safety.NuGet.Why");

        if (TempRule.IsMatch(normalized))
            return Safe("Safety.Temp.Category", "Safety.Temp.Effect", "Safety.Temp.Why");

        if ((lower.Contains("\\google\\chrome\\user data\\") || lower.Contains("\\microsoft\\edge\\user data\\") || lower.Contains("\\brave-browser\\user data\\")) &&
            GenericCacheRule.IsMatch(normalized))
            return Safe("Safety.Browser.Category", "Safety.Browser.Effect", "Safety.Browser.Why");

        if (GenericCacheRule.IsMatch(normalized))
            return Review("Safety.AppCache.Category", "Safety.AppCache.Effect", "Safety.AppCache.Why");

        if ((ext is ".qcow2" or ".img") && (lower.Contains("\\.android\\avd\\") || lower.Contains("\\android\\avd\\")))
            return Block("Safety.Android.Category", "Safety.Android.Effect", "Safety.Android.Why");

        if (ext is ".vhd" or ".vhdx" or ".vmdk" or ".vdi" or ".qcow" or ".qcow2")
        {
            if (lower.Contains("\\docker\\") || name.Contains("docker"))
                return Block("Safety.Docker.Category", "Safety.Docker.Effect", "Safety.Docker.Why");
            if (lower.Contains("\\wsl\\") || name == "ext4.vhdx")
                return Block("Safety.Wsl.Category", "Safety.Wsl.Effect", "Safety.Wsl.Why");
            return Block("Safety.VirtualDisk.Category", "Safety.VirtualDisk.Effect", "Safety.VirtualDisk.Why");
        }

        if (ext is ".db" or ".sqlite" or ".sqlite3" or ".mdb" or ".accdb")
            return Block("Safety.Database.Category", "Safety.Database.Effect", "Safety.Database.Why");

        if ((ext is ".sys" or ".dll") || (ext == ".exe" && lower.Contains("\\windows\\")))
            return Block("Safety.System.Category", "Safety.System.Effect", "Safety.System.Why");

        if (ext is ".safetensors" or ".ckpt" or ".pth" or ".pt" or ".onnx" or ".gguf")
            return Review("Safety.Model.Category", "Safety.Model.Effect", "Safety.Model.Why");

        if (ext is ".iso" or ".zip" or ".7z" or ".rar" or ".tar" or ".gz")
            return Review("Safety.Archive.Category", "Safety.Archive.Effect", "Safety.Archive.Why");

        if (ext is ".log" or ".dmp" or ".mdmp")
            return Review("Safety.Log.Category", "Safety.Log.Effect", "Safety.Log.Why");

        if (lower.Contains("\\program files\\") || lower.Contains("\\program files (x86)\\"))
            return Block("Safety.Program.Category", "Safety.Program.Effect", "Safety.Program.Why");

        return new FileSafetyAssessment(
            FileSafetyLevel.Unknown,
            _loc["Safety.Unknown.Category"],
            _loc["Safety.Unknown"],
            _loc["Safety.Unknown.Effect"],
            _loc["Safety.Unknown.Why"],
            "?",
            false);
    }

    private static Regex BuildPathRule(params string[] pathFragments) =>
        new(string.Join("|", pathFragments.Select(Regex.Escape)), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private FileSafetyAssessment Safe(string categoryKey, string effectKey, string whyKey) =>
        new(FileSafetyLevel.SafeToRemove, _loc[categoryKey], _loc["Safety.Safe"], _loc[effectKey], _loc[whyKey], "✓", true);

    private FileSafetyAssessment Review(string categoryKey, string effectKey, string whyKey) =>
        new(FileSafetyLevel.ReviewFirst, _loc[categoryKey], _loc["Safety.Review"], _loc[effectKey], _loc[whyKey], "!", false);

    private FileSafetyAssessment Block(string categoryKey, string effectKey, string whyKey) =>
        new(FileSafetyLevel.DoNotRemoveDirectly, _loc[categoryKey], _loc["Safety.Block"], _loc[effectKey], _loc[whyKey], "×", false);
}
