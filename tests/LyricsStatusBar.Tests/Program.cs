using LyricsStatusBar.Core;

var tests = new (string Name, Action Run)[]
{
    ("LRC parses fractions and duplicate timestamps", () =>
    {
        var lines = LrcParser.Parse("[00:01.2][00:02.34]Hello\n[00:03.456]World");
        Equal(3, lines.Count);
        Equal(1_200L, lines[0].TimeMs);
        Equal(2_340L, lines[1].TimeMs);
        Equal(3_456L, lines[2].TimeMs);
    }),
    ("LRC applies offset and ignores metadata", () =>
    {
        var lines = LrcParser.Parse("[ar:Artist]\n[offset:-250]\n[00:01.00]Line");
        Equal(1, lines.Count);
        Equal(750L, lines[0].TimeMs);
    }),
    ("Timeline selects current original and aligned translation", () =>
    {
        var timeline = new LyricTimeline(
            [new LyricLine(1_000, "One"), new LyricLine(2_000, "Two")],
            [new LyricLine(1_080, "Uno"), new LyricLine(2_000, "Dos")]);
        Equal(new DisplayLine("", ""), timeline.At(999));
        Equal(new DisplayLine("One", "Uno"), timeline.At(1_500));
        Equal(new DisplayLine("Two", "Dos"), timeline.At(2_500));
    }),
    ("Protocol parses track and progress", () =>
    {
        var track = BridgeProtocol.Parse(
            "{\"type\":\"track\",\"version\":1,\"track\":{\"id\":\"42\",\"title\":\"Title\",\"artist\":\"Artist\",\"original\":[{\"timeMs\":1000,\"text\":\"Line\"}],\"translation\":[]}}");
        True(track is TrackMessage { Track.Id: "42" });
        var progress = BridgeProtocol.Parse(
            "{\"type\":\"progress\",\"version\":1,\"trackId\":\"42\",\"positionMs\":1234}");
        Equal(1_234L, ((ProgressMessage)progress).PositionMs);
    }),
    ("Protocol rejects incompatible version", () =>
    {
        Throws<NotSupportedException>(() =>
            BridgeProtocol.Parse("{\"type\":\"hello\",\"version\":2,\"pluginVersion\":\"x\",\"clientVersion\":\"y\"}"));
    }),
    ("Settings clamp unsafe values", () =>
    {
        var normalized = new AppSettings
        {
            PrimaryFontSize = 100,
            SecondaryFontSize = 1,
            MaxWidth = 50,
            HideDelayMs = 100,
            LyricAdvanceMs = 10_000,
            PrimaryColor = "bad"
        }.Normalize();
        Equal(28d, normalized.PrimaryFontSize);
        Equal(8d, normalized.SecondaryFontSize);
        Equal(180, normalized.MaxWidth);
        Equal(1_000, normalized.HideDelayMs);
        Equal(3_000, normalized.LyricAdvanceMs);
        Equal("#FFFFFFFF", normalized.PrimaryColor);
    }),
    ("Taskbar placement uses right-side gap", () =>
    {
        var result = TaskbarGapCalculator.Calculate(
            new PixelRect(0, 1104, 2048, 1152),
            occupiedButtonsRight: 627,
            trayLeft: 1738,
            maxWidth: 720,
            horizontalOffset: 0);
        True(result.HasValue);
        Equal(new PixelRect(1006, 1104, 1726, 1152), result!.Value);
    }),
    ("Taskbar placement rejects vertical or cramped areas", () =>
    {
        True(TaskbarGapCalculator.Calculate(new PixelRect(0, 0, 48, 1080), 0, 48, 300, 0) is null);
        True(TaskbarGapCalculator.Calculate(new PixelRect(0, 0, 500, 48), 300, 450, 300, 0) is null);
    })
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine("PASS " + test.Name);
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine("FAIL " + test.Name + ": " + exception.Message);
    }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void True(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Expected condition to be true.");
    }
}

static void Throws<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
