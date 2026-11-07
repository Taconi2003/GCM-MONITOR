using System.Globalization;
using System.IO;
using GcmMonitor.Core;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;

namespace GcmMonitor.Reporting;

public sealed class PdfReportExporter : IPdfReportExporter
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Color Navy = Color.FromRgb(18, 37, 61);
    private static readonly Color Muted = Color.FromRgb(63, 83, 103);

    public Task<long> ExportAsync(DiagnosticSession session, string destination, bool overwrite, CancellationToken ct = default)
    {
        var completion = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { ct.ThrowIfCancellationRequested(); completion.SetResult(Export(session, destination, overwrite, ct)); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { completion.SetCanceled(ct); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "GCM PDF" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static long Export(DiagnosticSession session, string destination, bool overwrite, CancellationToken ct)
    {
        if (!session.IsTerminal) throw new InvalidOperationException("A exportação exige uma sessão finalizada.");
        var path = Path.GetFullPath(destination);
        if (!string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Escolha um arquivo com a extensão .pdf.", nameof(destination));
        if (!overwrite && File.Exists(path)) throw new IOException("Já existe um PDF no destino escolhido.");
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, ".gcm-report-" + Guid.NewGuid().ToString("N") + ".tmp");
        var renderer = new PdfDocumentRenderer { Document = Build(session, ct) };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        ct.ThrowIfCancellationRequested();
        bool temporaryCreated = false;
        try
        {
            long bytes;
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                temporaryCreated = true;
                pdf.Save(file, closeStream: false);
                file.Flush(flushToDisk: true);
                bytes = file.Length;
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite);
            temporaryCreated = false;
            return bytes;
        }
        finally { if (temporaryCreated) File.Delete(temporary); }
    }

    private static Document Build(DiagnosticSession session, CancellationToken ct)
    {
        var document = new Document { Culture = Pt };
        document.Info.Title = "GCM Monitor - Relatório técnico";
        document.Info.Subject = session.IsDemo ? "Dados demonstrativos" : "Diagnóstico do equipamento";
        document.Info.Author = "GCM Monitor";
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 10;
        normal.Font.Color = Navy;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);
        foreach (var name in new[] { StyleNames.Heading1, StyleNames.Heading2 })
        {
            var style = document.Styles[name]!;
            style.Font.Name = "Arial";
            style.Font.Size = name == StyleNames.Heading1 ? 16 : 12;
            style.Font.Color = Navy;
            style.Font.Bold = true;
            style.ParagraphFormat.KeepWithNext = true;
            style.ParagraphFormat.SpaceBefore = Unit.FromPoint(12);
            style.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);
        }
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(2.0);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.9);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2.0);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2.0);
        var header = section.Headers.Primary.AddParagraph("GCM MONITOR  /  GIMENES COLETA E MONITORAMENTO");
        header.Format.Font.Size = 8;
        header.Format.Font.Color = Muted;
        var footer = section.Footers.Primary.AddParagraph(session.IsDemo ? "Dados demonstrativos  |  " : "Relatório técnico local  |  ");
        footer.Format.Font.Size = 8;
        footer.Format.Font.Color = Muted;
        footer.AddPageField(); footer.AddText(" / "); footer.AddNumPagesField();

        string label = session.State == SessionState.Completed ? "Relatório técnico" : "Relatório parcial";
        var title = section.AddParagraph(label);
        title.Format.Font.Size = 26;
        title.Format.Font.Bold = true;
        title.Format.KeepWithNext = true;
        if (session.IsDemo) section.AddParagraph("DADOS DEMONSTRATIVOS - nenhuma medição deste equipamento", StyleNames.Heading2);
        section.AddParagraph($"{session.ComputerName}  |  {SessionLabel(session.State)}", StyleNames.Heading2);
        section.AddParagraph($"Início {Time(session.StartedAt)}  ·  Fim {Time(session.EndedAt)}");
        section.AddParagraph($"Duração efetiva {session.EffectiveDuration:c}  ·  Planejada {session.Options.MonitoringMinutes} min");
        section.AddParagraph($"Versão da sessão {session.AppVersion}  ·  Exportador {ProductVersion.Value}");
        section.AddParagraph(session.Stress?.LoadExecuted == true ? "Estresse registrado na sessão original." : "Estresse de CPU - Não executado.");
        if (session.State != SessionState.Completed)
            section.AddParagraph("O diagnóstico terminou antes da consolidação completa. Os resultados refletem somente os dados recuperados.");
        if (session is { IsDemo: true, ThermalRehearsal: { } trace }) AddThermal(section, session, trace);

        section.AddParagraph("Equipamento", StyleNames.Heading1);
        if (session.Inventory is { } inventory)
        {
            section.AddParagraph($"{inventory.WindowsEdition} · {inventory.WindowsVersion} · {inventory.Architecture}");
            section.AddParagraph($"CPU {inventory.CpuName} · {inventory.LogicalProcessors} processadores lógicos");
            section.AddParagraph("RAM física " + Size(inventory.PhysicalMemoryBytes));
            foreach (var disk in inventory.Disks)
            {
                section.AddParagraph($"Disco {disk.Number} · {disk.Model} · {disk.BusType} · {Size(disk.CapacityBytes)}");
                foreach (var volume in disk.Volumes)
                    section.AddParagraph($"{volume.Name} · {Size(volume.FreeBytes)} livres de {Size(volume.CapacityBytes)}");
            }
            foreach (var gpu in inventory.Gpus) section.AddParagraph($"GPU {gpu.Name} · driver {gpu.DriverVersion}");
            foreach (var limitation in inventory.Limitations) section.AddParagraph(limitation);
        }
        else section.AddParagraph("Não disponível - inventário não preservado na sessão.");

        section.AddParagraph("Principais ocorrências", StyleNames.Heading1);
        if (session.Findings.Length == 0) section.AddParagraph("Nenhum achado consolidado na sessão.");
        foreach (var finding in session.Findings.OrderByDescending(x => x.Severity == FindingSeverity.Failure)
            .ThenByDescending(x => x.Severity == FindingSeverity.Attention))
        {
            section.AddParagraph(finding.Observation, StyleNames.Heading2);
            section.AddParagraph(finding.Evidence);
            section.AddParagraph(finding.Interpretation);
            section.AddParagraph(finding.Recommendation);
        }

        section.AddParagraph("Resultados por módulo", StyleNames.Heading1);
        foreach (var definition in ModuleCatalog.All.Where(x => session.Options.Modules.Contains(x.Id)))
        {
            ct.ThrowIfCancellationRequested();
            section.AddParagraph(definition.Code + "  " + definition.Name, StyleNames.Heading2);
            var run = session.Modules.FirstOrDefault(x => x.Id == definition.Id);
            if (run is null) { section.AddParagraph("Não executado - resultado ausente na sessão."); continue; }
            section.AddParagraph($"{ModuleLabel(run.State)} · {run.ReadingCount:N0} leituras · {Time(run.StartedAt)} a {Time(run.EndedAt)}");
            section.AddParagraph(run.Summary);
            section.AddParagraph("Fonte · " + run.Source);
            if (!string.IsNullOrWhiteSpace(run.Limitation)) section.AddParagraph(run.Limitation);
            var stats = session.Statistics.Where(x => x.Module == run.Id).ToArray();
            foreach (var stat in stats.Take(12))
                section.AddParagraph($"{stat.Metric} · {stat.Component} · mín. {Number(stat.Minimum)} · média {Number(stat.TimeWeightedMean)} · máx. {Number(stat.Maximum)} {stat.Unit} · {stat.ValidSamples} pontos");
            if (stats.Length > 12) section.AddParagraph($"Mais {stats.Length - 12} séries estatísticas disponíveis no resultado salvo.");
            var records = session.Records.Where(x => x.Module == run.Id).OrderByDescending(x => x.Severity == FindingSeverity.Attention)
                .ThenByDescending(x => x.Timestamp).ToArray();
            foreach (var record in records.Take(20))
            {
                var paragraph = section.AddParagraph();
                paragraph.AddFormattedText(record.Title + " · ", TextFormat.Bold);
                paragraph.AddText(record.Detail + "\n" + record.Source + " · " + Time(record.Timestamp));
            }
            if (records.Length > 20) section.AddParagraph($"Mais {records.Length - 20} registros disponíveis no resultado salvo.");
            if (stats.Length + records.Length == 0 && run.ReadingCount == 0) section.AddParagraph("Não disponível - sem dados preservados para esta consulta.");
        }

        if (session.Options.Modules.Contains(ModuleId.Processes)) AddProcesses(section, session);
        ReportCharts.Add(section, session);
        section.AddParagraph("Ocorrências da coleta e limitações", StyleNames.Heading1);
        section.AddParagraph(session.IntegrityNote);
        foreach (var issue in session.Issues) section.AddParagraph(Time(issue.Timestamp) + " · " + issue.Message);
        if (session.Issues.Length == 0) section.AddParagraph("Nenhuma falha do coletor registrada nesta sessão.");
        section.AddParagraph("Observações do técnico", StyleNames.Heading1);
        foreach (var line in (string.IsNullOrWhiteSpace(session.Notes) ? "Nenhuma observação salva." : session.Notes).Replace("\r\n", "\n").Split('\n'))
            section.AddParagraph(line);
        return document;
    }

    private static void AddProcesses(Section section, DiagnosticSession session)
    {
        section.AddParagraph("Top 10 de processos", StyleNames.Heading1);
        if (session.Processes.Length == 0) { section.AddParagraph("Não disponível - ranking sem dados preservados."); return; }
        section.AddParagraph("Tempo de CPU acumulado", StyleNames.Heading2);
        foreach (var item in session.Processes.Where(x => x.CpuObservedSeconds > 0).OrderByDescending(x => x.CpuSeconds).Take(10))
            section.AddParagraph($"{item.Name} · {Number(item.CpuSeconds)} s de CPU · observado por {Number(item.CpuObservedSeconds)} s");
        section.AddParagraph("Memória média observada", StyleNames.Heading2);
        foreach (var item in session.Processes.Where(x => x.WorkingSetMeanBytes.HasValue).OrderByDescending(x => x.WorkingSetMeanBytes).Take(10))
            section.AddParagraph($"{item.Name} · {Number(item.WorkingSetMeanBytes / 1048576)} MiB · pico {Number(item.WorkingSetPeakBytes / 1048576.0)} MiB");
    }

    private static void AddThermal(Section section, DiagnosticSession session, ThermalTrace trace)
    {
        var plan = StressPlan.Create(session.Options.MonitoringMinutes, session.Options.StressMinutes);
        var review = ThermalRecoveryAnalyzer.Analyze(trace, plan);
        section.AddParagraph("Análise térmica simulada", StyleNames.Heading1);
        section.AddParagraph($"{trace.Sensor} · {trace.Source}. Cenário fictício, sem estresse físico.");
        void Phase(string title, ThermalPhaseStatistics stats)
        {
            section.AddParagraph(title, StyleNames.Heading2);
            section.AddParagraph($"{stats.Samples} amostras válidas · Inicial {Number(stats.First)} °C · média {Number(stats.Average)} °C · pico {Number(stats.Peak)} °C · final {Number(stats.Last)} °C.");
            section.AddParagraph($"Médias de CPU {Number(stats.CpuAverage)} % · frequência {Number(stats.FrequencyAverage)} MHz · ventoinha {Number(stats.FanAverage)} RPM · energia {stats.Power}.");
        }
        Phase("Antes da carga fictícia", review.Baseline);
        Phase("Durante a carga fictícia", review.Load);
        Phase("Recuperação observada", review.Recovery);
        if (trace.LoadStartedAt is { } started && trace.LoadEndedAt is { } ended)
            section.AddParagraph($"Carga fictícia efetiva {ended - started:c} · observação após a parada {review.ObservedRecovery:c}.");
        section.AddParagraph($"No fim da carga {Number(review.AtLoadEnd)} °C · queda {Number(review.Drop)} °C · taxa {Number(review.CoolingPerMinute)} °C/min · recuperação {Number(review.RecoveredPercent)} % · diferença da referência {Number(review.DifferenceFromBaseline)} °C.");
        section.AddParagraph("Os valores descrevem a observação. Os critérios para classificar o comportamento térmico precisam de qualificação.");
        foreach (var limitation in review.Limitations) section.AddParagraph(limitation);
        foreach (var notice in trace.Notices)
        {
            string choice = notice.Choice == ThermalChoice.Stop ? "Parar estresse" : "Continuar monitorando";
            section.AddParagraph($"{Time(notice.At)} (simulado) · {notice.Message}" +
                (notice.Choice is not null ? $" Escolha {choice}. Registro local {Time(notice.ChoiceRecordedAt)}." : ""));
        }
        if (trace.StopReason is { } reason) section.AddParagraph(reason);
        ReportCharts.AddThermal(section, trace);
    }

    private static string Time(DateTimeOffset? time) => time?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss zzz", Pt) ?? "não disponível";
    private static string Number(double? value) => value?.ToString("0.##", Pt) ?? "indisponível";
    private static string Size(long? bytes) => bytes is null ? "não disponível" : Number(bytes.Value / 1073741824.0) + " GiB";
    private static string SessionLabel(SessionState state) => state switch
    { SessionState.Completed => "Concluída", SessionState.Cancelled => "Cancelada", SessionState.Interrupted => "Interrompida", _ => "Erro" };
    private static string ModuleLabel(ModuleState state) => state switch
    {
        ModuleState.Completed => "Concluído", ModuleState.Partial => "Parcial", ModuleState.Cancelled => "Cancelado",
        ModuleState.Error => "Erro de coleta", ModuleState.Unavailable => "Não disponível", _ => "Não executado"
    };
}
