using System.Buffers.Binary;
using System.Text.Json;

namespace GcmMonitor.Core;

/// <summary>Mensagens JSON com tamanho prefixado e limite antes da alocação.</summary>
public sealed class EngineChannel(Stream stream)
{
    private readonly SemaphoreSlim writeLock = new(1);

    public async Task WriteAsync<T>(T message, CancellationToken ct = default)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(message, EngineProtocol.Json);
        if (data.Length > EngineProtocol.MaxMessageLength) throw new InvalidDataException("Mensagem excede o limite.");
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
        if (size is <= 0 or > EngineProtocol.MaxMessageLength) throw new InvalidDataException("Tamanho da mensagem inválido.");
        var data = new byte[size];
        await stream.ReadExactlyAsync(data, ct);
        return JsonSerializer.Deserialize<T>(data, EngineProtocol.Json) ?? throw new InvalidDataException("Mensagem vazia.");
    }
}
