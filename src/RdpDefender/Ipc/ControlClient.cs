using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using RdpDefender.Infrastructure;

namespace RdpDefender.Ipc;

public sealed class ControlClient
{
    private readonly string _pipeName;

    public ControlClient(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async Task<ControlResponse> SendAsync(
        ControlRequest request,
        int timeoutMs = 5000,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        using var pipe = new NamedPipeClientStream(
            ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Служба RDP Defender не отвечает. Проверьте, что она запущена.");
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("Служба RDP Defender не отвечает. Проверьте, что она запущена.");
        }
        catch (IOException ex)
        {
            throw new IOException("Не удалось подключиться к службе RDP Defender: " + ex.Message, ex);
        }

        using var reader = new StreamReader(
            pipe, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, 4096, leaveOpen: true);
        using var writer = new StreamWriter(
            pipe, new UTF8Encoding(false), 4096, leaveOpen: true) { AutoFlush = true };

        var json = JsonSerializer.Serialize(request, JsonDefaults.Compact);
        await writer.WriteLineAsync(json.AsMemory(), timeout.Token).ConfigureAwait(false);

        string? line;
        try
        {
            line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Служба RDP Defender не ответила вовремя.");
        }

        if (string.IsNullOrWhiteSpace(line))
            throw new IOException("Служба RDP Defender закрыла канал без ответа.");

        return JsonSerializer.Deserialize<ControlResponse>(line, JsonDefaults.Options)
               ?? throw new IOException("Некорректный ответ службы RDP Defender.");
    }
}
