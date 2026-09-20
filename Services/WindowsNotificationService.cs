using System.Drawing;

namespace NODR.Services;

public sealed class WindowsNotificationService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly Action _onNotificationClicked;
    private readonly System.Windows.Forms.Timer _hideTimer;
    private bool _disposed;

    public WindowsNotificationService(Action onNotificationClicked)
    {
        _onNotificationClicked = onNotificationClicked;
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "NODR",
            Visible = false
        };

        try
        {
            _notifyIcon.Icon = !string.IsNullOrWhiteSpace(Environment.ProcessPath)
                ? Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application
                : SystemIcons.Application;
        }
        catch
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }

        _notifyIcon.BalloonTipClicked += OnBalloonTipClicked;
        _notifyIcon.Click += OnTrayIconClicked;

        _hideTimer = new System.Windows.Forms.Timer { Interval = 12_000 };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!_disposed)
                _notifyIcon.Visible = false;
        };
    }

    public void Show(string title, string message)
    {
        if (_disposed)
            return;

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
        _notifyIcon.Visible = true;
        _notifyIcon.ShowBalloonTip(8000);
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void OnBalloonTipClicked(object? sender, EventArgs e) => Activate();
    private void OnTrayIconClicked(object? sender, EventArgs e) => Activate();

    private void Activate()
    {
        if (_disposed)
            return;
        _hideTimer.Stop();
        _notifyIcon.Visible = false;
        _onNotificationClicked();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _hideTimer.Stop();
        _hideTimer.Dispose();
        _notifyIcon.BalloonTipClicked -= OnBalloonTipClicked;
        _notifyIcon.Click -= OnTrayIconClicked;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
