using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GcmMonitor.Core;

namespace GcmMonitor.Collector;

public interface IWindowsHistoryReader
{
    DiagnosticRecord[] ReadEvents(DateTimeOffset timestamp, CancellationToken ct);
    DiagnosticRecord[] ReadUpdates(DateTimeOffset timestamp, CancellationToken ct);
}

public static class WindowsEventSelection
{
    public const int LookbackDays = 7;
    public const int MaximumPerLog = 128;
    public static string Query(string log) => "*[System[TimeCreated[timediff(@SystemTime) <= 604800000] and (" +
        (log == "Application" ? "(Provider[@Name='Application Error'] and EventID=1000) or (Provider[@Name='Windows Error Reporting'] and EventID=1001)" :
        "(Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41) or (Provider[@Name='EventLog'] and EventID=6008) or " +
        "(Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001) or (Provider[@Name='Display'] and EventID=4101) or " +
        "Provider[@Name='Microsoft-Windows-WHEA-Logger'] or Provider[@Name='disk'] or Provider[@Name='Microsoft-Windows-StorPort'] or " +
        "Provider[@Name='storahci'] or Provider[@Name='stornvme'] or Provider[@Name='Ntfs'] or Provider[@Name='Microsoft-Windows-Ntfs']") + ")]]";
    public static bool Relevant(string provider, int id) => provider switch
    {
        "Microsoft-Windows-Kernel-Power" => id == 41,
        "EventLog" => id == 6008,
        "Microsoft-Windows-WER-SystemErrorReporting" or "Windows Error Reporting" => id == 1001,
        "Display" => id == 4101,
        "Application Error" => id == 1000,
        "Microsoft-Windows-WHEA-Logger" or "disk" or "Microsoft-Windows-StorPort" or "storahci" or "stornvme" or "Ntfs" or "Microsoft-Windows-Ntfs" => true,
        _ => false
    };
}

[SupportedOSPlatform("windows")]
public sealed class WindowsHistoryReader : IWindowsHistoryReader
{
    public DiagnosticRecord[] ReadEvents(DateTimeOffset timestamp, CancellationToken ct)
    {
        var records = new List<DiagnosticRecord>();
        foreach (var log in new[] { "System", "Application" })
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, WindowsEventSelection.Query(log)) { ReverseDirection = true });
                int count = 0;
                while (count <= WindowsEventSelection.MaximumPerLog)
                {
                    ct.ThrowIfCancellationRequested();
                    using var item = reader.ReadEvent(TimeSpan.FromSeconds(3));
                    if (item is null) break;
                    if (count == WindowsEventSelection.MaximumPerLog)
                    {
                        records.Add(new(ModuleId.WindowsEvents, "limit:" + log, "Janela limitada · " + log,
                            "Mais de 128 ocorrências em sete dias. Exibição parcial, com as mais recentes primeiro.", timestamp, "Windows Event Log / XPath", DataQuality.Unavailable));
                        break;
                    }
                    if (!WindowsEventSelection.Relevant(item.ProviderName ?? "", item.Id)) continue;
                    count++;
                    var time = item.TimeCreated is { } date ? new DateTimeOffset(date.ToUniversalTime(), TimeSpan.Zero) : timestamp;
                    records.Add(new(ModuleId.WindowsEvents, $"{log}:{item.RecordId}", DiagnosticValue.Clean(item.ProviderName ?? "Origem desconhecida") + " · ID " + item.Id,
                        $"Log {log}; registro {item.RecordId}; nível {item.Level?.ToString(CultureInfo.InvariantCulture) ?? "não disponível"}. " +
                        "Ocorrência na janela histórica de sete dias; correlação temporal não determina a causa. Conteúdo integral omitido por privacidade.",
                        time, "Windows Event Log / " + log, DataQuality.Valid)
                    { Severity = item.Level is >= 1 and <= 3 ? FindingSeverity.Attention : null });
                }
                records.Add(new(ModuleId.WindowsEvents, "scope:" + log, "Consulta · " + log,
                    $"Janela: {timestamp.AddDays(-7):O} a {timestamp:O}; {count} ocorrências selecionadas. Ausência nessa janela não comprova ausência de falhas.",
                    timestamp, "Windows Event Log / XPath", DataQuality.Valid));
            }
            catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
            {
                records.Add(new(ModuleId.WindowsEvents, "unavailable:" + log, "Consulta indisponível · " + log,
                    "Log não acessível: " + ex.GetType().Name + ". Outros logs/módulos permanecem independentes.", timestamp, "Windows Event Log", DataQuality.Unavailable));
            }
        }
        return records.ToArray();
    }

    public DiagnosticRecord[] ReadUpdates(DateTimeOffset timestamp, CancellationToken ct)
    {
        object? session = null, searcher = null, history = null, system = null;
        var records = new List<DiagnosticRecord>();
        try
        {
            ct.ThrowIfCancellationRequested();
            session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", true)!);
            searcher = ((dynamic)session!).CreateUpdateSearcher();
            int count = ((dynamic)searcher).GetTotalHistoryCount();
            records.Add(new(ModuleId.WindowsUpdate, "scope", "Histórico local do Windows Update",
                $"{count} entradas conhecidas; até 32 recentes consultadas. Não executa Search, download ou instalação. Histórico não representa lista completa de pendências.",
                timestamp, "WUA / GetTotalHistoryCount + QueryHistory", DataQuality.Valid));
            if (count > 0)
            {
                history = ((dynamic)searcher).QueryHistory(0, Math.Min(count, 32));
                for (int i = 0; i < Math.Min(count, 32); i++)
                {
                    ct.ThrowIfCancellationRequested();
                    object? entry = null;
                    try
                    {
                        entry = ((dynamic)history).Item(i);
                        DateTime date = ((dynamic)entry).Date;
                        int operation = ((dynamic)entry).Operation, result = ((dynamic)entry).ResultCode;
                        string title = ((dynamic)entry).Title;
                        records.Add(new(ModuleId.WindowsUpdate, "history:" + i, DiagnosticValue.Clean(title),
                            $"Operação {(operation == 1 ? "instalação" : operation == 2 ? "desinstalação" : operation.ToString(CultureInfo.InvariantCulture))}; resultado WUA {result}. Entrada histórica; não comprova situação atual de todas as atualizações.",
                            new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc)), "WUA / QueryHistory local", DataQuality.Valid));
                    }
                    finally { Release(entry); }
                }
            }
        }
        catch (COMException)
        { records.Add(new(ModuleId.WindowsUpdate, "history-unavailable", "Histórico indisponível", "WUA não acessível. Nenhuma busca foi iniciada.", timestamp, "WUA / histórico local", DataQuality.Unavailable)); }
        finally { Release(history); Release(searcher); Release(session); }
        try
        {
            ct.ThrowIfCancellationRequested();
            system = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.SystemInfo", true)!);
            bool reboot = ((dynamic)system!).RebootRequired;
            records.Add(new(ModuleId.WindowsUpdate, "reboot", "Reinicialização informada pelo WUA",
                reboot ? "Reinicialização pendente segundo WUA. O GCM Monitor não reinicia o computador." : "WUA não informa reinicialização pendente; outras fontes não consultadas.",
                timestamp, "WUA / ISystemInformation.RebootRequired", DataQuality.Valid));
        }
        catch (COMException)
        { records.Add(new(ModuleId.WindowsUpdate, "reboot", "Reinicialização pendente indisponível", "Estado WUA não consultável.", timestamp, "WUA / ISystemInformation", DataQuality.Unavailable)); }
        finally { Release(system); }
        records.Add(new(ModuleId.WindowsUpdate, "pending", "Atualizações pendentes: não determinado",
            "Consulta limitada a histórico e reinicialização do WUA. Não afirma que o Windows está atualizado; nenhuma busca ativa foi autorizada ou executada.", timestamp, "WUA / limites da consulta", DataQuality.Unavailable));
        return records.ToArray();
    }

    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
}
