using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DevezTools.Infrastructure;
using DevezTools.Services;

namespace DevezTools.Views;

public partial class OcrView : UserControl
{
    private int _runVersion; // 추출 중에 새 이미지가 오면 늦게 끝난 결과는 버린다
    private bool _busy;

    public OcrView()
    {
        InitializeComponent();
        // 레일 단추에 포커스가 남아 있으면 Ctrl+V 가 이 화면을 거치지 않으므로, 보이자마자 포커스를 가져온다
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
        };
    }

    // 이미지 칸을 눌러도 Ctrl+V 를 받을 수 있게 한다
    private void ImageArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Focus();

    private async Task LoadAsync(BitmapSource image)
    {
        int version = ++_runVersion;
        Preview.Source = image;
        EmptyText.Visibility = Visibility.Collapsed;
        ImageArea.SetResourceReference(Border.BackgroundProperty, "PanelSoftBrush");
        ImageMeta.Text = $"{image.PixelWidth:N0} × {image.PixelHeight:N0}";
        ResultText.Clear();
        ShowStatus("추출 중…");
        _busy = true;
        try
        {
            byte[] png = ClipboardImage.ToPng(image); // UI 스레드에서 인코딩하고 인식은 백그라운드에서
            var progress = new Progress<string>(message =>
            {
                if (version == _runVersion) ShowStatus(message);
            });
            string text = await OcrEngine.ExtractAsync(png, progress);
            if (version != _runVersion) return;
            ResultText.Text = text;
            ShowStatus(text.Length == 0 ? "글자를 찾지 못했습니다" : Ui.Stats(text));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (version != _runVersion) return;
            ShowStatus("추출하지 못했습니다", "DangerBrush");
            Ui.Toast("글을 추출하지 못했습니다: " + ex.Message);
        }
        finally
        {
            if (version == _runVersion) _busy = false;
        }
    }

    private void ShowStatus(string text, string brushKey = "TextMutedBrush")
    {
        StatusText.Text = text;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
    }

    private async Task LoadFileAsync(string path)
    {
        BitmapSource image;
        try
        {
            image = ClipboardImage.FromFile(path);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or FileFormatException)
        {
            Ui.Toast("이미지를 열지 못했습니다: " + ex.Message);
            return;
        }
        await LoadAsync(image);
    }

    private async Task PasteAsync()
    {
        BitmapSource? image;
        try
        {
            image = ClipboardImage.FromClipboard();
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            Ui.Toast("클립보드를 읽지 못했습니다. 잠시 뒤 다시 시도해 주십시오.");
            return;
        }
        if (image is null) Ui.Toast("클립보드에 이미지가 없습니다");
        else await LoadAsync(image);
    }

    // Ctrl+V: 결과 칸에서 글을 붙여 넣을 때는 그대로 두고, 그 밖에서는 이미지를 붙여 넣는다
    private async void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control || ResultText.IsKeyboardFocusWithin) return;
        e.Handled = true;
        await PasteAsync();
    }

    private void View_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        e.Effects = e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Any(ClipboardImage.IsImageFile) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void View_PreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        e.Handled = true;
        if (files.FirstOrDefault(ClipboardImage.IsImageFile) is { } path) await LoadFileAsync(path);
    }

    private async void Paste_Click(object sender, RoutedEventArgs e) => await PasteAsync();

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "글을 추출할 이미지",
            Filter = "이미지|" + string.Join(";", ClipboardImage.Extensions.Select(x => "*" + x)) + "|모든 파일|*.*",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await LoadFileAsync(dialog.FileName);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _runVersion++;
        _busy = false;
        Preview.Source = null;
        EmptyText.Visibility = Visibility.Visible;
        ImageArea.Background = Brushes.Transparent;
        ImageMeta.Text = "";
        ResultText.Clear();
        ShowStatus("");
    }

    private void ResultText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_busy && ResultText.Text.Length > 0) ShowStatus(Ui.Stats(ResultText.Text));
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => Ui.Copy(ResultText.Text);
}
