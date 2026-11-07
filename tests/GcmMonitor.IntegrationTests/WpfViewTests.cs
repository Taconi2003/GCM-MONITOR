using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls;
using GcmMonitor.App;
using GcmMonitor.Collector;
using GcmMonitor.Core;
using GcmMonitor.Persistence;
using GcmMonitor.Presentation;

namespace GcmMonitor.IntegrationTests;

public sealed class WpfViewTests
{
    [Fact]
    public async Task FiveViewsRenderAtTwoWindowSizesWithoutBindingErrors()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var bindingErrors = new BindingErrorListener();
            PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            string folder = Path.Combine(Path.GetTempPath(), "GcmMonitor.Tests", Guid.NewGuid().ToString("N"));
            System.Windows.Application? application = null;
            try
            {
                application = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/GcmMonitor.App;component/Themes/Dark.xaml", UriKind.Relative) });
                var vm = new ShellViewModel(new NoEngine(), new SqliteSessionRepository(Path.Combine(folder, "demo.db")), new NoDialogs());
                vm.InitializeAsync().GetAwaiter().GetResult();
                var window = new MainWindow(vm, new NoEngine());
                var surface = (FrameworkElement)window.Content;
                surface.DataContext = vm;
                var artifacts = Path.Combine(EngineProcessTests.FindRoot(), ".artifacts", "ui"); Directory.CreateDirectory(artifacts);
                void Render(Screen screen, Size size, string scenario)
                {
                    vm.CurrentScreen = screen; window.Width = size.Width; window.Height = size.Height;
                    surface.Measure(size); surface.Arrange(new Rect(size)); surface.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(surface);
                    var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(1, 1, 1, 1), pixel, 4, 0);
                    Assert.True(pixel[3] > 0, "A renderização deve conter pixels visíveis.");
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(artifacts, $"{screen}-{size.Width}-{scenario}.png")); encoder.Save(file);
                }
                foreach (var size in new[] { new Size(1440, 960), new Size(1000, 700) })
                    foreach (var screen in Enum.GetValues<Screen>()) Render(screen, size, "empty");
                vm.LoadExamplesCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                foreach (var row in vm.History.ToArray())
                {
                    vm.OpenHistoryCommand.ExecuteAsync(row).GetAwaiter().GetResult();
                    foreach (var size in new[] { new Size(1440, 960), new Size(1000, 700) }) Render(Screen.Results, size, row.Session.State.ToString());
                }
                foreach (var size in new[] { new Size(1440, 960), new Size(1000, 700) }) Render(Screen.History, size, "examples");
                vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                Assert.NotEmpty(vm.MetricCards);
                foreach (var size in new[] { new Size(1440, 960), new Size(1000, 700) }) Render(Screen.Monitoring, size, "series");
                foreach (var size in new[] { new Size(1440, 960), new Size(1000, 700) }) Render(Screen.Results, size, "series-and-rankings");
                Assert.Equal(3, vm.ModuleResults.Sum(x => x.EvidenceRows.Length));
                foreach (var expander in Descendants(surface).OfType<Expander>()) expander.IsExpanded = true;
                foreach (var size in new[] { new Size(1440, 960), new Size(1000, 700) }) Render(Screen.Results, size, "advanced-expanded");
                window.Close(); Assert.Empty(bindingErrors.Errors);
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
            finally
            {
                PresentationTraceSources.DataBindingSource.Listeners.Remove(bindingErrors);
                application?.Shutdown();
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(40));
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
    private sealed class NoDialogs : IUserDialogs
    {
        public bool Confirm(string title, string message) => false;
        public string? ChooseReportFolder(string current) => null;
    }
    private sealed class NoEngine : IDiagnosticEngineClient
    {
        public async IAsyncEnumerable<DiagnosticSession> RunAsync(DiagnosticOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            var session = DemoFixtures.Archived(SessionState.Completed, DateTimeOffset.UtcNow.AddMinutes(-30));
            var samples = new List<Measurement>();
            foreach (var module in new[] { ModuleId.Cpu, ModuleId.Memory, ModuleId.Storage })
                for (int sample = 0; sample < 6; sample++) samples.AddRange(await new DemoCollector(module).CollectAsync(session.StartedAt.AddSeconds(sample * 2), sample, cancellationToken));
            var accumulator = new MetricAccumulator(); foreach (var reading in samples) accumulator.Add(reading);
            yield return session with
            {
                Measurements = samples.ToArray(), Statistics = accumulator.Snapshot(),
                Records = [new(ModuleId.WindowsEvents, "fixture-event", "Evento fictício · ID 41", "Ocorrência sintética; nenhuma causa determinada.", session.StartedAt, "Fixture / Windows Event Log", DataQuality.Valid, true),
                    new(ModuleId.Devices, "fixture-driver", "Dispositivo fictício", "Versão instalada fictícia 1.0. Sem alterações.", session.StartedAt, "Fixture / WMI", DataQuality.Valid, true),
                    new(ModuleId.Sensors, "fixture-sensor", "Sensor fictício indisponível", "Exemplo de ausência de leitura, sem valor zero.", session.StartedAt, "Fixture / sensor", DataQuality.Unavailable, true)],
                Processes = [new(42, session.StartedAt, "processo-ficticio.exe", 2, 10, 10, 50.0 * 1024 * 1024 * 10, 10,
                    60 * 1024 * 1024, session.StartedAt, session.StartedAt.AddSeconds(10), true, 6)]
            };
        }
        public Task CancelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
