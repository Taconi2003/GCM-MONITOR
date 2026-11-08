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
using GcmMonitor.Reporting;
using PdfSharp.Pdf.IO;

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
                vm.ReportFolder = @"C:\GCM-Demo\Relatórios";
                var window = new MainWindow(vm, new NoEngine());
                var surface = (FrameworkElement)window.Content;
                surface.DataContext = vm;
                var versionFolder = ProductVersion.Value.Split('-', 2)[^1];
                var artifacts = Path.Combine(EngineProcessTests.FindRoot(), ".artifacts", "ui", versionFolder); Directory.CreateDirectory(artifacts);
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
                    using var file = File.Create(Path.Combine(artifacts, $"{screen}-{size.Width}x{size.Height}-{scenario}.png")); encoder.Save(file);
                }
                Size[] sizes = [new Size(1440, 810), new Size(1000, 700)];
                foreach (var size in sizes)
                    foreach (var screen in Enum.GetValues<Screen>()) Render(screen, size, "empty");
                vm.LoadExamplesCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                foreach (var row in vm.History.ToArray())
                {
                    vm.OpenHistoryCommand.ExecuteAsync(row).GetAwaiter().GetResult();
                    foreach (var size in sizes) Render(Screen.Results, size, row.Session.State.ToString());
                }
                foreach (var size in sizes) Render(Screen.History, size, "examples");
                vm.StartCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                Assert.NotEmpty(vm.MetricCards);
                foreach (var size in sizes) Render(Screen.Monitoring, size, "series");
                foreach (var size in sizes) Render(Screen.Results, size, "series-and-rankings");
                Assert.Equal(4, vm.ModuleResults.Sum(x => x.EvidenceRows.Length));
                foreach (var expander in Descendants(surface).OfType<Expander>()) expander.IsExpanded = true;
                foreach (var size in sizes) Render(Screen.Results, size, "advanced-expanded");
                var results = Descendants(surface).OfType<GcmMonitor.App.Views.ResultsView>().Single();
                var scroll = Descendants(results).OfType<ScrollViewer>().First();
                var startup = Descendants(results).OfType<TextBlock>().Single(x => x.Text == "Inicialização · Programa fictício");
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset + startup.TransformToAncestor(scroll).Transform(new Point()).Y - 160);
                Render(Screen.Results, sizes[0], "startup-demo");
                if (Environment.GetEnvironmentVariable("GCM_CAPTURE_THERMAL_REVIEW") == "1")
                {
                    var previousContext = SynchronizationContext.Current;
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
                    try
                    {
                        var capture = CaptureThermalAsync();
                        var frame = new DispatcherFrame();
                        _ = capture.ContinueWith(_ => window.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
                            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                        Dispatcher.PushFrame(frame);
                        capture.GetAwaiter().GetResult();
                    }
                    finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
                }
                window.Close(); Assert.Empty(bindingErrors.Errors);
                completion.SetResult();

                async Task CaptureThermalAsync()
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(28));
                    string database = Path.Combine(folder, "thermal-demo.db");
                    await using var engine = new SessionEngineClient(EngineProcessTests.Executable("GcmMonitor.Engine"),
                        database, CollectionMode.Demonstration, rehearseThermal: true);
                    var repository = new SqliteSessionRepository(database);
                    var exporter = new PdfReportExporter();
                    vm = new ShellViewModel(engine, repository, new NoDialogs(confirm: true), pdfExporter: exporter);
                    surface.DataContext = vm;
                    await vm.InitializeAsync();
                    vm.MonitoringDuration = vm.Durations[0];
                    foreach (var module in vm.Modules)
                        module.IsSelected = module.Definition.Id is ModuleId.Cpu or ModuleId.Sensors;
                    var run = vm.StartCommand.ExecuteAsync(null);
                    while (!vm.HasThermalAttention && !run.IsCompleted) await Task.Delay(20, timeout.Token);
                    Assert.True(vm.HasThermalAttention, vm.Message);
                    Render(Screen.Monitoring, sizes[0], "thermal-attention");
                    await vm.ResolveThermalAttentionCommand.ExecuteAsync(ThermalChoice.Continue);
                    while (!vm.HasThermalCritical && !run.IsCompleted) await Task.Delay(20, timeout.Token);
                    Assert.True(vm.HasThermalCritical);
                    Render(Screen.Monitoring, sizes[0], "thermal-critical");
                    await run.WaitAsync(timeout.Token);
                    Assert.True(vm.HasResultThermal);
                    foreach (var size in sizes) Render(Screen.Results, size, "thermal-recovery");
                    var stored = Assert.Single(await repository.ListAsync());
                    string pdfPath = Path.Combine(artifacts, "GCM-relatorio-termico-demonstrativo.pdf");
                    await exporter.ExportAsync(stored, pdfPath, true, timeout.Token);
                    using var report = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
                    Assert.InRange(report.PageCount, 2, 10);
                    Assert.Equal("Dados demonstrativos", report.Info.Subject);
                }
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
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(60));
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
    private sealed class NoDialogs(bool confirm = false) : IUserDialogs
    {
        public bool Confirm(string title, string message) => confirm;
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
                    new(ModuleId.WindowsEvents, "startup:fixture", "Inicialização · Programa fictício", "Cadastro encontrado. Estado de habilitação e execução não verificados.", session.StartedAt, "Fixture / Registro / Run", DataQuality.Valid, true),
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
