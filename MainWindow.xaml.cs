using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Input;
using NODR.Models;
using NODR.Services;
using NODR.ViewModels;

namespace NODR;

public partial class MainWindow : Window
{
    private const int WmSettingChange = 0x001A;
    private const uint FlashwStop = 0;
    private const uint FlashwAll = 3;
    private const uint FlashwTimerNoFg = 12;

    private readonly MainViewModel _viewModel = new();
    private readonly WindowsNotificationService _notifications;
    private HwndSource? _hwndSource;

    public MainWindow()
    {
        _notifications = new WindowsNotificationService(FocusFromNotification);
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.BackgroundNotificationRequested += OnBackgroundNotificationRequested;
        ThemeService.Instance.ThemeChanged += OnThemeChanged;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Activated += OnActivated;
        Closed += OnClosed;
    }


    private static void TogglePopup(System.Windows.Controls.Primitives.Popup popup)
    {
        popup.IsOpen = !popup.IsOpen;
    }

    private void OnLanguageMenuClick(object sender, RoutedEventArgs e) => TogglePopup(LanguagePopup);

    private void OnLanguagePopupItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string code }) _viewModel.CurrentLanguageCode = code;
        LanguagePopup.IsOpen = false;
    }

    private void OnThemeMenuClick(object sender, RoutedEventArgs e) => TogglePopup(ThemePopup);

    private void OnThemePopupItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string mode }) _viewModel.CurrentThemeMode = mode;
        ThemePopup.IsOpen = false;
    }

    private void OnUninstallerSortMenuClick(object sender, RoutedEventArgs e) => TogglePopup(UninstallerSortPopup);

    private void OnUninstallerSortPopupItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string sort }) _viewModel.UninstallerSort = sort;
        UninstallerSortPopup.IsOpen = false;
    }

    private void OnFilesSortMenuClick(object sender, RoutedEventArgs e) => TogglePopup(FilesSortPopup);

    private void OnFilesSortPopupItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string sort }) _viewModel.FilesSort = sort;
        FilesSortPopup.IsOpen = false;
    }

    private void OnUninstallerSearchPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        _viewModel.UninstallerSearch = string.Empty;
        if (sender is System.Windows.Controls.TextBox box) box.Focus();
        e.Handled = true;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        SourceInitialized -= OnSourceInitialized;
        var handle = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);
        ApplyTitleBarTheme();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.StartAsync();
    }

    private void OnBackgroundNotificationRequested(object? sender, BackgroundNotification notification)
    {
        if (WindowState != WindowState.Minimized && IsActive)
            return;

        _notifications.Show(notification.Title, notification.Message);
        FlashTaskbar();
    }

    private void FocusFromNotification()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Show();
            Activate();
            Topmost = true;
            Topmost = false;
            try { SetForegroundWindow(new WindowInteropHelper(this).Handle); } catch { }
        }));
    }

    private void OnActivated(object? sender, EventArgs e) => StopTaskbarFlash();

    private void OnThemeChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(ApplyTitleBarTheme));

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmSettingChange)
            ThemeService.Instance.RefreshSystemTheme();
        return IntPtr.Zero;
    }

    private void ApplyTitleBarTheme()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;
            var enabled = ThemeService.Instance.IsDarkEffective ? 1 : 0;
            if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        }
        catch
        {
            // Window chrome styling is cosmetic; never block startup for it.
        }
    }

    private void FlashTaskbar()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;
            var info = new FlashWindowInfo
            {
                cbSize = (uint)Marshal.SizeOf<FlashWindowInfo>(),
                hwnd = handle,
                dwFlags = FlashwAll | FlashwTimerNoFg,
                uCount = 4,
                dwTimeout = 0
            };
            FlashWindowEx(ref info);
        }
        catch { }
    }

    private void StopTaskbarFlash()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;
            var info = new FlashWindowInfo
            {
                cbSize = (uint)Marshal.SizeOf<FlashWindowInfo>(),
                hwnd = handle,
                dwFlags = FlashwStop,
                uCount = 0,
                dwTimeout = 0
            };
            FlashWindowEx(ref info);
        }
        catch { }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        Activated -= OnActivated;
        _viewModel.BackgroundNotificationRequested -= OnBackgroundNotificationRequested;
        ThemeService.Instance.ThemeChanged -= OnThemeChanged;
        if (_hwndSource is not null)
            _hwndSource.RemoveHook(WndProc);
        _notifications.Dispose();
        _viewModel.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo pwfi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
