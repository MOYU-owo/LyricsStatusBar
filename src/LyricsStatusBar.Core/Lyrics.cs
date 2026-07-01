using System.Globalization;
using System.Text.RegularExpressions;

namespace LyricsStatusBar.Core;

public sealed record LyricLine(long TimeMs, string Text);

public sealed record TrackData(
    string Id,
    string Title,
    string Artist,
    IReadOnlyList<LyricLine> Original,
    IReadOnlyList<LyricLine> Translation);

public sealed record DisplayLine(string Primary, string Secondary)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Primary) && string.IsNullOrWhiteSpace(Secondary);
}

public static partial class LrcParser
{
    [GeneratedRegex(@"\[(?<minutes>\d{1,3}):(?<seconds>\d{1,2})(?:[\.:](?<fraction>\d{1,3}))?\]", RegexOptions.Compiled)]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"^\[offset:(?<offset>[+-]?\d+)\]$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex OffsetRegex();

    public static IReadOnlyList<LyricLine> Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return [];
        }

        var offset = 0L;
        var parsed = new List<LyricLine>();
        foreach (var raw in lrc.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            var offsetMatch = OffsetRegex().Match(line);
            if (offsetMatch.Success &&
                long.TryParse(offsetMatch.Groups["offset"].Value, CultureInfo.InvariantCulture, out var value))
            {
                offset = value;
                continue;
            }

            var matches = TimestampRegex().Matches(line);
            if (matches.Count == 0)
            {
                continue;
            }

            var textStart = matches[^1].Index + matches[^1].Length;
            var text = line[textStart..].Trim();
            if (text.Length == 0)
            {
                continue;
            }

            foreach (Match match in matches)
            {
                var minutes = int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture);
                var seconds = int.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture);
                var fractionText = match.Groups["fraction"].Value;
                var fraction = fractionText.Length switch
                {
                    0 => 0,
                    1 => int.Parse(fractionText, CultureInfo.InvariantCulture) * 100,
                    2 => int.Parse(fractionText, CultureInfo.InvariantCulture) * 10,
                    _ => int.Parse(fractionText[..3], CultureInfo.InvariantCulture)
                };
                parsed.Add(new LyricLine(Math.Max(0, minutes * 60_000L + seconds * 1_000L + fraction + offset), text));
            }
        }

        return parsed
            .OrderBy(item => item.TimeMs)
            .ThenBy(item => item.Text, StringComparer.Ordinal)
            .DistinctBy(item => (item.TimeMs, item.Text))
            .ToArray();
    }
}

public sealed class LyricTimeline
{
    private const long TranslationToleranceMs = 150;
    private readonly LyricLine[] _original;
    private readonly LyricLine[] _translation;

    public LyricTimeline(IEnumerable<LyricLine> original, IEnumerable<LyricLine>? translation = null)
    {
        _original = original.OrderBy(item => item.TimeMs).ToArray();
        _translation = (translation ?? []).OrderBy(item => item.TimeMs).ToArray();
    }

    public DisplayLine At(long positionMs)
    {
        var current = FindCurrent(_original, positionMs);
        if (current is null)
        {
            return new DisplayLine(string.Empty, string.Empty);
        }

        var translated = FindNearest(_translation, current.TimeMs, TranslationToleranceMs);
        return new DisplayLine(current.Text, translated?.Text ?? string.Empty);
    }

    public static LyricLine? FindCurrent(IReadOnlyList<LyricLine> lines, long positionMs)
    {
        if (lines.Count == 0 || positionMs < lines[0].TimeMs)
        {
            return null;
        }

        var low = 0;
        var high = lines.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            if (lines[mid].TimeMs <= positionMs)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        return lines[Math.Max(0, high)];
    }

    private static LyricLine? FindNearest(IReadOnlyList<LyricLine> lines, long targetMs, long toleranceMs)
    {
        LyricLine? best = null;
        var bestDistance = long.MaxValue;
        foreach (var line in lines)
        {
            var distance = Math.Abs(line.TimeMs - targetMs);
            if (distance < bestDistance)
            {
                best = line;
                bestDistance = distance;
            }
            if (line.TimeMs > targetMs + toleranceMs)
            {
                break;
            }
        }
        return bestDistance <= toleranceMs ? best : null;
    }
}
