namespace LyricsStatusBar.Core;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);
    public bool IsHorizontal => Width > Height;
}

public static class TaskbarGapCalculator
{
    public static PixelRect? Calculate(
        PixelRect taskbar,
        int occupiedButtonsRight,
        int trayLeft,
        int maxWidth,
        int horizontalOffset,
        int margin = 12,
        int minimumWidth = 180)
    {
        if (!taskbar.IsHorizontal || taskbar.Height <= 3)
        {
            return null;
        }

        var gapLeft = Math.Clamp(occupiedButtonsRight + margin, taskbar.Left + margin, taskbar.Right - margin);
        var gapRight = Math.Clamp(trayLeft - margin, taskbar.Left + margin, taskbar.Right - margin);
        if (gapRight - gapLeft < minimumWidth)
        {
            return null;
        }

        var width = Math.Min(Math.Max(minimumWidth, maxWidth), gapRight - gapLeft);
        var idealLeft = gapRight - width + horizontalOffset;
        var left = Math.Clamp(idealLeft, gapLeft, gapRight - width);
        return new PixelRect(left, taskbar.Top, left + width, taskbar.Bottom);
    }
}
