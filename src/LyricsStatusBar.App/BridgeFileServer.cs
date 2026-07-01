using System.IO;
using System.Text;
using System.Text.Json;
using LyricsStatusBar.Core;

namespace LyricsStatusBar.App;

internal sealed class BridgeFileServer : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan ConnectedTtl = TimeSpan.FromSeconds(5);
    private static readonly string[] MessageFiles = ["hello.json", "track.json", "clear.json", "progress.json", "latest.json"];
    private readonly CancellationTokenSource _cancellation = new();
    private readonly string[] _directories;
    private readonly Dictionary<string, string> _lastSignatures = new(StringComparer.OrdinalIgnoreCase);
    private Task? _worker;

    public BridgeFileServer()
    {
        _directories = BuildCandidateDirectories().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public event Action<BridgeMessage>? MessageReceived;
    public event Action? StatusChanged;
    public bool IsConnected { get; private set; }
    public string LastError { get; private set; } = string.Empty;
    public DateTimeOffset? LastMessageAt { get; private set; }
    public string? ActivePath { get; private set; }

    public void Start() => _worker ??= Task.Run(() => RunAsync(_cancellation.Token));

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                PollOnce();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidDataException)
            {
                LastError = exception.Message;
            }

            var connected = LastMessageAt is not null && DateTimeOffset.UtcNow - LastMessageAt.Value <= ConnectedTtl;
            if (connected != IsConnected)
            {
                IsConnected = connected;
                StatusChanged?.Invoke();
            }

            try
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void PollOnce()
    {
        var changed = new List<CandidateMessage>();
        foreach (var directory in _directories)
        {
            for (var index = 0; index < MessageFiles.Length; index++)
            {
                var path = Path.Combine(directory, MessageFiles[index]);
                var file = new FileInfo(path);
                if (!file.Exists)
                {
                    continue;
                }

                var signature = $"{file.FullName}|{file.LastWriteTimeUtc.Ticks}|{file.Length}";
                if (_lastSignatures.TryGetValue(file.FullName, out var previous) && previous == signature)
                {
                    continue;
                }
                changed.Add(new CandidateMessage(file.FullName, file.LastWriteTimeUtc, file.Length, index, signature));
            }
        }

        foreach (var candidate in changed.OrderBy(item => item.LastWriteUtc).ThenBy(item => item.Priority))
        {
            var json = ReadAllTextShared(candidate.Path).Trim();
            if (json.Length == 0)
            {
                _lastSignatures[candidate.Path] = candidate.Signature;
                continue;
            }
            if (Encoding.UTF8.GetByteCount(json) > BridgeProtocol.MaxMessageBytes)
            {
                throw new InvalidDataException("File bridge message exceeded the size limit.");
            }

            var message = BridgeProtocol.Parse(json);
            _lastSignatures[candidate.Path] = candidate.Signature;
            ActivePath = candidate.Path;
            LastError = string.Empty;
            LastMessageAt = DateTimeOffset.UtcNow;
            MessageReceived?.Invoke(message);
        }
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static IEnumerable<string> BuildCandidateDirectories()
    {
        foreach (var dataDirectory in BetterNcmPluginDeployment.CandidateDataDirectories())
        {
            yield return Path.Combine(dataDirectory, "LyricsStatusBarBridge");
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

    private sealed record CandidateMessage(string Path, DateTime LastWriteUtc, long Length, int Priority, string Signature);
}