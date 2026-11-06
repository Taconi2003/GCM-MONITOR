using System.Globalization;
using System.Windows;
using GcmMonitor.Core;
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
        instanceMutex = new Mutex(true, "Local\\GcmMonitor.Demo.v0.1", out bool created);
        if (!created)
        {
            MessageBox.Show("A demonstração do GCM Monitor já está aberta nesta sessão do Windows.", "GCM Monitor");
            Shutdown(); return;
        }
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GCM Monitor", "Data", "demo-v0.1.db");
        var collection = new ServiceCollection();
        collection.AddSingleton<ISessionRepository>(new SqliteSessionRepository(data));
        collection.AddSingleton<IDiagnosticEngineClient>(new DemoEngineClient(Path.Combine(AppContext.BaseDirectory, "engine", "GcmMonitor.Engine.exe"), data));
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
