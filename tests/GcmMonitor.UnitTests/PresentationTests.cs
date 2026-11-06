using GcmMonitor.Collector;
using GcmMonitor.Core;
using GcmMonitor.Presentation;

namespace GcmMonitor.UnitTests;

public sealed class PresentationTests
{
    [Fact]
    public async Task FiveScreensAreNavigableAndSelectionSurvivesNavigation()
    {
        var vm = Create(); await vm.InitializeAsync();
        Assert.Equal(new[] { "Início", "Monitoramento", "Resultados", "Histórico", "Configurações" }, vm.Navigation.Select(x => x.Title));
        vm.Modules[0].IsSelected = false;
        foreach (var item in vm.Navigation) { vm.NavigateCommand.Execute(item); Assert.Equal(item.Screen, vm.CurrentScreen); }
        Assert.False(vm.Modules[0].IsSelected);
    }
    [Fact]
    public async Task EnableDisableAndRestoreNeverEnableStress()
    {
        var vm = Create(); await vm.InitializeAsync(); vm.DisableAllCommand.Execute(null); Assert.Equal(0, vm.SelectedModuleCount);
        vm.EnableAllCommand.Execute(null); Assert.Equal(11, vm.SelectedModuleCount); Assert.False(vm.CreateOptions().StressEnabled);
        vm.RestoreDefaultsCommand.Execute(null); Assert.False(vm.CreateOptions().StressEnabled); Assert.Equal(30, vm.MonitoringDuration.Minutes);
    }
    [Fact]
    public async Task EmptySelectionDisplaysValidationAndNeverStartsEngine()
    {
        var engine = new FakeEngine(); var vm = Create(engine); await vm.InitializeAsync(); vm.DisableAllCommand.Execute(null);
        await vm.StartCommand.ExecuteAsync(null); Assert.Contains("Selecione", vm.Message); Assert.Equal(0, engine.Starts);
    }
    [Fact]
    public async Task DetailsAreClearedWhenTheirModuleIsDisabled()
    {
        var vm = Create(); await vm.InitializeAsync(); vm.DetailedBattery = vm.DetailedWifi = true;
        vm.Modules.Single(x => x.Definition.Id == ModuleId.Battery).IsSelected = false;
        vm.Modules.Single(x => x.Definition.Id == ModuleId.Wifi).IsSelected = false;
        Assert.False(vm.DetailedBattery); Assert.False(vm.DetailedWifi); vm.CreateOptions().Validate();
    }
    [Fact]
    public async Task TerminalDemoProducesResultsWithoutSensorOrGpuCharts()
    {
        var vm = Create(); await vm.InitializeAsync(); await vm.StartCommand.ExecuteAsync(null);
        Assert.Equal(Screen.Results, vm.CurrentScreen); Assert.True(vm.HasResults); Assert.False(vm.IsActive);
        Assert.Equal(11, vm.ModuleResults.Count); Assert.Equal(2, vm.UnavailableCount);
        Assert.Empty(vm.MetricCards); Assert.Contains("demonstra", vm.ResultStatus);
    }
    [Fact]
    public async Task HistoryFiltersStatusComputerAndDate()
    {
        var repository = new MemoryRepository(); var vm = Create(repository: repository); await vm.InitializeAsync();
        await vm.LoadExamplesCommand.ExecuteAsync(null); Assert.Equal(4, vm.History.Count);
        vm.HistoryStatus = "Interrompida"; Assert.Single(vm.History);
        vm.HistorySearch = "missing"; Assert.Empty(vm.History);
        vm.HistorySearch = "DEMO"; Assert.Single(vm.History);
        vm.HistoryDateText = vm.History[0].Session.StartedAt.ToLocalTime().ToString("dd/MM/yyyy"); Assert.Single(vm.History);
        vm.HistoryDateText = "inválida"; Assert.Empty(vm.History);
    }
    [Fact]
    public async Task DeleteRequiresConfirmationAndKeepsActiveSessions()
    {
        var repository = new MemoryRepository(); var dialogs = new FakeDialogs(); var vm = Create(repository: repository, dialogs: dialogs);
        await vm.InitializeAsync(); await vm.LoadExamplesCommand.ExecuteAsync(null); var row = vm.History[0];
        dialogs.Confirmed = false; await vm.DeleteHistoryCommand.ExecuteAsync(row); Assert.Equal(4, vm.History.Count);
        dialogs.Confirmed = true; await vm.DeleteHistoryCommand.ExecuteAsync(row); Assert.Equal(3, vm.History.Count);
    }
    [Fact]
    public async Task NavigationDoesNotCancelActiveSessionOrAllowASecondRun()
    {
        var engine = new GatedEngine(); var vm = new ShellViewModel(engine, new MemoryRepository(), new FakeDialogs());
        await vm.InitializeAsync(); var execution = vm.StartCommand.ExecuteAsync(null);
        await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.True(vm.IsActive); Assert.False(vm.StartCommand.CanExecute(null));
            foreach (var item in vm.Navigation) { vm.NavigateCommand.Execute(item); Assert.True(vm.IsActive); }
            Assert.Equal(0, engine.Cancels); await vm.CancelCommand.ExecuteAsync(null); Assert.Equal(1, engine.Cancels);
        }
        finally { engine.Release.TrySetResult(); await execution; }
        Assert.False(vm.IsActive); Assert.Contains("parcial", vm.ResultStatus);
    }
    private static ShellViewModel Create(FakeEngine? engine = null, MemoryRepository? repository = null, FakeDialogs? dialogs = null) => new(engine ?? new(), repository ?? new(), dialogs ?? new());
}

internal sealed class GatedEngine : IDiagnosticEngineClient
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Cancels { get; private set; }
    public async IAsyncEnumerable<DiagnosticSession> RunAsync(DiagnosticOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var session = new DiagnosticSession { Options = options }.TransitionTo(SessionState.Validating, "test", DateTimeOffset.UtcNow).TransitionTo(SessionState.Running, "test", DateTimeOffset.UtcNow);
        yield return session; Started.TrySetResult(); await Release.Task.WaitAsync(cancellationToken);
        yield return session.TransitionTo(SessionState.Cancelled, "test", DateTimeOffset.UtcNow);
    }
    public Task CancelAsync(CancellationToken cancellationToken = default) { Cancels++; Release.TrySetResult(); return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeEngine : IDiagnosticEngineClient
{
    public int Starts { get; private set; }
    public async IAsyncEnumerable<DiagnosticSession> RunAsync(DiagnosticOptions options, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Starts++; await Task.Yield(); yield return DemoFixtures.Archived(SessionState.Completed, DateTimeOffset.UtcNow) with { Options = options };
    }
    public Task CancelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
internal sealed class FakeDialogs : IUserDialogs
{
    public bool Confirmed { get; set; } = true;
    public bool Confirm(string title, string message) => Confirmed;
    public string? ChooseReportFolder(string current) => null;
}
internal sealed class MemoryRepository : ISessionRepository
{
    private readonly Dictionary<Guid, DiagnosticSession> sessions = [];
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SaveAsync(DiagnosticSession session, CancellationToken cancellationToken = default) { sessions[session.Id] = session; return Task.CompletedTask; }
    public Task<DiagnosticSession?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(sessions.GetValueOrDefault(id));
    public Task<IReadOnlyList<DiagnosticSession>> ListAsync(int limit = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DiagnosticSession>>(sessions.Values.OrderByDescending(x => x.StartedAt).Take(limit).ToArray());
    public Task SaveNotesAsync(Guid id, string notes, CancellationToken cancellationToken = default) { sessions[id] = sessions[id] with { Notes = notes }; return Task.CompletedTask; }
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(sessions.TryGetValue(id, out var s) && s.IsTerminal && sessions.Remove(id));
    public Task<int> ClearTerminalAsync(CancellationToken cancellationToken = default) { var keys = sessions.Values.Where(x => x.IsTerminal).Select(x => x.Id).ToArray(); foreach (var key in keys) sessions.Remove(key); return Task.FromResult(keys.Length); }
    public Task<int> RecoverInterruptedAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
}
