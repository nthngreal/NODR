using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using Microsoft.Win32;

namespace NODR.Services;

public sealed class ThemeService
{
    private ThemeService() { }

    public static ThemeService Instance { get; } = new();

    public event EventHandler? ThemeChanged;

    public string CurrentMode { get; private set; } = "System";
    public bool IsDarkEffective { get; private set; } = true;

    public void Initialize(string? mode)
    {
        SetTheme(Normalize(mode), persist: false);
    }

    public void SetTheme(string mode, bool persist = true)
    {
        mode = Normalize(mode);
        CurrentMode = mode;
        ApplyEffectiveTheme();
        if (persist)
            AppSettingsService.SetTheme(mode);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshSystemTheme()
    {
        if (!string.Equals(CurrentMode, "System", StringComparison.OrdinalIgnoreCase))
            return;
        var before = IsDarkEffective;
        ApplyEffectiveTheme();
        if (before != IsDarkEffective)
            ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Normalize(string? mode) => mode switch
    {
        "Light" => "Light",
        "Dark" => "Dark",
        _ => "System"
    };

    private void ApplyEffectiveTheme()
    {
        var dark = CurrentMode switch
        {
            "Dark" => true,
            "Light" => false,
            _ => !IsWindowsLightTheme()
        };
        IsDarkEffective = dark;

        if (System.Windows.Application.Current is null)
            return;

        var palette = dark ? DarkPalette : LightPalette;
        foreach (var pair in palette)
            System.Windows.Application.Current.Resources[pair.Key] = new MediaSolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(pair.Value));
    }

    private static bool IsWindowsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int number ? number != 0 : true;
        }
        catch
        {
            return true;
        }
    }

    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["AppBackgroundBrush"] = "#121519",
        ["SidebarBrush"] = "#121519",
        ["SurfaceBrush"] = "#1A1D24",
        ["SurfaceHoverBrush"] = "#222630",
        ["TextPrimaryBrush"] = "#F3F4F6",
        ["TextSecondaryBrush"] = "#9CA3AF",
        ["AccentBrush"] = "#10B981",
        ["AccentHoverBrush"] = "#059669",
        ["AccentSoftBrush"] = "#2610B981",
        ["BorderBrush"] = "#2D333F",
        ["SuccessBrush"] = "#10B981",
        ["WarningBrush"] = "#E0B66A",
        ["DangerBrush"] = "#E07A7A",
        ["TrackBrush"] = "#2D333F",
        ["ControlBackgroundBrush"] = "#1E232B",
        ["PopupBackgroundBrush"] = "#161A20",
        ["SecondarySurfaceBrush"] = "#2D343F",
        ["SecondaryHoverBrush"] = "#39414D",
        ["ScrollThumbBrush"] = "#4B5563",
        ["ScrollThumbHoverBrush"] = "#6B7280",
        ["ModalSurfaceBrush"] = "#1E2228",
        ["ModalBorderBrush"] = "#2A3038",
        ["OverlayBrush"] = "#A6121519",
        ["DangerSurfaceBrush"] = "#3A2026",
        ["DangerHoverBrush"] = "#4A252C",
        ["DangerBorderBrush"] = "#7F1D2D",
        ["DangerBorderHoverBrush"] = "#B33A4A",
        ["DangerTextBrush"] = "#FCA5A5",
        ["WarningSoftBrush"] = "#2D2A22"
    };

    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["AppBackgroundBrush"] = "#F8FAFC",
        ["SidebarBrush"] = "#F8FAFC",
        ["SurfaceBrush"] = "#FFFFFF",
        ["SurfaceHoverBrush"] = "#F1F5F9",
        ["TextPrimaryBrush"] = "#0F172A",
        ["TextSecondaryBrush"] = "#64748B",
        ["AccentBrush"] = "#10B981",
        ["AccentHoverBrush"] = "#059669",
        ["AccentSoftBrush"] = "#2610B981",
        ["BorderBrush"] = "#E2E8F0",
        ["SuccessBrush"] = "#059669",
        ["WarningBrush"] = "#B45309",
        ["DangerBrush"] = "#DC2626",
        ["TrackBrush"] = "#E2E8F0",
        ["ControlBackgroundBrush"] = "#FFFFFF",
        ["PopupBackgroundBrush"] = "#FFFFFF",
        ["SecondarySurfaceBrush"] = "#E9EEF5",
        ["SecondaryHoverBrush"] = "#DCE4EE",
        ["ScrollThumbBrush"] = "#CBD5E1",
        ["ScrollThumbHoverBrush"] = "#94A3B8",
        ["ModalSurfaceBrush"] = "#FFFFFF",
        ["ModalBorderBrush"] = "#D8E0EA",
        ["OverlayBrush"] = "#660F172A",
        ["DangerSurfaceBrush"] = "#FEF2F2",
        ["DangerHoverBrush"] = "#FEE2E2",
        ["DangerBorderBrush"] = "#FCA5A5",
        ["DangerBorderHoverBrush"] = "#EF4444",
        ["DangerTextBrush"] = "#B91C1C",
        ["WarningSoftBrush"] = "#FFF7E6"
    };
}
