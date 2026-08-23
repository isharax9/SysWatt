using System.Globalization;
using System.Windows.Data;
using SysWatt.Core.Alerts;
using SysWatt.Core.Sensors;

namespace SysWatt.App.Converters;

public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        MetricKind.CpuUsage => "CPU usage",
        MetricKind.CpuTemperature => "CPU temperature",
        MetricKind.CpuPower => "CPU power",
        MetricKind.GpuUsage => "GPU usage",
        MetricKind.GpuTemperature => "GPU temperature",
        MetricKind.GpuPower => "GPU power",
        MetricKind.MemoryUsage => "Memory usage",
        MetricKind.StorageActivity => "Storage activity",
        MetricKind.StorageReadRate => "Storage read rate",
        MetricKind.StorageWriteRate => "Storage write rate",
        MetricKind.StorageTemperature => "Storage temperature",
        MetricKind.StoragePower => "Storage power",
        MetricKind.FanSpeed => "Fan speed",
        MetricKind.SystemPower => "Measured system power",
        MetricKind.EstimatedDcPower => "PC DC load",
        MetricKind.EstimatedWallPower => "Current wall draw",
        MetricKind.BaseSystemPower => "Motherboard + RAM power",
        MetricKind.CoolingPower => "Cooling power",
        MetricKind.ExternalPower => "Monitors + peripherals",
        SysWatt.Core.Settings.AppTheme.Dark => "Dark",
        SysWatt.Core.Settings.AppTheme.Light => "Light",
        ComparisonOperator.GreaterThan => ">",
        ComparisonOperator.GreaterThanOrEqual => ">=",
        ComparisonOperator.LessThan => "<",
        ComparisonOperator.LessThanOrEqual => "<=",
        AlertSeverity.Info => "Info",
        AlertSeverity.Warning => "Warning",
        AlertSeverity.Critical => "Critical",
        _ => value?.ToString() ?? string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
