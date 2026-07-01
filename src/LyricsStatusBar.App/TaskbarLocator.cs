using LyricsStatusBar.Core;

namespace LyricsStatusBar.App;

internal sealed record TaskbarSnapshot(PixelRect Placement, PixelRect Taskbar, string Description);

internal sealed class TaskbarLocator
{
    private static readonly HashSet<string> ButtonClasses =
    [
        "MSTaskSwWClass",
        "MSTaskListWClass",
        "ReBarWindow32",
        "TaskbandHWND"
    ];

    public TaskbarSnapshot? Locate(AppSettings settings)
    {
        var taskbarHandle = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbarHandle == IntPtr.Zero ||
            !NativeMethods.IsWindowVisible(taskbarHandle) ||
            NativeMethods.IsCloaked(taskbarHandle) ||
            !NativeMethods.GetWindowRect(taskbarHandle, out var taskbarNative))
        {
            return null;
        }

        if (NativeMethods.IsFullscreenWindow(NativeMethods.GetForegroundWindow()))
        {
            return null;
        }

        var taskbar = ToPixelRect(taskbarNative);
        if (!taskbar.IsHorizontal || taskbar.Height <= 3)
        {
            return null;
        }

        var trayLeft = taskbar.Right - Math.Min(310, taskbar.Width / 5);
        var buttonsRight = taskbar.Left;
        var children = new List<(string ClassName, PixelRect Rectangle)>();
        NativeMethods.EnumWindowsProc callback = (window, _) =>
        {
            if (NativeMethods.IsWindowVisible(window) &&
                NativeMethods.GetWindowRect(window, out var native))
            {
                var rectangle = ToPixelRect(native);
                if (rectangle.Width > 0 && rectangle.Height > 0)
                {
                    children.Add((NativeMethods.ClassName(window), rectangle));
                }
            }
            return true;
        };
        _ = NativeMethods.EnumChildWindows(taskbarHandle, callback, IntPtr.Zero);

        foreach (var child in children)
        {
            if (child.ClassName == "TrayNotifyWnd")
            {
                trayLeft = Math.Min(trayLeft, child.Rectangle.Left);
            }
            else if (ButtonClasses.Contains(child.ClassName) &&
                     child.Rectangle.Width < taskbar.Width * 0.85)
            {
                buttonsRight = Math.Max(buttonsRight, child.Rectangle.Right);
            }
        }

        if (buttonsRight <= taskbar.Left)
        {
            buttonsRight = taskbar.Left + (taskbar.Width / 2) + Math.Min(420, taskbar.Width / 5);
        }

        var placement = TaskbarGapCalculator.Calculate(
            taskbar,
            buttonsRight,
            trayLeft,
            settings.MaxWidth,
            settings.HorizontalOffset);

        if (placement is null)
        {
            return null;
        }

        var description = $"Taskbar={taskbar.Left},{taskbar.Top},{taskbar.Width}x{taskbar.Height}; " +
                          $"ButtonsRight={buttonsRight}; TrayLeft={trayLeft}; " +
                          $"Overlay={placement.Value.Left},{placement.Value.Top},{placement.Value.Width}x{placement.Value.Height}";
        return new TaskbarSnapshot(placement.Value, taskbar, description);
    }

    private static PixelRect ToPixelRect(NativeMethods.Rect rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
}
