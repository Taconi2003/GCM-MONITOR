using GcmMonitor.Core;

namespace GcmMonitor.Collector;

/// <summary>Fixtures determinísticas: nunca consulta o hardware ou o Windows.</summary>
public sealed class DemoCollector(ModuleId module) : IDiagnosticCollector
{
    public ModuleId Module => module;

    public ValueTask<ModuleRun> CheckCapabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ModuleRun(module,
            module is ModuleId.Battery or ModuleId.Sensors ? ModuleState.Unavailable : ModuleState.Pending,
            "Fixture sintética v0.1", module switch
            {
                ModuleId.Battery => "Indisponível no cenário demonstrativo: desktop sem bateria.",
                ModuleId.Sensors => "Indisponível no cenário demonstrativo: sensores não expostos. Não equivale a defeito.",
                _ => "Exemplo ilustrativo; este computador não foi consultado."
            }));
    }

    public ValueTask<IReadOnlyList<Measurement>> CollectAsync(DateTimeOffset timestamp, int sample, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new List<Measurement>();
        void Add(string metric, string component, double? value, string unit, string? reason = null) =>
            result.Add(new(module, metric, component, value, unit, timestamp, "Fixture sintética v0.1",
                value is null ? DataQuality.Unavailable : DataQuality.Valid, true, reason));
        switch (module)
        {
            case ModuleId.Cpu: Add("CPU", "CPU demonstrativa", 25 + sample % 5 * 7, "%"); break;
            case ModuleId.Memory: Add("RAM", "RAM demonstrativa", 42 + sample % 4 * 2, "%"); break;
            case ModuleId.Storage:
                Add("Atividade de disco", "demo-disk-0", 8 + sample % 3 * 4, "%");
                Add("Atividade de disco", "demo-disk-1", 3 + sample % 2 * 3, "%");
                break;
            case ModuleId.Sensors:
                Add("Temperatura CPU", "Sensor demonstrativo", null, "°C", "Sensor não exposto no cenário.");
                Add("Ventoinhas", "Sensor demonstrativo", null, "RPM", "Sensor não exposto no cenário.");
                break;
        }
        foreach (var measurement in result) measurement.Validate();
        return ValueTask.FromResult<IReadOnlyList<Measurement>>(result);
    }
}

public static class DemoFixtures
{
    public static DiagnosticSession Archived(SessionState state, DateTimeOffset startedAt)
    {
        if (state is not (SessionState.Completed or SessionState.Cancelled or SessionState.Interrupted or SessionState.Error))
            throw new ArgumentOutOfRangeException(nameof(state));
        var options = new DiagnosticOptions();
        var session = new DiagnosticSession { Options = options, StartedAt = startedAt };
        session = session.TransitionTo(SessionState.Validating, "Cenário demonstrativo", startedAt)
            .TransitionTo(SessionState.Running, "Cenário demonstrativo", startedAt);
        if (state == SessionState.Completed)
            session = session.TransitionTo(SessionState.Finalizing, "Consolidação sintética", startedAt.AddMinutes(30));
        return session.TransitionTo(state, "Estado de exemplo; não corresponde a diagnóstico real.", startedAt.AddMinutes(30)) with
        {
            EffectiveDuration = TimeSpan.FromMinutes(30),
            Modules = ModuleCatalog.All.Select(x => new ModuleRun(x.Id,
                x.Id is ModuleId.Battery or ModuleId.Sensors ? ModuleState.Unavailable :
                state == SessionState.Completed ? ModuleState.Completed : ModuleState.Partial,
                "Fixture sintética v0.1", SummaryFor(x.Id))).ToArray(),
            Findings = FindingsFor(options.Modules),
            Notes = "Sessão de exemplo, criada para demonstrar os estados da interface."
        };
    }

    public static Finding[] FindingsFor(IEnumerable<ModuleId> modules) => modules.Select(x => new Finding(x,
        x is ModuleId.Battery or ModuleId.Sensors ? FindingSeverity.Unavailable :
        x == ModuleId.WindowsEvents ? FindingSeverity.Attention : FindingSeverity.Normal,
        SummaryFor(x),
        x == ModuleId.WindowsEvents ? "Exemplo: Kernel-Power, ID 41, no intervalo sintético; sem evidência de causa." : "Fixture sintética v0.1; sem evidências reais.",
        x is ModuleId.Battery or ModuleId.Sensors ? "Indisponibilidade não comprova falha física." :
        x == ModuleId.WindowsEvents ? "Evento isolado não determina a causa de desligamento." : "Exemplo de apresentação, sem conclusão sobre este computador.",
        "Em diagnóstico real, correlacionar fontes e validar o contexto antes de orientar uma ação.")).ToArray();

    public static string SummaryFor(ModuleId module) => module switch
    {
        ModuleId.Cpu => "Exemplo de utilização da CPU; sem estresse.",
        ModuleId.Memory => "Exemplo de uso de RAM e paginação.",
        ModuleId.Storage => "Dois discos fictícios. SMART não coletado; integridade não avaliada.",
        ModuleId.WindowsEvents => "Ocorrência ilustrativa de desligamento inesperado; causalidade não estabelecida.",
        ModuleId.Battery => "Indisponível: desktop demonstrativo sem bateria.",
        ModuleId.WindowsUpdate => "Estado ilustrativo; sem busca, download ou instalação.",
        ModuleId.Sensors => "Indisponível: temperatura e RPM não expostos no cenário.",
        ModuleId.Processes => "Exemplo: editor-demo.exe, PID fictício 100, CPU acumulada 12 s, RAM média 240 MB/pico 320 MB (working set); sem processos reais.",
        ModuleId.Devices => "Inventário fictício; nenhum driver consultado ou alterado.",
        ModuleId.Wifi => "Adaptador fictício; sinal ilustrativo 82%. Sem ping ou tráfego ativo.",
        ModuleId.Power => "Exemplo: alimentação AC, plano Equilibrado; nenhuma alteração.",
        _ => throw new ArgumentOutOfRangeException(nameof(module))
    };
}
