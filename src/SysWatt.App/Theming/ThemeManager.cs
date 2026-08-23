using SysWatt.Core.Settings;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace SysWatt.App.Theming;

public static class ThemeManager
{
    private static readonly IReadOnlyDictionary<string, (string Dark, string Light)> Palette =
        new Dictionary<string, (string, string)>
        {
            ["WindowBackground"] = ("#0B1015", "#F3F6F8"),
            ["CardBackground"] = ("#121920", "#FFFFFF"),
            ["SurfaceBrush"] = ("#121920", "#FFFFFF"),
            ["SurfaceElevatedBrush"] = ("#18222B", "#EAF0F3"),
            ["SurfaceHoverBrush"] = ("#21303A", "#DDE8EC"),
            ["InputBrush"] = ("#0E151B", "#F8FAFB"),
            ["SidebarBrush"] = ("#0E151B", "#E8EEF1"),
            ["BorderBrush"] = ("#2B3B46", "#CCD8DE"),
            ["BorderStrongBrush"] = ("#3C515F", "#AABBC4"),
            ["AccentBrush"] = ("#68EFC0", "#087A5E"),
            ["AccentDarkBrush"] = ("#1D7258", "#0C8C69"),
            ["TextPrimaryBrush"] = ("#F1F7F5", "#132129"),
            ["MutedBrush"] = ("#91A3AE", "#526873"),
            ["DangerBrush"] = ("#FF776F", "#B42318"),
            ["ChartGridBrush"] = ("#31414A", "#D8E1E5"),
            ["SuccessSurfaceBrush"] = ("#11271F", "#E7F6F0"),
            ["WarningSurfaceBrush"] = ("#2A2418", "#FFF6DF")
        };

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static void Apply(AppTheme theme)
    {
        Current = theme;
        if (WpfApplication.Current is null) return;
        foreach (var (key, colors) in Palette)
        {
            var color = (WpfColor)WpfColorConverter.ConvertFromString(theme == AppTheme.Dark ? colors.Dark : colors.Light);
            WpfApplication.Current.Resources[key] = new SolidColorBrush(color);
        }
    }
}
