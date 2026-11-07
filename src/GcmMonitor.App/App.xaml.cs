using System.Globalization;
using System.Windows;
using GcmMonitor.Core;
using GcmMonitor.Collector;
using GcmMonitor.Persistence;
using GcmMonitor.Presentation;
using GcmMonitor.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace GcmMonitor.App;

public partial class App : Application
{
    private ServiceProvider? services;
    private Mutex? instanceMutex;
    private Mutex? installationUse;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
#if GCM_LAB_UI
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "profile"));
        bool thermalDemo = e.Args.Contains("--thermal-rehearsal", StringComparer.Ordinal);
        if (thermalDemo) root = Path.Combine(root, "DemoTermica");
        instanceMutex = new Mutex(true, "Local\\GcmMonitor.Ui.Laboratorio", out bool created);
#else
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string userScope = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            localAppData.ToUpperInvariant())));
        instanceMutex = new Mutex(true, "Global\\GcmMonitor." + userScope, out bool created);
        string root = Path.Combine(localAppData, "GCM Monitor");
#endif
        if (!created)
        {
            MessageBox.Show("O GCM Monitor já está aberto nesta sessão do Windows.", "GCM Monitor");
            Shutdown(); return;
        }
#if GCM_LAB_UI
        installationUse = new Mutex(false, "Local\\GcmMonitor.InUse.Laboratorio");
#else
        installationUse = InstallationUse.OpenMarker();
#endif
        var data = Path.Combine(root, "Data", "gcm-monitor.db");
        var collection = new ServiceCollection();
        collection.AddSingleton<ISessionRepository>(new SqliteSessionRepository(data));
        collection.AddSingleton<IUserPreferencesStore>(new JsonUserPreferencesStore(Path.Combine(root, "Settings", "preferences.json")));
        collection.AddSingleton<IPdfReportExporter, PdfReportExporter>();
#if GCM_LAB_UI
        if (thermalDemo)
            collection.AddSingleton<IDiagnosticEngineClient>(new SessionEngineClient(
                Path.Combine(AppContext.BaseDirectory, "engine", "GcmMonitor.Engine.exe"), data, CollectionMode.Demonstration, rehearseThermal: true));
        else
#endif
        {
            collection.AddSingleton<IInventoryProvider, WindowsInventoryProvider>();
            collection.AddSingleton<IDiagnosticEngineClient>(sp => new SessionEngineClient(Path.Combine(AppContext.BaseDirectory, "engine", "GcmMonitor.Engine.exe"), data,
                authorizeCpuTemperature: () => sp.GetRequiredService<IUserDialogs>().Confirm("Temperaturas da CPU",
                    "Permitir uma leitura passiva dos sensores da CPU por um auxiliar temporário com permissão de administrador?\n\n" +
                    "A confirmação do Windows será exibida. Nenhum estresse será executado e nenhuma configuração será alterada.\n\n" +
                    "Escolhendo Não, o diagnóstico continua com as fontes já disponíveis.")));
        }
        collection.AddSingleton<IUserDialogs, UserDialogs>();
        collection.AddSingleton<ShellViewModel>(); collection.AddSingleton<MainWindow>();
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
#if GCM_LAB_UI
        if (thermalDemo)
        {
            var vm = services.GetRequiredService<ShellViewModel>();
            vm.MonitoringDuration = vm.Durations[0];
            foreach (var module in vm.Modules)
                module.IsSelected = module.Definition.Id is ModuleId.Cpu or ModuleId.Sensors;
        }
#endif
        MainWindow = services.GetRequiredService<MainWindow>();
#if GCM_LAB_UI
        MainWindow.Title = "GCM Monitor · Laboratório";
#endif
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Encerramento assíncrono dos processos acontece em MainWindow antes de fechar a UI.
        if (services is not null) services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        installationUse?.Dispose();
        instanceMutex?.Dispose(); base.OnExit(e);
    }
}
