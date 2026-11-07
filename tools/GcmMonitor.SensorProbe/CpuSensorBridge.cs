using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using GcmMonitor.Core;
using GcmMonitor.Execution;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

internal static class CpuSensorBridge
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 6 || !args[1].StartsWith("gcm-thermal-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(args[1][12..], "N", out _) || !Guid.TryParse(args[2], out var session) || session == Guid.Empty ||
            args[3].Length != 64 || !args[3].All(Uri.IsHexDigit) ||
            !int.TryParse(args[4], NumberStyles.None, CultureInfo.InvariantCulture, out int parentPid) || parentPid <= 0 ||
            !long.TryParse(args[5], NumberStyles.None, CultureInfo.InvariantCulture, out long parentTicks) ||
            parentTicks < DateTime.MinValue.Ticks || parentTicks > DateTime.MaxValue.Ticks) return 2;
        using var lifetime = new CancellationTokenSource(TimeSpan.FromHours(4.1));
        using var pipe = ThermalPipe.CreateClient(args[1]);
        try
        {
            await pipe.ConnectAsync(8000, lifetime.Token);
            ThermalPipe.ValidateServer(pipe, parentPid, new(new DateTime(parentTicks, DateTimeKind.Utc)));
            var channel = new EngineChannel(pipe, CpuThermalProtocol.MaximumBytes, CpuThermalProtocol.Json);
            bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            string driverVersion = VerifyReviewedFiles();
            await channel.WriteAsync(new CpuThermalHello(CpuThermalProtocol.Version, session, args[3],
                typeof(Computer).Assembly.GetName().Version!.ToString(), driverVersion, elevated), lifetime.Token);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                timeout.CancelAfter(CpuThermalProtocol.HeartbeatTimeout);
                var start = await channel.ReadAsync<CpuThermalControl>(timeout.Token);
                CpuThermalProtocol.ValidateIdentity(start.Version, start.SessionId, session, start.Nonce, args[3]);
                if (start.Sequence != 1 || start.Command != "start" || !elevated || driverVersion != "2.2.0.0") return 3;
            }
            // Somente depois da autenticação/versões/permissão começa o acesso ao hardware.
            var reading = Task.Run(() => ReadLoopAsync(channel, session, lifetime.Token));
            var controls = WatchAsync(channel, session, args[3], lifetime.Token);
            await Task.WhenAny(reading, controls);
            lifetime.Cancel();
            // Um driver bloqueado não mantém o auxiliar elevado vivo depois de perder o controlador.
            if (await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(2))) != reading) Environment.Exit(4);
            await Task.WhenAll(reading, controls);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException or
            System.ComponentModel.Win32Exception or System.Text.Json.JsonException or InvalidOperationException or System.Security.SecurityException)
        { return 3; }
        finally { lifetime.Cancel(); }
    }

    private static async Task WatchAsync(EngineChannel channel, Guid session, string nonce, CancellationToken ct)
    {
        long sequence = 1;
        while (!ct.IsCancellationRequested)
        {
            var elapsed = Stopwatch.StartNew();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CpuThermalProtocol.HeartbeatTimeout);
            var command = await channel.ReadAsync<CpuThermalControl>(timeout.Token);
            if (elapsed.Elapsed >= CpuThermalProtocol.HeartbeatTimeout) throw new InvalidDataException("Heartbeat térmico atrasado.");
            CpuThermalProtocol.ValidateIdentity(command.Version, command.SessionId, session, command.Nonce, nonce);
            if (command.Sequence != ++sequence || command.Command is not ("heartbeat" or "stop"))
                throw new InvalidDataException("Operação térmica não permitida.");
            if (command.Command == "stop") return;
        }
    }

    private static async Task ReadLoopAsync(EngineChannel channel, Guid session, CancellationToken ct)
    {
        var computer = new Computer
        {
            IsCpuEnabled = true, IsGpuEnabled = false, IsMotherboardEnabled = false, IsMemoryEnabled = false,
            IsStorageEnabled = false, IsNetworkEnabled = false, IsControllerEnabled = false,
            IsPsuEnabled = false, IsBatteryEnabled = false
        };
        var clock = Stopwatch.StartNew();
        try
        {
            computer.Open();
            long sequence = 0;
            while (!ct.IsCancellationRequested)
            {
                double started = clock.Elapsed.TotalMilliseconds;
                var sensors = new List<CpuThermalSensor>();
                foreach (var cpu in computer.Hardware.Where(x => x.HardwareType == HardwareType.Cpu &&
                    x.Identifier.ToString().StartsWith("/intelcpu/", StringComparison.Ordinal)))
                {
                    cpu.Update();
                    foreach (var sensor in cpu.Sensors.Where(s => s.SensorType == SensorType.Temperature))
                    {
                        if (CpuThermalProtocol.ClassifyIntel(sensor.Name) is not { } role) continue;
                        double? tj = sensor.Parameters.SingleOrDefault(p => p.Name == "TjMax [°C]")?.Value;
                        if (tj is not > 0 || !double.IsFinite(tj.Value)) tj = null;
                        bool slopeValid = sensor.Parameters.SingleOrDefault(p => p.Name == "TSlope [°C]")?.Value == 1;
                        double? value = sensor.Value;
                        if (value is null || !double.IsFinite(value.Value) || tj is null || value > tj || !slopeValid) value = null;
                        sensors.Add(new(cpu.Identifier.ToString(), sensor.Identifier.ToString(), sensor.Name, role, value, tj));
                        if (sensors.Count > CpuThermalProtocol.MaximumSensors) throw new InvalidDataException("Limite de sensores da fonte direta excedido.");
                    }
                }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await channel.WriteAsync(new CpuThermalFrame(CpuThermalProtocol.Version, session, ++sequence,
                    started, clock.Elapsed.TotalMilliseconds, sensors.Count == 0 ? CpuThermalStatus.UnsupportedHardware : CpuThermalStatus.Reading,
                    sensors.ToArray()), timeout.Token);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        finally { computer.Close(); }
    }

    private static string VerifyReviewedFiles()
    {
        static bool Matches(string path, string expected)
        { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)) == expected; }
        if (!Matches(Path.Combine(AppContext.BaseDirectory, "LibreHardwareMonitorLib.dll"),
            "984B539048880D37257A5EDEAB36378152C54943069F400D68A34565292D35DA")) throw new InvalidDataException("Biblioteca não revisada.");
        using var install = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
        using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
        string driverPath = (service?.GetValue("ImagePath")?.ToString() ?? "").Replace(@"\SystemRoot", Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase);
        if (install?.GetValue("DisplayVersion")?.ToString() != "2.2.0.0" || !Matches(driverPath,
            "FCA6E7D58B0CF38DBB913A2B9E532F48629145D395F454B16A9F58E97B8D3940")) throw new InvalidDataException("Driver não revisado.");
        return "2.2.0.0";
    }
}
