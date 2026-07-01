using System.Globalization;
using System.Windows;
using LyricsStatusBar.Core;

namespace LyricsStatusBar.App;

public partial class SettingsWindow : Window
{
    private readonly bool _enabled;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _enabled = settings.Enabled;
        AlignmentBox.ItemsSource = Enum.GetValues<TextAlignmentOption>();
        Populate(settings);
    }

    public AppSettings? ResultSettings { get; private set; }

    private void Populate(AppSettings settings)
    {
        FontFamilyBox.Text = settings.FontFamily;
        PrimarySizeBox.Text = settings.PrimaryFontSize.ToString(CultureInfo.InvariantCulture);
        SecondarySizeBox.Text = settings.SecondaryFontSize.ToString(CultureInfo.InvariantCulture);
        PrimaryColorBox.Text = settings.PrimaryColor;
        SecondaryColorBox.Text = settings.SecondaryColor;
        ShadowColorBox.Text = settings.ShadowColor;
        AlignmentBox.SelectedItem = settings.Alignment;
        MaxWidthBox.Text = settings.MaxWidth.ToString(CultureInfo.InvariantCulture);
        OffsetBox.Text = settings.HorizontalOffset.ToString(CultureInfo.InvariantCulture);
        LyricAdvanceBox.Text = settings.LyricAdvanceMs.ToString(CultureInfo.InvariantCulture);
        HideDelayBox.Text = settings.HideDelayMs.ToString(CultureInfo.InvariantCulture);
        AutoStartBox.IsChecked = settings.AutoStart;
    }

    private void ResetClicked(object sender, RoutedEventArgs e) => Populate(new AppSettings());

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(PrimarySizeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var primarySize) ||
            !double.TryParse(SecondarySizeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var secondarySize) ||
            !int.TryParse(MaxWidthBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxWidth) ||
            !int.TryParse(OffsetBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset) ||
            !int.TryParse(LyricAdvanceBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lyricAdvance) ||
            !int.TryParse(HideDelayBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hideDelay))
        {
            System.Windows.MessageBox.Show(
                this,
                "Sizes, width, offsets, and delay must be valid numbers.",
                "Invalid settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        ResultSettings = new AppSettings
        {
            Enabled = _enabled,
            AutoStart = AutoStartBox.IsChecked == true,
            FontFamily = FontFamilyBox.Text,
            PrimaryFontSize = primarySize,
            SecondaryFontSize = secondarySize,
            PrimaryColor = PrimaryColorBox.Text,
            SecondaryColor = SecondaryColorBox.Text,
            ShadowColor = ShadowColorBox.Text,
            Alignment = AlignmentBox.SelectedItem is TextAlignmentOption alignment
                ? alignment
                : TextAlignmentOption.Center,
            MaxWidth = maxWidth,
            HorizontalOffset = offset,
            LyricAdvanceMs = lyricAdvance,
            HideDelayMs = hideDelay
        }.Normalize();
        DialogResult = true;
    }
}
