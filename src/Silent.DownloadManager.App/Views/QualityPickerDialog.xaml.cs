using System.Windows;
using Silent.DownloadManager.App.Services;

namespace Silent.DownloadManager.App.Views;

public partial class QualityPickerDialog : Window
{
    private readonly YtDlpService _ytDlpService;
    private readonly string _url;

    public QualityPickerDialog(YtDlpService ytDlpService, string url)
    {
        InitializeComponent();
        _ytDlpService = ytDlpService;
        _url = url;
        UrlLabel.Text = url;
        Loaded += OnLoaded;

        FormatListBox.SelectionChanged += (_, _) =>
        {
            DownloadButton.IsEnabled = FormatListBox.SelectedItem != null;
        };
    }

    /// <summary>The selected quality option. Null if user cancelled.</summary>
    public VideoQualityOption? SelectedQuality { get; private set; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadingPanel.Visibility = Visibility.Visible;
            ErrorLabel.Visibility = Visibility.Collapsed;
            DownloadButton.IsEnabled = false;

            var formats = await _ytDlpService.GetAvailableFormatsAsync(_url);

            LoadingPanel.Visibility = Visibility.Collapsed;

            if (formats.Count == 0)
            {
                ErrorLabel.Text = "No formats found. The URL may be unsupported or require login.";
                ErrorLabel.Visibility = Visibility.Visible;
                return;
            }

            // Group: show combined/video+audio first, then video-only, then audio-only
            var ordered = formats
                .OrderBy(f =>
                {
                    if (f.FormatId == "best") return 0;
                    var l = f.Label.ToLowerInvariant();
                    if (l.Contains("audio only")) return 3;
                    if (l.Contains("video only")) return 2;
                    return 1;
                })
                .ThenByDescending(f =>
                {
                    // Sort by resolution height descending
                    var m = System.Text.RegularExpressions.Regex.Match(f.Resolution, @"(\d+)");
                    return m.Success && int.TryParse(m.Groups[1].Value, out var h) ? h : 0;
                })
                .ToList();

            foreach (var fmt in ordered)
                FormatListBox.Items.Add(fmt);

            // Pre-select "Best Quality"
            FormatListBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorLabel.Text = $"Could not fetch formats: {ex.Message}";
            ErrorLabel.Visibility = Visibility.Visible;
        }
    }

    private void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedQuality = FormatListBox.SelectedItem as VideoQualityOption;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedQuality = null;
        DialogResult = false;
    }
}
