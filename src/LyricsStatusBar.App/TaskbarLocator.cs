using System.Runtime.InteropServices;
using LyricsStatusBar.Core;

namespace LyricsStatusBar.App;

internal sealed record TaskbarSnapshot(PixelRect Placement, PixelRect Taskbar, string Description);

internal sealed class TaskbarLocator
{
    private const int GapMargin = 12;
    private const int MinimumOverlayWidth = 180;
    private const int DefaultTaskbarHeight = 48;
    private const int MinimumTaskbarHeight = 24;
    private const int MaximumTaskbarHeight = 120;

    private static readonly HashSet<string> ButtonClasses =
    [
        "MSTaskSwWClass",
        "MSTaskListWClass",
        "TaskbandHWND"
    ];

    public string LastFailure { get; private set; } = "Not checked";

    public TaskbarSnapshot? Locate(AppSettings settings)
    {
        var taskbarHandle = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbarHandle == IntPtr.Zero)
        {
            LastFailure = "Shell_TrayWnd not found";
            return null;
        }

        if (!NativeMethods.IsWindowVisible(taskbarHandle))
        {
            LastFailure = "Shell_TrayWnd is not visible";
            return null;
        }

        if (NativeMethods.IsCloaked(taskbarHandle))
        {
            LastFailure = "Shell_TrayWnd is cloaked";
            return null;
        }

        if (!NativeMethods.GetWindowRect(taskbarHandle, out var taskbarNative))
        {
            LastFailure = "Shell_TrayWnd rectangle unavailable";
            return null;
        }

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        if (NativeMethods.IsFullscreenWindow(foregroundWindow))
        {
            LastFailure = $"Fullscreen foreground window: {NativeMethods.ClassName(foregroundWindow)}";
            return null;
        }

        if (!TryGetMonitorInfo(taskbarHandle, out var monitorInfo))
        {
            LastFailure = "Monitor information unavailable";
            return null;
        }

        var nativeTaskbar = ToPixelRect(taskbarNative);
        var monitor = ToPixelRect(monitorInfo.Monitor);
        var work = ToPixelRect(monitorInfo.Work);
        var taskbar = NormalizeHorizontalTaskbar(nativeTaskbar, monitor, work, out var taskbarSource);
        if (taskbar is null)
        {
            LastFailure = $"Unsupported taskbar rectangle: Native={Describe(nativeTaskbar)}; Monitor={Describe(monitor)}; Work={Describe(work)}";
            return null;
        }

        var trayLeft = taskbar.Value.Right - Math.Min(310, taskbar.Value.Width / 5);
        var buttonsRight = taskbar.Value.Left;
        var buttonSource = "none";
        var children = new List<(string ClassName, PixelRect Rectangle)>();
        NativeMethods.EnumWindowsProc callback = (window, _) =>
        {
            if (NativeMethods.IsWindowVisible(window) &&
                NativeMethods.GetWindowRect(window, out var native))
            {
                var rectangle = ToPixelRect(native);
                if (rectangle.Width > 0 && rectangle.Height > 0 && IntersectsHorizontally(rectangle, taskbar.Value))
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
                trayLeft = Math.Min(trayLeft, Math.Clamp(child.Rectangle.Left, taskbar.Value.Left, taskbar.Value.Right));
            }
        }

        foreach (var child in children)
        {
            if (!ButtonClasses.Contains(child.ClassName))
            {
                continue;
            }

            if (LooksLikeTaskButtonRegion(child.Rectangle, taskbar.Value, trayLeft))
            {
                buttonsRight = Math.Max(buttonsRight, Math.Clamp(child.Rectangle.Right, taskbar.Value.Left, taskbar.Value.Right));
                buttonSource = child.ClassName;
            }
        }

        var requiredGap = MinimumOverlayWidth + (GapMargin * 2);
        if (buttonsRight <= taskbar.Value.Left || trayLeft - buttonsRight < requiredGap)
        {
            var fallbackButtonsRight = taskbar.Value.Left + Math.Min(taskbar.Value.Width / 2, 640);
            buttonsRight = Math.Min(fallbackButtonsRight, trayLeft - requiredGap);
            buttonsRight = Math.Max(taskbar.Value.Left, buttonsRight);
            buttonSource = buttonSource == "none" ? "fallback" : $"{buttonSource}+fallback";
        }

        var placement = TaskbarGapCalculator.Calculate(
            taskbar.Value,
            buttonsRight,
            trayLeft,
            settings.MaxWidth,
            settings.HorizontalOffset,
            GapMargin,
            MinimumOverlayWidth);

        if (placement is null)
        {
            LastFailure = $"No usable gap; Taskbar={Describe(taskbar.Value)}; Source={taskbarSource}; " +
                          $"ButtonsRight={buttonsRight}; TrayLeft={trayLeft}; ButtonSource={buttonSource}; " +
                          $"Monitor={Describe(monitor)}; Work={Describe(work)}; Children={DescribeChildren(children)}";
            return null;
        }

        LastFailure = "None";
        var description = $"Taskbar={taskbar.Value.Left},{taskbar.Value.Top},{taskbar.Value.Width}x{taskbar.Value.Height}; " +
                          $"Source={taskbarSource}; ButtonsRight={buttonsRight}; TrayLeft={trayLeft}; " +
                          $"ButtonSource={buttonSource}; " +
                          $"Overlay={placement.Value.Left},{placement.Value.Top},{placement.Value.Width}x{placement.Value.Height}";
        return new TaskbarSnapshot(placement.Value, taskbar.Value, description);
    }

    private static PixelRect? NormalizeHorizontalTaskbar(PixelRect nativeTaskbar, PixelRect monitor, PixelRect work, out string source)
    {
        if (LooksLikeHorizontalEdgeTaskbar(nativeTaskbar, monitor))
        {
            source = "native";
            return ClampTaskbarHeight(nativeTaskbar, monitor);
        }

        if (work.Bottom < monitor.Bottom && work.Bottom > monitor.Top)
        {
            source = "monitor_work_bottom";
            return new PixelRect(monitor.Left, work.Bottom, monitor.Right, monitor.Bottom);
        }

        if (work.Top > monitor.Top && work.Top < monitor.Bottom)
        {
            source = "monitor_work_top";
            return new PixelRect(monitor.Left, monitor.Top, monitor.Right, work.Top);
        }

        source = "monitor_default_bottom";
        return new PixelRect(monitor.Left, monitor.Bottom - DefaultTaskbarHeight, monitor.Right, monitor.Bottom);
    }

    private static bool LooksLikeHorizontalEdgeTaskbar(PixelRect rectangle, PixelRect monitor)
    {
        if (!rectangle.IsHorizontal || rectangle.Height < MinimumTaskbarHeight || rectangle.Height > MaximumTaskbarHeight)
        {
            return false;
        }

        if (rectangle.Width < monitor.Width / 2)
        {
            return false;
        }

        var nearTop = Math.Abs(rectangle.Top - monitor.Top) <= 4;
        var nearBottom = Math.Abs(rectangle.Bottom - monitor.Bottom) <= 4;
        return nearTop || nearBottom;
    }

    private static PixelRect ClampTaskbarHeight(PixelRect taskbar, PixelRect monitor)
    {
        var height = Math.Clamp(taskbar.Height, MinimumTaskbarHeight, MaximumTaskbarHeight);
        if (Math.Abs(taskbar.Top - monitor.Top) <= 4)
        {
            return new PixelRect(monitor.Left, monitor.Top, monitor.Right, monitor.Top + height);
        }

        return new PixelRect(monitor.Left, monitor.Bottom - height, monitor.Right, monitor.Bottom);
    }

    private static bool IntersectsHorizontally(PixelRect rectangle, PixelRect taskbar) =>
        rectangle.Bottom > taskbar.Top &&
        rectangle.Top < taskbar.Bottom &&
        rectangle.Right > taskbar.Left &&
        rectangle.Left < taskbar.Right;

    private static bool LooksLikeTaskButtonRegion(PixelRect rectangle, PixelRect taskbar, int trayLeft)
    {
        if (rectangle.Height < Math.Max(8, taskbar.Height / 3))
        {
            return false;
        }

        if (rectangle.Left > taskbar.Left + Math.Max(160, taskbar.Width / 4))
        {
            return false;
        }

        // On Windows 10 the task-list window often spans almost the entire area up to the tray,
        // including empty space. Treating that full rectangle as occupied makes placement fail.
        return rectangle.Right < trayLeft - MinimumOverlayWidth;
    }

    private static bool TryGetMonitorInfo(IntPtr window, out NativeMethods.MonitorInfo information)
    {
        var monitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
        information = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        return monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref information);
    }

    private static string Describe(PixelRect rectangle) =>
        $"{rectangle.Left},{rectangle.Top},{rectangle.Width}x{rectangle.Height}";

    private static string DescribeChildren(IReadOnlyCollection<(string ClassName, PixelRect Rectangle)> children) =>
        string.Join(", ", children.Take(12).Select(child => $"{child.ClassName}:{Describe(child.Rectangle)}"));

    private static PixelRect ToPixelRect(NativeMethods.Rect rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
}