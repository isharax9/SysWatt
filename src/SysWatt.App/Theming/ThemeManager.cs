using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SysWatt.Core.Settings;
using WpfApplication = System.Windows.Application;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace SysWatt.App.Theming;

public static class ThemeManager
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private static readonly IReadOnlyDictionary<string, (string Dark, string Light)> Palette =
        new Dictionary<string, (string, string)>
        {
            ["WindowBackground"] = ("#202020", "#F0F0F0"),
            ["CardBackground"] = ("#2B2B2B", "#FFFFFF"),
            ["SurfaceBrush"] = ("#2B2B2B", "#FFFFFF"),
            ["SurfaceElevatedBrush"] = ("#323232", "#E8E8E8"),
            ["SurfaceHoverBrush"] = ("#383838", "#E0EEF9"),
            ["InputBrush"] = ("#1E1E1E", "#FFFFFF"),
            ["SidebarBrush"] = ("#262626", "#F7F7F7"),
            ["BorderBrush"] = ("#444444", "#D0D0D0"),
            ["BorderStrongBrush"] = ("#666666", "#999999"),
            ["AccentBrush"] = ("#4CC2FF", "#0067C0"),
            ["AccentDarkBrush"] = ("#0078D4", "#005A9E"),
            ["TextPrimaryBrush"] = ("#FFFFFF", "#000000"),
            ["MutedBrush"] = ("#A6A6A6", "#5C5C5C"),
            ["DangerBrush"] = ("#FF99A4", "#C42B1C"),
            ["ChartGridBrush"] = ("#383838", "#E0E0E0"),
            ["SuccessSurfaceBrush"] = ("#1F382B", "#DFF6DD"),
            ["WarningSurfaceBrush"] = ("#3F3519", "#FFF4CE")
        };

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    public static void Apply(AppTheme theme)
    {
        Current = theme;
        if (WpfApplication.Current is null) return;
        foreach (var (key, colors) in Palette)
        {
            var color = (WpfColor)WpfColorConverter.ConvertFromString(theme == AppTheme.Dark ? colors.Dark : colors.Light);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            WpfApplication.Current.Resources[key] = brush;
        }

        foreach (Window window in WpfApplication.Current.Windows)
        {
            ApplyToWindow(window);
        }
    }

    public static void ApplyToWindow(Window window)
    {
        var helper = new WindowInteropHelper(window);
        var hwnd = helper.Handle;
        if (hwnd == IntPtr.Zero)
        {
            window.SourceInitialized += (_, _) => ApplyWindowDarkMode(helper.Handle, Current == AppTheme.Dark);
        }
        else
        {
            ApplyWindowDarkMode(hwnd, Current == AppTheme.Dark);
        }
    }

    private static void ApplyWindowDarkMode(IntPtr hwnd, bool isDark)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var useDark = isDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDark, sizeof(int));
            }
        }
        catch { }
    }
}
