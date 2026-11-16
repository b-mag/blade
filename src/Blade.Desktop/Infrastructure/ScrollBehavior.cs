using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Blade.Desktop.Infrastructure;

/// <summary>
/// A DataGrid inside a scrolling page swallows the mouse wheel, so the page stops scrolling while the pointer
/// is over the table. Setting <c>inf:ScrollBehavior.BubbleWheel="True"</c> passes the wheel on to the page.
/// </summary>
public static class ScrollBehavior
{
    public static readonly DependencyProperty BubbleWheelProperty = DependencyProperty.RegisterAttached(
        "BubbleWheel", typeof(bool), typeof(ScrollBehavior), new PropertyMetadata(false, OnChanged));

    public static bool GetBubbleWheel(DependencyObject o) => (bool)o.GetValue(BubbleWheelProperty);
    public static void SetBubbleWheel(DependencyObject o, bool value) => o.SetValue(BubbleWheelProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        if ((bool)e.NewValue) element.PreviewMouseWheel += Bubble;
        else element.PreviewMouseWheel -= Bubble;
    }

    private static void Bubble(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not UIElement element) return;
        e.Handled = true;
        (VisualTreeHelper.GetParent(element) as UIElement)?.RaiseEvent(
            new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = element });
    }
}
