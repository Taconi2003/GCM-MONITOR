using CommunityToolkit.Mvvm.Input;
using GcmMonitor.Core;

namespace GcmMonitor.Presentation;

public sealed partial class ShellViewModel
{
    private readonly IPdfReportExporter? pdfExporter;
    private bool CanExportPdf(HistoryRow? row) => pdfExporter is not null &&
        (row is null ? resultSession?.IsTerminal == true : row.Session.IsTerminal);

    public Task WaitForReportAsync() => ExportPdfCommand.ExecutionTask ?? Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanExportPdf))]
    private async Task ExportPdfAsync(HistoryRow? row)
    {
        if (!CanExportPdf(row)) return;
        var id = row?.Session.Id ?? resultSession!.Id;
        try
        {
            if (await repository.GetAsync(id) is not { IsTerminal: true } session)
            { Message = "A sessão finalizada já não está disponível para exportação."; return; }
            var suggested = $"GCM-Relatorio-{session.StartedAt.ToLocalTime():yyyyMMdd-HHmmss}-{id.ToString("N")[..8]}.pdf";
            if (dialogs.ChoosePdfPath(ReportFolder, suggested) is not { } destination) return;
            Message = "Preparando o relatório PDF.";
            long bytes = await pdfExporter!.ExportAsync(session, destination, overwrite: true);
            try { await repository.RecordPdfExportAsync(id, bytes, DateTimeOffset.UtcNow); }
            catch (Exception ex) when (ex is IOException or System.Data.Common.DbException or KeyNotFoundException)
            { Message = "PDF salvo. O registro de exportação ficou indisponível."; return; }
            Message = "Relatório PDF salvo na pasta escolhida.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Data.Common.DbException or
            InvalidOperationException or ArgumentException or OperationCanceledException)
        { Message = "Não foi possível exportar o relatório PDF. " + ex.Message; }
    }
}
