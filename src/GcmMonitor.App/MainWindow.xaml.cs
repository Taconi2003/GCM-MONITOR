using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using GcmMonitor.Core;
using GcmMonitor.Presentation;

namespace GcmMonitor.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel viewModel;
    private readonly IDiagnosticEngineClient engine;
    private bool closeAllowed;

    public MainWindow(ShellViewModel viewModel, IDiagnosticEngineClient engine)
    {
        InitializeComponent(); DataContext = this.viewModel = viewModel; this.engine = engine;
        Width = Math.Min(Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width - 32));
        Height = Math.Min(Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height - 32));
        Loaded += async (_, _) => await viewModel.InitializeAsync();
        SourceInitialized += (_, _) => { int dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int)); };
        Closing += OnClosing;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closeAllowed) return;
        e.Cancel = true;
        if (viewModel.IsActive && MessageBox.Show(this, "Encerrar a coleta passiva e preservar os dados parciais?", "Fechar GCM Monitor", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        IsEnabled = false;
        await engine.DisposeAsync();
        await viewModel.WaitForReportAsync();
        closeAllowed = true;
        // Close não pode ser chamado dentro do próprio evento Closing se a tarefa completar sincronicamente.
        _ = Dispatcher.BeginInvoke(new Action(Close));
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
