using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GcmMonitor.Collector;
using GcmMonitor.Core;

namespace GcmMonitor.Presentation;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IDiagnosticEngineClient engine;
    private readonly ISessionRepository repository;
    private readonly IUserDialogs dialogs;
    private DiagnosticSession? activeSession;
    private DiagnosticSession? resultSession;

    public ShellViewModel(IDiagnosticEngineClient engine, ISessionRepository repository, IUserDialogs dialogs)
    {
        this.engine = engine; this.repository = repository; this.dialogs = dialogs;
        Modules = new(ModuleCatalog.All.Select(x => new ModuleSelection(x)));
        foreach (var module in Modules) module.PropertyChanged += SelectionChanged;
    }

    public IReadOnlyList<NavigationItem> Navigation { get; } = new[]
    {
        new NavigationItem(Screen.Home, "Início", "\uE80F"), new NavigationItem(Screen.Monitoring, "Monitoramento", "\uE9D9"),
        new NavigationItem(Screen.Results, "Resultados", "\uE9D2"), new NavigationItem(Screen.History, "Histórico", "\uE81C"),
        new NavigationItem(Screen.Settings, "Configurações", "\uE713")
    };
    public ObservableCollection<ModuleSelection> Modules { get; }
    public ObservableCollection<DiskSelection> Disks { get; } = [new("demo-disk-0", "Disco 0 · SSD NVMe · 512 GB (fictício)"), new("demo-disk-1", "Disco 1 · HDD SATA · 1 TB (fictício)")];
    public IReadOnlyList<DurationOption> Durations { get; } = new[] { new DurationOption(5, "5 minutos"), new DurationOption(30, "30 minutos"), new DurationOption(60, "1 hora"), new DurationOption(240, "4 horas") };
    public IReadOnlyList<SummaryCard> Hardware { get; } = new[]
    {
        new SummaryCard("\uE770", "Computador", "DEMO-GCM01", "Windows 11 Pro · fictício\nÚltimo boot: exemplo\nUptime: 2 h (simulado)"),
        new SummaryCard("\uE9D9", "CPU", "CPU demonstrativa", "8 núcleos · 16 threads\nSem consulta ao processador"),
        new SummaryCard("\uE950", "RAM", "16 GB", "2 × 8 GB · fictícios\nSem leitura de memória"),
        new SummaryCard("\uE7F4", "GPU", "GPU demonstrativa", "Integrada · VRAM indisponível\nSem leitura de sensores"),
        new SummaryCard("\uEDA2", "Armazenamento", "SSD + HDD fictícios", "SSD: 512 GB · 196 GB livres\nHDD: 1 TB · 720 GB livres\nSMART não coletado")
    };

    [ObservableProperty] private Screen currentScreen = Screen.Home;
    [ObservableProperty] private string message = "";
    [ObservableProperty] private bool isReady;
    [ObservableProperty] private bool isActive;
    [ObservableProperty] private DurationOption monitoringDuration = new(30, "30 minutos");
    [ObservableProperty] private DurationOption stressDuration = new(5, "5 minutos");
    [ObservableProperty] private bool detailedBattery;
    [ObservableProperty] private bool detailedWifi;
    [ObservableProperty] private string technicianNotes = "";
    [ObservableProperty] private string historySearch = "";
    [ObservableProperty] private string historyDateText = "";
    [ObservableProperty] private string historyStatus = "Todos";
    [ObservableProperty] private HistoryRow? selectedHistory;
    [ObservableProperty] private bool informativeNotifications = true;
    [ObservableProperty] private string reportFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GCM Monitor", "Relatórios");

    public ObservableCollection<HistoryRow> History { get; } = [];
    private IReadOnlyList<DiagnosticSession> storedHistory = [];
    public IReadOnlyList<string> HistoryStatuses { get; } = new[] { "Todos", "Concluída", "Cancelada", "Interrompida", "Erro" };
    public int SelectedModuleCount => Modules.Count(x => x.IsSelected);
    public bool BatteryEnabled => !IsActive && Modules.Single(x => x.Definition.Id == ModuleId.Battery).IsSelected;
    public bool WifiEnabled => !IsActive && Modules.Single(x => x.Definition.Id == ModuleId.Wifi).IsSelected;
    public bool StorageEnabled => !IsActive && Modules.Single(x => x.Definition.Id == ModuleId.Storage).IsSelected;
    public bool CanConfigure => IsReady && !IsActive;
    public string Title => Navigation.Single(x => x.Screen == CurrentScreen).Title;
    public string Subtitle => CurrentScreen switch
    {
        Screen.Home => "Visão geral do computador, seleção e execução do diagnóstico",
        Screen.Monitoring => "Acompanhe a reprodução demonstrativa da sessão",
        Screen.Results => "Resumo e análise do diagnóstico realizado.",
        Screen.History => "Diagnósticos realizados neste computador.",
        _ => "Preferências e gerenciamento do GCM Monitor"
    };
    public string SessionStatus => activeSession is null ? "Nenhuma sessão iniciada" : Labels.Session(activeSession.State) + " · demonstração";
    public string Stage => activeSession?.Transitions.LastOrDefault()?.Reason ?? "Selecione módulos no Início para iniciar uma demonstração.";
    public string Clock => activeSession is null ? "— / —" : $"{activeSession.EffectiveDuration:hh\\:mm\\:ss} / {TimeSpan.FromMinutes(activeSession.Options.MonitoringMinutes):hh\\:mm\\:ss} · tempo simulado";
    public double Progress => activeSession is null ? 0 : Math.Clamp(activeSession.EffectiveDuration.TotalMinutes / activeSession.Options.MonitoringMinutes * 100, 0, 100);
    public bool HasSession => activeSession is not null;
    public bool HasResults => resultSession is not null;
    public bool HasNoResults => !HasResults;
    public bool HasNoMetrics => MetricCards.Count == 0;
    public bool HasNoHistory => History.Count == 0;
    public string ResultStatus => resultSession is null ? IsActive ? "Sessão ativa: resultados ainda não consolidados." : "Nenhum resultado selecionado." :
        $"{Labels.Session(resultSession.State)} · {(resultSession.State == SessionState.Completed ? "Resumo demonstrativo" : "Relatório parcial demonstrativo")}";
    public string ResultSummary => resultSession is null ? "Inicie uma demonstração no Início ou abra uma sessão pelo Histórico." :
        $"{resultSession.ComputerName} · {resultSession.StartedAt.ToLocalTime():dd/MM/yyyy HH:mm} → {resultSession.EndedAt?.ToLocalTime():HH:mm}\n{resultSession.EffectiveDuration.TotalMinutes:0.#} min simulados · {resultSession.Modules.Length} módulos · Estresse desativado\n{resultSession.IntegrityNote}";
    public int NormalCount => resultSession?.Findings.Count(x => x.Severity == FindingSeverity.Normal) ?? 0;
    public int AttentionCount => resultSession?.Findings.Count(x => x.Severity == FindingSeverity.Attention) ?? 0;
    public int FailureCount => resultSession?.Findings.Count(x => x.Severity == FindingSeverity.Failure) ?? 0;
    public int UnavailableCount => resultSession?.Findings.Count(x => x.Severity == FindingSeverity.Unavailable) ?? 0;
    public string StorageSummary => $"{storedHistory.Count} sessões demonstrativas carregadas (limite de 100). Retenção: manter até exclusão manual.";
    public string InstallationPath => AppContext.BaseDirectory;
    public string CollectionIssues => resultSession is null ? "Nenhuma coleta realizada." :
        string.Join("\n", resultSession.Modules.Where(x => x.State is ModuleState.Unavailable or ModuleState.Error or ModuleState.Partial)
            .Select(x => $"{Labels.Module(x.State)} · {ModuleCatalog.All.Single(m => m.Id == x.Id).Name}: {x.Summary}")) is { Length: > 0 } issues ? issues : "Nenhuma falha de coletor simulada nesta sessão.";
    public ObservableCollection<MetricCard> MetricCards { get; } = [];
    public ObservableCollection<ModuleResult> ModuleResults { get; } = [];
    public ObservableCollection<Finding> MainFindings { get; } = [];

    public async Task InitializeAsync()
    {
        try
        {
            await repository.InitializeAsync();
            await repository.RecoverInterruptedAsync();
            await RefreshHistoryAsync();
            IsReady = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Data.Common.DbException or UnauthorizedAccessException)
        { Message = "Não foi possível abrir o banco demonstrativo. Verifique o acesso à pasta de dados. " + ex.Message; }
    }

    private void SelectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ModuleSelection.IsSelected)) return;
        if (!BatteryEnabled) DetailedBattery = false;
        if (!WifiEnabled) DetailedWifi = false;
        OnPropertyChanged(nameof(SelectedModuleCount)); OnPropertyChanged(nameof(BatteryEnabled));
        OnPropertyChanged(nameof(WifiEnabled)); OnPropertyChanged(nameof(StorageEnabled));
    }
    partial void OnCurrentScreenChanged(Screen value) { OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Subtitle)); }
    partial void OnIsReadyChanged(bool value) { OnPropertyChanged(nameof(CanConfigure)); StartCommand.NotifyCanExecuteChanged(); }
    partial void OnIsActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(CanConfigure)); OnPropertyChanged(nameof(BatteryEnabled)); OnPropertyChanged(nameof(WifiEnabled)); OnPropertyChanged(nameof(StorageEnabled));
        StartCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(ResultStatus));
    }
    partial void OnHistorySearchChanged(string value) => FilterHistory();
    partial void OnHistoryDateTextChanged(string value) => FilterHistory();
    partial void OnHistoryStatusChanged(string value) => FilterHistory();

    [RelayCommand] private void Navigate(NavigationItem? item) { if (item is not null) CurrentScreen = item.Screen; }
    [RelayCommand] private void OpenMonitoring() => CurrentScreen = Screen.Monitoring;
    [RelayCommand] private void OpenResults() => CurrentScreen = Screen.Results;
    [RelayCommand] private void EnableAll() { if (!CanConfigure) return; foreach (var m in Modules) m.IsSelected = true; }
    [RelayCommand] private void DisableAll() { if (!CanConfigure) return; foreach (var m in Modules) m.IsSelected = false; }
    [RelayCommand]
    private void RestoreDefaults()
    {
        if (!CanConfigure) return;
        // A especificação não define um subconjunto recomendado: padrão demonstrativo = 11 módulos.
        EnableAll(); MonitoringDuration = Durations[1]; StressDuration = Durations[0]; DetailedBattery = DetailedWifi = false;
        foreach (var disk in Disks) disk.IsSelected = true;
        Message = "Padrão demonstrativo restaurado: 11 módulos; estresse permanece desativado.";
    }

    public DiagnosticOptions CreateOptions() => new()
    {
        Modules = Modules.Where(x => x.IsSelected).Select(x => x.Definition.Id).ToArray(),
        MonitoringMinutes = MonitoringDuration.Minutes,
        StressEnabled = false,
        StressMinutes = StressDuration.Minutes,
        DiskIds = Disks.Where(x => x.IsSelected).Select(x => x.Id).ToArray(),
        DetailedBattery = DetailedBattery && BatteryEnabled,
        DetailedWifi = DetailedWifi && WifiEnabled
    };

    private bool CanStart() => CanConfigure;
    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (IsActive) { CurrentScreen = Screen.Monitoring; return; }
        try
        {
            var options = CreateOptions(); options.Validate();
            activeSession = null;
            UpdateMonitoring();
            IsActive = true; Message = "Reprodução acelerada de dados sintéticos. Nenhum diagnóstico real em execução.";
            resultSession = null; NotifyResults(); CurrentScreen = Screen.Monitoring;
            await foreach (var snapshot in engine.RunAsync(options))
            {
                activeSession = snapshot; UpdateMonitoring();
                if (snapshot.IsTerminal) ShowResult(snapshot);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or System.Data.Common.DbException or OperationCanceledException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Message = "A demonstração não pôde continuar: " + ex.Message;
            if (activeSession is { IsTerminal: false })
            {
                activeSession = activeSession.TransitionTo(SessionState.Interrupted, "Falha de comunicação ou persistência da demonstração.", DateTimeOffset.UtcNow);
                UpdateMonitoring(); ShowResult(activeSession);
            }
        }
        finally
        {
            IsActive = false;
            try { await RefreshHistoryAsync(); } catch (Exception ex) when (ex is System.Data.Common.DbException or IOException) { Message = "Falha ao atualizar o histórico demonstrativo: " + ex.Message; }
        }
    }

    [RelayCommand(CanExecute = nameof(IsActive))]
    private async Task CancelAsync()
    {
        try { await engine.CancelAsync(); Message = "Cancelamento solicitado ao motor demonstrativo."; }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { Message = "Canal indisponível. O motor demonstrativo será encerrado."; }
    }

    private void UpdateMonitoring()
    {
        foreach (var name in new[] { nameof(SessionStatus), nameof(Stage), nameof(Clock), nameof(Progress), nameof(HasSession) }) OnPropertyChanged(name);
        MetricCards.Clear();
        foreach (var group in (activeSession?.Measurements ?? []).Where(x => x.Quality == DataQuality.Valid && x.Value.HasValue).GroupBy(x => (x.Metric, x.Component)))
        {
            var measurements = group.TakeLast(32).ToArray();
            if (measurements.Length < 2) continue;
            var last = measurements[^1];
            MetricCards.Add(new(last.Metric, last.Value!.Value.ToString("0.#"), last.Unit, last.Component,
                "SINTÉTICO · " + last.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                measurements.Select(x => x.Timestamp.ToLocalTime().ToString("HH:mm:ss")).ToArray(), measurements.Select(x => x.Value!.Value).ToArray()));
        }
        OnPropertyChanged(nameof(HasNoMetrics));
    }

    private void ShowResult(DiagnosticSession session)
    {
        resultSession = session; TechnicianNotes = session.Notes; CurrentScreen = Screen.Results;
        ModuleResults.Clear(); MainFindings.Clear();
        foreach (var run in session.Modules)
        {
            var def = ModuleCatalog.All.Single(x => x.Id == run.Id);
            var finding = session.Findings.FirstOrDefault(x => x.Module == run.Id);
            ModuleResults.Add(new(def.Code, def.Name, Labels.Module(run.State), run.Summary, finding?.Evidence ?? "Sem evidência consolidada.",
                finding?.Interpretation ?? "Dados parciais.", finding?.Recommendation ?? "Revisar limitações.",
                run.State == ModuleState.Completed ? "#42D6B0" : run.State == ModuleState.Error ? "#FF8292" : "#FFC76A"));
        }
        foreach (var finding in session.Findings.Where(x => x.Severity != FindingSeverity.Normal)) MainFindings.Add(finding);
        NotifyResults();
    }

    private void NotifyResults()
    {
        foreach (var name in new[] { nameof(HasResults), nameof(HasNoResults), nameof(ResultStatus), nameof(ResultSummary), nameof(NormalCount), nameof(AttentionCount), nameof(FailureCount), nameof(UnavailableCount), nameof(CollectionIssues) }) OnPropertyChanged(name);
    }

    [RelayCommand]
    private async Task LoadExamplesAsync()
    {
        if (IsActive) { Message = "Aguarde o fim da demonstração ativa."; return; }
        try
        {
            if (storedHistory.Count > 0) { Message = "O histórico já contém sessões. Abra uma linha para revisar."; return; }
            foreach (var state in new[] { SessionState.Completed, SessionState.Cancelled, SessionState.Interrupted, SessionState.Error })
                await repository.SaveAsync(DemoFixtures.Archived(state, DateTimeOffset.UtcNow.AddDays(-1 - (int)state)));
            await RefreshHistoryAsync(); Message = "Quatro sessões fictícias criadas para revisão visual.";
        }
        catch (Exception ex) when (ex is IOException or System.Data.Common.DbException) { Message = "Falha ao salvar exemplos: " + ex.Message; }
    }

    private async Task RefreshHistoryAsync() { storedHistory = await repository.ListAsync(); FilterHistory(); OnPropertyChanged(nameof(StorageSummary)); }
    private void FilterHistory()
    {
        History.Clear();
        DateTime? date = null;
        if (!string.IsNullOrWhiteSpace(HistoryDateText))
        {
            if (!DateTime.TryParseExact(HistoryDateText, "dd/MM/yyyy", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), System.Globalization.DateTimeStyles.None, out var parsed))
            { OnPropertyChanged(nameof(HasNoHistory)); return; }
            date = parsed;
        }
        foreach (var s in storedHistory.Where(x => x.ComputerName.Contains(HistorySearch, StringComparison.CurrentCultureIgnoreCase) &&
            (date is null || x.StartedAt.ToLocalTime().Date == date.Value.Date) && (HistoryStatus == "Todos" || Labels.Session(x.State) == HistoryStatus)))
            History.Add(new(s));
        OnPropertyChanged(nameof(HasNoHistory));
    }
    [RelayCommand] private void OpenHistory(HistoryRow? row) { row ??= SelectedHistory; if (row is not null) ShowResult(row.Session); else Message = "Selecione uma sessão do histórico."; }
    [RelayCommand]
    private async Task SaveNotesAsync()
    {
        if (resultSession is null) return;
        try
        {
            await repository.SaveNotesAsync(resultSession.Id, TechnicianNotes);
            resultSession = resultSession with { Notes = TechnicianNotes }; await RefreshHistoryAsync(); Message = "Observações salvas na sessão demonstrativa.";
        }
        catch (Exception ex) when (ex is IOException or System.Data.Common.DbException or KeyNotFoundException or ArgumentException) { Message = "Não foi possível salvar observações: " + ex.Message; }
    }
    [RelayCommand]
    private async Task DeleteHistoryAsync(HistoryRow? row)
    {
        row ??= SelectedHistory;
        if (row is null || !row.Session.IsTerminal) { Message = "Selecione uma sessão finalizada."; return; }
        if (!dialogs.Confirm("Excluir sessão", "Excluir somente a sessão demonstrativa selecionada? PDFs externos serão preservados.")) return;
        try { await repository.DeleteAsync(row.Session.Id); ClearResultIfDeleted(row.Session.Id); await RefreshHistoryAsync(); }
        catch (Exception ex) when (ex is IOException or System.Data.Common.DbException) { Message = "Falha ao excluir a sessão: " + ex.Message; }
    }
    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (!dialogs.Confirm("Limpar histórico", "Excluir as sessões demonstrativas finalizadas? Sessões ativas e arquivos externos serão preservados.")) return;
        try
        {
            await repository.ClearTerminalAsync(); if (resultSession?.IsTerminal == true) ClearResultIfDeleted(resultSession.Id);
            await RefreshHistoryAsync(); Message = "Histórico demonstrativo limpo. Sessões ativas e arquivos externos preservados.";
        }
        catch (Exception ex) when (ex is IOException or System.Data.Common.DbException) { Message = "Falha ao limpar histórico: " + ex.Message; }
    }
    private void ClearResultIfDeleted(Guid id)
    {
        if (resultSession?.Id != id) return;
        resultSession = null; TechnicianNotes = ""; ModuleResults.Clear(); MainFindings.Clear(); NotifyResults();
    }
    [RelayCommand] private void ChooseFolder() { var path = dialogs.ChooseReportFolder(ReportFolder); if (path is not null) ReportFolder = path; Message = "Preferência de pasta demonstrativa; exportação PDF prevista para v1.0."; }
}
