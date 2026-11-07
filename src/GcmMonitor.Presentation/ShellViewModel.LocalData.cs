using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GcmMonitor.Core;

namespace GcmMonitor.Presentation;

public sealed record RetentionOption(int Days, string Label);

public sealed partial class ShellViewModel
{
    private readonly IUserPreferencesStore? preferencesStore;
    private readonly SemaphoreSlim historyGate = new(1);
    private HistoryQuery historyQuery = new();
    private HistoryPage historyPage = new([], 0, 0, 0, null);
    [ObservableProperty] private int retentionDays;
    public IReadOnlyList<RetentionOption> RetentionOptions { get; } =
        [new(0, "Manter todas"), new(30, "Revisar após 30 dias"), new(90, "Revisar após 90 dias"), new(365, "Revisar após 365 dias")];

    public string StorageSummary => $"{historyPage.TotalCount:N0} sessões armazenadas · " +
        (historyPage.DatabaseBytes is { } bytes ? $"{bytes / 1048576.0:N2} MiB de banco local" : "Uso em disco indisponível");
    private int LastHistoryPage => Math.Max(0, (historyPage.MatchingCount - 1) / HistoryQuery.PageSize);
    public string HistoryPageLabel => $"Página {historyQuery.Page + 1} de {LastHistoryPage + 1} · {historyPage.MatchingCount:N0} sessões encontradas";
    private bool CanPreviousHistory() => historyQuery.Page > 0;
    private bool CanNextHistory() => historyQuery.Page < LastHistoryPage;

    private async Task RefreshHistoryAsync(HistoryQuery? requested = null)
    {
        await historyGate.WaitAsync();
        try
        {
            var query = requested ?? historyQuery;
            var page = await repository.QueryHistoryAsync(query);
            int lastPage = Math.Max(0, (page.MatchingCount - 1) / HistoryQuery.PageSize);
            if (query.Page > lastPage)
            {
                query = query with { Page = lastPage };
                page = await repository.QueryHistoryAsync(query);
            }
            historyQuery = query;
            historyPage = page;
            SelectedHistory = null;
            History.Clear();
            foreach (var entry in page.Entries) History.Add(new(entry));
            OnPropertyChanged(nameof(StorageSummary));
            OnPropertyChanged(nameof(HistoryPageLabel));
            OnPropertyChanged(nameof(HasNoHistory));
            PreviousHistoryCommand.NotifyCanExecuteChanged();
            NextHistoryCommand.NotifyCanExecuteChanged();
        }
        finally { historyGate.Release(); }
    }

    [RelayCommand]
    private async Task ApplyHistoryFiltersAsync()
    {
        DateTimeOffset? from = null, until = null;
        if (!string.IsNullOrWhiteSpace(HistoryDateText))
        {
            if (!DateTime.TryParseExact(HistoryDateText.Trim(), "dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out var date) ||
                date.Date == DateTime.MaxValue.Date)
            { Message = "Informe uma data no formato dd/MM/aaaa."; return; }
            from = new DateTimeOffset(date.ToUniversalTime());
            until = new DateTimeOffset(date.AddDays(1).ToUniversalTime());
        }
        SessionState? state = HistoryStatus == "Todos" ? null : Enum.GetValues<SessionState>().Single(x => Labels.Session(x) == HistoryStatus);
        await LoadHistoryPageAsync(new(0, HistorySearch.Trim(), state, from, until));
    }

    [RelayCommand(CanExecute = nameof(CanPreviousHistory))]
    private Task PreviousHistoryAsync() => LoadHistoryPageAsync(historyQuery with { Page = historyQuery.Page - 1 });

    [RelayCommand(CanExecute = nameof(CanNextHistory))]
    private Task NextHistoryAsync() => LoadHistoryPageAsync(historyQuery with { Page = historyQuery.Page + 1 });

    private async Task LoadHistoryPageAsync(HistoryQuery query)
    {
        try { await RefreshHistoryAsync(query); Message = ""; }
        catch (Exception ex) when (ex is IOException or System.Data.Common.DbException or ArgumentException)
        { Message = "Não foi possível consultar o histórico. " + ex.Message; }
    }

    private async Task<string?> LoadPreferencesAsync()
    {
        if (preferencesStore is null) return null;
        try
        {
            if (await preferencesStore.LoadAsync() is { } saved)
            {
                InformativeNotifications = saved.InformativeNotifications;
                ReportFolder = saved.ReportFolder;
                RetentionDays = saved.RetentionDays;
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { return "Preferências indisponíveis. Os valores padrão estão em uso; o arquivo anterior será preservado até um novo salvamento."; }
    }

    [RelayCommand]
    private void ChooseFolder()
    {
        if (dialogs.ChooseReportFolder(ReportFolder) is not { } chosen) return;
        ReportFolder = chosen;
        Message = "Salve as preferências para usar esta pasta nas próximas execuções.";
    }

    [RelayCommand]
    private async Task SavePreferencesAsync()
    {
        if (preferencesStore is null) { Message = "Preferências desta demonstração valem durante a execução."; return; }
        try
        {
            await preferencesStore.SaveAsync(new(InformativeNotifications, ReportFolder, RetentionDays));
            Message = "Preferências salvas neste perfil do Windows.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Message = "Não foi possível salvar as preferências. " + ex.Message; }
    }

    [RelayCommand]
    private async Task ApplyRetentionAsync()
    {
        if (RetentionDays is not (30 or 90 or 365)) { Message = "Selecione um prazo de retenção para aplicar a limpeza."; return; }
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
        if (!dialogs.Confirm("Aplicar retenção", $"Excluir sessões finalizadas iniciadas antes de {cutoff.ToLocalTime():dd/MM/yyyy HH:mm}? PDFs externos serão preservados.")) return;
        try
        {
            int removed = await repository.DeleteTerminalBeforeAsync(cutoff);
            if (resultSession is { IsTerminal: true } open && open.StartedAt < cutoff && await repository.GetAsync(open.Id) is null)
                ClearResultIfDeleted(open.Id);
            await RefreshHistoryAsync();
            Message = $"{removed:N0} sessões finalizadas removidas.";
        }
        catch (Exception ex) when (ex is IOException or System.Data.Common.DbException)
        { Message = "Não foi possível aplicar a retenção. " + ex.Message; }
    }
}
