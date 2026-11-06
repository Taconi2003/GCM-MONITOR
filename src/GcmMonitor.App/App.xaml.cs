using System.Globalization;
using System.Windows;
using GcmMonitor.Core;
using GcmMonitor.Collector;
using GcmMonitor.Persistence;
using GcmMonitor.Presentation;
using Microsoft.Extensions.DependencyInjection;

namespace GcmMonitor.App;

public partial class App : Application
{
    private ServiceProvider? services;
    private Mutex? instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("pt-BR");
        string userScope = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).ToUpperInvariant())));
        instanceMutex = new Mutex(true, "Global\\GcmMonitor." + userScope, out bool created);
        if (!created)
        {
            MessageBox.Show("O GCM Monitor já está aberto nesta sessão do Windows.", "GCM Monitor");
            Shutdown(); return;
        }
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GCM Monitor", "Data", "gcm-monitor.db");
        var collection = new ServiceCollection();
        collection.AddSingleton<ISessionRepository>(new SqliteSessionRepository(data));
        collection.AddSingleton<IInventoryProvider, WindowsInventoryProvider>();
        collection.AddSingleton<IDiagnosticEngineClient>(new SessionEngineClient(Path.Combine(AppContext.BaseDirectory, "engine", "GcmMonitor.Engine.exe"), data));
        collection.AddSingleton<IUserDialogs, UserDialogs>();
        collection.AddSingleton<ShellViewModel>(); collection.AddSingleton<MainWindow>();
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        MainWindow = services.GetRequiredService<MainWindow>(); MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Encerramento assíncrono dos processos acontece em MainWindow antes de fechar a UI.
        if (services is not null) services.DisposeAsync().AsTask().GetAwaiter().GetResult();
        instanceMutex?.Dispose(); base.OnExit(e);
    }
}
