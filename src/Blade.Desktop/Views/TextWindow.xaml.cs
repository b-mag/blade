using System.Windows;

namespace Blade.Desktop.Views;

public partial class TextWindow : Window
{
    public TextWindow(string heading, string subheading, string body)
    {
        InitializeComponent();
        Title = heading;
        Head.Text = heading;
        Sub.Text = subheading.ToUpperInvariant();
        Body.Text = body;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
