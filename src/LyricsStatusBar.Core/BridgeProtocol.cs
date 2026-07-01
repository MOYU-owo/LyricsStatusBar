using System.Text.Json;

namespace LyricsStatusBar.Core;

public abstract record BridgeMessage(string Type, int Version);
public sealed record HelloMessage(int Version, string PluginVersion, string ClientVersion)
    : BridgeMessage("hello", Version);
public sealed record TrackMessage(int Version, TrackData Track)
    : BridgeMessage("track", Version);
public sealed record ProgressMessage(int Version, string TrackId, long PositionMs)
    : BridgeMessage("progress", Version);
public sealed record ClearMessage(int Version, string Reason)
    : BridgeMessage("clear", Version);

public static class BridgeProtocol
{
    public const int CurrentVersion = 1;
    public const string PipeName = "LyricsStatusBar.Bridge.v1";
    public const int MaxMessageBytes = 2 * 1024 * 1024;

    public static BridgeMessage Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var type = root.GetProperty("type").GetString() ?? throw new JsonException("Missing message type.");
        var version = root.GetProperty("version").GetInt32();
        if (version != CurrentVersion)
        {
            throw new NotSupportedException($"Unsupported bridge protocol version: {version}.");
        }

        return type switch
        {
            "hello" => new HelloMessage(version, ReadString(root, "pluginVersion"), ReadString(root, "clientVersion")),
            "track" => new TrackMessage(version, ParseTrack(root.GetProperty("track"))),
            "progress" => new ProgressMessage(
                version,
                ReadString(root, "trackId"),
                Math.Max(0, root.GetProperty("positionMs").GetInt64())),
            "clear" => new ClearMessage(version, ReadString(root, "reason")),
            _ => throw new JsonException($"Unknown bridge message type: {type}.")
        };
    }

    private static TrackData ParseTrack(JsonElement element)
    {
        var id = ReadString(element, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new JsonException("Track id cannot be empty.");
        }
        return new TrackData(
            id,
            ReadString(element, "title"),
            ReadString(element, "artist"),
            ParseLines(element, "original"),
            ParseLines(element, "translation"));
    }

    private static IReadOnlyList<LyricLine> ParseLines(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<LyricLine>();
        foreach (var item in array.EnumerateArray())
        {
            var text = ReadString(item, "text").Trim();
            if (text.Length > 0)
            {
                result.Add(new LyricLine(Math.Max(0, item.GetProperty("timeMs").GetInt64()), text));
            }
        }
        return result.OrderBy(item => item.TimeMs).DistinctBy(item => (item.TimeMs, item.Text)).ToArray();
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
