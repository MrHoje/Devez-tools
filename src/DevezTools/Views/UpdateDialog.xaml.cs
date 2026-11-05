using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using DevezTools.Services;

namespace DevezTools.Views;

/// <summary>새 버전 노트를 보이고, 업데이트를 누르면 진행률과 함께 설치 파일을 받아 실행한다.
/// 설치 파일을 실행했으면 true 로 닫힌다(부른 쪽이 앱을 끝내 설치 파일이 파일을 바꾸게 한다).</summary>
internal partial class UpdateDialog : Window
{
    private readonly UpdateService.Release _release;
    private bool _busy;

    public UpdateDialog(UpdateService.Release release)
    {
        InitializeComponent();
        _release = release;
        TitleText.Text = $"새 버전 v{release.Version}";
        NoteList.ItemsSource = release.Notes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        _busy = true;
        Footer.Visibility = Visibility.Collapsed;
        ProgressArea.Visibility = Visibility.Visible;
        ProgressLabel.Text = "다운로드 중…";
        var progress = new Progress<double>(value =>
        {
            ProgressPercent.Text = $"{value:P0}";
            ProgressFill.Width = ProgressTrack.ActualWidth * value;
        });

        try
        {
            string setup = await UpdateService.DownloadAsync(_release, progress);
            ProgressLabel.Text = "설치를 시작합니다…";
            UpdateService.Install(setup);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException
                                       or InvalidDataException or Win32Exception)
        {
            // 창은 그대로 두고 까닭을 보인 뒤 다시 시도할 수 있게 한다
            _busy = false;
            ProgressLabel.Text = "업데이트하지 못했습니다: " + ex.Message;
            ProgressPercent.Text = "";
            UpdateButton.Content = "다시 시도";
            Footer.Visibility = Visibility.Visible;
        }
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_busy) DialogResult = false;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    // 받는 중에는 닫지 않는다(Alt+F4 포함)
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = _busy && DialogResult != true;
        base.OnClosing(e);
    }
}
