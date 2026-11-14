using System.Windows.Controls;

namespace Blade.Desktop.Views;

public partial class AuditView : UserControl
{
    public AuditView()
    {
        InitializeComponent();
        // Load (or show the access-denied message) whenever the tab is opened.
        Loaded += (_, _) => (DataContext as ViewModels.AuditViewModel)?.RefreshCommand.Execute(null);
    }
}
