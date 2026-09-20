using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using System.Windows.Media;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaFontFamily = System.Windows.Media.FontFamily;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using NODR.Services;

namespace NODR;

internal sealed class FatalErrorWindow : Window
{
    public FatalErrorWindow(string title, string message)
    {
        var dark = ThemeService.Instance.IsDarkEffective;
        Title = title;
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new MediaSolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(dark ? "#121519" : "#F8FAFC"));
        Foreground = new MediaSolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(dark ? "#F3F4F6" : "#0F172A"));
        FontFamily = new MediaFontFamily("Segoe UI");

        var panel = new StackPanel { Margin = new Thickness(26) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 12, 0, 22),
            Foreground = new MediaSolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(dark ? "#9CA3AF" : "#64748B")),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 500
        });
        var close = new WpfButton
        {
            Content = LocalizationService.Instance["Fatal.Close"],
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Padding = new Thickness(18, 9, 18, 9),
            Background = new MediaSolidColorBrush(MediaColor.FromRgb(0x10, 0xB9, 0x81)),
            Foreground = MediaBrushes.White,
            BorderThickness = new Thickness(0),
            IsDefault = true,
            IsCancel = true
        };
        close.Click += (_, _) => Close();
        panel.Children.Add(close);
        Content = panel;
    }
}
