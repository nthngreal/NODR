using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace NODR.Controls;

public sealed class Sparkline : FrameworkElement
{
    private INotifyCollectionChanged? _observedCollection;

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(MediaBrush),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(MediaBrushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(Sparkline),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public MediaBrush Stroke
    {
        get => (MediaBrush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (Sparkline)d;
        control.DetachCollection();
        control.AttachCollection(e.NewValue as INotifyCollectionChanged);
        control.InvalidateVisual();
    }

    private void AttachCollection(INotifyCollectionChanged? collection)
    {
        _observedCollection = collection;
        if (_observedCollection is not null)
            _observedCollection.CollectionChanged += OnCollectionChanged;
    }

    private void DetachCollection()
    {
        if (_observedCollection is not null)
            _observedCollection.CollectionChanged -= OnCollectionChanged;
        _observedCollection = null;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (ActualWidth <= 0 || ActualHeight <= 0 || ItemsSource is null)
            return;

        var values = ItemsSource.Cast<object?>()
            .Select(v => Convert.ToDouble(v))
            .ToArray();

        if (values.Length < 2)
            return;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var i = 0; i < values.Length; i++)
            {
                var x = values.Length == 1 ? 0 : i * ActualWidth / (values.Length - 1);
                var normalized = Math.Clamp(values[i], 0, 100) / 100.0;
                var y = ActualHeight - normalized * (ActualHeight - StrokeThickness) - StrokeThickness / 2;
                var point = new WpfPoint(x, y);

                if (i == 0)
                    context.BeginFigure(point, false, false);
                else
                    context.LineTo(point, true, false);
            }
        }

        geometry.Freeze();
        var pen = new MediaPen(Stroke, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }
}
