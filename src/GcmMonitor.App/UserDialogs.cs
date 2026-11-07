using System.Windows;
using GcmMonitor.Presentation;
using Microsoft.Win32;

namespace GcmMonitor.App;

public sealed class UserDialogs : IUserDialogs
{
    public bool Confirm(string title, string message) => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    public string? ChooseReportFolder(string current)
    {
        var dialog = new OpenFolderDialog { Title = "Pasta padrão dos relatórios PDF", InitialDirectory = Directory.Exists(current) ? current : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
    public string? ChoosePdfPath(string folder, string fileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exportar relatório PDF", Filter = "Documento PDF (*.pdf)|*.pdf", DefaultExt = ".pdf",
            FileName = fileName, AddExtension = true, CheckPathExists = true, OverwritePrompt = true,
            InitialDirectory = Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
