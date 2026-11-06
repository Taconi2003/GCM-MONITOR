using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace GcmMonitor.App.Controls;

/// <summary>Renderização de janela limitada, sem coleta. NaN interrompe linhas nas lacunas.</summary>
public sealed class MetricChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(nameof(Values), typeof(IEnumerable<double>), typeof(MetricChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(nameof(Labels), typeof(IEnumerable<string>), typeof(MetricChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(string), typeof(MetricChart), new FrameworkPropertyMetadata("%", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AxisLabelProperty = DependencyProperty.Register(nameof(AxisLabel), typeof(string), typeof(MetricChart), new FrameworkPropertyMetadata("horário local", FrameworkPropertyMetadataOptions.AffectsRender));
    public string AxisLabel { get => (string)GetValue(AxisLabelProperty); set => SetValue(AxisLabelProperty, value); }
    public IEnumerable<double>? Values { get => (IEnumerable<double>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IEnumerable<string>? Labels { get => (IEnumerable<string>?)GetValue(LabelsProperty); set => SetValue(LabelsProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var values = Values?.ToArray();
        if (values is not { Length: >= 2 } || ActualWidth < 100 || ActualHeight < 60) return;
        const double left = 34, top = 8, bottom = 28;
        double width = ActualWidth - left - 8, height = ActualHeight - top - bottom;
        var finite = values.Where(double.IsFinite).ToArray();
        if (finite.Length == 0) return;
        double max = Unit == "%" ? 100 : Math.Max(1, finite.Max() * 1.2);
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(39, 63, 85)), 1);
        void Text(string text, Point point) => dc.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("pt-BR"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), 9,
            new SolidColorBrush(Color.FromRgb(152, 176, 201)), VisualTreeHelper.GetDpi(this).PixelsPerDip), point);
        for (int i = 0; i <= 2; i++)
        {
            double y = top + i * height / 2;
            dc.DrawLine(grid, new(left, y), new(left + width, y));
            Text((max * (2 - i) / 2).ToString("0"), new(0, y - 6));
        }
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            bool drawing = false;
            for (int i = 0; i < values.Length; i++)
            {
                if (!double.IsFinite(values[i])) { drawing = false; continue; }
                var point = new Point(left + width * i / (values.Length - 1), top + height * (1 - Math.Clamp(values[i] / max, 0, 1)));
                if (!drawing) context.BeginFigure(point, false, false); else context.LineTo(point, true, false);
                drawing = true;
            }
        }
        geometry.Freeze(); dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(70, 217, 245)), 2), geometry);
        if (Labels?.ToArray() is { Length: >= 2 } labels) { Text(labels[0], new(left, ActualHeight - 19)); Text(labels[^1], new(ActualWidth - 48, ActualHeight - 19)); }
        Text(AxisLabel, new(left + 68, ActualHeight - 19));
    }
}
