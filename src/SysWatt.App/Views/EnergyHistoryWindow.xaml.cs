using System.Windows;
using SysWatt.App.Theming;
using SysWatt.App.ViewModels;

namespace SysWatt.App.Views;

public partial class EnergyHistoryWindow : Window
{
    public event EventHandler? ImportRequested;
    public event EventHandler? ExportRequested;

    public EnergyHistoryWindow(EnergyHistoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += LoadHistoryOnFirstShow;
        viewModel.RequestClose += (_, _) => Close();
        viewModel.ImportRequested += (_, _) => ImportRequested?.Invoke(this, EventArgs.Empty);
        viewModel.ExportRequested += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty);
        ThemeManager.ApplyToWindow(this);
    }

    private async void LoadHistoryOnFirstShow(object sender, RoutedEventArgs e)
    {
        Loaded -= LoadHistoryOnFirstShow;
        if (DataContext is EnergyHistoryViewModel viewModel)
        {
            await viewModel.RefreshAsync();
        }
    }
}
