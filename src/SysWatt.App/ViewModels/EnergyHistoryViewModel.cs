using System.Collections.ObjectModel;
using System.Globalization;
using SysWatt.App.Commands;
using SysWatt.Core.Energy;
using SysWatt.Core.Sensors;

namespace SysWatt.App.ViewModels;

public sealed record EnergyListItem(
    string DateText,
    string DcKwhText,
    string WallKwhText,
    string AverageWattsText,
    string PeakWattsText,
    double FigureWidthRatio,
    string FigureColor);

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
    private IReadOnlyList<EnergyListItem> _listItems = [];
    private IReadOnlyList<CalendarDayItem> _calendarDays = [];

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
        set { if (Set(ref _viewType, value)) _ = RefreshAsync(); }
    }

    public string FigureScale
    {
        get => _figureScale;
        set { if (Set(ref _figureScale, value)) UpdateListFigures(); }
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
    public string SelectedDaySummary => _selectedDaySummary;

    public CalendarDayItem? SelectedCalendarDay
    {
        get => _selectedCalendarDay;
        set
        {
            if (_selectedCalendarDay is not null) _selectedCalendarDay.IsSelected = false;
            if (Set(ref _selectedCalendarDay, value) && value is not null)
            {
                value.IsSelected = true;
                _selectedDaySummary = value.HasData
                    ? $"{value.Date:yyyy/MM/dd}: {value.KilowattHours:0.00} kWh"
                    : $"{value.Date:yyyy/MM/dd}: No recorded session";
                OnPropertyChanged(nameof(SelectedDaySummary));
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
        PreviousMonthCommand = new(PreviousMonth);
        NextMonthCommand = new(NextMonth);
        CurrentMonthCommand = new(CurrentMonth);
        OkCommand = new(() => RequestClose?.Invoke(this, EventArgs.Empty));
        ImportCommand = new(() => ImportRequested?.Invoke(this, EventArgs.Empty));
        ExportCommand = new(() => ExportRequested?.Invoke(this, EventArgs.Empty));
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            var year = SelectedYear;
            var month = SelectedMonth;
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var startOfMonth = new DateOnly(year, month, 1);
            var endOfMonth = new DateOnly(year, month, daysInMonth);

            // Fetch current month data plus recent 30 days
            var monthRange = await _store.GetRangeAsync(startOfMonth, endOfMonth);
            var recent30Range = await _store.GetRangeAsync(DateOnly.FromDateTime(DateTime.Today.AddDays(-29)), DateOnly.FromDateTime(DateTime.Today));

            BuildCalendar(year, month, monthRange);
            BuildList(recent30Range);
        }
        catch { }
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
                ? $"{date:yyyy/MM/dd} ({date:ddd}): {kwh:0.00} kWh · Avg {summary!.AverageWatts:0.#} W · Peak {summary.PeakWatts:0.#} W"
                : $"{date:yyyy/MM/dd} ({date:ddd}): No recorded usage";

            if (hasData)
            {
                totalKwh += kwh;
                countDaysWithData++;
                totalAvgWatts += summary!.AverageWatts;
                if (summary.PeakWatts > peakWatts) peakWatts = summary.PeakWatts;
            }

            var item = new CalendarDayItem(day, date, true, hasData, kwh, color, isToday, tooltip);
            if (isToday) item.IsSelected = true;
            list.Add(item);
        }

        // Pad after up to 35 or 42
        var remaining = 42 - list.Count;
        if (remaining >= 7 && list.Count <= 35) remaining = 35 - list.Count;
        for (var i = 1; i <= remaining; i++)
        {
            var nextMonth = month == 12 ? 1 : month + 1;
            var nextYear = month == 12 ? year + 1 : year;
            var date = new DateOnly(nextYear, nextMonth, i);
            list.Add(new CalendarDayItem(i, date, false, false, 0, "Transparent", false, string.Empty));
        }

        _calendarDays = list;
        var avg = countDaysWithData > 0 ? totalAvgWatts / countDaysWithData : 0;
        _monthSummary = $"Current month total energy: {totalKwh:0.00} kWh (Average: {avg:0.#} W, Peak: {peakWatts:0.#} W)";

        OnPropertyChanged(nameof(CalendarDays));
        OnPropertyChanged(nameof(MonthSummary));
    }

    private void BuildList(IReadOnlyList<DailyEnergySummary> rawDays)
    {
        var days = rawDays.OrderByDescending(d => d.Date).ToList();
        var maxKwh = days.Count > 0 ? days.Max(d => d.KilowattHours) : 1;
        if (maxKwh < 0.001) maxKwh = 1;

        var isLog = FigureScale.StartsWith("Log", StringComparison.OrdinalIgnoreCase);
        var result = new List<EnergyListItem>(days.Count);

        foreach (var day in days)
        {
            var ratio = isLog
                ? (day.KilowattHours > 0 ? Math.Clamp(Math.Log10(day.KilowattHours + 1) / Math.Log10(maxKwh + 1), 0.03, 1.0) : 0)
                : Math.Clamp(day.KilowattHours / maxKwh, day.KilowattHours > 0 ? 0.03 : 0, 1.0);

            var dcKwh = day.KilowattHours * 0.88; // Hybrid model DC load
            var color = ColorForKwh(day.HasData, day.KilowattHours);

            result.Add(new EnergyListItem(
                $"{day.Date:yyyy/MM/dd} ({day.Date:ddd})",
                day.HasData ? $"{dcKwh:0.00} kWh" : "0.00 kWh",
                day.HasData ? $"{day.KilowattHours:0.00} kWh" : "0.00 kWh",
                day.HasData ? $"{day.AverageWatts:0.#} W" : "0.0 W",
                day.HasData ? $"{day.PeakWatts:0.#} W" : "0.0 W",
                ratio,
                color));
        }

        _listItems = result;
        OnPropertyChanged(nameof(ListItems));
    }

    private void UpdateListFigures()
    {
        if (_listItems.Count == 0) return;
        _ = RefreshAsync();
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
