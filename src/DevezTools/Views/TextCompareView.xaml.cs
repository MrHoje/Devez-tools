using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using DevezTools.Infrastructure;
using DevezTools.Services;

namespace DevezTools.Views;

/// <summary>
/// 텍스트 비교(talkRemind WPF 도구 상자의 비교 이식): 두 편집기를 나란히 두고 줄 번호 칸과 달라진 줄 배경을
/// 보이는 줄만 다시 그린다. 세로 스크롤은 양쪽이 함께 움직인다.
/// </summary>
public partial class TextCompareView : UserControl
{
    // 이 크기(줄 수의 곱)까지는 타자마다 바로 비교하고, 넘으면 입력이 멈춘 뒤 비교한다
    private const long InstantCells = 1_000_000;

    private readonly Debouncer _diff;
    private HashSet<int> _leftChanged = [];
    private HashSet<int> _rightChanged = [];
    private TextBox? _following; // 상대 쪽 스크롤을 따라 움직이는 중인 편집기
    private bool _redrawQueued;

    public TextCompareView()
    {
        InitializeComponent();
        Root.Children.Remove(HeaderBar); // MainWindow 가 제목 줄 오른쪽에 띄운다
        _diff = new Debouncer(250, Recompute);
        // 숨겨 둔 사이에는 그리지 않고, 다시 보일 때 그린다
        IsVisibleChanged += (_, _) => QueueRedraw();
    }

    private void Box_TextChanged(object sender, TextChangedEventArgs e)
    {
        LeftMeta.Text = Ui.Stats(LeftBox.Text);
        RightMeta.Text = Ui.Stats(RightBox.Text);
        if ((long)CountLines(LeftBox.Text) * CountLines(RightBox.Text) <= InstantCells) _diff.Flush();
        else _diff.Kick();
    }

    private static int CountLines(string text) => text.Count(c => c == '\n') + 1;

    private void Recompute()
    {
        List<DiffRow> rows = LineDiff.Compute(LeftBox.Text, RightBox.Text);
        _leftChanged = rows.Where(r => r.Kind == DiffKind.Removed).Select(r => r.LeftLine - 1).ToHashSet();
        _rightChanged = rows.Where(r => r.Kind == DiffKind.Added).Select(r => r.RightLine - 1).ToHashSet();

        bool empty = LeftBox.Text.Length == 0 && RightBox.Text.Length == 0;
        int removed = _leftChanged.Count, added = _rightChanged.Count;
        RemovedChip.Visibility = removed > 0 ? Visibility.Visible : Visibility.Collapsed;
        AddedChip.Visibility = added > 0 ? Visibility.Visible : Visibility.Collapsed;
        RemovedText.Text = $"−{removed:N0}줄 삭제";
        AddedText.Text = $"+{added:N0}줄 추가";
        SummaryText.Text = empty ? ""
            : removed + added == 0 ? "두 글이 같습니다."
            : "";
        QueueRedraw();
    }

    // 글이 바뀐 직후에는 배치가 끝나지 않아 줄 위치를 모른다. 배치가 끝난 뒤 한 번만 그린다.
    private void QueueRedraw()
    {
        if (_redrawQueued) return;
        _redrawQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _redrawQueued = false;
            Redraw();
        });
    }

    private void Redraw()
    {
        DrawSide(LeftBox, LeftMarks, LeftGutter, _leftChanged, "DiffDelBrush", "DangerBrush");
        DrawSide(RightBox, RightMarks, RightGutter, _rightChanged, "DiffAddBrush", "SuccessBrush");
    }

    /// <summary>보이는 줄만 줄 번호와 달라진 줄 배경을 그린다.</summary>
    private static void DrawSide(TextBox box, Canvas marks, Canvas gutter, HashSet<int> changed, string markBrush, string numberBrush)
    {
        marks.Children.Clear();
        gutter.Children.Clear();
        // 다른 도구로 넘어가 떼어진 편집기는 줄 정보가 없어(LineCount 0) 줄 위치를 물으면 예외가 난다
        if (!box.IsVisible || box.Text.Length == 0) return;

        int first = box.GetFirstVisibleLineIndex();
        int last = Math.Min(box.GetLastVisibleLineIndex(), box.LineCount - 1);
        if (first < 0 || last < first) return;

        for (int line = first; line <= last; line++)
        {
            int index = box.GetCharacterIndexFromLineIndex(line);
            if (index < 0) continue;
            Rect rect = box.GetRectFromCharacterIndex(index);
            if (rect.IsEmpty) continue;
            bool isChanged = changed.Contains(line);

            if (isChanged)
            {
                var bar = new Rectangle { Width = marks.ActualWidth, Height = rect.Height };
                bar.SetResourceReference(Shape.FillProperty, markBrush);
                Canvas.SetTop(bar, rect.Top);
                marks.Children.Add(bar);
            }

            var number = new TextBlock
            {
                Text = (line + 1).ToString(),
                FontFamily = box.FontFamily,
                FontSize = box.FontSize,
                Width = Math.Max(0, gutter.ActualWidth - 10),
                TextAlignment = TextAlignment.Right,
                Opacity = isChanged ? 1 : 0.6,
            };
            number.SetResourceReference(TextBlock.ForegroundProperty, isChanged ? numberBrush : "TextMutedBrush");
            Canvas.SetTop(number, rect.Top);
            gutter.Children.Add(number);
        }
    }

    // 세로 스크롤은 양쪽이 함께 움직인다
    private void LeftBox_ScrollChanged(object sender, ScrollChangedEventArgs e) => OnScroll(LeftBox, e, RightBox);
    private void RightBox_ScrollChanged(object sender, ScrollChangedEventArgs e) => OnScroll(RightBox, e, LeftBox);

    private void OnScroll(TextBox source, ScrollChangedEventArgs e, TextBox other)
    {
        // 따라 움직인 쪽의 이벤트는 되돌려 보내지 않는다(짧은 쪽이 끝에 막혀 생긴 위치로 긴 쪽이 끌려가지 않게)
        if (ReferenceEquals(source, _following))
        {
            _following = null;
        }
        else if (e.VerticalChange != 0)
        {
            double target = Math.Min(e.VerticalOffset, Math.Max(0, other.ExtentHeight - other.ViewportHeight));
            if (Math.Abs(other.VerticalOffset - target) > 0.5)
            {
                _following = other;
                other.ScrollToVerticalOffset(e.VerticalOffset);
            }
        }
        Redraw();
    }

    private void Box_SizeChanged(object sender, SizeChangedEventArgs e) => QueueRedraw();

    private void Swap_Click(object sender, RoutedEventArgs e) => (LeftBox.Text, RightBox.Text) = (RightBox.Text, LeftBox.Text);

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        LeftBox.Clear();
        RightBox.Clear();
    }

    private void PasteLeft_Click(object sender, RoutedEventArgs e)
    {
        if (Ui.PasteText() is { } text) LeftBox.Text = text;
    }

    private void PasteRight_Click(object sender, RoutedEventArgs e)
    {
        if (Ui.PasteText() is { } text) RightBox.Text = text;
    }

    private void CopyLeft_Click(object sender, RoutedEventArgs e) => Ui.Copy(LeftBox.Text);
    private void CopyRight_Click(object sender, RoutedEventArgs e) => Ui.Copy(RightBox.Text);
    private void ClearLeft_Click(object sender, RoutedEventArgs e) => LeftBox.Clear();
    private void ClearRight_Click(object sender, RoutedEventArgs e) => RightBox.Clear();
}
