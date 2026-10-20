using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace NexusOptimizer.App.Controls;

public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(nameof(Values), typeof(IList), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, ValuesChanged));
    public IList? Values { get => (IList?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    private static void ValuesChanged(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        var chart = (Sparkline)obj;
        if (e.OldValue is INotifyCollectionChanged old) CollectionChangedEventManager.RemoveHandler(old, chart.OnChanged);
        if (e.NewValue is INotifyCollectionChanged current) CollectionChangedEventManager.AddHandler(current, chart.OnChanged);
    }
    private void OnChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var color = (Brush)FindResource("AccentBrush");
        var grid = (Brush)FindResource("LineBrush");
        for (int i = 1; i <= 3; i++) dc.DrawLine(new Pen(grid, .5), new(0, ActualHeight * i / 4), new(ActualWidth, ActualHeight * i / 4));
        if (Values is null || Values.Count < 2) return;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (int i = 0; i < Values.Count; i++)
            {
                var point = new Point(ActualWidth * i / Math.Max(59, Values.Count - 1), ActualHeight * (1 - Math.Clamp(Convert.ToDouble(Values[i]), 0, 100) / 100));
                if (i == 0) ctx.BeginFigure(point, false, false); else ctx.LineTo(point, true, false);
            }
        }
        geometry.Freeze(); dc.DrawGeometry(null, new Pen(color, 2), geometry);
    }
}
