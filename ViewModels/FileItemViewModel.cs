using System.IO;
using NODR.Models;
using NODR.Services;

namespace NODR.ViewModels;

public sealed class FileItemViewModel : ViewModelBase
{
    private readonly FileFinderService _service;
    private readonly FileSafetyClassifier _classifier;
    private readonly Action<FileItemViewModel> _onRemoved;
    private readonly Action<FileItemViewModel> _onDetails;
    private readonly Action _onSelectionChanged;
    private readonly Action<FileItemViewModel> _onQuickRecycleRequested;
    private string _status = string.Empty;
    private bool _isSelected;
    private bool _isRemoving;
    private FileSafetyAssessment _safety;

    public FileItemViewModel(
        FoundFile file,
        FileFinderService service,
        FileSafetyClassifier classifier,
        Action<FileItemViewModel> onRemoved,
        Action<FileItemViewModel> onDetails,
        Action onSelectionChanged,
        Action<FileItemViewModel> onQuickRecycleRequested)
    {
        File = file;
        _service = service;
        _classifier = classifier;
        _onRemoved = onRemoved;
        _onDetails = onDetails;
        _onSelectionChanged = onSelectionChanged;
        _onQuickRecycleRequested = onQuickRecycleRequested;
        _safety = classifier.Assess(file.Path);
        OpenLocationCommand = new RelayCommand(OpenLocation);
        OpenParentFolderCommand = new RelayCommand(OpenParentFolder);
        DetailsCommand = new RelayCommand(() => _onDetails(this));
        RecycleCommand = new RelayCommand(RecycleQuick, () => Safety.AllowQuickRecycle);
    }

    public FoundFile File { get; }
    public FileSafetyAssessment Safety => _safety;
    public string Name => Path.GetFileName(File.Path);
    public string Folder => Path.GetDirectoryName(File.Path) ?? string.Empty;
    public string FullPath => File.Path;
    public string SizeText => ByteFormatter.Format(File.SizeBytes);
    public string ModifiedText => File.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd");
    public string SafetyLabel => Safety.Label;
    public string SafetyCategory => Safety.Category;
    public string SafetyEffect => Safety.Effect;
    public string SafetyWhy => Safety.Why;
    public string SafetyIcon => Safety.IconGlyph;
    public string SafetyForeground => Safety.Level switch
    {
        FileSafetyLevel.SafeToRemove => "#10B981",
        FileSafetyLevel.ReviewFirst => "#E0B66A",
        FileSafetyLevel.DoNotRemoveDirectly => "#E07A7A",
        _ => "#9CA3AF"
    };
    public bool CanQuickRecycle => Safety.AllowQuickRecycle;
    public bool CanOpenCleanup => Safety.Level == FileSafetyLevel.SafeToRemove &&
                                  FullPath.Contains("nvidia", StringComparison.OrdinalIgnoreCase) &&
                                  FullPath.Contains("cache", StringComparison.OrdinalIgnoreCase);
    public bool CanSearchFileName => Safety.Level is FileSafetyLevel.Unknown or FileSafetyLevel.ReviewFirst;
    public bool IsRemoving { get => _isRemoving; set => SetField(ref _isRemoving, value); }
    public bool CanRecycleFromDetails => Safety.Level is FileSafetyLevel.SafeToRemove or FileSafetyLevel.ReviewFirst or FileSafetyLevel.Unknown;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!CanQuickRecycle && value)
                return;
            if (SetField(ref _isSelected, value))
                _onSelectionChanged();
        }
    }
    public string Status { get => _status; private set => SetField(ref _status, value); }
    public RelayCommand OpenLocationCommand { get; }
    public RelayCommand OpenParentFolderCommand { get; }
    public RelayCommand DetailsCommand { get; }
    public RelayCommand RecycleCommand { get; }

    public void RefreshLocalization()
    {
        _safety = _classifier.Assess(File.Path);
        OnPropertyChanged(nameof(Safety));
        OnPropertyChanged(nameof(SafetyLabel));
        OnPropertyChanged(nameof(SafetyCategory));
        OnPropertyChanged(nameof(SafetyEffect));
        OnPropertyChanged(nameof(SafetyWhy));
        OnPropertyChanged(nameof(SafetyIcon));
        OnPropertyChanged(nameof(CanQuickRecycle));
        OnPropertyChanged(nameof(CanOpenCleanup));
        OnPropertyChanged(nameof(CanSearchFileName));
        OnPropertyChanged(nameof(CanRecycleFromDetails));
        RecycleCommand.RaiseCanExecuteChanged();
    }

    public void MoveToRecycleBinWithoutPrompt()
    {
        _service.MoveToRecycleBin(File.Path);
        _onRemoved(this);
    }

    private void OpenLocation()
    {
        try { _service.OpenLocation(File.Path); Status = string.Empty; }
        catch (Exception ex) { Status = ex.Message; }
    }

    private void OpenParentFolder()
    {
        try { _service.OpenParentFolder(File.Path); Status = string.Empty; }
        catch (Exception ex) { Status = ex.Message; }
    }

    private void RecycleQuick()
    {
        if (!Safety.AllowQuickRecycle)
            return;
        _onQuickRecycleRequested(this);
    }
}
