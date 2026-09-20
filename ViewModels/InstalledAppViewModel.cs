using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NODR.Models;
using NODR.Services;
using DrawingIcon = System.Drawing.Icon;
using Imaging = System.Windows.Interop.Imaging;

namespace NODR.ViewModels;

public sealed class InstalledAppViewModel : ViewModelBase
{
    private bool _isBusy;
    private string _status = string.Empty;

    public InstalledAppViewModel(InstalledAppInfo app)
    {
        App = app;
        Icon = TryLoadIcon(app.DisplayIcon, app.InstallLocation);
    }

    public InstalledAppInfo App { get; }
    public string Name => App.Name;
    public string Publisher => string.IsNullOrWhiteSpace(App.Publisher) ? LocalizationService.Instance["Uninstaller.UnknownPublisher"] : App.Publisher;
    public string Version => App.Version;
    public string PublisherLine => string.IsNullOrWhiteSpace(Version) ? Publisher : $"{Publisher} · {Version}";
    public string InstallDateText => App.InstallDate?.ToString("yyyy-MM-dd HH:mm") ?? LocalizationService.Instance["Common.Unknown"];
    public string SizeText => App.EstimatedSizeBytes is > 0 ? ByteFormatter.Format(App.EstimatedSizeBytes.Value) : LocalizationService.Instance["Common.Unknown"];
    public long SortSize => App.EstimatedSizeBytes ?? -1;
    public DateTime SortDate => App.InstallDate ?? DateTime.MinValue;
    public bool CanUninstall => !string.IsNullOrWhiteSpace(App.UninstallString) && !IsBusy;
    public ImageSource? Icon { get; }
    public bool HasIcon => Icon is not null;
    public string FallbackLetter => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1].ToUpperInvariant();
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetField(ref _isBusy, value))
                OnPropertyChanged(nameof(CanUninstall));
        }
    }
    public string Status { get => _status; set => SetField(ref _status, value); }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Publisher));
        OnPropertyChanged(nameof(PublisherLine));
        OnPropertyChanged(nameof(InstallDateText));
        OnPropertyChanged(nameof(SizeText));
    }

    private static ImageSource? TryLoadIcon(string? displayIcon, string? installLocation)
    {
        try
        {
            var path = ExtractIconPath(displayIcon);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
                    return null;
                path = Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
            }
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            using var icon = DrawingIcon.ExtractAssociatedIcon(path);
            if (icon is null)
                return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(24, 24));
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractIconPath(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon))
            return null;
        var expanded = Environment.ExpandEnvironmentVariables(displayIcon.Trim());
        if (expanded.StartsWith('"'))
        {
            var endQuote = expanded.IndexOf('"', 1);
            if (endQuote > 1)
                return expanded[1..endQuote];
        }
        var comma = expanded.LastIndexOf(',');
        if (comma > 2 && int.TryParse(expanded[(comma + 1)..].Trim(), out _))
            expanded = expanded[..comma];
        return expanded.Trim().Trim('"');
    }
}
