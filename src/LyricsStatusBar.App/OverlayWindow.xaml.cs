using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using LyricsStatusBar.Core;

namespace LyricsStatusBar.App;

public partial class OverlayWindow : Window
{
    private readonly DispatcherTimer _hideDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private readonly DispatcherTimer _topmostKeepAliveTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private IntPtr _handle;

    public OverlayWindow()
    {
        InitializeComponent();
        _hideDebounceTimer.Tick += (_, _) =>
        {
            _hideDebounceTimer.Stop();
            FadeTo(0);
        };
        _topmostKeepAliveTimer.Tick += (_, _) => ReassertTopmost();
        SourceInitialized += OnSourceInitialized;
    }

    public void ApplySettings(AppSettings settings)
    {
        var font = new System.Windows.Media.FontFamily(settings.FontFamily);
        PrimaryText.FontFamily = font;
        SecondaryText.FontFamily = font;
        PrimaryText.FontSize = settings.PrimaryFontSize;
        SecondaryText.FontSize = settings.SecondaryFontSize;
        PrimaryText.Foreground = ParseBrush(settings.PrimaryColor);
        SecondaryText.Foreground = ParseBrush(settings.SecondaryColor);
        var alignment = settings.Alignment switch
        {
            TextAlignmentOption.Left => TextAlignment.Left,
            TextAlignmentOption.Right => TextAlignment.Right,
            _ => TextAlignment.Center
        };
        PrimaryText.TextAlignment = alignment;
        SecondaryText.TextAlignment = alignment;
        var effect = new DropShadowEffect
        {
            Color = ParseColor(settings.ShadowColor),
            BlurRadius = 3,
            Direction = 270,
            ShadowDepth = 1,
            Opacity = ParseColor(settings.ShadowColor).A / 255d
        };
        PrimaryText.Effect = effect;
        SecondaryText.Effect = effect.Clone();
    }

    public void SetLine(DisplayLine line)
    {
        _hideDebounceTimer.Stop();
        PrimaryText.Text = line.Primary;
        SecondaryText.Text = line.Secondary;
        var hasSecondary = !string.IsNullOrWhiteSpace(line.Secondary);
        SecondaryText.Visibility = hasSecondary ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Controls.Grid.SetRowSpan(PrimaryText, hasSecondary ? 1 : 2);
        PrimaryText.VerticalAlignment = VerticalAlignment.Center;
        FadeTo(1);
        ReassertTopmost();
    }

    public void HideLyrics()
    {
        if (Opacity <= 0 || _hideDebounceTimer.IsEnabled)
        {
            return;
        }
        _hideDebounceTimer.Start();
    }

    public void SetPlacement(PixelRect placement)
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }
        _ = NativeMethods.SetWindowPos(
            _handle,
            NativeMethods.HwndTopmost,
            placement.Left,
            placement.Top,
            placement.Width,
            placement.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        ReassertTopmost();
    }

    private void ReassertTopmost()
    {
        if (_handle == IntPtr.Zero || Opacity <= 0)
        {
            return;
        }
        _ = NativeMethods.SetWindowPos(
            _handle,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        NativeMethods.MakeOverlayWindow(_handle);
        _topmostKeepAliveTimer.Start();
    }

    private void FadeTo(double opacity)
    {
        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new QuadraticEase()
            });
    }

    private static SolidColorBrush ParseBrush(string value)
    {
        var brush = new SolidColorBrush(ParseColor(value));
        brush.Freeze();
        return brush;
    }

    private static System.Windows.Media.Color ParseColor(string value) =>
        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value)!;
}
