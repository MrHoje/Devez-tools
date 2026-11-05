using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using DevezTools.Infrastructure;
using DevezTools.Services;

namespace DevezTools.Views;

public partial class RegexView : UserControl
{
    private readonly Debouncer _run;
    private IReadOnlyList<RegexMatchRow> _rows = [];
    private bool _marksQueued;
    private int _runVersion;

    public RegexView()
    {
        InitializeComponent();
        _run = new Debouncer(150, Run);
        // 숨겨 둔 사이에는 그리지 않고, 다시 보일 때 그린다
        IsVisibleChanged += (_, _) => QueueMarks();
    }

    private void Input_Changed(object sender, TextChangedEventArgs e)
    {
        InputMeta.Text = Ui.Stats(InputText.Text);
        _rows = []; // 다시 찾기 전까지 어긋난 위치를 칠하지 않는다
        MatchMarks.Children.Clear();
        _run.Kick();
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (IsInitialized) _run.Flush();
    }

    // 무거운 패턴에도 화면이 멈추지 않게 찾기는 백그라운드에서 하고, 그 사이 입력이 바뀌었으면 지난 결과는 버린다
    private async void Run()
    {
        int version = ++_runVersion;
        string pattern = PatternText.Text, input = InputText.Text;
        bool ignoreCase = IgnoreCaseOption.IsChecked == true, multiline = MultilineOption.IsChecked == true;
        RegexRunResult? result = pattern.Length > 0 && input.Length > 0
            ? await Task.Run(() => RegexRunner.Run(pattern, input, ignoreCase, multiline))
            : null;
        if (version != _runVersion) return;

        PatternMeta.Text = "";
        SummaryMeta.Text = "";
        _rows = [];
        if (result != null)
        {
            _rows = result.Rows;
            if (result.Error != null)
            {
                PatternMeta.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
                PatternMeta.Text = result.Error;
            }
            else
            {
                SummaryMeta.Text = result.Total == 0 ? "일치하는 곳이 없습니다"
                    : result.Total > result.Rows.Count ? $"{result.Total:N0}개 · 앞 {result.Rows.Count:N0}개만 표시합니다"
                    : $"{result.Total:N0}개";
            }
        }
        MatchList.ItemsSource = _rows;
        QueueMarks();
    }

    // 고른 일치가 보이도록 그 줄로 스크롤하고 진하게 칠한다
    private void MatchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MatchList.SelectedItem is RegexMatchRow row && row.Position <= InputText.Text.Length)
            InputText.ScrollToLine(InputText.GetLineIndexFromCharacterIndex(row.Position));
        QueueMarks();
    }

    private void InputText_ScrollChanged(object sender, ScrollChangedEventArgs e) => DrawMarks();
    private void InputText_SizeChanged(object sender, SizeChangedEventArgs e) => QueueMarks();

    // 글을 바꾼 직후에는 줄 위치를 모르므로 배치가 끝난 뒤 한 번만 그린다
    private void QueueMarks()
    {
        if (_marksQueued) return;
        _marksQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _marksQueued = false;
            DrawMarks();
        });
    }

    /// <summary>보이는 줄에 걸친 일치만 줄마다 사각형으로 칠한다(줄을 넘는 일치도 줄 단위로 나눈다).</summary>
    private void DrawMarks()
    {
        MatchMarks.Children.Clear();
        TextBox box = InputText;
        string text = box.Text;
        // 다른 도구로 넘어가 떼어진 편집기는 줄 정보가 없어(LineCount 0) 줄 위치를 물으면 예외가 난다
        if (!box.IsVisible || _rows.Count == 0 || text.Length == 0) return;
        int first = box.GetFirstVisibleLineIndex();
        int last = Math.Min(box.GetLastVisibleLineIndex(), box.LineCount - 1);
        if (first < 0 || last < first) return;

        int visibleStart = box.GetCharacterIndexFromLineIndex(first);
        int visibleEnd = box.GetCharacterIndexFromLineIndex(last) + box.GetLineLength(last);
        var selected = MatchList.SelectedItem as RegexMatchRow;

        foreach (RegexMatchRow row in _rows)
        {
            int start = row.Position, end = row.Position + row.Length;
            if (row.Length == 0 || end > text.Length || end <= visibleStart || start >= visibleEnd) continue;

            int lineTo = Math.Min(last, box.GetLineIndexFromCharacterIndex(end - 1));
            for (int line = Math.Max(first, box.GetLineIndexFromCharacterIndex(start)); line <= lineTo; line++)
            {
                int lineStart = box.GetCharacterIndexFromLineIndex(line);
                int s = Math.Max(start, lineStart), e = Math.Min(end, lineStart + box.GetLineLength(line));
                while (e > s && text[e - 1] is '\n' or '\r') e--; // 줄 끝 줄바꿈은 칠하지 않는다
                if (e <= s) continue;

                Rect from = box.GetRectFromCharacterIndex(s);
                Rect to = box.GetRectFromCharacterIndex(e - 1, trailingEdge: true);
                if (from.IsEmpty || to.IsEmpty) continue;

                var mark = new Rectangle { Width = Math.Max(2, to.Left - from.Left), Height = from.Height, RadiusX = 3, RadiusY = 3 };
                mark.SetResourceReference(Shape.FillProperty, ReferenceEquals(row, selected) ? "PrimaryBrush" : "SearchHitBrush");
                if (ReferenceEquals(row, selected)) mark.Opacity = 0.45;
                Canvas.SetLeft(mark, from.Left);
                Canvas.SetTop(mark, from.Top);
                MatchMarks.Children.Add(mark);
            }
        }
    }

    private void CopyValue_Click(object sender, RoutedEventArgs e)
    {
        if (MatchList.SelectedItem is RegexMatchRow row) Ui.Copy(row.Value);
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (Ui.PasteText() is { } text) InputText.Text = text;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        PatternText.Clear();
        InputText.Clear();
    }
}
