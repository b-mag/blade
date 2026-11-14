using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Blade.Core;

namespace Blade.Desktop.Controls;

/// <summary>
/// Native WPF force-directed graph (no web view, no CDN). Click selects, drag moves, double-click expands.
/// </summary>
public sealed class GraphCanvas : Canvas
{
    public static readonly DependencyProperty GraphProperty = DependencyProperty.Register(nameof(Graph), typeof(GraphView), typeof(GraphCanvas),
        new PropertyMetadata(null, (d, _) => ((GraphCanvas)d).Build()));
    public static readonly DependencyProperty SelectedNodeProperty = DependencyProperty.Register(nameof(SelectedNode), typeof(GraphNodeDto), typeof(GraphCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((GraphCanvas)d).Restyle()));
    public static readonly DependencyProperty ActivateCommandProperty = DependencyProperty.Register(nameof(ActivateCommand), typeof(ICommand), typeof(GraphCanvas));

    public GraphView? Graph { get => (GraphView?)GetValue(GraphProperty); set => SetValue(GraphProperty, value); }
    public GraphNodeDto? SelectedNode { get => (GraphNodeDto?)GetValue(SelectedNodeProperty); set => SetValue(SelectedNodeProperty, value); }
    public ICommand? ActivateCommand { get => (ICommand?)GetValue(ActivateCommandProperty); set => SetValue(ActivateCommandProperty, value); }

    private readonly Dictionary<string, Point> _pos = [];
    private readonly Dictionary<string, Border> _nodeViews = [];
    private readonly List<(GraphEdgeDto Edge, Line Line, TextBlock Label)> _edgeViews = [];
    private (double Scale, double Ox, double Oy, double MinX, double MinY) _fit;
    private int _overlapPasses;
    private string? _dragging;
    private Point _dragStart;

    public static Color KindColor(string kind) => kind switch
    {
        "INCIDENT" => Color.FromRgb(0x5A, 0xA9, 0xFF),
        "PERSON" => Color.FromRgb(0xE2, 0xB2, 0x4F),
        "VEHICLE" => Color.FromRgb(0x3F, 0xC1, 0xB0),
        "PHONE" => Color.FromRgb(0xA7, 0x8B, 0xFA),
        "FIELDINTERVIEW" => Color.FromRgb(0x7C, 0xC4, 0x5E),
        "BOOKING" => Color.FromRgb(0xEF, 0x6B, 0x63),
        "PAWN" => Color.FromRgb(0xF0, 0x8C, 0x3C),
        "SERIES" => Color.FromRgb(0xE2, 0xB2, 0x4F),
        _ => Color.FromRgb(0x8F, 0xA1, 0xB5),
    };

    private static string KindName(string kind) => kind switch { "FIELDINTERVIEW" => "FIELD INTERVIEW", "PAWN" => "PAWN / SCRAP", _ => kind };

    public GraphCanvas()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
        SizeChanged += (_, _) => Place();
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += (_, _) => { _dragging = null; ReleaseMouseCapture(); };
    }

    private void Build()
    {
        Children.Clear();
        _nodeViews.Clear();
        _edgeViews.Clear();
        _pos.Clear();
        var g = Graph;
        if (g is null || g.Nodes.Count == 0) return;
        Layout(g);

        foreach (var e in g.Edges.Where(e => _pos.ContainsKey(e.From) && _pos.ContainsKey(e.To)))
        {
            var line = new Line { Stroke = new SolidColorBrush(Color.FromArgb(0x70, 0x8F, 0xA1, 0xB5)), StrokeThickness = 1.2, IsHitTestVisible = false };
            var label = new TextBlock { Text = e.Label, FontSize = 10, Foreground = (Brush)Application.Current.Resources["Muted"], IsHitTestVisible = false };
            Children.Add(line);
            Children.Add(label);
            _edgeViews.Add((e, line, label));
        }
        foreach (var n in g.Nodes)
        {
            var color = KindColor(n.Kind);
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = KindName(n.Kind), FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(color) });
            stack.Children.Add(new TextBlock { Text = n.Label, FontSize = 11, Foreground = (Brush)Application.Current.Resources["Text"], TextWrapping = TextWrapping.Wrap, MaxWidth = 150 });
            var border = new Border
            {
                Child = stack,
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1A, 0x25, 0x32)),
                BorderBrush = new SolidColorBrush(color),
                BorderThickness = new Thickness(n.Focus ? 2.5 : 1.3),
                CornerRadius = new CornerRadius(n.Kind is "PERSON" or "SERIES" ? 14 : 5),
                Padding = new Thickness(8, 4, 8, 5),
                Cursor = Cursors.Hand,
                Tag = n,
                ToolTip = n.Detail is null ? n.Label : $"{n.Label}\n{n.Detail}",
            };
            border.MouseLeftButtonDown += OnNodeDown;
            Children.Add(border);
            _nodeViews[n.Id] = border;
        }
        Place();
        Restyle();
    }

    /// <summary>Fruchterman–Reingold layout with a fixed seed so the same graph always looks the same.</summary>
    private void Layout(GraphView g)
    {
        var ids = g.Nodes.Select(n => n.Id).ToList();
        var rnd = new Random(17);
        const double W = 1400, H = 900;
        var focus = g.Nodes.Where(n => n.Focus).Select(n => n.Id).ToHashSet();
        for (var i = 0; i < ids.Count; i++)
        {
            var a = 2 * Math.PI * i / ids.Count;
            var r = focus.Contains(ids[i]) ? 120 : 300;
            _pos[ids[i]] = new Point(W / 2 + r * Math.Cos(a) + rnd.NextDouble() * 10, H / 2 + r * Math.Sin(a) + rnd.NextDouble() * 10);
        }
        var edges = g.Edges.Where(e => _pos.ContainsKey(e.From) && _pos.ContainsKey(e.To) && e.From != e.To).ToList();
        var k = Math.Sqrt(W * H / Math.Max(1, ids.Count)) * 0.75;
        var t = W / 8;
        for (var iter = 0; iter < 350; iter++)
        {
            var disp = ids.ToDictionary(id => id, _ => new Vector());
            for (var i = 0; i < ids.Count; i++)
                for (var j = i + 1; j < ids.Count; j++)
                {
                    var d = _pos[ids[i]] - _pos[ids[j]];
                    var len = Math.Max(d.Length, 0.01);
                    var f = k * k / len;
                    d /= len;
                    disp[ids[i]] += d * f;
                    disp[ids[j]] -= d * f;
                }
            foreach (var e in edges)
            {
                var d = _pos[e.From] - _pos[e.To];
                var len = Math.Max(d.Length, 0.01);
                var f = len * len / k;
                d /= len;
                disp[e.From] -= d * f;
                disp[e.To] += d * f;
            }
            foreach (var id in ids)
            {
                var v = disp[id];
                // gentle pull to the center keeps disconnected pieces on screen
                v += (new Point(W / 2, H / 2) - _pos[id]) * 0.12;
                var len = Math.Max(v.Length, 0.01);
                _pos[id] += v / len * Math.Min(len, t);
            }
            t = Math.Max(1, t * 0.985);
        }
    }

    private void Place()
    {
        if (_pos.Count == 0 || ActualWidth < 10) return;
        var minX = _pos.Values.Min(p => p.X); var maxX = _pos.Values.Max(p => p.X);
        var minY = _pos.Values.Min(p => p.Y); var maxY = _pos.Values.Max(p => p.Y);
        const double margin = 90;
        var scale = Math.Min((ActualWidth - 2 * margin) / Math.Max(1, maxX - minX), (ActualHeight - 2 * margin) / Math.Max(1, maxY - minY));
        scale = Math.Min(scale, 1.6);
        var ox = (ActualWidth - (maxX - minX) * scale) / 2;
        var oy = (ActualHeight - (maxY - minY) * scale) / 2;
        _fit = (scale, ox, oy, minX, minY);
        if (_overlapPasses < 4 && ResolveOverlaps())
        {
            _overlapPasses++;
            Place();
            _overlapPasses = 0;
            return;
        }
        foreach (var (id, view) in _nodeViews) Position(id, view);
        UpdateEdges();
    }

    /// <summary>Pushes apart node boxes that overlap on screen (force layout treats nodes as points). Returns true if anything moved.</summary>
    private bool ResolveOverlaps()
    {
        var ids = _nodeViews.Keys.ToList();
        var sizes = ids.ToDictionary(id => id, id =>
        {
            _nodeViews[id].Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return _nodeViews[id].DesiredSize;
        });
        var movedAny = false;
        for (var iter = 0; iter < 120; iter++)
        {
            var moved = false;
            for (var i = 0; i < ids.Count; i++)
                for (var j = i + 1; j < ids.Count; j++)
                {
                    var a = Screen(ids[i]); var b = Screen(ids[j]);
                    var ox = (sizes[ids[i]].Width + sizes[ids[j]].Width) / 2 + 10 - Math.Abs(a.X - b.X);
                    var oy = (sizes[ids[i]].Height + sizes[ids[j]].Height) / 2 + 10 - Math.Abs(a.Y - b.Y);
                    if (ox <= 0 || oy <= 0) continue;
                    var push = ox < oy * 2.5
                        ? new Vector(Math.Sign(b.X - a.X == 0 ? 1 : b.X - a.X) * (ox / 2 + 1), 0)
                        : new Vector(0, Math.Sign(b.Y - a.Y == 0 ? 1 : b.Y - a.Y) * (oy / 2 + 1));
                    _pos[ids[i]] -= push / _fit.Scale;
                    _pos[ids[j]] += push / _fit.Scale;
                    moved = true;
                }
            if (!moved) break;
            movedAny = true;
        }
        return movedAny;
    }

    private Point Screen(string id) => new((_pos[id].X - _fit.MinX) * _fit.Scale + _fit.Ox, (_pos[id].Y - _fit.MinY) * _fit.Scale + _fit.Oy);

    private void Position(string id, Border view)
    {
        view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var p = Screen(id);
        SetLeft(view, p.X - view.DesiredSize.Width / 2);
        SetTop(view, p.Y - view.DesiredSize.Height / 2);
    }

    private void UpdateEdges()
    {
        var showAll = _edgeViews.Count <= 30;
        foreach (var (e, line, label) in _edgeViews)
        {
            var a = Screen(e.From);
            var b = Screen(e.To);
            line.X1 = a.X; line.Y1 = a.Y; line.X2 = b.X; line.Y2 = b.Y;
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SetLeft(label, (a.X + b.X) / 2 - label.DesiredSize.Width / 2);
            SetTop(label, (a.Y + b.Y) / 2 - label.DesiredSize.Height / 2);
            var sel = SelectedNode is { } s && (e.From == s.Id || e.To == s.Id);
            label.Visibility = showAll || sel ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void Restyle()
    {
        var sel = SelectedNode?.Id;
        var gold = (SolidColorBrush)Application.Current.Resources["Gold"];
        foreach (var (e, line, label) in _edgeViews)
        {
            var hot = sel is not null && (e.From == sel || e.To == sel);
            line.Stroke = hot ? gold : new SolidColorBrush(Color.FromArgb(0x70, 0x8F, 0xA1, 0xB5));
            line.StrokeThickness = hot ? 2.2 : 1.2;
            label.Foreground = hot ? gold : (Brush)Application.Current.Resources["Muted"];
        }
        foreach (var (id, view) in _nodeViews)
        {
            var n = (GraphNodeDto)view.Tag;
            view.BorderThickness = new Thickness(id == sel ? 3 : n.Focus ? 2.5 : 1.3);
            view.Background = new SolidColorBrush(id == sel ? Color.FromArgb(0xF2, 0x2A, 0x3A, 0x50) : Color.FromArgb(0xE6, 0x1A, 0x25, 0x32));
        }
        if (_fit.Scale > 0) UpdateEdges();
    }

    private void OnNodeDown(object sender, MouseButtonEventArgs e)
    {
        var view = (Border)sender;
        var n = (GraphNodeDto)view.Tag;
        SelectedNode = n;
        if (e.ClickCount == 2)
        {
            if (ActivateCommand?.CanExecute(n) == true) ActivateCommand.Execute(n);
            e.Handled = true;
            return;
        }
        _dragging = n.Id;
        _dragStart = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging is null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        var delta = p - _dragStart;
        _dragStart = p;
        _pos[_dragging] += delta / _fit.Scale;
        Position(_dragging, _nodeViews[_dragging]);
        UpdateEdges();
    }
}
