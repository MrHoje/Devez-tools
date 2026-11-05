using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using DevezTools.Infrastructure;
using DevezTools.Services;

namespace DevezTools.Views;

public enum FormatKind { Json, Xml }

public partial class FormatView : UserControl
{
    private readonly FormatKind _kind;
    private readonly Debouncer _reformat;

    public FormatView(FormatKind kind)
    {
        InitializeComponent();
        _kind = kind;
        _reformat = new Debouncer(150, Reformat);
        InputText.Tag = kind == FormatKind.Json ? "JSON을 입력하거나 붙여 넣어 주십시오." : "XML을 입력하거나 붙여 넣어 주십시오.";
        if (kind == FormatKind.Json)
        {
            ModeSelector.Visibility = Visibility.Visible;
            SegmentedTabs.Attach(ModeHost, ModeTabs, ModeIndicator);
        }
    }

    private void InputText_TextChanged(object sender, TextChangedEventArgs e)
    {
        InputMeta.Text = Ui.Stats(InputText.Text);
        _reformat.Kick();
    }

    // 정렬/한 줄 전환은 곧바로 다시 그린다(XAML 을 읽는 중 IsChecked 로 먼저 불릴 때는 건너뛴다)
    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (IsInitialized) _reformat.Flush();
    }

    private void Reformat()
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        string input = InputText.Text;
        if (string.IsNullOrWhiteSpace(input))
        {
            OutputText.Text = "";
            OutputMeta.Text = "";
            return;
        }

        try
        {
            OutputText.Text = _kind == FormatKind.Json
                ? JsonTools.Format(input, IndentMode.IsChecked == true).ReplaceLineEndings()
                : XmlTools.Format(input);
            OutputMeta.SetResourceReference(TextBlock.ForegroundProperty, "SuccessBrush");
            OutputMeta.Text = _kind == FormatKind.Json ? "올바른 JSON입니다" : "올바른 XML입니다";
        }
        catch (JsonException ex)
        {
            ShowError(JsonTools.DescribeError(ex));
        }
        catch (XmlException ex)
        {
            ShowError(XmlTools.DescribeError(ex));
        }
    }

    private void ShowError(string message)
    {
        OutputText.Text = "";
        OutputMeta.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
        OutputMeta.Text = "문법 오류";
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (Ui.PasteText() is { } text) InputText.Text = text;
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => InputText.Clear();
    private void Copy_Click(object sender, RoutedEventArgs e) => Ui.Copy(OutputText.Text);
}
