using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using NODR.Models;
using NODR.Services;

namespace NODR.ViewModels;

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private const int HistoryLength = 60;
    private readonly HardwareMonitorService _monitor = new();
    private readonly CleanupService _cleanupService = new();
    private readonly FileFinderService _fileFinderService = new();
    private readonly FileSafetyClassifier _fileSafetyClassifier = new();
    private readonly DriveBreakdownService _driveBreakdownService = new();
    private readonly InstalledAppsService _installedAppsService = new();
    private readonly UninstallerService _uninstallerService = new();
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly DispatcherTimer _timer;
    private bool _isRefreshing;
    private bool _disposed;
    private CancellationTokenSource? _filesScanCts;
    private CancellationTokenSource? _cleanupScanCts;
    private CancellationTokenSource? _driveBreakdownCts;
    private DiskItemViewModel? _selectedDrive;
    private bool _isDriveBreakdownBusy;
    private string _driveBreakdownStatus = string.Empty;
    private double _driveBreakdownPercent;
    private string _driveBreakdownCurrentPath = string.Empty;
    private string _filesSort = "Size";
    private string _uninstallerSort = "DateDesc";
    private string _uninstallerSearch = string.Empty;
    private string _uninstallerStatus = string.Empty;
    private bool _isUninstallerSelected;
    private bool _isUninstallerBusy;
    private bool _hasLoadedInstalledApps;
    private bool _isUninstallerResidualOpen;
    private InstalledAppViewModel? _uninstallTarget;
    private UninstallEvidenceSnapshot? _uninstallEvidence;
    private string _uninstallerResidualStatus = string.Empty;
    private string? _driveCategoryFilter;

    private double _cpuUsage;
    private double _gpuUsage;
    private double _ramUsage;
    private string _cpuValue = "—";
    private string _gpuValue = "—";
    private string _ramValue = "—";
    private string _cpuSubtitle = string.Empty;
    private bool _hasCpuTemperature;
    private string _gpuSubtitle = string.Empty;
    private bool _hasGpuData;
    private string _ramSubtitle = string.Empty;
    private string _cpuName = "CPU";
    private string _gpuName = "GPU";
    private string _sensorStatus = string.Empty;
    private string _lastUpdated = string.Empty;
    private bool _isOverviewSelected = true;
    private bool _isCleanupSelected;
    private bool _isFilesSelected;
    private bool _isFilesBusy;
    private string _filesHeadline = string.Empty;
    private string _filesStatus = string.Empty;
    private string _filesRoot = string.Empty;
    private string _filesFilter = "All";
    private FileItemViewModel? _selectedFileDetails;
    private bool _isCleanupBusy;
    private bool _hasCleanupResults;
    private bool _isCleanupReview;
    private bool _isCleanupCleaning;
    private bool _isCleanupSuccess;
    private double _cleanupProgressPercent;
    private string _cleanupProgressText = string.Empty;
    private string _cleanupHeadline = string.Empty;
    private string _cleanupStatus = string.Empty;
    private string _cleanupResultText = string.Empty;
    private string _recycleBinSummary = string.Empty;
    private bool _isDialogOpen;
    private string _dialogTitle = string.Empty;
    private string _dialogMessage = string.Empty;
    private string _dialogConfirmText = string.Empty;
    private bool _dialogShowCancel = true;
    private Action? _dialogConfirmAction;
    private CleanupResult? _lastCleanupResult;
    private long? _lastRecycleBinBytes;

    public MainViewModel()
    {
        WindowsInfo = RuntimeInformation.OSDescription;
        ArchitectureInfo = RuntimeInformation.OSArchitecture.ToString();

        _gpuSubtitle = L("VM.GpuDetecting");
        _ramSubtitle = L("VM.MemoryReading");
        _sensorStatus = L("VM.Starting");
        _filesHeadline = L("Files.InitialHeadline");
        _filesStatus = L("Files.InitialStatus");
        _cleanupHeadline = L("Cleanup.ReadyHeadline");
        _cleanupStatus = L("Cleanup.ReadyStatus");
        _recycleBinSummary = L("Recycle.Checking");
        _uninstallerStatus = L("Uninstaller.ReadyStatus");
        _dialogConfirmText = L("Common.Confirm");
        BuildChoiceOptions();
        _loc.LanguageChanged += OnLanguageChanged;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += OnTimerTick;

        ShowOverviewCommand = new RelayCommand(ShowOverview);
        ShowCleanupCommand = new RelayCommand(ShowCleanup);
        ShowFilesCommand = new RelayCommand(ShowFiles);
        ShowUninstallerCommand = new RelayCommand(OpenUninstaller);
        ChooseFilesFolderCommand = new RelayCommand(() => _ = ChooseAndScanFilesAsync(), () => !IsFilesBusy);
        CancelFilesScanCommand = new RelayCommand(CancelFilesScan, () => IsFilesBusy);
        BulkRecycleFilesCommand = new RelayCommand(BulkRecycleSelectedFiles, () => HasSelectedSafeFiles);
        CloseFileDetailsCommand = new RelayCommand(CloseFileDetails, () => HasFileDetails);
        RecycleFileFromDetailsCommand = new RelayCommand(RecycleFileFromDetails, () => SelectedFileDetails?.CanRecycleFromDetails == true);
        DeleteContainingFolderCommand = new RelayCommand(() => _ = DeleteContainingFolderAsync(), () => SelectedFileDetails is not null);
        SearchFileNameCommand = new RelayCommand(SearchSelectedFileName, () => SelectedFileDetails?.CanSearchFileName == true);
        ScanCleanupCommand = new RelayCommand(() => _ = ScanCleanupAsync(), () => !IsCleanupBusy);
        CancelCleanupScanCommand = new RelayCommand(CancelCleanupScan, () => IsCleanupBusy && !IsCleanupReview);
        ReviewCleanupCommand = new RelayCommand(() => _ = CleanSelectedAsync(), () => CanClean);
        BackToCleanupResultsCommand = new RelayCommand(() => { }, () => false);
        CleanSelectedCommand = new RelayCommand(() => _ = CleanSelectedAsync(), () => CanClean);
        SelectAllCleanupCommand = new RelayCommand(SelectAllCleanup, () => HasCleanupResults && !IsCleanupBusy);
        ClearAllCleanupCommand = new RelayCommand(ClearAllCleanup, () => HasCleanupResults && !IsCleanupBusy);
        DoneCleanupCommand = new RelayCommand(CompleteCleanup);
        ShowDriveDetailsCommand = new RelayCommand(parameter => { if (parameter is DiskItemViewModel disk) _ = ScanFilesRootAsync(disk.RootPath); });
        CloseDriveDetailsCommand = new RelayCommand(CloseDriveDetails);
        ScanSelectedDriveCommand = new RelayCommand(() => { if (SelectedDrive is not null) _ = ScanFilesRootAsync(SelectedDrive.RootPath); }, () => SelectedDrive is not null && !IsFilesBusy && !IsDriveBreakdownBusy);
        OpenSelectedDriveCommand = new RelayCommand(OpenSelectedDrive, () => SelectedDrive is not null);
        OpenDriveCategoryCommand = new RelayCommand(parameter => { if (parameter is DriveCategoryViewModel category) _ = OpenDriveCategoryAsync(category); });
        QuickCleanupCommand = new RelayCommand(() => _ = OpenQuickCleanupAsync());
        QuickRecycleBinCommand = new RelayCommand(() => _ = OpenCleanupPresetAsync(CleanupCategoryKind.RecycleBin));
        QuickShaderCacheCommand = new RelayCommand(() => _ = OpenCleanupPresetAsync(CleanupCategoryKind.DirectXShaderCache, CleanupCategoryKind.NvidiaShaderCache, CleanupCategoryKind.AmdShaderCache));
        QuickLargeFilesCommand = new RelayCommand(ShowFiles);
        QuickDownloadsCommand = new RelayCommand(() => _ = OpenDownloadsAsync());
        QuickBrowserCleanupCommand = new RelayCommand(OpenBrowserCleanup);
        ScanQuickFolderCommand = new RelayCommand(parameter => { if (parameter is string target) _ = ScanQuickFolderAsync(target); }, () => !IsFilesBusy);
        SetFilesFilterCommand = new RelayCommand(parameter => SetFilesFilter(parameter as string ?? "All"));
        RefreshInstalledAppsCommand = new RelayCommand(() => _ = LoadInstalledAppsAsync(force: true), () => !IsUninstallerBusy);
        ClearUninstallerSearchCommand = new RelayCommand(() => UninstallerSearch = string.Empty);
        UninstallAppCommand = new RelayCommand(parameter => { if (parameter is InstalledAppViewModel app) RequestUninstall(app); }, () => !IsUninstallerBusy);
        CloseUninstallerResidualsCommand = new RelayCommand(CloseUninstallerResiduals, () => IsUninstallerResidualOpen && !IsUninstallerBusy);
        CleanUninstallerResidualsCommand = new RelayCommand(() => _ = CleanSelectedResidualsAsync(), () => CanCleanUninstallerResiduals && !IsUninstallerBusy);
        RescanUninstallerResidualsCommand = new RelayCommand(() => _ = RescanUninstallerResidualsAsync(), () => _uninstallTarget is not null && !IsUninstallerBusy);
        ConfirmDialogCommand = new RelayCommand(ConfirmDialog, () => IsDialogOpen);
        CancelDialogCommand = new RelayCommand(CancelDialog, () => IsDialogOpen);
    }

    public ObservableCollection<double> CpuHistory { get; } = new();
    public ObservableCollection<double> GpuHistory { get; } = new();
    public ObservableCollection<double> RamHistory { get; } = new();
    public ObservableCollection<DiskItemViewModel> Disks { get; } = new();
    public ObservableCollection<CleanupCategoryViewModel> CleanupCategories { get; } = new();
    public ObservableCollection<CleanupCategoryViewModel> RecommendedCleanupCategories { get; } = new();
    public ObservableCollection<CleanupCategoryViewModel> OptionalCleanupCategories { get; } = new();
    public ObservableCollection<FileItemViewModel> FoundFiles { get; } = new();
    public ObservableCollection<FileItemViewModel> VisibleFiles { get; } = new();
    public ObservableCollection<DriveCategoryViewModel> DriveCategories { get; } = new();
    public ObservableCollection<ChoiceOption> LanguageOptions { get; } = new();
    public ObservableCollection<ChoiceOption> ThemeOptions { get; } = new();
    public ObservableCollection<ChoiceOption> FilesSortOptions { get; } = new();
    public ObservableCollection<ChoiceOption> UninstallerSortOptions { get; } = new();
    public ObservableCollection<InstalledAppViewModel> InstalledApps { get; } = new();
    public ObservableCollection<InstalledAppViewModel> VisibleInstalledApps { get; } = new();
    public ObservableCollection<UninstallResidualViewModel> UninstallerResiduals { get; } = new();

    public event EventHandler<BackgroundNotification>? BackgroundNotificationRequested;

    public RelayCommand ShowOverviewCommand { get; }
    public RelayCommand ShowCleanupCommand { get; }
    public RelayCommand ShowFilesCommand { get; }
    public RelayCommand ShowUninstallerCommand { get; }
    public RelayCommand ChooseFilesFolderCommand { get; }
    public RelayCommand CancelFilesScanCommand { get; }
    public RelayCommand BulkRecycleFilesCommand { get; }
    public RelayCommand CloseFileDetailsCommand { get; }
    public RelayCommand RecycleFileFromDetailsCommand { get; }
    public RelayCommand DeleteContainingFolderCommand { get; }
    public RelayCommand SearchFileNameCommand { get; }
    public RelayCommand ScanCleanupCommand { get; }
    public RelayCommand CancelCleanupScanCommand { get; }
    public RelayCommand ReviewCleanupCommand { get; }
    public RelayCommand BackToCleanupResultsCommand { get; }
    public RelayCommand CleanSelectedCommand { get; }
    public RelayCommand SelectAllCleanupCommand { get; }
    public RelayCommand ClearAllCleanupCommand { get; }
    public RelayCommand DoneCleanupCommand { get; }
    public RelayCommand ShowDriveDetailsCommand { get; }
    public RelayCommand CloseDriveDetailsCommand { get; }
    public RelayCommand ScanSelectedDriveCommand { get; }
    public RelayCommand OpenSelectedDriveCommand { get; }
    public RelayCommand OpenDriveCategoryCommand { get; }
    public RelayCommand QuickCleanupCommand { get; }
    public RelayCommand QuickRecycleBinCommand { get; }
    public RelayCommand QuickShaderCacheCommand { get; }
    public RelayCommand QuickLargeFilesCommand { get; }
    public RelayCommand QuickDownloadsCommand { get; }
    public RelayCommand QuickBrowserCleanupCommand { get; }
    public RelayCommand ScanQuickFolderCommand { get; }
    public RelayCommand SetFilesFilterCommand { get; }
    public RelayCommand RefreshInstalledAppsCommand { get; }
    public RelayCommand ClearUninstallerSearchCommand { get; }
    public RelayCommand UninstallAppCommand { get; }
    public RelayCommand CloseUninstallerResidualsCommand { get; }
    public RelayCommand CleanUninstallerResidualsCommand { get; }
    public RelayCommand RescanUninstallerResidualsCommand { get; }
    public RelayCommand ConfirmDialogCommand { get; }
    public RelayCommand CancelDialogCommand { get; }

    public bool IsDialogOpen
    {
        get => _isDialogOpen;
        private set
        {
            if (SetField(ref _isDialogOpen, value))
            {
                ConfirmDialogCommand.RaiseCanExecuteChanged();
                CancelDialogCommand.RaiseCanExecuteChanged();
            }
        }
    }
    public string DialogTitle { get => _dialogTitle; private set => SetField(ref _dialogTitle, value); }
    public string DialogMessage { get => _dialogMessage; private set => SetField(ref _dialogMessage, value); }
    public string DialogConfirmText { get => _dialogConfirmText; private set => SetField(ref _dialogConfirmText, value); }
    public bool DialogShowCancel { get => _dialogShowCancel; private set => SetField(ref _dialogShowCancel, value); }

    public string WindowsInfo { get; }
    public string ArchitectureInfo { get; }
    public string CurrentLanguageCode
    {
        get => _loc.CurrentLanguage;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!string.Equals(_loc.CurrentLanguage, value, StringComparison.OrdinalIgnoreCase))
                _loc.SetLanguage(value);
        }
    }
    public bool EffectiveThemeIsDark => ThemeService.Instance.IsDarkEffective;

    public string CurrentThemeMode
    {
        get => ThemeService.Instance.CurrentMode;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!string.Equals(ThemeService.Instance.CurrentMode, value, StringComparison.OrdinalIgnoreCase))
            {
                ThemeService.Instance.SetTheme(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(EffectiveThemeIsDark));
            }
        }
    }

    public DiskItemViewModel? SelectedDrive
    {
        get => _selectedDrive;
        private set
        {
            if (SetField(ref _selectedDrive, value))
            {
                OnPropertyChanged(nameof(HasDriveDetails));
                ScanSelectedDriveCommand.RaiseCanExecuteChanged();
                OpenSelectedDriveCommand.RaiseCanExecuteChanged();
            }
        }
    }
    public bool HasDriveDetails => SelectedDrive is not null;
    public bool IsDriveBreakdownBusy { get => _isDriveBreakdownBusy; private set { if (SetField(ref _isDriveBreakdownBusy, value)) ScanSelectedDriveCommand.RaiseCanExecuteChanged(); } }
    public string DriveBreakdownStatus { get => _driveBreakdownStatus; private set => SetField(ref _driveBreakdownStatus, value); }
    public double DriveBreakdownPercent { get => _driveBreakdownPercent; private set { if (SetField(ref _driveBreakdownPercent, value)) OnPropertyChanged(nameof(DriveBreakdownPercentText)); } }
    public string DriveBreakdownPercentText => $"{DriveBreakdownPercent:0}%";
    public string DriveBreakdownCurrentPath { get => _driveBreakdownCurrentPath; private set => SetField(ref _driveBreakdownCurrentPath, value); }

    public double CpuUsage
    {
        get => _cpuUsage;
        private set => SetField(ref _cpuUsage, value);
    }

    public double GpuUsage
    {
        get => _gpuUsage;
        private set => SetField(ref _gpuUsage, value);
    }

    public double RamUsage
    {
        get => _ramUsage;
        private set => SetField(ref _ramUsage, value);
    }

    public string CpuValue
    {
        get => _cpuValue;
        private set => SetField(ref _cpuValue, value);
    }

    public string GpuValue
    {
        get => _gpuValue;
        private set => SetField(ref _gpuValue, value);
    }

    public string RamValue
    {
        get => _ramValue;
        private set => SetField(ref _ramValue, value);
    }

    public string CpuSubtitle
    {
        get => _cpuSubtitle;
        private set => SetField(ref _cpuSubtitle, value);
    }

    public bool HasCpuTemperature
    {
        get => _hasCpuTemperature;
        private set => SetField(ref _hasCpuTemperature, value);
    }

    public bool HasGpuData
    {
        get => _hasGpuData;
        private set => SetField(ref _hasGpuData, value);
    }

    public string GpuSubtitle
    {
        get => _gpuSubtitle;
        private set => SetField(ref _gpuSubtitle, value);
    }

    public string RamSubtitle
    {
        get => _ramSubtitle;
        private set => SetField(ref _ramSubtitle, value);
    }

    public string CpuName
    {
        get => _cpuName;
        private set => SetField(ref _cpuName, value);
    }

    public string GpuName
    {
        get => _gpuName;
        private set => SetField(ref _gpuName, value);
    }

    public string SensorStatus
    {
        get => _sensorStatus;
        private set => SetField(ref _sensorStatus, value);
    }

    public string LastUpdated
    {
        get => _lastUpdated;
        private set => SetField(ref _lastUpdated, value);
    }


    public bool IsOverviewSelected
    {
        get => _isOverviewSelected;
        private set => SetField(ref _isOverviewSelected, value);
    }

    public bool IsCleanupSelected
    {
        get => _isCleanupSelected;
        private set
        {
            if (SetField(ref _isCleanupSelected, value))
                OnPropertyChanged(nameof(CanShowCleanupBottomBar));
        }
    }

    public bool IsFilesSelected
    {
        get => _isFilesSelected;
        private set => SetField(ref _isFilesSelected, value);
    }

    public bool IsUninstallerSelected
    {
        get => _isUninstallerSelected;
        private set => SetField(ref _isUninstallerSelected, value);
    }

    public bool IsUninstallerBusy
    {
        get => _isUninstallerBusy;
        private set
        {
            if (SetField(ref _isUninstallerBusy, value))
            {
                RefreshInstalledAppsCommand.RaiseCanExecuteChanged();
                UninstallAppCommand.RaiseCanExecuteChanged();
                CloseUninstallerResidualsCommand.RaiseCanExecuteChanged();
                CleanUninstallerResidualsCommand.RaiseCanExecuteChanged();
                RescanUninstallerResidualsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string UninstallerSearch
    {
        get => _uninstallerSearch;
        set { if (SetField(ref _uninstallerSearch, value ?? string.Empty)) RefreshVisibleInstalledApps(); }
    }

    public string UninstallerSort
    {
        get => _uninstallerSort;
        set
        {
            var normalized = value is "SizeDesc" or "SizeAsc" or "DateDesc" or "DateAsc" or "NameAsc" or "NameDesc" ? value : "DateDesc";
            if (SetField(ref _uninstallerSort, normalized)) RefreshVisibleInstalledApps();
        }
    }

    public string UninstallerStatus { get => _uninstallerStatus; private set => SetField(ref _uninstallerStatus, value); }
    public string UninstallerCountText => LF("Uninstaller.Count", VisibleInstalledApps.Count);
    public bool HasInstalledApps => VisibleInstalledApps.Count > 0;
    public bool IsUninstallerResidualOpen
    {
        get => _isUninstallerResidualOpen;
        private set
        {
            if (SetField(ref _isUninstallerResidualOpen, value))
            {
                CloseUninstallerResidualsCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanCleanUninstallerResiduals));
            }
        }
    }
    public string UninstallerResidualTitle => _uninstallTarget is null ? L("Uninstaller.ResidualTitle") : LF("Uninstaller.ResidualTitleApp", _uninstallTarget.Name);
    public string UninstallerResidualSummary
    {
        get
        {
            var selected = UninstallerResiduals.Where(item => item.IsSelected && item.CanClean).ToArray();
            var bytes = selected.Sum(item => item.Residual.SizeBytes);
            var folders = selected.Count(item => !item.IsRegistry);
            var registryEntries = selected.Count(item => item.IsRegistry);
            return LF("Uninstaller.ResidualSummary", ByteFormatter.Format(bytes), folders, registryEntries);
        }
    }
    public string UninstallerResidualStatus { get => _uninstallerResidualStatus; private set => SetField(ref _uninstallerResidualStatus, value); }
    public bool CanCleanUninstallerResiduals => UninstallerResiduals.Any(item => item.IsSelected && item.CanClean);


    public bool IsFilesBusy
    {
        get => _isFilesBusy;
        private set
        {
            if (SetField(ref _isFilesBusy, value))
            {
                ChooseFilesFolderCommand.RaiseCanExecuteChanged();
                CancelFilesScanCommand.RaiseCanExecuteChanged();
                ScanQuickFolderCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(ShowFilesEmptyState));
            }
        }
    }

    public string FilesHeadline { get => _filesHeadline; private set => SetField(ref _filesHeadline, value); }
    public string FilesStatus { get => _filesStatus; private set => SetField(ref _filesStatus, value); }
    public string FilesRoot { get => _filesRoot; private set => SetField(ref _filesRoot, value); }
    public string FilesFilter { get => _filesFilter; private set { if (SetField(ref _filesFilter, value)) { RefreshVisibleFiles(); OnPropertyChanged(nameof(IsAllFilesFilter)); OnPropertyChanged(nameof(IsArchivesFilesFilter)); OnPropertyChanged(nameof(IsVideosFilesFilter)); OnPropertyChanged(nameof(IsExecutablesFilesFilter)); OnPropertyChanged(nameof(IsCachesFilesFilter)); } } }
    public string FilesSort { get => _filesSort; set { if (string.IsNullOrWhiteSpace(value)) return; var normalized = value == "Date" ? "Date" : "Size"; if (SetField(ref _filesSort, normalized)) { RefreshVisibleFiles(); UpdateFilesSummary(); } } }
    public bool IsAllFilesFilter => FilesFilter == "All";
    public bool IsArchivesFilesFilter => FilesFilter == "Archives";
    public bool IsVideosFilesFilter => FilesFilter == "Videos";
    public bool IsExecutablesFilesFilter => FilesFilter == "Executables";
    public bool IsCachesFilesFilter => FilesFilter == "Caches";
    public bool HasScannedFiles => IsFilesBusy || !string.IsNullOrWhiteSpace(FilesRoot);
    public bool ShowFilesEmptyState => !IsFilesBusy && VisibleFiles.Count == 0;
    public string FilesEmptyTitle => string.IsNullOrWhiteSpace(FilesRoot)
        ? L("Files.Empty.NoneTitle")
        : FilesFilter == "All" ? L("Files.Empty.NoLarge") : LF("Files.Empty.FilterTitle", LocalizedFilterName(FilesFilter).ToLowerInvariant());
    public string FilesEmptyHint => string.IsNullOrWhiteSpace(FilesRoot)
        ? L("Files.Empty.NoneHint")
        : FilesFilter == "All" ? L("Files.Empty.NoLargeHint") : L("Files.Empty.FilterHint");
    public FileItemViewModel? SelectedFileDetails
    {
        get => _selectedFileDetails;
        private set
        {
            if (SetField(ref _selectedFileDetails, value))
            {
                OnPropertyChanged(nameof(HasFileDetails));
                CloseFileDetailsCommand.RaiseCanExecuteChanged();
                RecycleFileFromDetailsCommand.RaiseCanExecuteChanged();
                DeleteContainingFolderCommand.RaiseCanExecuteChanged();
                SearchFileNameCommand.RaiseCanExecuteChanged();
            }
        }
    }
    public bool HasFileDetails => SelectedFileDetails is not null;
    public bool HasSelectedSafeFiles => FoundFiles.Any(file => file.CanQuickRecycle && file.IsSelected);
    public string SelectedFilesSummary
    {
        get
        {
            var selected = FoundFiles.Where(file => file.CanQuickRecycle && file.IsSelected).ToList();
            return selected.Count == 0
                ? L("Files.SelectedNone")
                : LF("Files.SelectedSummary", selected.Count, ByteFormatter.Format(selected.Sum(file => file.File.SizeBytes)));
        }
    }

    public bool IsCleanupBusy
    {
        get => _isCleanupBusy;
        private set
        {
            if (SetField(ref _isCleanupBusy, value))
            {
                OnPropertyChanged(nameof(CanCancelCleanupScan));
                OnPropertyChanged(nameof(CanReview));
                OnPropertyChanged(nameof(CanClean));
                ScanCleanupCommand.RaiseCanExecuteChanged();
                CancelCleanupScanCommand.RaiseCanExecuteChanged();
                ReviewCleanupCommand.RaiseCanExecuteChanged();
                BackToCleanupResultsCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(OverviewCleanupHeadline));
                OnPropertyChanged(nameof(OverviewCleanupStatus));
                OnPropertyChanged(nameof(CanShowCleanupQuickAction));
                CleanSelectedCommand.RaiseCanExecuteChanged();
                SelectAllCleanupCommand.RaiseCanExecuteChanged();
                ClearAllCleanupCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasCleanupResults
    {
        get => _hasCleanupResults;
        private set
        {
            if (SetField(ref _hasCleanupResults, value))
            {
                OnPropertyChanged(nameof(OverviewCleanupHeadline));
                OnPropertyChanged(nameof(OverviewCleanupStatus));
                OnPropertyChanged(nameof(CanShowCleanupQuickAction));
                SelectAllCleanupCommand.RaiseCanExecuteChanged();
                ClearAllCleanupCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCleanupReview
    {
        get => _isCleanupReview;
        private set
        {
            if (SetField(ref _isCleanupReview, value))
            {
                OnPropertyChanged(nameof(CanCancelCleanupScan));
                OnPropertyChanged(nameof(CanReview));
                OnPropertyChanged(nameof(CanClean));
                CancelCleanupScanCommand.RaiseCanExecuteChanged();
                ReviewCleanupCommand.RaiseCanExecuteChanged();
                BackToCleanupResultsCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(OverviewCleanupHeadline));
                OnPropertyChanged(nameof(OverviewCleanupStatus));
                OnPropertyChanged(nameof(CanShowCleanupQuickAction));
                CleanSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }


    public bool IsCleanupCleaning
    {
        get => _isCleanupCleaning;
        private set => SetField(ref _isCleanupCleaning, value);
    }

    public bool IsCleanupSuccess
    {
        get => _isCleanupSuccess;
        private set
        {
            if (SetField(ref _isCleanupSuccess, value))
            {
                DoneCleanupCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(CanShowCleanupBottomBar));
            }
        }
    }

    public double CleanupProgressPercent
    {
        get => _cleanupProgressPercent;
        private set => SetField(ref _cleanupProgressPercent, value);
    }

    public string CleanupProgressText
    {
        get => _cleanupProgressText;
        private set => SetField(ref _cleanupProgressText, value);
    }

    public string CleanupHeadline
    {
        get => _cleanupHeadline;
        private set => SetField(ref _cleanupHeadline, value);
    }

    public string CleanupStatus
    {
        get => _cleanupStatus;
        private set => SetField(ref _cleanupStatus, value);
    }

    public string CleanupResultText
    {
        get => _cleanupResultText;
        private set => SetField(ref _cleanupResultText, value);
    }

    public string RecycleBinSummary
    {
        get => _recycleBinSummary;
        private set => SetField(ref _recycleBinSummary, value);
    }

    public bool CanCancelCleanupScan => IsCleanupBusy && !IsCleanupReview;
    public bool CanReview => CanClean;
    public bool CanClean => HasCleanupResults && !IsCleanupBusy && CleanupCategories.Any(category => category.IsSelected && category.HasData);
    public string SelectedCleanupSizeText => ByteFormatter.Format(CleanupCategories.Where(category => category.IsSelected).Sum(category => category.Scan.SizeBytes));
    public string TotalCleanupSizeText => ByteFormatter.Format(CleanupCategories.Sum(category => category.Scan.SizeBytes));
    public string CleanupTotalFoundText => LF("Cleanup.TotalFound", TotalCleanupSizeText);
    public string SelectedCleanupCountText => LF("Cleanup.SelectedCount", CleanupCategories.Count(category => category.IsSelected && category.HasData));
    public string SelectedCleanupSummaryText => LF("Cleanup.SelectedSummary", SelectedCleanupSizeText);
    public string TopCleanupActionText => LF("Cleanup.CleanAction", SelectedCleanupSizeText);
    public bool CanShowCleanupQuickAction => HasCleanupResults && !IsCleanupBusy;
    public bool CanShowCleanupBottomBar => IsCleanupSelected && HasCleanupResults && !IsCleanupSuccess;

    public long RecommendedCleanupBytes => CleanupCategories.Where(category => category.IsRecommended).Sum(category => category.Scan.SizeBytes);
    public string OverviewCleanupHeadline => IsCleanupBusy && !HasCleanupResults
        ? L("Overview.CleanupChecking")
        : RecommendedCleanupBytes > 0 ? LF("Overview.CleanupCanFree", ByteFormatter.Format(RecommendedCleanupBytes)) : L("Overview.CleanupClean");
    public string OverviewCleanupStatus => IsCleanupBusy && !HasCleanupResults
        ? L("Overview.CleanupCheckingStatus")
        : RecommendedCleanupBytes > 0
            ? L("Overview.CleanupReadyStatus")
            : L("Overview.CleanupCleanStatus");

    private async Task EnsureCleanupScanAsync()
    {
        if (HasCleanupResults || IsCleanupBusy)
        {
            while (IsCleanupBusy)
                await Task.Delay(80);
            return;
        }
        await ScanCleanupAsync();
    }

    private async Task OpenQuickCleanupAsync()
    {
        ShowCleanup();
        await EnsureCleanupScanAsync();
    }

    private async Task OpenCleanupPresetAsync(params CleanupCategoryKind[] kinds)
    {
        ShowCleanup();
        await EnsureCleanupScanAsync();
        var wanted = kinds.ToHashSet();
        foreach (var category in CleanupCategories)
            category.IsSelected = category.HasData && wanted.Contains(category.Scan.Kind);
        CleanupHeadline = CleanupCategories.Any(c => c.IsSelected)
            ? LF("Cleanup.SelectedHeadline", SelectedCleanupSizeText)
            : L("Cleanup.QuickNothing");
        CleanupStatus = CleanupCategories.Any(c => c.IsSelected)
            ? L("Cleanup.QuickSelectedStatus")
            : L("Cleanup.QuickNothingStatus");
    }

    private void SelectAllCleanup()
    {
        foreach (var category in CleanupCategories.Where(category => category.HasData && category.CanSelect))
            category.IsSelected = true;
    }

    private void ClearAllCleanup()
    {
        foreach (var category in CleanupCategories.Where(category => category.CanSelect))
            category.IsSelected = false;
    }

    private void CompleteCleanup()
    {
        IsCleanupSuccess = false;
        ShowOverview();
        _ = RefreshAsync();
    }

    private void ShowOverview()
    {
        SelectedFileDetails = null;
        IsOverviewSelected = true;
        IsCleanupSelected = false;
        IsFilesSelected = false;
        IsUninstallerSelected = false;
    }

    private void ShowCleanup()
    {
        SelectedFileDetails = null;
        IsOverviewSelected = false;
        IsCleanupSelected = true;
        IsFilesSelected = false;
        IsUninstallerSelected = false;
    }

    private void ShowFiles()
    {
        IsOverviewSelected = false;
        IsCleanupSelected = false;
        IsFilesSelected = true;
        IsUninstallerSelected = false;
    }

    private void OpenUninstaller()
    {
        SelectedFileDetails = null;
        IsOverviewSelected = false;
        IsCleanupSelected = false;
        IsFilesSelected = false;
        IsUninstallerSelected = true;
        _ = LoadInstalledAppsAsync(force: false);
    }

    private void OpenBrowserCleanup()
    {
        var running = new[] { ("chrome", "Chrome"), ("msedge", "Edge"), ("firefox", "Firefox"), ("brave", "Brave"), ("opera", "Opera") }
            .Where(browser => Process.GetProcessesByName(browser.Item1).Length > 0)
            .Select(browser => browser.Item2)
            .Distinct()
            .ToArray();

        if (running.Length == 0)
        {
            _ = OpenCleanupPresetAsync(CleanupCategoryKind.BrowserWebCache, CleanupCategoryKind.BrowserCodeGpuCache, CleanupCategoryKind.BrowserDownloadTempCache);
            return;
        }

        ShowConfirmation(
            L("Browser.RunningTitle"),
            LF("Browser.RunningMessage", string.Join(", ", running)),
            L("Common.Continue"),
            () => _ = OpenCleanupPresetAsync(CleanupCategoryKind.BrowserWebCache, CleanupCategoryKind.BrowserCodeGpuCache, CleanupCategoryKind.BrowserDownloadTempCache));
    }

    private async Task OpenDownloadsAsync()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads))
            await ScanFilesRootAsync(downloads);
        else
            ShowFiles();
    }

    private async Task ScanQuickFolderAsync(string target)
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = target switch
        {
            "Downloads" => Path.Combine(user, "Downloads"),
            "Desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "Documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "C" => @"C:\",
            _ => string.Empty
        };
        if (Directory.Exists(path)) await ScanFilesRootAsync(path);
        else ShowNotice(L("Folder.UnavailableTitle"), LF("Folder.UnavailableMessage", target));
    }

    private void SetFilesFilter(string filter) => FilesFilter = filter;

    private void RefreshVisibleFiles()
    {
        VisibleFiles.Clear();
        var filtered = FoundFiles.Where(MatchesFilesFilter);
        var sorted = FilesSort == "Date"
            ? filtered.OrderBy(item => item.File.LastWriteTimeUtc).ThenByDescending(item => item.File.SizeBytes)
            : filtered.OrderByDescending(item => item.File.SizeBytes);
        foreach (var item in sorted) VisibleFiles.Add(item);
        OnPropertyChanged(nameof(ShowFilesEmptyState));
        OnPropertyChanged(nameof(FilesEmptyTitle));
        OnPropertyChanged(nameof(FilesEmptyHint));
    }

    private bool MatchesFilesFilter(FileItemViewModel item)
    {
        if (FilesFilter == "All") return true;
        var ext = Path.GetExtension(item.FullPath).ToLowerInvariant();
        return FilesFilter switch
        {
            "Archives" => ext is ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".bz2" or ".xz",
            "Videos" => ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".wmv" or ".m4v",
            "Executables" => ext is ".exe" or ".msi" or ".msix" or ".appx" or ".bat" or ".cmd",
            "Caches" => item.Safety.Level == FileSafetyLevel.SafeToRemove || item.FullPath.Contains("cache", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private async Task ChooseAndScanFilesAsync()
    {
        if (IsFilesBusy)
            return;

        var folder = _fileFinderService.PickFolder();
        if (string.IsNullOrWhiteSpace(folder))
            return;

        await ScanFilesRootAsync(folder);
    }

    private async Task ScanFilesRootAsync(string folder)
    {
        if (IsFilesBusy) return;
        SelectedDrive = null;
        IsOverviewSelected = false;
        IsCleanupSelected = false;
        IsFilesSelected = true;
        IsUninstallerSelected = false;
        IsFilesBusy = true;
        FilesRoot = folder;
        OnPropertyChanged(nameof(HasScannedFiles));
        OnPropertyChanged(nameof(ShowFilesEmptyState));
        FilesHeadline = L("Files.ScanningHeadline");
        FilesStatus = L("Files.ScanningStatus");
        SelectedFileDetails = null;
        FoundFiles.Clear();
        VisibleFiles.Clear();
        _filesScanCts?.Dispose();
        _filesScanCts = new CancellationTokenSource();
        try
        {
            var scanProgress = new Progress<FileScanProgress>(progress =>
            {
                if (!IsFilesBusy)
                    return;

                var current = GetFriendlyScanPath(progress.CurrentDirectory, folder);
                var skippedText = progress.SkippedEntries > 0 ? LF("Files.ScanSkipped", progress.SkippedEntries) : string.Empty;
                FilesStatus = LF("Files.ScanProgress", progress.FilesChecked, progress.DirectoriesChecked, progress.LargeFilesFound, skippedText, current);
            });
            var result = await _fileFinderService.ScanAsync(folder, _filesScanCts.Token, scanProgress);
            var categoryFilter = _driveCategoryFilter;
            _driveCategoryFilter = null;
            var rootForCategory = Path.GetPathRoot(Path.GetFullPath(folder)) ?? folder;
            var visibleFiles = string.IsNullOrEmpty(categoryFilter)
                ? result.Files
                : result.Files.Where(file => DriveBreakdownService.ClassifyPath(rootForCategory, file.Path, Path.GetExtension(file.Path)).Equals(categoryFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var file in visibleFiles)
                FoundFiles.Add(new FileItemViewModel(file, _fileFinderService, _fileSafetyClassifier, RemoveFoundFile, ShowFileDetails, OnFileSelectionChanged, RequestQuickRecycle));
            RefreshVisibleFiles();
            FilesHeadline = visibleFiles.Count == 0 ? L("Files.ScanNoLarge") : LF("Files.ScanHeadline", visibleFiles.Count, visibleFiles.Count == 1 ? L("Files.FileSingular") : L("Files.FilePlural"));
            var total = visibleFiles.Sum(file => file.SizeBytes);
            var filterText = string.IsNullOrEmpty(categoryFilter) ? string.Empty : LF("Files.FilterSuffix", LocalizeDriveCategory(categoryFilter));
            var inaccessible = result.SkippedEntries > 0 ? LF("Files.InaccessibleSuffix", result.SkippedEntries) : string.Empty;
            FilesStatus = visibleFiles.Count == 0
                ? LF("Files.ScanNoMatch", string.IsNullOrEmpty(categoryFilter) ? string.Empty : $" {LocalizeDriveCategory(categoryFilter)}")
                : LF("Files.TotalStatus", ByteFormatter.Format(total), L(FilesSort == "Date" ? "Files.SortStatus.Date" : "Files.SortStatus.Size"), filterText, inaccessible);
            BackgroundNotificationRequested?.Invoke(this, new BackgroundNotification(L("Notification.ScanTitle"), LF("Notification.FilesScanBody", visibleFiles.Count, ByteFormatter.Format(total))));
        }
        catch (OperationCanceledException) { FilesHeadline = L("Files.ScanCancelled"); FilesStatus = L("Files.ScanCancelledStatus"); }
        catch (Exception ex) { FilesHeadline = L("Files.ScanFailed"); FilesStatus = ex.Message; }
        finally { _filesScanCts?.Dispose(); _filesScanCts = null; IsFilesBusy = false; ScanSelectedDriveCommand.RaiseCanExecuteChanged(); }
    }


    private static string GetFriendlyScanPath(string currentDirectory, string root)
    {
        try
        {
            var relative = Path.GetRelativePath(root, currentDirectory);
            if (relative == ".")
                return root;
            return relative.Length <= 70 ? relative : $"…{relative[^67..]}";
        }
        catch
        {
            return currentDirectory;
        }
    }

    private async Task ShowDriveDetailsAsync(DiskItemViewModel disk)
    {
        _driveBreakdownCts?.Cancel();
        _driveBreakdownCts?.Dispose();
        _driveBreakdownCts = new CancellationTokenSource();
        SelectedDrive = disk;
        DriveCategories.Clear();
        IsDriveBreakdownBusy = true;
        DriveBreakdownStatus = L("Drive.Analyzing");
        DriveBreakdownPercent = 0;
        DriveBreakdownCurrentPath = LF("Drive.Scanning", disk.RootPath);
        try
        {
            var breakdownProgress = new Progress<DriveBreakdownProgress>(progress =>
            {
                DriveBreakdownPercent = progress.Percent;
                DriveBreakdownCurrentPath = LF("Drive.Scanning", GetFriendlyScanPath(progress.CurrentDirectory, disk.RootPath));
                DriveBreakdownStatus = LF("Drive.Checked", progress.FilesChecked);
            });
            var result = await _driveBreakdownService.ScanAsync(disk.RootPath, _driveBreakdownCts.Token, breakdownProgress);
            DriveBreakdownPercent = 100;
            DriveBreakdownCurrentPath = L("Drive.Complete");
            var usedBytes = disk.TotalBytes > 0 ? disk.UsedBytes : result.Categories.Sum(x => x.Bytes);
            foreach (var category in result.Categories.Where(x => x.Bytes > 0).OrderByDescending(x => x.Bytes))
                DriveCategories.Add(new DriveCategoryViewModel(category, usedBytes, categoryVm => _ = OpenDriveCategoryAsync(categoryVm)));
            DriveBreakdownStatus = result.SkippedEntries > 0
                ? LF("Drive.EstimatedSkipped", result.SkippedEntries)
                : L("Drive.Estimated");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { DriveBreakdownStatus = LF("Drive.Unavailable", ex.Message); }
        finally { IsDriveBreakdownBusy = false; ScanSelectedDriveCommand.RaiseCanExecuteChanged(); }
    }

    private async Task OpenDriveCategoryAsync(DriveCategoryViewModel category)
    {
        if (SelectedDrive is null || IsDriveBreakdownBusy) return;
        var root = SelectedDrive.RootPath;
        _driveCategoryFilter = category.RawName;
        CloseDriveDetails();
        await ScanFilesRootAsync(root);
    }

    private void CloseDriveDetails()
    {
        _driveBreakdownCts?.Cancel();
        SelectedDrive = null;
        DriveCategories.Clear();
    }

    private void OpenSelectedDrive()
    {
        if (SelectedDrive is null) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", SelectedDrive.RootPath) { UseShellExecute = true }); }
        catch (Exception ex) { ShowNotice(L("Drive.OpenFailed"), ex.Message); }
    }

    private void CancelFilesScan() => _filesScanCts?.Cancel();

    private async void RemoveFoundFile(FileItemViewModel item)
    {
        if (ReferenceEquals(SelectedFileDetails, item))
            SelectedFileDetails = null;
        item.IsRemoving = true;
        await Task.Delay(140);
        FoundFiles.Remove(item);
        VisibleFiles.Remove(item);
        OnPropertyChanged(nameof(ShowFilesEmptyState));
        OnPropertyChanged(nameof(FilesEmptyTitle));
        OnPropertyChanged(nameof(FilesEmptyHint));
        OnFileSelectionChanged();
        UpdateFilesSummary();
    }

    private void UpdateFilesSummary()
    {
        FilesHeadline = FoundFiles.Count == 0 ? L("Files.NoLargeLeft") : LF("Files.ScanHeadline", FoundFiles.Count, FoundFiles.Count == 1 ? L("Files.FileSingular") : L("Files.FilePlural"));
        FilesStatus = FoundFiles.Count == 0
            ? L("Files.ListEmptyStatus")
            : LF("Files.TotalStatus", ByteFormatter.Format(FoundFiles.Sum(file => file.File.SizeBytes)), L(FilesSort == "Date" ? "Files.SortStatus.Date" : "Files.SortStatus.Size"), string.Empty, string.Empty);
    }

    private void SearchSelectedFileName()
    {
        var item = SelectedFileDetails;
        if (item is null || !item.CanSearchFileName) return;
        try
        {
            var query = Uri.EscapeDataString(item.Name);
            Process.Start(new ProcessStartInfo($"https://www.google.com/search?q={query}") { UseShellExecute = true });
        }
        catch (Exception ex) { ShowNotice(L("Search.OpenFailed"), ex.Message); }
    }


    private void OnFileSelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelectedSafeFiles));
        OnPropertyChanged(nameof(SelectedFilesSummary));
        BulkRecycleFilesCommand.RaiseCanExecuteChanged();
    }

    private void ShowFileDetails(FileItemViewModel item) => SelectedFileDetails = item;
    private void CloseFileDetails() => SelectedFileDetails = null;

    private void BulkRecycleSelectedFiles()
    {
        var selected = FoundFiles.Where(file => file.CanQuickRecycle && file.IsSelected).ToList();
        if (selected.Count == 0)
            return;

        var total = selected.Sum(file => file.File.SizeBytes);
        ShowConfirmation(
            L("Bulk.Title"),
            LF("Bulk.Message", selected.Count, ByteFormatter.Format(total)),
            L("Files.MoveToBin"),
            () =>
            {
                var removed = 0;
                var failed = 0;
                foreach (var item in selected)
                {
                    try { item.MoveToRecycleBinWithoutPrompt(); removed++; }
                    catch { item.IsSelected = false; failed++; }
                }
                ShowNotice(
                    L("Bulk.FinishedTitle"),
                    failed == 0 ? LF("Bulk.Finished", removed) : LF("Bulk.FinishedPartial", removed, failed));
            });
    }

    private void RecycleFileFromDetails()
    {
        var item = SelectedFileDetails;
        if (item is null || !item.CanRecycleFromDetails)
            return;

        var warning = item.Safety.Level switch
        {
            FileSafetyLevel.ReviewFirst => L("File.Warning.Review"),
            FileSafetyLevel.Unknown => L("File.Warning.Unknown"),
            _ => L("File.Warning.Safe")
        };
        ShowConfirmation(
            L("File.ConfirmTitle"),
            LF("File.ConfirmMessage", warning, item.SafetyEffect, item.Name, item.SizeText),
            L("Files.MoveToBin"),
            () => TryRecycleFile(item));
    }

    private async Task DeleteContainingFolderAsync()
    {
        var item = SelectedFileDetails;
        if (item is null) return;

        var folder = item.Folder;
        try
        {
            var summary = await _fileFinderService.GetFolderSummaryAsync(folder);
            var skippedNote = summary.SkippedCount > 0
                ? LF("Folder.SkippedNote", summary.SkippedCount)
                : string.Empty;
            ShowConfirmation(
                L("Folder.DeleteTitle"),
                LF("Folder.DeleteMessage", summary.Path, ByteFormatter.Format(summary.SizeBytes), summary.FileCount, skippedNote),
                L("Files.MoveParentToBin"),
                () => TryRecycleContainingFolder(summary.Path));
        }
        catch (Exception ex)
        {
            ShowNotice(L("Folder.InspectFailed"), ex.Message);
        }
    }

    private async void TryRecycleContainingFolder(string folderPath)
    {
        try
        {
            _fileFinderService.MoveFolderToRecycleBin(folderPath);
            var prefix = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var removed = FoundFiles.Where(file => file.FullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            SelectedFileDetails = null;
            foreach (var file in removed)
                file.IsRemoving = true;
            await Task.Delay(140);
            foreach (var file in removed)
            {
                FoundFiles.Remove(file);
                VisibleFiles.Remove(file);
            }
            OnPropertyChanged(nameof(ShowFilesEmptyState));
            OnPropertyChanged(nameof(FilesEmptyTitle));
            OnPropertyChanged(nameof(FilesEmptyHint));
            OnFileSelectionChanged();
            UpdateFilesSummary();
        }
        catch (Exception ex)
        {
            ShowNotice(L("Folder.MoveFailed"), ex.Message);
        }
    }

    private void RequestQuickRecycle(FileItemViewModel item)
    {
        if (!item.CanQuickRecycle) return;
        ShowConfirmation(
            L("File.QuickTitle"),
            LF("File.QuickMessage", item.Name, item.SizeText, item.SafetyEffect),
            L("Files.MoveToBin"),
            () => TryRecycleFile(item));
    }

    private void TryRecycleFile(FileItemViewModel item)
    {
        try { item.MoveToRecycleBinWithoutPrompt(); }
        catch (Exception ex) { ShowNotice(L("File.MoveFailed"), ex.Message); }
    }

    private async Task<bool> LoadInstalledAppsAsync(bool force)
    {
        if (IsUninstallerBusy || (_hasLoadedInstalledApps && !force))
            return _hasLoadedInstalledApps;

        IsUninstallerBusy = true;
        UninstallerStatus = L("Uninstaller.Loading");
        try
        {
            var apps = await _installedAppsService.LoadAsync();
            InstalledApps.Clear();
            foreach (var app in apps)
                InstalledApps.Add(new InstalledAppViewModel(app));
            _hasLoadedInstalledApps = true;
            RefreshVisibleInstalledApps();
            UninstallerStatus = apps.Count == 0 ? L("Uninstaller.Empty") : LF("Uninstaller.Loaded", apps.Count);
            return true;
        }
        catch (Exception ex)
        {
            UninstallerStatus = LF("Uninstaller.LoadFailed", ex.Message);
            return false;
        }
        finally
        {
            IsUninstallerBusy = false;
        }
    }

    private void RefreshVisibleInstalledApps()
    {
        var query = UninstallerSearch.Trim();
        IEnumerable<InstalledAppViewModel> filtered = InstalledApps;
        if (!string.IsNullOrWhiteSpace(query))
            filtered = filtered.Where(app => app.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || app.Publisher.Contains(query, StringComparison.CurrentCultureIgnoreCase));

        filtered = UninstallerSort switch
        {
            "SizeAsc" => filtered.OrderBy(app => app.SortSize < 0).ThenBy(app => app.SortSize).ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase),
            "DateDesc" => filtered.OrderBy(app => app.SortDate == DateTime.MinValue).ThenByDescending(app => app.SortDate).ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase),
            "DateAsc" => filtered.OrderBy(app => app.SortDate == DateTime.MinValue).ThenBy(app => app.SortDate).ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase),
            "NameAsc" => filtered.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase),
            "NameDesc" => filtered.OrderByDescending(app => app.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => filtered.OrderBy(app => app.SortSize < 0).ThenByDescending(app => app.SortSize).ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        VisibleInstalledApps.Clear();
        foreach (var app in filtered)
            VisibleInstalledApps.Add(app);
        OnPropertyChanged(nameof(UninstallerCountText));
        OnPropertyChanged(nameof(HasInstalledApps));
    }

    private void RequestUninstall(InstalledAppViewModel app)
    {
        if (IsUninstallerBusy || !app.CanUninstall)
            return;

        // Always keep the first destructive decision inside NODR. Some vendor uninstallers
        // return immediately, launch a child process, or expose no useful UI at all. The user
        // must still get a clear chance to cancel before NODR launches anything external.
        ShowConfirmation(
            LF("Uninstaller.ConfirmTitle", app.Name),
            LF("Uninstaller.ConfirmMessage", app.Name, app.Publisher, app.SizeText),
            L("Uninstaller.ConfirmAction"),
            () => _ = UninstallAppAsync(app));
    }

    private async Task UninstallAppAsync(InstalledAppViewModel app)
    {
        if (IsUninstallerBusy || !app.CanUninstall)
            return;

        _uninstallTarget = app;
        _uninstallEvidence = _uninstallerService.CaptureUninstallEvidence(app.App);
        app.IsBusy = true;
        IsUninstallerBusy = true;
        var refreshApps = false;
        UninstallerStatus = LF("Uninstaller.Opening", app.Name);
        try
        {
            await _uninstallerService.RunStockUninstallerAsync(app.App);
            UninstallerStatus = L("Uninstaller.ScanningResiduals");
            await PopulateResidualsAsync(app, _uninstallEvidence);
            _hasLoadedInstalledApps = false;
            refreshApps = true;
        }
        catch (OperationCanceledException)
        {
            _uninstallEvidence = null;
            UninstallerStatus = L("Uninstaller.Cancelled");
        }
        catch (Exception ex)
        {
            _uninstallEvidence = null;
            UninstallerStatus = LF("Uninstaller.UninstallFailed", ex.Message);
        }
        finally
        {
            app.IsBusy = false;
            IsUninstallerBusy = false;
        }

        if (refreshApps)
        {
            var postUninstallStatus = UninstallerStatus;
            if (await LoadInstalledAppsAsync(force: true))
                UninstallerStatus = postUninstallStatus;
        }
    }

    private async Task PopulateResidualsAsync(InstalledAppViewModel app, UninstallEvidenceSnapshot? evidence = null)
    {
        // Native installer exit codes are not treated as proof of removal. Registry cleanup
        // requires a real before -> after disappearance of app-specific filesystem evidence.
        var scan = await _uninstallerService.ScanResidualsAsync(app.App, evidence);
        foreach (var existing in UninstallerResiduals)
            existing.PropertyChanged -= OnUninstallerResidualChanged;
        UninstallerResiduals.Clear();

        // The residual modal is action-only. Report-only or uncertain findings are deliberately
        // omitted: if NODR cannot safely clean anything, there is no modal to dismiss.
        foreach (var residual in scan.Items.Where(item => item.CanClean))
        {
            var vm = new UninstallResidualViewModel(residual);
            vm.PropertyChanged += OnUninstallerResidualChanged;
            UninstallerResiduals.Add(vm);
        }

        if (UninstallerResiduals.Count == 0)
        {
            IsUninstallerResidualOpen = false;
            UninstallerResidualStatus = string.Empty;
            _uninstallTarget = null;
            _uninstallEvidence = null;
            UninstallerStatus = scan.OriginalUninstallEntryRemains && !scan.OriginalUninstallEntryCanClean
                ? L("Uninstaller.NotCompleted")
                : L("Uninstaller.NoActionableResiduals");
            return;
        }

        var folderCount = UninstallerResiduals.Count(item => !item.IsRegistry);
        var registryCount = UninstallerResiduals.Count(item => item.IsRegistry);
        UninstallerResidualStatus = LF(
            "Uninstaller.ActionableResidualsFound",
            ByteFormatter.Format(scan.CleanableBytes),
            folderCount,
            registryCount);
        OnPropertyChanged(nameof(UninstallerResidualTitle));
        OnPropertyChanged(nameof(UninstallerResidualSummary));
        OnPropertyChanged(nameof(CanCleanUninstallerResiduals));
        CleanUninstallerResidualsCommand.RaiseCanExecuteChanged();
        IsUninstallerResidualOpen = true;
    }

    private async Task RescanUninstallerResidualsAsync()
    {
        if (_uninstallTarget is null || IsUninstallerBusy)
            return;
        IsUninstallerBusy = true;
        try
        {
            foreach (var residual in UninstallerResiduals)
                residual.PropertyChanged -= OnUninstallerResidualChanged;
            await PopulateResidualsAsync(_uninstallTarget, _uninstallEvidence);
        }
        finally
        {
            IsUninstallerBusy = false;
        }
    }

    private async Task CleanSelectedResidualsAsync()
    {
        var selected = UninstallerResiduals.Where(item => item.IsSelected && item.CanClean).ToArray();
        if (selected.Length == 0 || IsUninstallerBusy)
            return;

        IsUninstallerBusy = true;
        UninstallerResidualStatus = L("Uninstaller.CleaningResiduals");
        try
        {
            var result = await _uninstallerService.CleanResidualsAsync(selected.Select(item => item.Residual));
            var removedPaths = result.RemovedDisplayPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in selected)
            {
                if (removedPaths.Contains(item.Residual.DisplayPath))
                {
                    item.PropertyChanged -= OnUninstallerResidualChanged;
                    UninstallerResiduals.Remove(item);
                }
            }
            var resultText = result.SkippedItems == 0
                ? LF("Uninstaller.ResidualCleaned", result.RemovedItems)
                : LF("Uninstaller.ResidualCleanedPartial", result.RemovedItems, result.SkippedItems);
            UninstallerStatus = resultText;

            if (UninstallerResiduals.Count == 0)
            {
                CloseUninstallerResiduals();
                return;
            }

            UninstallerResidualStatus = resultText;
            OnPropertyChanged(nameof(UninstallerResidualSummary));
            OnPropertyChanged(nameof(CanCleanUninstallerResiduals));
            CleanUninstallerResidualsCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            UninstallerResidualStatus = LF("Uninstaller.ResidualCleanFailed", ex.Message);
        }
        finally
        {
            IsUninstallerBusy = false;
        }
    }

    private void CloseUninstallerResiduals()
    {
        IsUninstallerResidualOpen = false;
        foreach (var residual in UninstallerResiduals)
            residual.PropertyChanged -= OnUninstallerResidualChanged;
        UninstallerResiduals.Clear();
        _uninstallTarget = null;
        _uninstallEvidence = null;
        UninstallerResidualStatus = string.Empty;
    }

    private void OnUninstallerResidualChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UninstallResidualViewModel.IsSelected))
            return;
        OnPropertyChanged(nameof(UninstallerResidualSummary));
        OnPropertyChanged(nameof(CanCleanUninstallerResiduals));
        CleanUninstallerResidualsCommand.RaiseCanExecuteChanged();
    }

    private async Task ScanCleanupAsync()
    {
        if (IsCleanupBusy)
            return;

        IsCleanupBusy = true;
        HasCleanupResults = false;
        IsCleanupReview = false;
        IsCleanupSuccess = false;
        foreach (var item in CleanupCategories)
            item.PropertyChanged -= OnCleanupCategoryChanged;
        CleanupHeadline = L("Cleanup.ScanningHeadline");
        CleanupStatus = L("Cleanup.ScanningStatus");
        CleanupResultText = string.Empty;
        CleanupCategories.Clear();
        RecommendedCleanupCategories.Clear();
        OptionalCleanupCategories.Clear();

        _cleanupScanCts?.Dispose();
        _cleanupScanCts = new CancellationTokenSource();

        try
        {
            var result = await _cleanupService.ScanAsync(_cleanupScanCts.Token);
            var recycle = result.Categories.FirstOrDefault(category => category.Kind == CleanupCategoryKind.RecycleBin);
            _lastRecycleBinBytes = recycle?.SizeBytes ?? 0;
            RecycleBinSummary = _lastRecycleBinBytes > 0
                ? LF("Recycle.Summary", ByteFormatter.Format(_lastRecycleBinBytes.Value))
                : L("Recycle.Empty");
            foreach (var scan in result.Categories)
            {
                if (scan.SizeBytes <= 0)
                    continue;
                var item = new CleanupCategoryViewModel(scan);
                item.PropertyChanged += OnCleanupCategoryChanged;
                CleanupCategories.Add(item);
                if (item.IsRecommended)
                    RecommendedCleanupCategories.Add(item);
                else
                    OptionalCleanupCategories.Add(item);
            }

            var total = result.Categories.Sum(category => category.SizeBytes);
            CleanupHeadline = total > 0 ? LF("Cleanup.SelectedHeadline", SelectedCleanupSizeText) : L("Cleanup.NothingHeadline");
            CleanupStatus = total > 0 ? L("Cleanup.FoundStatus") : L("Cleanup.NothingStatus");
            BackgroundNotificationRequested?.Invoke(this, new BackgroundNotification(L("Notification.ScanTitle"), LF("Notification.CleanupScanBody", ByteFormatter.Format(total))));
            OnPropertyChanged(nameof(SelectedCleanupSizeText));
            OnPropertyChanged(nameof(TotalCleanupSizeText));
            OnPropertyChanged(nameof(CleanupTotalFoundText));
            OnPropertyChanged(nameof(SelectedCleanupCountText));
            OnPropertyChanged(nameof(SelectedCleanupSummaryText));
            OnPropertyChanged(nameof(TopCleanupActionText));
            OnPropertyChanged(nameof(CanReview));
            OnPropertyChanged(nameof(RecommendedCleanupBytes));
            OnPropertyChanged(nameof(OverviewCleanupHeadline));
            OnPropertyChanged(nameof(OverviewCleanupStatus));
            HasCleanupResults = true;
        }
        catch (OperationCanceledException)
        {
            CleanupHeadline = L("Cleanup.ScanCancelled");
            CleanupStatus = L("Cleanup.ScanCancelledStatus");
        }
        catch (Exception ex)
        {
            CleanupHeadline = L("Cleanup.ScanFailed");
            CleanupStatus = ex.Message;
        }
        finally
        {
            _cleanupScanCts?.Dispose();
            _cleanupScanCts = null;
            IsCleanupBusy = false;
            OnPropertyChanged(nameof(CanClean));
            ReviewCleanupCommand.RaiseCanExecuteChanged();
            CleanSelectedCommand.RaiseCanExecuteChanged();
        }
    }

    private void CancelCleanupScan() => _cleanupScanCts?.Cancel();

    private async Task CleanSelectedAsync()
    {
        if (!CanClean)
            return;

        var selected = CleanupCategories
            .Where(category => category.IsSelected && category.HasData)
            .Select(category => category.Scan)
            .ToList();

        // The category checkboxes are the explicit selection step. Both Cleanup CTAs
        // execute this same path directly; there is no intermediate review state.
        await ExecuteCleanupAsync(selected);
    }

    private async Task ExecuteCleanupAsync(IReadOnlyList<CleanupCategoryScan> selected)
    {
        IsCleanupCleaning = true;
        IsCleanupBusy = true;
        CleanupProgressPercent = 0;
        CleanupProgressText = L("Cleanup.Preparing");
        CleanupHeadline = L("Cleanup.CleaningHeadline");
        CleanupStatus = L("Cleanup.CleaningStatus");
        CleanupResultText = string.Empty;

        try
        {
            var progress = new Progress<CleanupProgress>(value =>
            {
                CleanupProgressPercent = value.Percent;
                CleanupProgressText = value.TotalFiles > 0
                    ? LF("Cleanup.ProgressWithCount", value.Status, value.ProcessedFiles, value.TotalFiles)
                    : value.Status;
            });
            var result = await _cleanupService.CleanAsync(selected, progress: progress);
            CleanupProgressPercent = 100;
            _lastCleanupResult = result;
            CleanupProgressText = L("Cleanup.CompleteProgress");
            CleanupHeadline = LF("Cleanup.SuccessHeadline", ByteFormatter.Format(result.FreedBytes));
            CleanupResultText = result.SkippedFiles > 0
                ? LF("Cleanup.SuccessResultSkipped", result.DeletedFiles, result.SkippedFiles)
                : LF("Cleanup.SuccessResult", result.DeletedFiles);
            CleanupStatus = result.SkippedFiles > 0
                ? L("Cleanup.SuccessStatusSkipped")
                : L("Cleanup.SuccessStatus");
            var skippedSuffix = result.SkippedFiles > 0 ? LF("Notification.SkippedSuffix", result.SkippedFiles) : string.Empty;
            BackgroundNotificationRequested?.Invoke(this, new BackgroundNotification(L("Notification.CleanupTitle"), LF("Notification.CleanupBody", ByteFormatter.Format(result.FreedBytes), result.DeletedFiles, skippedSuffix)));

            // The Overview card caches the last measured Recycle Bin size. If this
            // cleanup included the bin, update that cache immediately instead of
            // leaving the pre-clean value visible until another scan.
            if (selected.Any(category => category.Kind == CleanupCategoryKind.RecycleBin))
            {
                _lastRecycleBinBytes = 0;
                RecycleBinSummary = L("Recycle.Empty");
            }

            foreach (var item in CleanupCategories)
                item.PropertyChanged -= OnCleanupCategoryChanged;
            CleanupCategories.Clear();
            RecommendedCleanupCategories.Clear();
            OptionalCleanupCategories.Clear();
            HasCleanupResults = false;
            IsCleanupReview = false;
            IsCleanupSuccess = true;
        }
        catch (Exception ex)
        {
            CleanupHeadline = L("Cleanup.Stopped");
            CleanupStatus = ex.Message;
        }
        finally
        {
            IsCleanupCleaning = false;
            IsCleanupBusy = false;
            OnPropertyChanged(nameof(CanClean));
            CleanSelectedCommand.RaiseCanExecuteChanged();
        }
    }

    private void ShowConfirmation(string title, string message, string confirmText, Action onConfirm)
    {
        DialogTitle = title;
        DialogMessage = message;
        DialogConfirmText = confirmText;
        DialogShowCancel = true;
        _dialogConfirmAction = onConfirm;
        IsDialogOpen = true;
    }

    private void ShowNotice(string title, string message)
    {
        DialogTitle = title;
        DialogMessage = message;
        DialogConfirmText = L("Common.Close");
        DialogShowCancel = false;
        _dialogConfirmAction = null;
        IsDialogOpen = true;
    }

    private void ConfirmDialog()
    {
        var action = _dialogConfirmAction;
        CloseDialog();
        action?.Invoke();
    }

    private void CancelDialog() => CloseDialog();

    private void CloseDialog()
    {
        IsDialogOpen = false;
        _dialogConfirmAction = null;
    }

    private void OnCleanupCategoryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CleanupCategoryViewModel.IsSelected))
            return;

        OnPropertyChanged(nameof(CanReview));
        OnPropertyChanged(nameof(CanClean));
        OnPropertyChanged(nameof(SelectedCleanupSizeText));
        if (HasCleanupResults && !IsCleanupBusy)
            CleanupHeadline = LF("Cleanup.SelectedHeadline", SelectedCleanupSizeText);
        OnPropertyChanged(nameof(SelectedCleanupCountText));
        OnPropertyChanged(nameof(SelectedCleanupSummaryText));
        OnPropertyChanged(nameof(TopCleanupActionText));
        OnPropertyChanged(nameof(CanShowCleanupQuickAction));
        OnPropertyChanged(nameof(RecommendedCleanupBytes));
        OnPropertyChanged(nameof(OverviewCleanupHeadline));
        OnPropertyChanged(nameof(OverviewCleanupStatus));
        ReviewCleanupCommand.RaiseCanExecuteChanged();
        CleanSelectedCommand.RaiseCanExecuteChanged();
    }

    public async Task StartAsync()
    {
        _monitor.StartLowLevelSensors();
        await RefreshAsync();
        _timer.Start();
        _ = ScanCleanupAsync();
    }

    private async void OnTimerTick(object? sender, EventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_isRefreshing || _disposed)
            return;

        _isRefreshing = true;
        try
        {
            var snapshot = await Task.Run(_monitor.ReadSnapshot);
            Apply(snapshot);
        }
        catch
        {
            SensorStatus = L("Sensor.LiveLimitedData");
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void Apply(SystemSnapshot snapshot)
    {
        CpuUsage = Math.Clamp(snapshot.CpuUsagePercent, 0, 100);
        CpuValue = $"{CpuUsage:0}%";
        CpuName = snapshot.CpuName;
        HasCpuTemperature = snapshot.CpuTemperatureC is not null;
        CpuSubtitle = snapshot.CpuTemperatureC is { } cpuTemp ? $"{cpuTemp:0} °C" : string.Empty;

        HasGpuData = snapshot.GpuUsagePercent.HasValue;
        GpuName = snapshot.GpuName ?? L("Sensor.GpuUnavailable");
        GpuUsage = Math.Clamp(snapshot.GpuUsagePercent ?? 0, 0, 100);
        GpuValue = snapshot.GpuUsagePercent.HasValue ? $"{GpuUsage:0}%" : "—";
        GpuSubtitle = BuildGpuSubtitle(snapshot);

        RamUsage = snapshot.MemoryTotalBytes == 0
            ? 0
            : (double)snapshot.MemoryUsedBytes / snapshot.MemoryTotalBytes * 100.0;
        RamValue = $"{RamUsage:0}%";
        RamSubtitle = snapshot.MemoryTotalBytes == 0
            ? L("Sensor.MemoryUnavailable")
            : $"{ByteFormatter.Format(snapshot.MemoryUsedBytes)} / {ByteFormatter.Format(snapshot.MemoryTotalBytes)}";

        SensorStatus = snapshot.HardwareSensorsAvailable ? L("Sensor.Live") : L("Sensor.LiveLimited");
        LastUpdated = LF("Sensor.Updated", snapshot.Timestamp);

        PushHistory(CpuHistory, CpuUsage);
        PushHistory(GpuHistory, GpuUsage);
        PushHistory(RamHistory, RamUsage);
        UpdateDisks(snapshot.Disks);
    }

    private string BuildGpuSubtitle(SystemSnapshot snapshot)
    {
        var parts = new List<string>();

        if (snapshot.GpuTemperatureC is { } temp)
            parts.Add($"{temp:0} °C");

        if (snapshot.GpuMemoryUsedMb is { } used && snapshot.GpuMemoryTotalMb is { } total && total > 0)
            parts.Add($"VRAM {used / 1024:0.0} / {total / 1024:0.0} GB");

        return parts.Count > 0 ? string.Join("   ·   ", parts) : L("Sensor.DataUnavailable");
    }

    private static void PushHistory(ObservableCollection<double> history, double value)
    {
        history.Add(value);
        while (history.Count > HistoryLength)
            history.RemoveAt(0);
    }

    private void UpdateDisks(IReadOnlyList<DiskSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            var key = snapshot.Name.TrimEnd('\\');
            var existing = Disks.FirstOrDefault(d => d.RootName.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
                Disks.Add(new DiskItemViewModel(snapshot));
            else
                existing.Update(snapshot);
        }

        var active = snapshots.Select(s => s.Name.TrimEnd('\\')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = Disks.Count - 1; i >= 0; i--)
        {
            if (!active.Contains(Disks[i].RootName))
                Disks.RemoveAt(i);
        }
    }

    private string L(string key) => _loc[key];
    private string LF(string key, params object[] args) => _loc.Format(key, args);

    private void BuildChoiceOptions()
    {
        var currentSort = _filesSort;
        LanguageOptions.Clear();
        LanguageOptions.Add(new ChoiceOption("en", "EN", "/Assets/Ui/lang-en.png"));
        LanguageOptions.Add(new ChoiceOption("uk", "UA", "/Assets/Ui/lang-ua.png"));
        LanguageOptions.Add(new ChoiceOption("ru", "RU", "/Assets/Ui/lang-ru.png"));
        ThemeOptions.Clear();
        ThemeOptions.Add(new ChoiceOption("Light", "Light", "/Assets/Ui/theme-light.png"));
        ThemeOptions.Add(new ChoiceOption("Dark", "Dark", "/Assets/Ui/theme-dark.png"));
        ThemeOptions.Add(new ChoiceOption("System", "System", "/Assets/Ui/theme-system.png"));
        FilesSortOptions.Clear();
        FilesSortOptions.Add(new ChoiceOption("Size", L("Files.Sort.Size")));
        FilesSortOptions.Add(new ChoiceOption("Date", L("Files.Sort.Date")));
        UninstallerSortOptions.Clear();
        UninstallerSortOptions.Add(new ChoiceOption("SizeDesc", L("Uninstaller.Sort.SizeDesc")));
        UninstallerSortOptions.Add(new ChoiceOption("SizeAsc", L("Uninstaller.Sort.SizeAsc")));
        UninstallerSortOptions.Add(new ChoiceOption("DateDesc", L("Uninstaller.Sort.DateDesc")));
        UninstallerSortOptions.Add(new ChoiceOption("DateAsc", L("Uninstaller.Sort.DateAsc")));
        UninstallerSortOptions.Add(new ChoiceOption("NameAsc", L("Uninstaller.Sort.NameAsc")));
        UninstallerSortOptions.Add(new ChoiceOption("NameDesc", L("Uninstaller.Sort.NameDesc")));
        _filesSort = currentSort is "Date" ? "Date" : "Size";
        OnPropertyChanged(nameof(CurrentLanguageCode));
        OnPropertyChanged(nameof(CurrentThemeMode));
        OnPropertyChanged(nameof(FilesSort));
        OnPropertyChanged(nameof(UninstallerSort));
    }

    private string LocalizedFilterName(string filter) => filter switch
    {
        "Archives" => L("Files.Filter.Archives"),
        "Videos" => L("Files.Filter.Videos"),
        "Executables" => L("Files.Filter.Executables"),
        "Caches" => L("Files.Filter.Caches"),
        _ => L("Files.Filter.All")
    };

    private string LocalizeDriveCategory(string category) => category switch
    {
        "System" => L("Drive.Category.System"),
        "Programs" => L("Drive.Category.Programs"),
        "Cache & development" => L("Drive.Category.CacheDev"),
        "Media" => L("Drive.Category.Media"),
        "Documents & archives" => L("Drive.Category.Documents"),
        "Virtual disks & VM data" => L("Drive.Category.Virtual"),
        "Installers & disk images" => L("Drive.Category.Installers"),
        _ => L("Drive.Category.Other")
    };

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        BuildChoiceOptions();
        foreach (var category in CleanupCategories) category.RefreshLocalization();
        foreach (var file in FoundFiles) file.RefreshLocalization();
        foreach (var category in DriveCategories) category.RefreshLocalization();
        foreach (var app in InstalledApps) app.RefreshLocalization();
        foreach (var residual in UninstallerResiduals) residual.RefreshLocalization();

        OnPropertyChanged(nameof(FilesEmptyTitle));
        OnPropertyChanged(nameof(FilesEmptyHint));
        OnPropertyChanged(nameof(SelectedFilesSummary));
        OnPropertyChanged(nameof(SelectedCleanupCountText));
        OnPropertyChanged(nameof(SelectedCleanupSummaryText));
        OnPropertyChanged(nameof(TopCleanupActionText));
        OnPropertyChanged(nameof(OverviewCleanupHeadline));
        OnPropertyChanged(nameof(OverviewCleanupStatus));
        OnPropertyChanged(nameof(UninstallerCountText));
        OnPropertyChanged(nameof(UninstallerResidualTitle));
        OnPropertyChanged(nameof(UninstallerResidualSummary));
        if (!IsUninstallerBusy && _hasLoadedInstalledApps)
            UninstallerStatus = InstalledApps.Count == 0 ? L("Uninstaller.Empty") : LF("Uninstaller.Loaded", InstalledApps.Count);
        if (IsUninstallerResidualOpen)
        {
            var folders = UninstallerResiduals.Count(item => !item.IsRegistry);
            var registryEntries = UninstallerResiduals.Count(item => item.IsRegistry);
            UninstallerResidualStatus = LF(
                "Uninstaller.ActionableResidualsFound",
                ByteFormatter.Format(UninstallerResiduals.Sum(item => item.Residual.SizeBytes)),
                folders,
                registryEntries);
        }

        if (_lastRecycleBinBytes.HasValue)
            RecycleBinSummary = _lastRecycleBinBytes.Value > 0 ? LF("Recycle.Summary", ByteFormatter.Format(_lastRecycleBinBytes.Value)) : L("Recycle.Empty");
        else
            RecycleBinSummary = L("Recycle.Checking");

        if (IsCleanupSuccess && _lastCleanupResult is { } result)
        {
            CleanupProgressText = L("Cleanup.CompleteProgress");
            CleanupHeadline = LF("Cleanup.SuccessHeadline", ByteFormatter.Format(result.FreedBytes));
            CleanupResultText = result.SkippedFiles > 0 ? LF("Cleanup.SuccessResultSkipped", result.DeletedFiles, result.SkippedFiles) : LF("Cleanup.SuccessResult", result.DeletedFiles);
            CleanupStatus = result.SkippedFiles > 0 ? L("Cleanup.SuccessStatusSkipped") : L("Cleanup.SuccessStatus");
        }
        else if (!IsCleanupBusy && !HasCleanupResults)
        {
            CleanupHeadline = L("Cleanup.ReadyHeadline");
            CleanupStatus = L("Cleanup.ReadyStatus");
        }

        if (!IsFilesBusy && string.IsNullOrWhiteSpace(FilesRoot))
        {
            FilesHeadline = L("Files.InitialHeadline");
            FilesStatus = L("Files.InitialStatus");
        }
        else if (!IsFilesBusy && !string.IsNullOrWhiteSpace(FilesRoot))
        {
            UpdateFilesSummary();
        }

        if (!IsDialogOpen) DialogConfirmText = L("Common.Confirm");
        _ = RefreshAsync();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _loc.LanguageChanged -= OnLanguageChanged;
        _driveBreakdownCts?.Cancel();
        _driveBreakdownCts?.Dispose();
        _monitor.Dispose();
        _disposed = true;
    }
}
