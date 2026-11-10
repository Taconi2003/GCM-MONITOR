using System.Collections.ObjectModel;
using GcmMonitor.Core;

namespace GcmMonitor.Presentation;

public sealed partial class ShellViewModel
{
    private void ApplyInventory(ComputerInventory inventory)
    {
        static string Size(long? bytes) => bytes is { } value ? $"{value / (1024.0 * 1024 * 1024):0.#} GiB" : "Não disponível";
        Hardware.Clear();
        Hardware.Add(new("\uE770", "Computador", inventory.ComputerName,
            $"{inventory.WindowsEdition}\n{inventory.WindowsVersion} · {inventory.Architecture}\nÚltimo boot: {inventory.LastBoot?.ToLocalTime():dd/MM HH:mm}\nUptime: {(inventory.LastBoot.HasValue ? (inventory.Timestamp - inventory.LastBoot.Value).ToString(@"d\d\ hh\h\ mm\m") : "Não disponível")}"));
        Hardware.Add(new("\uE9D9", "CPU", string.IsNullOrWhiteSpace(inventory.CpuName) ? "Não disponível" : inventory.CpuName,
            $"{inventory.Cores?.ToString() ?? "?"} núcleos · {inventory.LogicalProcessors} processadores lógicos\nUtilização passiva; nenhum estresse"));
        Hardware.Add(new("\uE950", "RAM", Size(inventory.PhysicalMemoryBytes), inventory.MemoryModulesBytes.Length > 0
            ? string.Join(" + ", inventory.MemoryModulesBytes.Select(x => Size(x))) + "\nCapacidade física instalada" : "Módulos físicos não disponíveis"));
        Hardware.Add(new("\uE7F4", "GPU", inventory.Gpus.Length > 0 ? string.Join(" / ", inventory.Gpus.Select(x => x.Name)) : "Não disponível",
            string.Join("\n", inventory.Gpus.Select(x => "Driver " + x.DriverVersion)) + "\nTipo/VRAM não determinados; sensores conforme disponibilidade."));
        var shown = inventory.Disks.Take(2).ToArray();
        var usage = shown.SelectMany(x => x.Volumes).DistinctBy(x => x.Name)
            .Where(x => x.DiskIds.Length == 1 && x.CapacityBytes > 0 && x.FreeBytes.HasValue)
            .Select(x => new VolumeUsage(x.Name + " · ocupação do volume", (x.CapacityBytes!.Value - x.FreeBytes!.Value) / (double)x.CapacityBytes.Value * 100)).ToArray();
        Hardware.Add(new("\uEDA2", "Armazenamento", $"{inventory.Disks.Length} disco(s) físico(s)",
            string.Join("\n", shown.Select(x => $"Disco {x.Number}: {Size(x.CapacityBytes)} · {x.MediaType}/{x.BusType}\n" +
                string.Join("; ", x.Volumes.Select(v => $"{v.Name}: {Size(v.FreeBytes)} livres")))) +
            (inventory.Disks.Length > 2 ? $"\n+{inventory.Disks.Length - 2} dispositivo(s)" : "") + "\nConfiabilidade conforme capacidade do provedor")
        { Volumes = usage });
        Disks.Clear();
        foreach (var disk in inventory.Disks) Disks.Add(new(disk.Id, $"Disco {disk.Number} · {disk.Model} · {Size(disk.CapacityBytes)}"));
        foreach (var module in Modules)
        {
            module.IsAvailable = module.Definition.Id != ModuleId.Storage || Disks.Count > 0;
            module.IsSelected = module.IsAvailable;
        }
        Message = inventory.Limitations.Length > 0 ? string.Join(" ", inventory.Limitations) : "Inventário carregado por fontes de leitura do Windows. Selecione os módulos para iniciar.";
    }

    private static void FillCharts(DiagnosticSession? session, ObservableCollection<MetricCard> cards)
    {
        cards.Clear();
        foreach (var group in (session?.Measurements ?? []).GroupBy(x => (x.Module, x.Metric, x.Component))
            .OrderBy(x => x.Key.Module == ModuleId.Cpu ? 0 : x.Key.Module == ModuleId.Memory ? 1 : 2)
            .ThenBy(x => x.Key.Metric == "RAM" ? 0 : 1))
        {
            var samples = group.OrderBy(x => x.Timestamp).TakeLast(32).ToArray();
            if (samples.Count(x => x.Quality == DataQuality.Valid) < 2) continue;
            var last = samples[^1];
            var chartPoints = new List<(string Label, double Value)>(samples.Length);
            for (int i = 0; i < samples.Length; i++)
            {
                var sample = samples[i];
                if (!sample.IsDemo && i > 0 && sample.Quality == DataQuality.Valid && samples[i - 1].Quality == DataQuality.Valid &&
                    (sample.Timestamp - samples[i - 1].Timestamp).TotalSeconds > SamplingContinuity.MaximumIntervalSeconds)
                    chartPoints.Add(("", double.NaN));
                chartPoints.Add((sample.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                    sample.Quality == DataQuality.Valid ? sample.Value!.Value : double.NaN));
            }
            var visible = chartPoints.TakeLast(32).SkipWhile(point => point.Label.Length == 0).ToArray();
            cards.Add(new(last.Metric, last.Value?.ToString("0.##") ?? "—", last.Unit, last.Component,
                (last.IsDemo ? "SINTÉTICO · " : "") + last.Source + " · " + last.Timestamp.ToLocalTime().ToString("HH:mm:ss") +
                (last.Quality != DataQuality.Valid ? " · Indisponível: " + last.Reason : ""),
                visible.Select(point => point.Label).ToArray(), visible.Select(point => point.Value).ToArray(), last.IsDemo));
        }
    }

    private void FillProcessRankings(DiagnosticSession session)
    {
        CpuProcesses.Clear(); MemoryProcesses.Clear();
        int processors = session.Inventory?.LogicalProcessors ?? Environment.ProcessorCount;
        string Identity(ProcessAggregate p) => $"{p.Name} · PID {p.Pid} · início {p.StartedAt.ToLocalTime():dd/MM HH:mm:ss}" + (p.Ended ? " · encerrado" : "");
        foreach (var p in session.Processes.Where(x => x.CpuObservedSeconds > 0).OrderByDescending(x => x.CpuSeconds).Take(10))
            CpuProcesses.Add(new(Identity(p), $"CPU acumulada: {p.CpuSeconds:0.###} s · média {p.CpuMeanPercent(processors):0.##}% · pico {p.CpuPeakPercent:0.##}% · observado {p.CpuObservedSeconds:0.#} s"));
        foreach (var p in session.Processes.Where(x => x.MemoryObservedSeconds > 0).OrderByDescending(x => x.WorkingSetMeanBytes).Take(10))
            MemoryProcesses.Add(new(Identity(p), $"Working set · média {p.WorkingSetMeanBytes / (1024 * 1024):0.#} MiB · pico {p.WorkingSetPeakBytes / (1024.0 * 1024):0.#} MiB · observado {p.MemoryObservedSeconds:0.#} s"));
        OnPropertyChanged(nameof(HasProcessRankings));
    }
}
