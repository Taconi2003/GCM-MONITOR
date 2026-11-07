using System.Buffers.Binary;
using System.Text.Json;

namespace GcmMonitor.Core;

/// <summary>Mensagens JSON com tamanho prefixado e limite antes da alocação.</summary>
public sealed class EngineChannel(Stream stream, int maximumMessageLength = EngineProtocol.MaxMessageLength, JsonSerializerOptions? options = null)
{
    private readonly SemaphoreSlim writeLock = new(1);
    private readonly int limit = maximumMessageLength is >= 64 and <= EngineProtocol.MaxMessageLength ? maximumMessageLength : throw new ArgumentOutOfRangeException(nameof(maximumMessageLength));
    private readonly JsonSerializerOptions json = options ?? EngineProtocol.Json;

    public async Task WriteAsync<T>(T message, CancellationToken ct = default)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(message, json);
        if (data.Length > limit) throw new InvalidDataException("Mensagem excede o limite.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
        await writeLock.WaitAsync(ct);
        try
        {
            await stream.WriteAsync(header, ct);
            await stream.WriteAsync(data, ct);
            await stream.FlushAsync(ct);
        }
        finally { writeLock.Release(); }
    }

    public async Task<T> ReadAsync<T>(CancellationToken ct = default)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, ct);
        var size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size <= 0 || size > limit) throw new InvalidDataException("Tamanho da mensagem inválido.");
        var data = new byte[size];
        await stream.ReadExactlyAsync(data, ct);
        return JsonSerializer.Deserialize<T>(data, json) ?? throw new InvalidDataException("Mensagem vazia.");
    }
}
