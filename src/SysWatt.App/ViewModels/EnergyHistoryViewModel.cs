using System.Collections.ObjectModel;
using System.Globalization;
using SysWatt.App.Commands;
using SysWatt.Core.Energy;
using SysWatt.Core.Sensors;

using System.Windows;

namespace SysWatt.App.ViewModels;

public sealed record EnergyListItem(
    string DateText,
    string OnTimeText,
    string DcKwhText,
    string WallKwhText,
    string AverageWattsText,
    string PeakWattsText,
    double FigureWidthRatio,
    string FigureColor)
{
    public GridLength FigureStarWidth => new(Math.Max(0.0001, FigureWidthRatio), GridUnitType.Star);
    public GridLength FigureRemainingStarWidth => new(Math.Max(0.0001, 1.0 - FigureWidthRatio), GridUnitType.Star);
}

public sealed class CalendarDayItem : ViewModelBase
{
    private bool _isSelected;

    public int DayNumber { get; }
    public DateOnly Date { get; }
    public bool IsCurrentMonth { get; }
    public bool HasData { get; }
    public double KilowattHours { get; }
    public string ColorHex { get; }
    public bool IsToday { get; }
    public string Tooltip { get; }
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public CalendarDayItem(int dayNumber, DateOnly date, bool isCurrentMonth, bool hasData, double kwh, string colorHex, bool isToday, string tooltip)
    {
        DayNumber = dayNumber;
        Date = date;
        IsCurrentMonth = isCurrentMonth;
        HasData = hasData;
        KilowattHours = kwh;
        ColorHex = colorHex;
        IsToday = isToday;
        Tooltip = tooltip;
    }
}

public sealed class EnergyHistoryViewModel : ViewModelBase
{
    private readonly IEnergyHistoryStore _store;
    private string _viewType = "Day view";
    private string _figureScale = "Linear scale";
    private int _selectedYear = DateTime.Today.Year;
    private int _selectedMonth = DateTime.Today.Month;
    private CalendarDayItem? _selectedCalendarDay;
    private string _monthSummary = string.Empty;
    private string _selectedDaySummary = string.Empty;
    private IReadOnlyList<DailyEnergySummary> _cachedHistory = [];
    private IReadOnlyList<EnergyListItem> _listItems = [];
    private IReadOnlyList<CalendarDayItem> _calendarDays = [];
    private string _statusMessage = "Loading historical energy data…";

    public event EventHandler? RequestClose;
    public event EventHandler? ImportRequested;
    public event EventHandler? ExportRequested;

    public IReadOnlyList<string> ViewTypes { get; } = ["Day view", "Week view", "Month view"];
    public IReadOnlyList<string> FigureScales { get; } = ["Linear scale", "Logarithmic scale"];
    public IReadOnlyList<int> Years { get; } = [2024, 2025, 2026, 2027, 2028];
    public IReadOnlyList<int> Months { get; } = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

    public string ViewType
    {
        get => _viewType;
        set
        {
            if (Set(ref _viewType, value))
            {
                BuildList();
            }
        }
    }

    public string FigureScale
    {
        get => _figureScale;
        set
        {
            if (Set(ref _figureScale, value))
            {
                BuildList();
            }
        }
    }

    public int SelectedYear
    {
        get => _selectedYear;
        set { if (Set(ref _selectedYear, value)) _ = RefreshAsync(); }
    }

    public int SelectedMonth
    {
        get => _selectedMonth;
        set { if (Set(ref _selectedMonth, value)) _ = RefreshAsync(); }
    }

    public IReadOnlyList<EnergyListItem> ListItems => _listItems;
    public IReadOnlyList<CalendarDayItem> CalendarDays => _calendarDays;
    public string MonthSummary => _monthSummary;
    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }
    public string SelectedDaySummary
    {
        get => _selectedDaySummary;
        private set => Set(ref _selectedDaySummary, value);
    }

    public CalendarDayItem? SelectedCalendarDay
    {
        get => _selectedCalendarDay;
        set
        {
            if (Set(ref _selectedCalendarDay, value))
            {
                foreach (var day in _calendarDays)
                {
                    day.IsSelected = day == value;
                }

                if (value is not null && value.HasData)
                {
                    SelectedDaySummary = $"{value.Date:yyyy/MM/dd} ({value.Date:ddd}): {value.KilowattHours:0.00} kWh recorded.";
                }
                else if (value is not null)
                {
                    SelectedDaySummary = $"{value.Date:yyyy/MM/dd} ({value.Date:ddd}): No usage recorded.";
                }
                else
                {
                    SelectedDaySummary = string.Empty;
                }
            }
        }
    }

    public RelayCommand PreviousMonthCommand { get; }
    public RelayCommand NextMonthCommand { get; }
    public RelayCommand CurrentMonthCommand { get; }
    public RelayCommand OkCommand { get; }
    public RelayCommand ImportCommand { get; }
    public RelayCommand ExportCommand { get; }

    public EnergyHistoryViewModel(IEnergyHistoryStore store)
    {
        _store = store;
        PreviousMonthCommand = new RelayCommand(PreviousMonth);
        NextMonthCommand = new RelayCommand(NextMonth);
        CurrentMonthCommand = new RelayCommand(CurrentMonth);
        OkCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));
        ImportCommand = new RelayCommand(() => ImportRequested?.Invoke(this, EventArgs.Empty));
        ExportCommand = new RelayCommand(() => ExportRequested?.Invoke(this, EventArgs.Empty));
    }

    public async Task RefreshAsync()
    {
        StatusMessage = "Loading historical energy data…";
        try
        {
            var year = SelectedYear;
            var month = SelectedMonth;
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var startOfMonth = new DateOnly(year, month, 1);
            var endOfMonth = new DateOnly(year, month, daysInMonth);

            // Fetch current month data plus past 365 days of history for Day/Week/Month views
            var monthRange = await _store.GetRangeAsync(startOfMonth, endOfMonth);
            var historyRange = await _store.GetRangeAsync(DateOnly.FromDateTime(DateTime.Today.AddDays(-365)), DateOnly.FromDateTime(DateTime.Today));
            _cachedHistory = historyRange;

            BuildCalendar(year, month, monthRange);
            BuildList();
            StatusMessage = historyRange.Any(day => day.HasData)
                ? string.Empty
                : "No historical energy data has been recorded yet.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Historical energy data could not be loaded: {ex.Message}";
        }
    }

    private void PreviousMonth()
    {
        if (SelectedMonth == 1)
        {
            SelectedYear--;
            SelectedMonth = 12;
        }
        else
        {
            SelectedMonth--;
        }
    }

    private void NextMonth()
    {
        if (SelectedMonth == 12)
        {
            SelectedYear++;
            SelectedMonth = 1;
        }
        else
        {
            SelectedMonth++;
        }
    }

    private void CurrentMonth()
    {
        SelectedYear = DateTime.Today.Year;
        SelectedMonth = DateTime.Today.Month;
    }

    private void BuildCalendar(int year, int month, IReadOnlyList<DailyEnergySummary> monthData)
    {
        var dataByDate = monthData.ToDictionary(d => d.Date);
        var firstDayOfWeek = (int)new DateTime(year, month, 1).DayOfWeek; // 0 = Sunday
        var daysInMonth = DateTime.DaysInMonth(year, month);

        var list = new List<CalendarDayItem>(42);
        // Pad before
        var prevMonth = month == 1 ? 12 : month - 1;
        var prevYear = month == 1 ? year - 1 : year;
        var daysInPrevMonth = DateTime.DaysInMonth(prevYear, prevMonth);

        for (var i = firstDayOfWeek - 1; i >= 0; i--)
        {
            var d = daysInPrevMonth - i;
            var date = new DateOnly(prevYear, prevMonth, d);
            list.Add(new CalendarDayItem(d, date, false, false, 0, "Transparent", false, string.Empty));
        }

        double totalKwh = 0;
        var countDaysWithData = 0;
        double totalAvgWatts = 0;
        double peakWatts = 0;

        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(year, month, day);
            var hasData = dataByDate.TryGetValue(date, out var summary) && summary.HasData;
            var kwh = hasData ? summary!.KilowattHours : 0;
            var color = ColorForKwh(hasData, kwh);
            var isToday = date == DateOnly.FromDateTime(DateTime.Today);
            var tooltip = hasData
                ? $"{date:yyyy/MM/dd} ({date:ddd}): {kwh:0.00} kWh · On-time: {summary!.DurationFormatted} · Avg {summary.AverageWatts:0.#} W · Peak {summary.PeakWatts:0.#} W"
                : $"{date:yyyy/MM/dd} ({date:ddd}): No recorded usage";

            if (hasData)
            {
                totalKwh += kwh;
                countDaysWithData++;
                totalAvgWatts += summary!.AverageWatts;
                if (summary.PeakWatts > peakWatts) peakWatts = summary.PeakWatts;
            }

            list.Add(new CalendarDayItem(day, date, true, hasData, kwh, color, isToday, tooltip));
        }

        // Pad after up to 42 cells (6 rows x 7 cols)
        var remaining = 42 - list.Count;
        var nextMonth = month == 12 ? 1 : month + 1;
        var nextYear = month == 12 ? year + 1 : year;
        for (var i = 1; i <= remaining; i++)
        {
            var date = new DateOnly(nextYear, nextMonth, i);
            list.Add(new CalendarDayItem(i, date, false, false, 0, "Transparent", false, string.Empty));
        }

        _calendarDays = list;
        var avg = countDaysWithData > 0 ? totalAvgWatts / countDaysWithData : 0;
        _monthSummary = $"Current month total energy: {totalKwh:0.00} kWh (Average: {avg:0.#} W, Peak: {peakWatts:0.#} W)";

        OnPropertyChanged(nameof(CalendarDays));
        OnPropertyChanged(nameof(MonthSummary));
    }

    private void BuildList()
    {
        if (_cachedHistory.Count == 0)
        {
            _listItems = [];
            OnPropertyChanged(nameof(ListItems));
            return;
        }

        var isLog = FigureScale.StartsWith("Log", StringComparison.OrdinalIgnoreCase);

        if (ViewType.StartsWith("Week", StringComparison.OrdinalIgnoreCase))
        {
            // Group by calendar week (Monday to Sunday)
            var weekGroups = _cachedHistory
                .Where(d => d.HasData)
                .GroupBy(d =>
                {
                    var dt = d.Date.ToDateTime(TimeOnly.MinValue);
                    var diff = (7 + (dt.DayOfWeek - DayOfWeek.Monday)) % 7;
                    var monday = DateOnly.FromDateTime(dt.AddDays(-diff));
                    return monday;
                })
                .OrderByDescending(g => g.Key)
                .Take(26)
                .ToList();

            var maxKwh = weekGroups.Count > 0 ? weekGroups.Max(g => g.Sum(x => x.KilowattHours)) : 1;
            if (maxKwh < 0.001) maxKwh = 1;

            var list = new List<EnergyListItem>(weekGroups.Count);
            foreach (var group in weekGroups)
            {
                var monday = group.Key;
                var sunday = monday.AddDays(6);
                var totalKwh = group.Sum(x => x.KilowattHours);
                var totalDc = totalKwh * 0.88;
                var totalHours = group.Sum(x => x.DurationHours);
                var avgWatts = totalHours > 0 ? (totalKwh * 1000) / totalHours : group.Average(x => x.AverageWatts);
                var peakWatts = group.Max(x => x.PeakWatts);
                var ratio = CalculateFigureRatio(totalKwh, maxKwh, isLog);
                var color = ColorForKwh(totalKwh > 0, totalKwh / 7d);

                list.Add(new EnergyListItem(
                    $"{monday:yyyy/MM/dd} - {sunday:MM/dd}",
                    FormatDuration(totalHours),
                    $"{totalDc:0.00} kWh",
                    $"{totalKwh:0.00} kWh",
                    $"{avgWatts:0.#} W",
                    $"{peakWatts:0.#} W",
                    ratio,
                    color));
            }

            _listItems = list;
        }
        else if (ViewType.StartsWith("Month", StringComparison.OrdinalIgnoreCase))
        {
            // Group by Year and Month
            var monthGroups = _cachedHistory
                .Where(d => d.HasData)
                .GroupBy(d => (d.Date.Year, d.Date.Month))
                .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month)
                .Take(24)
                .ToList();

            var maxKwh = monthGroups.Count > 0 ? monthGroups.Max(g => g.Sum(x => x.KilowattHours)) : 1;
            if (maxKwh < 0.001) maxKwh = 1;

            var list = new List<EnergyListItem>(monthGroups.Count);
            foreach (var group in monthGroups)
            {
                var (yr, mo) = group.Key;
                var totalKwh = group.Sum(x => x.KilowattHours);
                var totalDc = totalKwh * 0.88;
                var totalHours = group.Sum(x => x.DurationHours);
                var avgWatts = totalHours > 0 ? (totalKwh * 1000) / totalHours : group.Average(x => x.AverageWatts);
                var peakWatts = group.Max(x => x.PeakWatts);
                var ratio = CalculateFigureRatio(totalKwh, maxKwh, isLog);
                var color = ColorForKwh(totalKwh > 0, totalKwh / 30d);

                list.Add(new EnergyListItem(
                    $"{yr:0000}/{mo:00} ({new DateTime(yr, mo, 1):MMM})",
                    FormatDuration(totalHours),
                    $"{totalDc:0.00} kWh",
                    $"{totalKwh:0.00} kWh",
                    $"{avgWatts:0.#} W",
                    $"{peakWatts:0.#} W",
                    ratio,
                    color));
            }

            _listItems = list;
        }
        else
        {
            // Day view: past 30 days
            var recentDays = _cachedHistory.OrderByDescending(d => d.Date).Take(30).ToList();
            var maxKwh = recentDays.Count > 0 ? recentDays.Max(d => d.KilowattHours) : 1;
            if (maxKwh < 0.001) maxKwh = 1;

            var list = new List<EnergyListItem>(recentDays.Count);
            foreach (var day in recentDays)
            {
                var ratio = CalculateFigureRatio(day.KilowattHours, maxKwh, isLog);
                var dcKwh = day.KilowattHours * 0.88;
                var color = ColorForKwh(day.HasData, day.KilowattHours);

                list.Add(new EnergyListItem(
                    $"{day.Date:yyyy/MM/dd} ({day.Date:ddd})",
                    day.HasData ? day.DurationFormatted : "0m",
                    day.HasData ? $"{dcKwh:0.00} kWh" : "0.00 kWh",
                    day.HasData ? $"{day.KilowattHours:0.00} kWh" : "0.00 kWh",
                    day.HasData ? $"{day.AverageWatts:0.#} W" : "0.0 W",
                    day.HasData ? $"{day.PeakWatts:0.#} W" : "0.0 W",
                    ratio,
                    color));
            }

            _listItems = list;
        }

        OnPropertyChanged(nameof(ListItems));
    }

    private static double CalculateFigureRatio(double kwh, double maxKwh, bool isLog)
    {
        if (kwh <= 0.0001 || maxKwh <= 0.0001) return 0;
        if (!isLog)
        {
            return Math.Clamp(kwh / maxKwh, 0.04, 1.0);
        }

        // Authentic logarithmic scale:
        // Dynamic decade response from 0.001 kWh (1 Wh) to maxKwh
        const double minKwh = 0.001;
        var logMin = Math.Log10(minKwh);
        var logMax = Math.Log10(Math.Max(maxKwh, minKwh * 10));
        var logVal = Math.Log10(Math.Max(kwh, minKwh));
        var logRatio = (logVal - logMin) / (logMax - logMin);
        return Math.Clamp(logRatio, 0.08, 1.0);
    }

    private static string FormatDuration(double totalHours)
    {
        if (totalHours <= 0) return "0m";
        var span = TimeSpan.FromHours(totalHours);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes:00}m"
            : $"{span.Minutes}m {span.Seconds:00}s";
    }

    private static string ColorForKwh(bool hasData, double kwh)
    {
        if (!hasData || kwh <= 0.001) return "Transparent";
        if (kwh <= 1.0) return "#0099FF";   // 0~1 kWh: Blue
        if (kwh <= 3.0) return "#55B555";   // 1~3 kWh: Green
        if (kwh <= 6.0) return "#F5B800";   // 3~6 kWh: Yellow
        if (kwh <= 12.0) return "#FF6633";  // 6~12 kWh: Orange
        return "#B30000";                   // >12 kWh: Dark Red
    }
}
