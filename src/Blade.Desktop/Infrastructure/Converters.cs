using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Blade.Desktop.Controls;

namespace Blade.Desktop.Infrastructure;

/// <summary>Visible when the value is "truthy": non-null, non-empty string/collection, true bool, non-zero number. Parameter "invert" flips it.</summary>
public sealed class VisibleWhenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var truthy = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            int i => i != 0,
            long l => l != 0,
            ICollection c => c.Count > 0,
            IEnumerable e => e.Cast<object>().Any(),
            _ => true,
        };
        if (parameter as string == "invert") truthy = !truthy;
        return truthy ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Maps status words to theme brushes (parameter "soft" for background tints).</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var soft = parameter as string == "soft";
        var s = value?.ToString() ?? "";
        var key = s switch
        {
            _ when s.StartsWith("Implemented") || s is "OK" or "S" or "True" or "LOCAL AI" => "Success",
            _ when s.StartsWith("Stubbed") || s is "M" or "OK_UNVERIFIED_CITATIONS" or "CLOUD (POC ONLY)" or "NOT_FOUND" => "Warning",
            _ when s is "DENIED" or "ERROR" or "L" or "False" or "OFFLINE" => "Danger",
            _ when s.StartsWith("Production") => "Accent",
            _ => "Muted",
        };
        if (soft) key = key == "Muted" ? "Panel" : key + "Soft";
        return Application.Current.Resources[key] ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class KindBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => new SolidColorBrush(GraphCanvas.KindColor(value?.ToString() ?? ""));
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Stage duration (ms) → bar width.</summary>
public sealed class MsToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long ms ? Math.Clamp(ms / 70.0, 3, 300) : 3.0;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is double d ? $"{d:P0}" : "";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Score 1-5 → dots. Parameter "filled" gives the earned dots, "empty" the rest, so the two can be colored
/// differently with the same glyph (the hollow-circle glyph renders at a different size in most fonts).
/// </summary>
public sealed class DotsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var n = value is int i ? Math.Clamp(i, 0, 5) : 0;
        return new string('●', parameter as string == "empty" ? 5 - n : n);
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
