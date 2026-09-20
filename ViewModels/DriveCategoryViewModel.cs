using MediaBrush = System.Windows.Media.Brush;
using MediaBrushConverter = System.Windows.Media.BrushConverter;
using NODR.Models;
using NODR.Services;

namespace NODR.ViewModels;

public sealed class DriveCategoryViewModel : ViewModelBase
{
    private readonly string _rawName;
    private readonly LocalizationService _loc = LocalizationService.Instance;

    public DriveCategoryViewModel(DriveCategoryUsage usage, long usedBytes, Action<DriveCategoryViewModel>? onOpen = null)
    {
        _rawName = usage.Name;
        Bytes = usage.Bytes;
        SizeText = ByteFormatter.Format(usage.Bytes);
        Percent = usedBytes <= 0 ? 0 : Math.Min(100, (double)usage.Bytes / usedBytes * 100.0);
        Brush = (MediaBrush)new MediaBrushConverter().ConvertFromString(usage.ColorHex)!;
        OpenCommand = new RelayCommand(() => onOpen?.Invoke(this));
    }

    public string RawName => _rawName;
    public string Name => _rawName switch
    {
        "System" => _loc["Drive.Category.System"],
        "Programs" => _loc["Drive.Category.Programs"],
        "Cache & development" => _loc["Drive.Category.CacheDev"],
        "Media" => _loc["Drive.Category.Media"],
        "Documents & archives" => _loc["Drive.Category.Documents"],
        "Virtual disks & VM data" => _loc["Drive.Category.Virtual"],
        "Installers & disk images" => _loc["Drive.Category.Installers"],
        _ => _loc["Drive.Category.Other"]
    };
    public long Bytes { get; }
    public string SizeText { get; }
    public double Percent { get; }
    public string PercentText => _loc.Format("Drive.PercentUsed", Percent);
    public MediaBrush Brush { get; }
    public RelayCommand OpenCommand { get; }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(PercentText));
    }
}
