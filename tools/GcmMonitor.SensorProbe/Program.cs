using System.Diagnostics;
using System.Runtime.Intrinsics.X86;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;
using Microsoft.Win32;

// Prova passiva separada da solução/produto. Nenhum caminho inicia carga ou ajusta parâmetros.
if (args.Length > 0 && args[0] == "--bridge") return await CpuSensorBridge.RunAsync(args);
if (args.Length is < 2 or > 3 || args[0] != "--output" || (args.Length == 3 && args[2] != "--package-status")) return 2;
bool packageStatusRequested = args.Length == 3;
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var frames = new List<ProbeFrame>();
var clock = Stopwatch.StartNew();
var startedAt = DateTimeOffset.UtcNow;
bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
using var installedDriver = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
string? driverVersion = installedDriver?.GetValue("DisplayVersion")?.ToString();
string? errorType = null;
IntelMsr? packageRegister = null;
string? packageStatusUnavailable = null;
var computer = new Computer
{
    IsCpuEnabled = true, IsGpuEnabled = false, IsMotherboardEnabled = false, IsMemoryEnabled = false,
    IsStorageEnabled = false, IsNetworkEnabled = false, IsControllerEnabled = false,
    IsPsuEnabled = false, IsBatteryEnabled = false
};
try
{
    computer.Open();
    var cpus = computer.Hardware.Where(x => x.HardwareType == HardwareType.Cpu).ToArray();
    if (packageStatusRequested)
    {
        packageStatusUnavailable = !elevated ? "Sessão sem privilégios para a leitura do registrador." :
            driverVersion != "2.2.0.0" ? "Versão do driver diferente da revisada." :
            cpus.Length != 1 || !cpus[0].Identifier.ToString().StartsWith("/intelcpu/", StringComparison.Ordinal) ||
                !X86Base.IsSupported || X86Base.CpuId(0, 0).Eax < 6 || (X86Base.CpuId(6, 0).Eax & (1 << 6)) == 0
                ? "A prova exige um único pacote Intel com gerenciamento térmico anunciado por CPUID."
                : null;
        if (packageStatusUnavailable is null) packageRegister = new IntelMsr();
    }
    for (int index = 0; index < 30; index++)
    {
        var readStarted = DateTimeOffset.UtcNow;
        double readStartedMilliseconds = clock.Elapsed.TotalMilliseconds;
        var readings = new List<ProbeSensor>();
        foreach (var cpu in cpus)
        {
            cpu.Update();
            foreach (var sensor in cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature))
            {
                string role = sensor.Name.Contains("Distance to TjMax", StringComparison.OrdinalIgnoreCase) ? "ThermalHeadroom" :
                    sensor.Name is "Core Max" or "Core Average" ? "Aggregate" :
                    sensor.Name == "CPU Package" ? "Package" :
                    Regex.IsMatch(sensor.Name, @"\A(?:P-|E-)?Core #[1-9][0-9]*\z", RegexOptions.CultureInvariant) ? "Core" : "Unreviewed";
                var parameters = sensor.Parameters.Select(p => new ProbeParameter(p.Name, float.IsFinite(p.Value) ? p.Value : null)).ToArray();
                float? value = sensor.Value is { } raw && float.IsFinite(raw) ? raw : null;
                // Metadados plausíveis apenas identificam candidatos. Não validam sensor/política de proteção.
                bool candidate = elevated && driverVersion == "2.2.0.0" &&
                    cpu.Identifier.ToString().StartsWith("/intelcpu/", StringComparison.Ordinal) &&
                    role is "Core" or "Package" && value.HasValue &&
                    parameters.Any(p => p.Name == "TjMax [°C]" && p.Value is > 0) &&
                    parameters.Any(p => p.Name == "TSlope [°C]" && p.Value == 1);
                readings.Add(new(cpu.Identifier.ToString(), sensor.Identifier.ToString(), sensor.Name, role,
                    value, role == "ThermalHeadroom" ? "Δ°C" : "°C", parameters, candidate));
            }
        }
        var receivedAt = DateTimeOffset.UtcNow;
        double completedMilliseconds = clock.Elapsed.TotalMilliseconds;
        ProbePackageStatus? packageStatus = null;
        if (packageRegister is not null)
        {
            const uint packageThermalStatus = 0x1B1;
            var queryStartedAt = DateTimeOffset.UtcNow;
            bool valid = packageRegister.ReadMsr(packageThermalStatus, out ulong status) && (status & (1UL << 31)) != 0;
            packageStatus = new(queryStartedAt, DateTimeOffset.UtcNow, valid ? (status & 1UL) != 0 : null);
        }
        frames.Add(new(index + 1, readStarted, receivedAt, readStartedMilliseconds,
            completedMilliseconds, readings.ToArray(), packageStatus));
        if (index < 29) await Task.Delay(TimeSpan.FromSeconds(2));
    }
}
catch (Exception error) { errorType = error.GetType().Name; }
finally
{
    try { packageRegister?.Close(); }
    catch (Exception error) { errorType ??= error.GetType().Name; }
    try { computer.Close(); }
    catch (Exception error) { errorType ??= error.GetType().Name; }
}

int validPackageStatus = frames.Count(f => f.PackageStatus?.ThermalProtectionActive is not null);
if (packageRegister is not null && validPackageStatus == 0)
    packageStatusUnavailable = "Nenhuma leitura válida do registrador de pacote.";
var json = new JsonSerializerOptions { WriteIndented = true };
async Task SaveAsync(string name, object value)
{
    var target = Path.Combine(output, name);
    var temporary = target + ".tmp";
    await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, json));
    File.Move(temporary, target, overwrite: true);
}
await SaveAsync("private-capture.json", new
{
    SchemaVersion = 1, IsPrivate = true, Elevated = elevated, StartedAt = startedAt,
    CompletedAt = DateTimeOffset.UtcNow, Source = "LibreHardwareMonitorLib 0.9.6 / CPU only",
    LibraryVersion = typeof(Computer).Assembly.GetName().Version?.ToString(), PawnIoInstalledVersion = driverVersion,
    TimestampMeaning = "Host receipt after Update; not firmware freshness or independent reference",
    StressExecuted = false, SafetyQualified = false, PackageStatusRequested = packageStatusRequested, ErrorType = errorType, Frames = frames
});
await SaveAsync("summary.json", new
{
    SchemaVersion = 1, Elevated = elevated, StressExecuted = false, SafetyQualified = false,
    Frames = frames.Count, Completed = frames.Count == 30 && errorType is null,
    NumericReadings = frames.Sum(f => f.Sensors.Count(s => s.Value.HasValue)),
    CandidateReadings = frames.Sum(f => f.Sensors.Count(s => s.CandidateForFurtherValidation)),
    MinimumCandidatesPerFrame = frames.Count == 0 ? 0 : frames.Min(f => f.Sensors.Count(s => s.CandidateForFurtherValidation)),
    AggregateObservations = frames.Sum(f => f.Sensors.Count(s => s.Role == "Aggregate")),
    HeadroomObservations = frames.Sum(f => f.Sensors.Count(s => s.Role == "ThermalHeadroom")),
    PackageStatusRequested = packageStatusRequested,
    PackageStatusAttempts = frames.Count(f => f.PackageStatus is not null),
    PackageStatusValidReadings = validPackageStatus,
    PackageStatusActiveReadings = frames.Count(f => f.PackageStatus?.ThermalProtectionActive == true),
    PackageStatusUnavailable = packageStatusUnavailable,
    MaximumReadMilliseconds = frames.Count == 0 ? 0 : frames.Max(f => f.ReadCompletedMilliseconds - f.ReadStartedMilliseconds),
    MaximumStartGapMilliseconds = frames.Zip(frames.Skip(1), (a, b) => b.ReadStartedMilliseconds - a.ReadStartedMilliseconds).DefaultIfEmpty(0).Max(),
    ErrorType = errorType
});
return errorType is null && frames.Count == 30 ? 0 : 3;

sealed record ProbeParameter(string Name, float? Value);
sealed record ProbeSensor(string HardwareId, string SensorId, string Name, string Role, float? Value,
    string Unit, ProbeParameter[] Parameters, bool CandidateForFurtherValidation);
sealed record ProbePackageStatus(DateTimeOffset QueryStartedAt, DateTimeOffset QueryCompletedAt, bool? ThermalProtectionActive);
sealed record ProbeFrame(int Sequence, DateTimeOffset ReadStartedAt, DateTimeOffset ReceivedAt,
    double ReadStartedMilliseconds, double ReadCompletedMilliseconds, ProbeSensor[] Sensors, ProbePackageStatus? PackageStatus);
