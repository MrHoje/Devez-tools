using System.Windows;
using System.Windows.Controls;
using DevezTools.Infrastructure;
using DevezTools.Services;

namespace DevezTools.Views;

public partial class Base64View : UserControl
{
    public Base64View() => InitializeComponent();

    private void Encode_Click(object sender, RoutedEventArgs e) => EncodedText.Text = Base64Tools.Encode(PlainText.Text);

    private void Decode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PlainText.Text = Base64Tools.Decode(EncodedText.Text);
        }
        // 원문을 덮어쓰지 않고 오류만 알린다
        catch (FormatException)
        {
            ShowDecodeError("Base64 형식이 아닙니다");
        }
        catch (System.Text.DecoderFallbackException)
        {
            ShowDecodeError("UTF-8 글자가 아닙니다. 바이너리이거나 다른 문자 집합일 수 있습니다");
        }
    }

    private void ShowDecodeError(string message)
    {
        EncodedMeta.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        EncodedMeta.Text = message;
    }

    private void PlainText_TextChanged(object sender, TextChangedEventArgs e) => PlainMeta.Text = Ui.Stats(PlainText.Text);

    private void EncodedText_TextChanged(object sender, TextChangedEventArgs e)
    {
        EncodedMeta.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        EncodedMeta.Text = Ui.Stats(EncodedText.Text);
    }

    private void PastePlain_Click(object sender, RoutedEventArgs e)
    {
        if (Ui.PasteText() is { } text) PlainText.Text = text;
    }

    private void PasteEncoded_Click(object sender, RoutedEventArgs e)
    {
        if (Ui.PasteText() is { } text) EncodedText.Text = text;
    }

    private void CopyPlain_Click(object sender, RoutedEventArgs e) => Ui.Copy(PlainText.Text);
    private void CopyEncoded_Click(object sender, RoutedEventArgs e) => Ui.Copy(EncodedText.Text);
    private void ClearPlain_Click(object sender, RoutedEventArgs e) => PlainText.Clear();
    private void ClearEncoded_Click(object sender, RoutedEventArgs e) => EncodedText.Clear();
}
