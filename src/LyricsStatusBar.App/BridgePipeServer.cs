using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using LyricsStatusBar.Core;

namespace LyricsStatusBar.App;

internal sealed class BridgePipeServer : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _worker;

    public event Action<BridgeMessage>? MessageReceived;
    public event Action? StatusChanged;
    public bool IsConnected { get; private set; }
    public string LastError { get; private set; } = string.Empty;
    public DateTimeOffset? LastMessageAt { get; private set; }

    public void Start() => _worker ??= Task.Run(() => RunAsync(_cancellation.Token));

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    BridgeProtocol.PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                IsConnected = true;
                LastError = string.Empty;
                StatusChanged?.Invoke();
                using var reader = new StreamReader(
                    pipe,
                    new UTF8Encoding(false, true),
                    detectEncodingFromByteOrderMarks: false,
                    bufferSize: 4096,
                    leaveOpen: true);
                while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null)
                    {
                        break;
                    }
                    if (Encoding.UTF8.GetByteCount(line) > BridgeProtocol.MaxMessageBytes)
                    {
                        throw new InvalidDataException("Bridge message exceeded the size limit.");
                    }
                    var message = BridgeProtocol.Parse(line);
                    LastMessageAt = DateTimeOffset.Now;
                    MessageReceived?.Invoke(message);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or NotSupportedException or InvalidDataException)
            {
                LastError = exception.Message;
            }
            finally
            {
                IsConnected = false;
                StatusChanged?.Invoke();
            }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }
        _cancellation.Dispose();
    }
}
