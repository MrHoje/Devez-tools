using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using DevezTools.Infrastructure;
using DevezTools.Services;

namespace DevezTools.Views;

public partial class JwtView : UserControl
{
    private enum Mark { None, Hit, Current }

    private bool _rendering;
    private bool _recolorQueued;
    private string _header = "";
    private string _payload = "";
    private string _signature = "";
    private readonly List<int> _hits = [];
    private int _hitIndex = -1;

    public JwtView()
    {
        InitializeComponent();
        TokenInput.Document = NewDocument(new Paragraph());
    }

    // ── 토큰 입력 ──────────────────────────────────────────────

    private void TokenInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_rendering || _recolorQueued) return;
        // TextChanged 는 RichTextBox 의 변경 블록 안에서 불려 여기서 Document 를 바꾸면 예외가 난다. 블록이 끝난 뒤 한 번만 다시 칠한다.
        _recolorQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _recolorQueued = false;
            RecolorAndDecode();
        });
    }

    private void RecolorAndDecode()
    {
        FlowDocument doc = TokenInput.Document;
        string raw = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
        int caret = CountNonSpace(new TextRange(doc.ContentStart, TokenInput.CaretPosition).Text);
        string token = new(raw.Where(c => !char.IsWhiteSpace(c)).ToArray());

        var paragraph = new Paragraph();
        string[] parts = token.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) paragraph.Inlines.Add(Colored(".", "TextMutedBrush"));
            if (parts[i].Length > 0)
                paragraph.Inlines.Add(Colored(parts[i], i switch { 0 => "JwtHeaderBrush", 1 => "JwtPayloadBrush", _ => "JwtSignatureBrush" }));
        }

        _rendering = true;
        TokenInput.Document = NewDocument(paragraph);
        TokenInput.CaretPosition = PositionAt(paragraph, caret) ?? TokenInput.Document.ContentEnd;
        _rendering = false;

        TokenHint.Visibility = token.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Decode(token);
    }

    private static int CountNonSpace(string text) => text.Count(c => !char.IsWhiteSpace(c));

    private static Run Colored(string text, string brushKey)
    {
        var run = new Run(text);
        run.SetResourceReference(TextElement.ForegroundProperty, brushKey);
        return run;
    }

    private static TextPointer? PositionAt(Paragraph paragraph, int offset)
    {
        foreach (Run run in paragraph.Inlines.OfType<Run>())
        {
            if (offset <= run.Text.Length) return run.ContentStart.GetPositionAtOffset(offset);
            offset -= run.Text.Length;
        }
        return null;
    }

    private static FlowDocument NewDocument(Paragraph paragraph)
    {
        paragraph.Margin = new Thickness(0);
        var doc = new FlowDocument { PagePadding = new Thickness(0) };
        doc.Blocks.Add(paragraph);
        return doc;
    }

    // 서식 있는 글을 붙여 넣어도 글자만 넣는다(색칠 규칙이 깨지지 않게)
    private void TokenInput_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        string? text = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? e.DataObject.GetData(DataFormats.Text) as string;
        e.CancelCommand();
        if (text is null) return;
        TokenInput.Selection.Text = text;
        TokenInput.CaretPosition = TokenInput.Selection.End;
    }

    private void SetToken(string text) => TokenInput.Document = NewDocument(new Paragraph(new Run(text)));

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (Ui.PasteText() is { } text) SetToken(text);
    }

    private void Clear_Click(object sender, RoutedEventArgs e) => SetToken("");

    // ── 풀어 보기 ──────────────────────────────────────────────

    private void Decode(string token)
    {
        JwtDecoder.Result? result = JwtDecoder.Decode(token);
        TokenMeta.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

        if (result is null)
        {
            _header = _payload = _signature = "";
            SignatureOutput.Text = "";
            AlgorithmMeta.Text = "";
            IssuedMeta.Text = "";
            ExpiryChip.Visibility = Visibility.Collapsed;
            TokenMeta.Text = "";
            if (token.Length > 0)
            {
                TokenMeta.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
                TokenMeta.Text = "점(.)으로 나뉜 2~3 조각이어야 합니다";
            }
        }
        else
        {
            _header = result.Header;
            _payload = result.Payload;
            _signature = result.Signature;
            TokenMeta.Text = $"{token.Length:N0}자";
            SignatureOutput.Text = result.Signature.Length > 0 ? result.Signature : "(서명 없음)";
            AlgorithmMeta.Text = result.Algorithm ?? "";
            IssuedMeta.Text = result.IssuedAt is { } iat ? $"발급 {iat.ToLocalTime():yyyy-MM-dd HH:mm:ss}" : "";
            ShowExpiry(result.Expires);
        }

        RenderJson(HeaderOutput, _header, 0, [], -1);
        RefreshSearch();
    }

    private void ShowExpiry(DateTimeOffset? expires)
    {
        if (expires is not { } exp)
        {
            ExpiryChip.Visibility = Visibility.Collapsed;
            return;
        }
        TimeSpan left = exp - DateTimeOffset.Now;
        bool expired = left <= TimeSpan.Zero;
        ExpiryText.Text = $"만료 {exp.ToLocalTime():yyyy-MM-dd HH:mm:ss} · {(expired ? "만료됨" : Remaining(left) + " 남음")}";
        ExpiryText.SetResourceReference(TextBlock.ForegroundProperty, expired ? "DangerBrush" : "SuccessBrush");
        ExpiryChip.Visibility = Visibility.Visible;
    }

    private static string Remaining(TimeSpan left) =>
        left.TotalDays >= 1 ? $"{(int)left.TotalDays}일" :
        left.TotalHours >= 1 ? $"{(int)left.TotalHours}시간" :
        left.TotalMinutes >= 1 ? $"{(int)left.TotalMinutes}분" : "1분 미만";

    /// <summary>JSON 을 문법 색으로 칠하고, 찾은 곳은 배경을 깐다. 현재 찾은 곳의 Run 을 돌려준다.</summary>
    private static Run? RenderJson(RichTextBox box, string text, int queryLength, IReadOnlyList<int> hits, int current)
    {
        var paragraph = new Paragraph();
        Run? currentRun = null;

        void Emit(string segment, string? brush, Mark mark)
        {
            string[] lines = segment.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) paragraph.Inlines.Add(new LineBreak());
                if (lines[i].Length == 0) continue;
                var run = new Run(lines[i]);
                if (mark == Mark.Current)
                {
                    run.SetResourceReference(TextElement.BackgroundProperty, "PrimaryBrush");
                    run.SetResourceReference(TextElement.ForegroundProperty, "OnPrimaryBrush");
                    currentRun ??= run;
                }
                else
                {
                    if (brush != null) run.SetResourceReference(TextElement.ForegroundProperty, brush);
                    if (mark == Mark.Hit) run.SetResourceReference(TextElement.BackgroundProperty, "SearchHitBrush");
                }
                paragraph.Inlines.Add(run);
            }
        }

        int position = 0, first = 0;
        foreach (JsonToken token in JsonTools.Tokenize(text))
        {
            string? brush = token.Kind switch
            {
                JsonTokenKind.Property => "JsonPropertyBrush",
                JsonTokenKind.String => "JsonStringBrush",
                JsonTokenKind.Number => "JsonNumberBrush",
                JsonTokenKind.Literal => "JsonBooleanBrush",
                JsonTokenKind.Punctuation => "JsonPunctuationBrush",
                _ => null,
            };
            int start = position, end = position + token.Text.Length;
            position = end;

            // 이 조각과 겹치는 찾은 곳만 잘라 따로 칠한다(찾은 곳은 앞에서부터 차례대로 나온다)
            while (first < hits.Count && hits[first] + queryLength <= start) first++;
            int cursor = start;
            for (int k = first; k < hits.Count && hits[k] < end; k++)
            {
                int hitStart = Math.Max(hits[k], cursor), hitEnd = Math.Min(hits[k] + queryLength, end);
                if (hitEnd <= hitStart) continue;
                if (hitStart > cursor) Emit(text[cursor..hitStart], brush, Mark.None);
                Emit(text[hitStart..hitEnd], brush, k == current ? Mark.Current : Mark.Hit);
                cursor = hitEnd;
            }
            if (cursor < end) Emit(text[cursor..end], brush, Mark.None);
        }

        box.Document = NewDocument(paragraph);
        return currentRun;
    }

    // ── Payload 찾기 ───────────────────────────────────────────

    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenSearch();
            e.Handled = true;
        }
    }

    private void SearchToggle_Click(object sender, RoutedEventArgs e)
    {
        if (SearchBar.Visibility == Visibility.Visible) CloseSearch();
        else OpenSearch();
    }

    private void OpenSearch()
    {
        SearchBar.Visibility = Visibility.Visible;
        SearchToggle.Tag = "active";
        SearchBox.Focus();
        SearchBox.SelectAll();
        RefreshSearch();
    }

    private void CloseSearch()
    {
        SearchBar.Visibility = Visibility.Collapsed;
        SearchToggle.Tag = null;
        RefreshSearch();
    }

    private void SearchClose_Click(object sender, RoutedEventArgs e) => CloseSearch();
    private void SearchNext_Click(object sender, RoutedEventArgs e) => Step(1);
    private void SearchPrev_Click(object sender, RoutedEventArgs e) => Step(-1);
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshSearch();

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseSearch();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Step(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            e.Handled = true;
        }
    }

    private string Query => SearchBar.Visibility == Visibility.Visible ? SearchBox.Text : "";

    private void RefreshSearch()
    {
        string query = Query;
        _hits.Clear();
        if (query.Length > 0)
        {
            for (int i = _payload.IndexOf(query, StringComparison.OrdinalIgnoreCase); i >= 0;
                 i = _payload.IndexOf(query, i + query.Length, StringComparison.OrdinalIgnoreCase))
                _hits.Add(i);
        }
        _hitIndex = _hits.Count > 0 ? 0 : -1;
        ShowHits(query);
    }

    private void Step(int delta)
    {
        if (_hits.Count == 0) return;
        _hitIndex = (_hitIndex + delta + _hits.Count) % _hits.Count;
        ShowHits(Query);
    }

    private void ShowHits(string query)
    {
        SearchCount.Text = query.Length == 0 ? "" : _hits.Count == 0 ? "없음" : $"{_hitIndex + 1}/{_hits.Count}";
        Run? current = RenderJson(PayloadOutput, _payload, query.Length, _hits, _hitIndex);
        if (current is null) return;

        // 현재 찾은 곳이 보이도록 위에서 1/3 지점으로 스크롤(찾기 칸의 포커스는 그대로 둔다)
        PayloadOutput.UpdateLayout();
        Rect rect = current.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        if (!rect.IsEmpty && (rect.Top < 0 || rect.Bottom > PayloadOutput.ViewportHeight))
            PayloadOutput.ScrollToVerticalOffset(PayloadOutput.VerticalOffset + rect.Top - PayloadOutput.ViewportHeight / 3);
    }

    // ── 복사 ───────────────────────────────────────────────────

    private void CopyHeader_Click(object sender, RoutedEventArgs e) => Ui.Copy(_header.ReplaceLineEndings());
    private void CopyPayload_Click(object sender, RoutedEventArgs e) => Ui.Copy(_payload.ReplaceLineEndings());
    private void CopySignature_Click(object sender, RoutedEventArgs e) => Ui.Copy(_signature);
}
