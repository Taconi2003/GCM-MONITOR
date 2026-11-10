using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GcmMonitor.Core;
using MigraDoc.DocumentObjectModel;
using FormattedText = System.Windows.Media.FormattedText;

namespace GcmMonitor.Reporting;

internal static class ReportCharts
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    public static void Add(Section section, DiagnosticSession session)
    {
        section.AddParagraph("Gráficos de observação", StyleNames.Heading1);
        var groups = session.Measurements.GroupBy(x => (x.Module, x.Metric, x.Component, x.Source))
            .Select(g => g.OrderBy(x => x.Timestamp).TakeLast(24).ToArray())
            .Where(g => g.Count(x => x.Quality == DataQuality.Valid) >= 2 && g[^1].Timestamp > g[0].Timestamp).ToArray();
        var choices = new[]
        {
            groups.FirstOrDefault(g => g[0].Module == ModuleId.Cpu && g[0].Metric == "CPU"),
            groups.FirstOrDefault(g => g[0].Module == ModuleId.Memory && g[0].Metric == "RAM"),
            groups.FirstOrDefault(g => g[0].Module == ModuleId.Sensors && g[0].Metric.Contains("CPU Package", StringComparison.Ordinal) && g[0].Unit == "°C"),
            groups.FirstOrDefault(g => g[0].Module == ModuleId.Storage && g[0].Metric == "Leitura do disco")
        };
        if (choices.All(x => x is null)) section.AddParagraph("Não disponível - faltam medições válidas em horários distintos na mesma série.");
        foreach (var points in choices.OfType<Measurement[]>())
        {
            var sample = points[0];
            section.AddParagraph($"{sample.Metric} · {sample.Component} ({sample.Unit})", StyleNames.Heading2);
            var caption = section.AddParagraph(sample.Source + " · janela preservada; lacunas interrompem a linha");
            caption.Format.KeepWithNext = true;
            var image = section.AddImage("base64:" + Convert.ToBase64String(Render(points)));
            image.Width = Unit.FromCentimeter(16.7);
            image.LockAspectRatio = true;
        }
    }

    public static void AddThermal(Section section, ThermalTrace trace)
    {
        var points = ThermalRecoveryAnalyzer.TemperatureSeries(trace).Select(p => new Measurement(ModuleId.Sensors, "Temperatura CPU", trace.Sensor,
            p.HasFreshTemperature ? p.Celsius : null, "°C", p.ObservedAt, trace.Source,
            p.HasFreshTemperature ? DataQuality.Valid : DataQuality.Unavailable, true)).ToArray();
        if (points.Count(p => p.Quality == DataQuality.Valid) < 2 || points[^1].Timestamp <= points[0].Timestamp) return;
        var caption = section.AddParagraph("Temperatura (°C) · laranja início da carga fictícia · verde início da recuperação");
        caption.Format.KeepWithNext = true;
        var image = section.AddImage("base64:" + Convert.ToBase64String(Render(points, trace.LoadStartedAt, trace.LoadEndedAt)));
        image.Width = Unit.FromCentimeter(16.7);
        image.LockAspectRatio = true;
    }

    private static byte[] Render(Measurement[] samples, DateTimeOffset? load = null, DateTimeOffset? recovery = null)
    {
        const double width = 800, height = 226, left = 72, top = 16, right = 750, bottom = 184;
        var numeric = samples.Where(x => x.Quality == DataQuality.Valid).Select(x => x.Value!.Value).ToArray();
        double scale = Math.Max(1, numeric.Select(Math.Abs).Max());
        double min = numeric.Min() / scale, max = numeric.Max() / scale;
        var totalSeconds = (samples[^1].Timestamp - samples[0].Timestamp).TotalSeconds;
        var navy = new SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 61, 83));
        var teal = new SolidColorBrush(System.Windows.Media.Color.FromRgb(14, 109, 137));
        var grid = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromRgb(216, 227, 235)), 1);
        var line = new Pen(teal, 2);
        var visual = new DrawingVisual();
        using (var draw = visual.RenderOpen())
        {
            draw.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            void Label(string text, double x, double y)
            {
                var formatted = new FormattedText(text, Pt, FlowDirection.LeftToRight, new Typeface("Arial"), 13, navy, 1);
                draw.DrawText(formatted, new Point(x, y));
            }
            for (int tick = 0; tick <= 4; tick++)
            {
                double y = bottom - (bottom - top) * tick / 4;
                draw.DrawLine(grid, new Point(left, y), new Point(right, y));
                if (tick is 0 or 2 or 4)
                    Label(((min + (max - min) * tick / 4) * scale).ToString("0.##", Pt), 8, y - 8);
            }
            void Marker(DateTimeOffset? time, byte red, byte green, byte blue)
            {
                if (time is not { } at || at < samples[0].Timestamp || at > samples[^1].Timestamp) return;
                double x = left + (right - left) * (at - samples[0].Timestamp).TotalSeconds / totalSeconds;
                var marker = new Pen(new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue)), 2) { DashStyle = DashStyles.Dash };
                draw.DrawLine(marker, new Point(x, top), new Point(x, bottom));
            }
            Marker(load, 174, 96, 22);
            Marker(recovery, 20, 128, 93);
            Point? previous = null;
            DateTimeOffset previousTimestamp = default;
            foreach (var sample in samples)
            {
                if (sample.Quality != DataQuality.Valid || sample.Value is not { } value) { previous = null; continue; }
                double position = min == max ? .5 : (value / scale - min) / (max - min);
                var point = new Point(left + (right - left) * (sample.Timestamp - samples[0].Timestamp).TotalSeconds / totalSeconds,
                    bottom - (bottom - top) * position);
                if (previous is { } before && (sample.IsDemo ||
                    (sample.Timestamp - previousTimestamp).TotalSeconds <= SamplingContinuity.MaximumIntervalSeconds))
                    draw.DrawLine(line, before, point);
                draw.DrawEllipse(teal, null, point, 3, 3);
                previous = point;
                previousTimestamp = sample.Timestamp;
            }
            Label(samples[0].Timestamp.ToLocalTime().ToString("HH:mm:ss", Pt), left, bottom + 12);
            Label(samples[^1].Timestamp.ToLocalTime().ToString("HH:mm:ss", Pt), right - 60, bottom + 12);
            Label(samples[0].IsDemo ? "Tempo simulado" : "Horário local", (left + right) / 2 - 42, bottom + 12);
        }
        var bitmap = new RenderTargetBitmap((int)width * 2, (int)height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        png.Save(stream);
        return stream.ToArray();
    }
}
