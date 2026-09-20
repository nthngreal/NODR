using System.IO;
using NODR.Models;
using NODR.Services;

namespace NODR.ViewModels;

public sealed class DiskItemViewModel : ViewModelBase
{
    private double _usedPercent;
    private string _usageText = string.Empty;
    private string _displayName = string.Empty;
    private bool _isLowSpace;
    private long _usedBytes;
    private long _totalBytes;
    private string _fileSystem = "Unknown";

    public DiskItemViewModel(DiskSnapshot snapshot) => Update(snapshot);

    public string RootName { get; private set; } = string.Empty;
    public string RootPath => RootName + "\\";
    public long UsedBytes { get => _usedBytes; private set => SetField(ref _usedBytes, value); }
    public long TotalBytes { get => _totalBytes; private set => SetField(ref _totalBytes, value); }
    public string UsedText => ByteFormatter.Format(UsedBytes);
    public string TotalText => ByteFormatter.Format(TotalBytes);
    public string FileSystem { get => _fileSystem; private set => SetField(ref _fileSystem, value); }

    public string DisplayName { get => _displayName; private set => SetField(ref _displayName, value); }
    public double UsedPercent { get => _usedPercent; private set => SetField(ref _usedPercent, value); }
    public string UsageText { get => _usageText; private set => SetField(ref _usageText, value); }
    public bool IsLowSpace { get => _isLowSpace; private set => SetField(ref _isLowSpace, value); }
    public string UsageBrush => UsedPercent > 90 ? "#EF4444" : UsedPercent >= 80 ? "#F59E0B" : "#10B981";

    public void Update(DiskSnapshot snapshot)
    {
        RootName = snapshot.Name.TrimEnd('\\');
        DisplayName = string.IsNullOrWhiteSpace(snapshot.VolumeLabel) ? RootName : $"{RootName}  {snapshot.VolumeLabel}";
        UsedBytes = snapshot.UsedBytes;
        TotalBytes = snapshot.TotalBytes;
        UsedPercent = snapshot.UsedPercent;
        var freePercent = snapshot.TotalBytes == 0 ? 100 : (double)(snapshot.TotalBytes - snapshot.UsedBytes) / snapshot.TotalBytes * 100.0;
        IsLowSpace = freePercent < 10;
        UsageText = $"{UsedPercent:0}% · {ByteFormatter.Format(snapshot.UsedBytes)} / {ByteFormatter.Format(snapshot.TotalBytes)}";
        OnPropertyChanged(nameof(UsageBrush));
        try { FileSystem = new DriveInfo(RootPath).DriveFormat; } catch { FileSystem = "Unknown"; }
        OnPropertyChanged(nameof(RootName));
        OnPropertyChanged(nameof(RootPath));
        OnPropertyChanged(nameof(UsedText));
        OnPropertyChanged(nameof(TotalText));
    }
}
