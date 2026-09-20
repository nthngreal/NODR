using NODR.Models;
using NODR.Services;

namespace NODR.ViewModels;

public sealed class UninstallResidualViewModel : ViewModelBase
{
    private bool _isSelected;

    public UninstallResidualViewModel(UninstallResidual residual)
    {
        Residual = residual;
        _isSelected = residual.CanClean && residual.Confidence == ResidualConfidence.Safe;
    }

    public UninstallResidual Residual { get; }
    public string Path => Residual.DisplayPath;
    public bool CanClean => Residual.CanClean;
    public bool IsRegistry => Residual.Kind == UninstallResidualKind.RegistryKey;
    public bool IsReview => Residual.Confidence == ResidualConfidence.Review;
    public string KindLabel => IsRegistry ? LocalizationService.Instance["Uninstaller.RegistryResidual"] : LocalizationService.Instance["Uninstaller.FolderResidual"];
    public string ConfidenceLabel => IsReview ? LocalizationService.Instance["Uninstaller.ConfidenceReview"] : LocalizationService.Instance["Uninstaller.ConfidenceSafe"];
    public string SizeText => Residual.SizeBytes > 0 ? ByteFormatter.Format(Residual.SizeBytes) : string.Empty;
    public string DetailText => IsRegistry
        ? LocalizationService.Instance["Uninstaller.RegistryVerifiedStale"]
        : LocalizationService.Instance.Format("Uninstaller.ResidualFiles", Residual.FileCount, Residual.SkippedEntries);
    public bool IsSelected
    {
        get => _isSelected;
        set { if (CanClean) SetField(ref _isSelected, value); }
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(KindLabel));
        OnPropertyChanged(nameof(ConfidenceLabel));
        OnPropertyChanged(nameof(DetailText));
    }
}
