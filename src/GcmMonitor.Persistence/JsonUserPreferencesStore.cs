using System.Text.Json;
using System.Text.Json.Serialization;
using GcmMonitor.Core;

namespace GcmMonitor.Persistence;

public sealed class JsonUserPreferencesStore(string filePath) : IUserPreferencesStore
{
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly string path = Path.GetFullPath(filePath);
    private const int MaximumBytes = 32768;

    public async Task<UserPreferences?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(path)) return null;
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, true);
        if (file.Length > MaximumBytes) throw new InvalidDataException("Arquivo de preferências acima do limite.");
        var preferences = await JsonSerializer.DeserializeAsync<UserPreferences>(file, Json, ct)
            ?? throw new InvalidDataException("Arquivo de preferências vazio.");
        preferences.Validate();
        return preferences;
    }

    public async Task SaveAsync(UserPreferences preferences, CancellationToken ct = default)
    {
        preferences.Validate();
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(preferences, Json);
        if (data.Length > MaximumBytes) throw new InvalidDataException("Preferências acima do limite.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await file.WriteAsync(data, ct);
                await file.FlushAsync(ct);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
