using System.Windows;
using Blade.Core;
using Blade.Desktop.ViewModels;

namespace Blade.Desktop.Views;

public partial class RecordWindow : Window
{
    private readonly MainViewModel _main;
    private readonly IncidentDetail _detail;

    public RecordWindow(IncidentDetail detail, MainViewModel main)
    {
        InitializeComponent();
        _detail = detail;
        _main = main;
        DataContext = detail;
        Title = $"{detail.CaseNumber} — {detail.OffenseDescription} (synthetic record)";
    }

    private void Explore_Click(object sender, RoutedEventArgs e) { _main.ShowInLinkExplorer(_detail.CaseNumber); Close(); }
    private void Person_Click(object sender, RoutedEventArgs e) { _main.ShowInLinkExplorer("P:" + ((FrameworkElement)sender).Tag); Close(); }
    private void Vehicle_Click(object sender, RoutedEventArgs e) { _main.ShowInLinkExplorer((string)((FrameworkElement)sender).Tag); Close(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
