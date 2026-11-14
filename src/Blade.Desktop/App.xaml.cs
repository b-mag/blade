using System.Windows;
using Blade.Desktop.Services;
using Blade.Desktop.ViewModels;

namespace Blade.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "BLADE — unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        var server = Environment.GetEnvironmentVariable("BLADE_SERVER") ?? "http://localhost:5179";
        var vm = new MainViewModel(new ApiClient(server), ContentLoader.Load());
        MainWindow = new MainWindow { DataContext = vm };
        MainWindow.Show();
    }
}
