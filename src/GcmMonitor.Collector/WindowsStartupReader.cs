using System.Runtime.Versioning;
using System.Security;
using GcmMonitor.Core;
using Microsoft.Win32;

namespace GcmMonitor.Collector;

/// <summary>Consulta pontual de nomes cadastrados para logon, sem ler comandos ou abrir atalhos.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsStartupReader
{
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\";
    private const int MaximumPerSource = 32;

    public static DiagnosticRecord[] Read(DateTimeOffset timestamp, CancellationToken ct)
    {
        var records = new List<DiagnosticRecord>();
        ReadRegistry(RegistryHive.CurrentUser, RegistryView.Default, "usuário atual");
        ReadRegistry(RegistryHive.LocalMachine, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default, "computador");
        if (Environment.Is64BitOperatingSystem) ReadRegistry(RegistryHive.LocalMachine, RegistryView.Registry32, "computador · 32 bits");
        AddSource("pasta do usuário atual", "Pasta Inicializar", () => ShortcutNames(Environment.SpecialFolder.Startup));
        AddSource("pasta comum", "Pasta Inicializar", () => ShortcutNames(Environment.SpecialFolder.CommonStartup));
        return records.ToArray();

        void ReadRegistry(RegistryHive hive, RegistryView view, string scope)
        {
            AddSource(scope + " · Run", "Registro / Run", () => ValueNames(hive, view, "Run"));
            AddSource(scope + " · RunOnce", "Registro / RunOnce", () => ValueNames(hive, view, "RunOnce"));
        }

        void AddSource(string scope, string source, Func<IEnumerable<string>> read)
        {
            ct.ThrowIfCancellationRequested();
            int first = records.Count;
            string key = "startup:" + scope;
            try
            {
                foreach (string name in read().Take(MaximumPerSource + 1))
                {
                    ct.ThrowIfCancellationRequested();
                    int index = records.Count - first;
                    if (index == MaximumPerSource)
                    {
                        AddRecord(key + ":limit", "Inicialização · consulta limitada", "Exibição limitada a 32 cadastros nesta fonte.", DataQuality.Unavailable);
                        break;
                    }
                    string label = name.IndexOfAny(['\\', '/', ':', '=']) < 0 ? DiagnosticValue.Clean(name, 180) : "Nome omitido";
                    AddRecord(key + ":" + index, "Inicialização · " + (string.IsNullOrWhiteSpace(label) ? "Sem nome" : label),
                        "Cadastro encontrado. Estado de habilitação e execução não verificados.", DataQuality.Valid);
                }
                if (records.Count == first)
                    AddRecord(key + ":empty", "Inicialização · nenhum cadastro nesta fonte", "Outras formas de inicialização podem existir.", DataQuality.Valid);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or InvalidOperationException)
            {
                AddRecord(key + ":unavailable", "Inicialização · fonte indisponível", "Consulta de nomes indisponível nesta fonte.", DataQuality.Unavailable);
            }

            void AddRecord(string id, string title, string detail, DataQuality quality) =>
                records.Add(new(ModuleId.WindowsEvents, id, title, $"{scope}. {detail}", timestamp, source, quality));
        }
    }

    private static string[] ValueNames(RegistryHive hive, RegistryView view, string name)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var key = root.OpenSubKey(StartupKey + name, writable: false);
        return key?.GetValueNames() ?? [];
    }

    private static IEnumerable<string> ShortcutNames(Environment.SpecialFolder folder)
    {
        string path = Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidOperationException("Pasta Inicializar local indisponível.");
        return Directory.EnumerateFiles(path, "*.lnk", SearchOption.TopDirectoryOnly).Select(file => Path.GetFileNameWithoutExtension(file));
    }
}
