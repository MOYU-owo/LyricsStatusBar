using System.Text.Json;
using System.Text.Json.Serialization;

namespace LyricsStatusBar.Core;

public enum TextAlignmentOption
{
    Left,
    Center,
    Right
}

public sealed record AppSettings
{
    public bool Enabled { get; init; } = true;
    public bool AutoStart { get; init; } = true;
    public string FontFamily { get; init; } = "Microsoft YaHei UI";
    public double PrimaryFontSize { get; init; } = 13;
    public double SecondaryFontSize { get; init; } = 11;
    public string PrimaryColor { get; init; } = "#FFFFFFFF";
    public string SecondaryColor { get; init; } = "#D9FFFFFF";
    public string ShadowColor { get; init; } = "#CC000000";

    [JsonConverter(typeof(JsonStringEnumConverter<TextAlignmentOption>))]
    public TextAlignmentOption Alignment { get; init; } = TextAlignmentOption.Center;

    public int MaxWidth { get; init; } = 720;
    public int HorizontalOffset { get; init; }
    public int LyricAdvanceMs { get; init; } = 500;
    public int HideDelayMs { get; init; } = 3_000;

    public AppSettings Normalize() => this with
    {
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "Microsoft YaHei UI" : FontFamily.Trim(),
        PrimaryFontSize = Math.Clamp(PrimaryFontSize, 8, 28),
        SecondaryFontSize = Math.Clamp(SecondaryFontSize, 8, 24),
        PrimaryColor = NormalizeColor(PrimaryColor, "#FFFFFFFF"),
        SecondaryColor = NormalizeColor(SecondaryColor, "#D9FFFFFF"),
        ShadowColor = NormalizeColor(ShadowColor, "#CC000000"),
        MaxWidth = Math.Clamp(MaxWidth, 180, 1_600),
        HorizontalOffset = Math.Clamp(HorizontalOffset, -1_000, 1_000),
        LyricAdvanceMs = Math.Clamp(LyricAdvanceMs, -3_000, 3_000),
        HideDelayMs = Math.Clamp(HideDelayMs, 1_000, 30_000)
    };

    private static string NormalizeColor(string value, string fallback)
    {
        var color = value.Trim();
        if (color.Length is 7 or 9 && color[0] == '#' && color[1..].All(Uri.IsHexDigit))
        {
            return color.ToUpperInvariant();
        }
        return fallback;
    }
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SettingsStore(string? rootDirectory = null)
    {
        RootDirectory = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LyricsStatusBar");
        FilePath = Path.Combine(RootDirectory, "settings.json");
    }

    public string RootDirectory { get; }
    public string FilePath { get; }

    public AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            return (JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings()).Normalize();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            Directory.CreateDirectory(RootDirectory);
            var backup = Path.Combine(RootDirectory, $"settings.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json");
            try
            {
                File.Move(FilePath, backup, overwrite: false);
            }
            catch (IOException)
            {
                // Safe defaults are preferable to blocking application startup.
            }
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(RootDirectory);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings.Normalize(), Options));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
