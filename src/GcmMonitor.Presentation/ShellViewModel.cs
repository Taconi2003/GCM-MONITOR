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

    private readonly IInventoryProvider? inventoryProvider;
    public bool IsDemoMode => inventoryProvider is null;
    public string DataBadge => IsDemoMode ? "DADOS DEMONSTRATIVOS" : "COLETA PASSIVA · " + ProductVersion.Value;
    public string VersionLabel => "v" + ProductVersion.Value;
    public string WindowTitle => "GCM Monitor — Gimenes Coleta e Monitoramento · " + VersionLabel;
    public string StressBlockReason => "Estresse bloqueado: sensores térmicos, limites por hardware e matriz P0 ainda precisam de validação em laboratório.";
    public string StressResultSummary => resultSession is null ? "Estresse bloqueado. Selecione um resultado para consultar a avaliação registrada." : resultSession.Stress is { } assessment
        ? "Estresse desativado · nenhuma carga executada.\n" + string.Join(" ", assessment.Reasons)
        : "Estresse não executado. Sessão anterior ou demonstrativa sem avaliação de segurança da v0.4.";
    public string DataBanner => IsDemoMode ? "MODO DEMONSTRAÇÃO · Dados sintéticos. Nenhuma medição real, teste de estresse ou alteração do Windows. O relógio da reprodução é acelerado." :
        "COLETA PASSIVA · Diagnósticos por capacidade e permissão das fontes Windows. Sensores ausentes são identificados. Estresse bloqueado.";
    public ShellViewModel(IDiagnosticEngineClient engine, ISessionRepository repository, IUserDialogs dialogs, IInventoryProvider? inventoryProvider = null,
        IUserPreferencesStore? preferencesStore = null, IPdfReportExporter? pdfExporter = null)
    {
        this.engine = engine; this.repository = repository; this.dialogs = dialogs;
        this.inventoryProvider = inventoryProvider;
        this.preferencesStore = preferencesStore;
        this.pdfExporter = pdfExporter;
        Modules = new(ModuleCatalog.All.Select(x => new ModuleSelection(x)));
        foreach (var module in Modules) module.PropertyChanged += SelectionChanged;
        if (!IsDemoMode)
        {
            Disks.Clear(); Hardware.Clear();
            foreach (var name in new[] { "Computador", "CPU", "RAM", "GPU", "Armazenamento" })
                Hardware.Add(new("\uE770", name, "Aguardando inventário", "Nenhum valor demonstrativo é utilizado nesta execução."));
        }
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
    public ObservableCollection<SummaryCard> Hardware { get; } = new(new[]
    {
        new SummaryCard("\uE770", "Computador", "DEMO-GCM01", "Windows 11 Pro · fictício\nÚltimo boot: exemplo\nUptime: 2 h (simulado)"),
        new SummaryCard("\uE9D9", "CPU", "CPU demonstrativa", "8 núcleos · 16 threads\nSem consulta ao processador"),
        new SummaryCard("\uE950", "RAM", "16 GB", "2 × 8 GB · fictícios\nSem leitura de memória"),
        new SummaryCard("\uE7F4", "GPU", "GPU demonstrativa", "Integrada · VRAM indisponível\nSem leitura de sensores"),
        new SummaryCard("\uEDA2", "Armazenamento", "SSD + HDD fictícios", "SSD: 512 GB · 196 GB livres\nHDD: 1 TB · 720 GB livres\nSMART não coletado")
    });

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
        Screen.Monitoring => IsDemoMode ? "Acompanhe a reprodução demonstrativa da sessão" : "Diagnóstico em execução — coletando e analisando dados.",
        Screen.Results => "Resumo e análise do diagnóstico realizado.",
        Screen.History => "Diagnósticos realizados neste computador.",
        _ => "Preferências e gerenciamento do GCM Monitor"
    };
    public string SessionStatus => activeSession is null ? "Nenhuma sessão iniciada" : Labels.Session(activeSession.State) + (activeSession.IsDemo ? " · demonstração" : " · coleta passiva");
    public string Stage => (activeSession?.Transitions.LastOrDefault()?.Reason ?? "Selecione módulos disponíveis no Início para iniciar uma sessão.") +
        (activeSession?.Issues.LastOrDefault() is { } issue ? "\nÚltima ocorrência da coleta: " + issue.Message : "");
    public string Clock => activeSession is null ? "— / —" : $"{activeSession.EffectiveDuration:hh\\:mm\\:ss} / {TimeSpan.FromMinutes(activeSession.Options.MonitoringMinutes):hh\\:mm\\:ss}" + (activeSession.IsDemo ? " · tempo simulado" : " · tempo efetivo");
    public double Progress => activeSession is null ? 0 : Math.Clamp(activeSession.EffectiveDuration.TotalMinutes / activeSession.Options.MonitoringMinutes * 100, 0, 100);
    public bool HasSession => activeSession is not null;
    public bool HasResults => resultSession is not null;
    public bool HasNoResults => !HasResults;
    public bool HasNoMetrics => MetricCards.Count == 0;
    public bool HasNoHistory => History.Count == 0;
    public string ResultStatus => resultSession is null ? IsActive ? "Sessão ativa: resultados ainda não consolidados." : "Nenhum resultado selecionado." :
        $"{Labels.Session(resultSession.State)} · {(resultSession.State == SessionState.Completed ? "Resumo da sessão" : "Relatório parcial")}" + (resultSession.IsDemo ? " demonstrativo" : "");
    public string ResultSummary => resultSession is null ? "Inicie uma sessão no Início ou abra um resultado pelo Histórico." :
        $"{resultSession.ComputerName} · {resultSession.StartedAt.ToLocalTime():dd/MM/yyyy HH:mm} → {resultSession.EndedAt?.ToLocalTime():HH:mm}\n{resultSession.EffectiveDuration.TotalMinutes:0.#} min {(resultSession.IsDemo ? "simulados" : "efetivos")} · {resultSession.Modules.Length} módulos · Estresse desativado\n{resultSession.IntegrityNote}";
    public int NormalCount => resultSession?.Findings.Count(x => x.Severity == FindingSeverity.Normal) ?? 0;
    public int AttentionCount => resultSession?.Findings.Count(x => x.Severity == FindingSeverity.Attention) ?? 0;
    public int FailureCount => resultSession?.Findings.Count(x => x.Severity == FindingSeverity.Failure) ?? 0;
    public int UnavailableCount => resultSession?.Findings.Count(x => x.Severity == FindingSeverity.Unavailable) ?? 0;
    public string InstallationPath => AppContext.BaseDirectory;
    public string CollectionIssues => resultSession is null ? "Nenhuma coleta realizada." :
        string.Join("\n", resultSession.Modules.Where(x => x.State is ModuleState.Unavailable or ModuleState.Error or ModuleState.Partial)
            .Select(x => $"{Labels.Module(x.State)} · {ModuleCatalog.All.Single(m => m.Id == x.Id).Name}: {x.Summary}")
            .Concat(resultSession.Issues.Select(x => $"{x.Timestamp.ToLocalTime():HH:mm:ss} · {x.Message}"))
            .Concat(resultSession.Inventory?.Limitations ?? [])) is { Length: > 0 } issues ? issues : "Nenhuma falha de coleta registrada nesta sessão.";
    public ObservableCollection<MetricCard> MetricCards { get; } = [];
    public ObservableCollection<ModuleResult> ModuleResults { get; } = [];
    public ObservableCollection<Finding> MainFindings { get; } = [];
    public ObservableCollection<ProcessRow> CpuProcesses { get; } = [];
    public ObservableCollection<ProcessRow> MemoryProcesses { get; } = [];
    public ObservableCollection<MetricCard> ResultMetricCards { get; } = [];
    public bool HasProcessRankings => CpuProcesses.Count + MemoryProcesses.Count > 0;

    public async Task InitializeAsync()
    {
        try
        {
            await repository.InitializeAsync();
            await repository.RecoverInterruptedAsync();
            string? preferencesError = await LoadPreferencesAsync();
            await RefreshHistoryAsync();
            if (historyPage.ActiveCount != 0)
            { Message = "Há uma sessão anterior com motor ativo ou identidade ainda não verificável. Aguarde o encerramento e reabra o GCM Monitor; outra sessão não será iniciada."; return; }
            if (inventoryProvider is not null) ApplyInventory(await inventoryProvider.ReadAsync());
            if (preferencesError is not null) Message = preferencesError;
            IsReady = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Data.Common.DbException or UnauthorizedAccessException)
        { Message = "Não foi possível inicializar o inventário ou banco. Verifique o acesso à pasta de dados. " + ex.Message; }
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

    [RelayCommand] private void Navigate(NavigationItem? item) { if (item is not null) CurrentScreen = item.Screen; }
    [RelayCommand] private void OpenMonitoring() => CurrentScreen = Screen.Monitoring;
    [RelayCommand] private void OpenResults() => CurrentScreen = Screen.Results;
    [RelayCommand] private void EnableAll() { if (!CanConfigure) return; foreach (var m in Modules) m.IsSelected = m.IsAvailable; }
    [RelayCommand] private void DisableAll() { if (!CanConfigure) return; foreach (var m in Modules) m.IsSelected = false; }
    [RelayCommand]
    private void RestoreDefaults()
    {
        if (!CanConfigure) return;
        // A especificação não define um subconjunto recomendado: padrão demonstrativo = 11 módulos.
        EnableAll(); MonitoringDuration = Durations[1]; StressDuration = Durations[0]; DetailedBattery = DetailedWifi = false;
        foreach (var disk in Disks) disk.IsSelected = true;
        Message = "Seleção dos módulos disponíveis restaurada; estresse permanece desativado.";
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
            IsActive = true; Message = IsDemoMode ? "Reprodução acelerada de dados sintéticos. Nenhum diagnóstico real em execução." : "Coleta passiva real em andamento. O Windows e seus componentes não serão modificados.";
            resultSession = null; NotifyResults(); CurrentScreen = Screen.Monitoring;
            await foreach (var snapshot in engine.RunAsync(options))
            {
                activeSession = snapshot; UpdateMonitoring();
                if (snapshot.IsTerminal) ShowResult(snapshot);
            }
            if (activeSession?.IsTerminal == true && await repository.GetAsync(activeSession.Id) is { } saved) ShowResult(saved);
            if (activeSession?.State == SessionState.Completed)
                Message = InformativeNotifications ? "Diagnóstico concluído. Consulte os resultados." : "";
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or System.Data.Common.DbException or OperationCanceledException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Message = "A sessão não pôde continuar: " + ex.Message;
            if (activeSession is { IsTerminal: false })
            {
                activeSession = activeSession.TransitionTo(SessionState.Interrupted, "Falha de comunicação ou persistência da sessão.", DateTimeOffset.UtcNow);
                UpdateMonitoring(); ShowResult(activeSession);
            }
        }
        finally
        {
            IsActive = false;
            try { await RefreshHistoryAsync(); } catch (Exception ex) when (ex is System.Data.Common.DbException or IOException) { Message = "Falha ao atualizar o histórico: " + ex.Message; }
        }
    }

    [RelayCommand(CanExecute = nameof(IsActive))]
    private async Task CancelAsync()
    {
        try { await engine.CancelAsync(); Message = "Cancelamento solicitado ao motor de coleta."; }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { Message = "Canal indisponível. O motor de sessão será encerrado."; }
    }

    private void UpdateMonitoring()
    {
        foreach (var name in new[] { nameof(SessionStatus), nameof(Stage), nameof(Clock), nameof(Progress), nameof(HasSession) }) OnPropertyChanged(name);
        FillCharts(activeSession, MetricCards);
        OnPropertyChanged(nameof(HasNoMetrics));
    }

    private void ShowResult(DiagnosticSession session)
    {
        resultSession = session; TechnicianNotes = session.Notes; CurrentScreen = Screen.Results;
        ModuleResults.Clear(); MainFindings.Clear();
        FillCharts(session, ResultMetricCards); FillProcessRankings(session);
        foreach (var run in session.Modules)
        {
            var def = ModuleCatalog.All.Single(x => x.Id == run.Id);
            var finding = session.Findings.FirstOrDefault(x => x.Module == run.Id);
            ModuleResults.Add(new(def.Code, def.Name, Labels.Module(run.State), run.Summary, finding?.Evidence ?? $"{run.ReadingCount} leituras · {run.Source} · início {run.StartedAt?.ToLocalTime():HH:mm:ss} · fim {run.EndedAt?.ToLocalTime():HH:mm:ss}",
                finding?.Interpretation ?? "Medições observacionais, sem conclusão automática de saúde física.", finding?.Recommendation ?? "Comparar mesma métrica, unidade e intervalo com fontes equivalentes do Windows.",
                run.State == ModuleState.Completed ? "#42D6B0" : run.State == ModuleState.Error ? "#FF8292" : "#FFC76A",
                string.Join("\n", session.Statistics.Where(x => x.Module == run.Id).Select(x =>
                    $"{x.Metric} · {x.Component}: mínimo {x.Minimum:0.##}; média ponderada {(x.TimeWeightedMean.HasValue ? x.TimeWeightedMean.Value.ToString("0.##") : "insuficiente")}; pico {x.Maximum:0.##} {x.Unit} · {x.ValidSamples} leituras · cobertura {x.ObservedSeconds:0.#} s\nFonte: {session.Measurements.FirstOrDefault(m => m.Module == x.Module && m.Metric == x.Metric && m.Component == x.Component)?.Source ?? run.Source}")),
                session.Records.Where(x => x.Module == run.Id).OrderByDescending(x => x.Severity == FindingSeverity.Attention).ThenByDescending(x => x.Timestamp).ToArray()));
        }
        foreach (var finding in session.Findings.Where(x => x.Severity != FindingSeverity.Normal).OrderByDescending(x => x.Severity == FindingSeverity.Failure).ThenByDescending(x => x.Severity == FindingSeverity.Attention)) MainFindings.Add(finding);
        NotifyResults();
    }

    private void NotifyResults()
    {
        foreach (var name in new[] { nameof(HasResults), nameof(HasNoResults), nameof(ResultStatus), nameof(ResultSummary), nameof(StressResultSummary), nameof(NormalCount), nameof(AttentionCount), nameof(FailureCount), nameof(UnavailableCount), nameof(CollectionIssues) }) OnPropertyChanged(name);
        ExportPdfCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task LoadExamplesAsync()
    {
        if (!IsDemoMode) return;
        if (IsActive) { Message = "Aguarde o fim da demonstração ativa."; return; }
        try
        {
            if (historyPage.TotalCount > 0) { Message = "O histórico já contém sessões. Abra uma linha para revisar."; return; }
            foreach (var state in new[] { SessionState.Completed, SessionState.Cancelled, SessionState.Interrupted, SessionState.Error })
                await repository.SaveAsync(DemoFixtures.Archived(state, DateTimeOffset.UtcNow.AddDays(-1 - (int)state)));
            await RefreshHistoryAsync(); Message = "Quatro sessões fictícias criadas para revisão visual.";
        }
        catch (Exception ex) when (ex is IOException or System.Data.Common.DbException) { Message = "Falha ao salvar exemplos: " + ex.Message; }
    }

    [RelayCommand]
    private async Task OpenHistoryAsync(HistoryRow? row)
    {
        row ??= SelectedHistory;
        if (row is not null && row.Session.IsTerminal)
        {
            try
            {
                if (await repository.GetAsync(row.Session.Id) is { IsTerminal: true } saved) ShowResult(saved);
                else { Message = "A sessão selecionada já não está disponível."; await RefreshHistoryAsync(); }
            }
            catch (Exception ex) when (ex is IOException or System.Data.Common.DbException) { Message = "Não foi possível abrir o resultado salvo."; }
        }
        else Message = row is null ? "Selecione uma sessão do histórico." : "A sessão ainda está ativa; não há resultado consolidado.";
    }
    [RelayCommand]
    private async Task SaveNotesAsync()
    {
        if (resultSession is null) return;
        try
        {
            await repository.SaveNotesAsync(resultSession.Id, TechnicianNotes);
            resultSession = resultSession with { Notes = TechnicianNotes }; await RefreshHistoryAsync(); Message = "Observações salvas na sessão.";
        }
        catch (Exception ex) when (ex is IOException or System.Data.Common.DbException or KeyNotFoundException or ArgumentException) { Message = "Não foi possível salvar observações: " + ex.Message; }
    }
    [RelayCommand]
    private async Task DeleteHistoryAsync(HistoryRow? row)
    {
        row ??= SelectedHistory;
        if (row is null || !row.Session.IsTerminal) { Message = "Selecione uma sessão finalizada."; return; }
        if (!dialogs.Confirm("Excluir sessão", "Excluir somente a sessão selecionada? PDFs externos serão preservados.")) return;
        try { await repository.DeleteAsync(row.Session.Id); ClearResultIfDeleted(row.Session.Id); await RefreshHistoryAsync(); }
        catch (Exception ex) when (ex is IOException or System.Data.Common.DbException) { Message = "Falha ao excluir a sessão: " + ex.Message; }
    }
    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (!dialogs.Confirm("Limpar histórico", "Excluir as sessões finalizadas? Sessões ativas e arquivos externos serão preservados.")) return;
        try
        {
            await repository.ClearTerminalAsync(); if (resultSession?.IsTerminal == true) ClearResultIfDeleted(resultSession.Id);
            await RefreshHistoryAsync(); Message = "Histórico limpo. Sessões ativas e arquivos externos preservados.";
        }
        catch (Exception ex) when (ex is IOException or System.Data.Common.DbException) { Message = "Falha ao limpar histórico: " + ex.Message; }
    }
    private void ClearResultIfDeleted(Guid id)
    {
        if (resultSession?.Id != id) return;
        resultSession = null; TechnicianNotes = ""; ModuleResults.Clear(); MainFindings.Clear(); NotifyResults();
    }
}
