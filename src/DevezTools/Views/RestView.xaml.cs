using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DevezTools.Infrastructure;
using DevezTools.Services;

namespace DevezTools.Views;

public partial class RestView : UserControl
{
    private readonly ObservableCollection<KeyValueRow> _headers = [];
    private readonly ObservableCollection<KeyValueRow> _params = [];
    private readonly ObservableCollection<SavedRequest> _saved;
    private CancellationTokenSource? _sending;
    private string _response = "";

    public RestView()
    {
        InitializeComponent();

        MethodBox.ItemsSource = RestClient.Methods;
        MethodBox.SelectedIndex = 0;
        // 헤더·파라미터 줄은 목록 템플릿 안이라 Drafts 가 이름으로 찾지 못해 직접 맡긴다(줄이 바뀌면 UpdateRowState 가 다시 담는다)
        SavedRequest? draft = null;
        try { draft = Drafts.Get(RowsDraftKey) is string json ? JsonSerializer.Deserialize<SavedRequest>(json) : null; }
        catch (JsonException) { }
        _headers.CollectionChanged += Rows_CollectionChanged;
        _params.CollectionChanged += Rows_CollectionChanged;
        Fill(_headers, draft?.Headers ?? []);
        Fill(_params, draft?.Params ?? []);
        KvList.ItemsSource = _headers;
        SegmentedTabs.Attach(KvTabHost, KvTabs, KvTabIndicator);

        _saved = new ObservableCollection<SavedRequest>(RestStore.Load(out string? problem));
        if (problem != null) Ui.Toast(problem);
        SavedList.ItemsSource = _saved;
        UpdateSavedEmpty();
        UpdateRowState();
    }

    private const string RowsDraftKey = "rest.rows";

    private ObservableCollection<KeyValueRow> ActiveRows => ParamsTab.IsChecked == true ? _params : _headers;

    // ── 헤더·파라미터 표 ───────────────────────────────────────

    private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (KeyValueRow row in e.NewItems?.OfType<KeyValueRow>() ?? [])
            row.PropertyChanged += (_, _) => UpdateRowState();
        UpdateRowState();
    }

    // 탭 글자에 채운 행 수, 모두 선택 상자에 전부 켜졌는지를 보인다
    private void UpdateRowState()
    {
        if (!IsInitialized) return;
        HeadersTab.Content = Label("헤더", _headers);
        ParamsTab.Content = Label("파라미터", _params);
        SelectAll.IsChecked = ActiveRows.Count > 0 && ActiveRows.All(r => r.Enabled);
        Drafts.Set(RowsDraftKey, JsonSerializer.Serialize(new SavedRequest { Headers = [.. _headers], Params = [.. _params] }));

        static string Label(string name, IEnumerable<KeyValueRow> rows)
        {
            int count = rows.Count(r => r.Key.Trim().Length > 0);
            return count > 0 ? $"{name} {count}" : name;
        }
    }

    private void KvTab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        KvList.ItemsSource = ActiveRows;
        UpdateRowState();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        bool enabled = SelectAll.IsChecked == true;
        foreach (KeyValueRow row in ActiveRows) row.Enabled = enabled;
    }

    private void AddRow_Click(object sender, RoutedEventArgs e) => ActiveRows.Add(new KeyValueRow());

    private void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not KeyValueRow row) return;
        ObservableCollection<KeyValueRow> rows = _headers.Contains(row) ? _headers : _params;
        rows.Remove(row);
        if (rows.Count == 0) rows.Add(new KeyValueRow()); // 입력할 빈 행 하나는 남긴다
    }

    private void AuthButton_Click(object sender, RoutedEventArgs e)
    {
        AuthMenu.PlacementTarget = AuthButton;
        AuthMenu.IsOpen = true;
    }

    private void AuthBearer_Click(object sender, RoutedEventArgs e) => SetHeader("Authorization", "Bearer ");
    private void AuthBasic_Click(object sender, RoutedEventArgs e) => SetHeader("Authorization", "Basic ");
    private void AuthApiKey_Click(object sender, RoutedEventArgs e) => SetHeader("X-API-Key", "");

    /// <summary>같은 키가 있으면 값만 바꾸고, 없으면 빈 행을 쓰거나 새 행을 붙인다.</summary>
    private void SetHeader(string key, string value)
    {
        HeadersTab.IsChecked = true;
        KeyValueRow? row = _headers.FirstOrDefault(h => h.Key.Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                           ?? _headers.LastOrDefault(h => h.Key.Length == 0 && h.Value.Length == 0);
        if (row is null) _headers.Add(row = new KeyValueRow());
        row.Key = key;
        row.Value = value;
        row.Enabled = true;
    }

    private void MethodBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        BodyMeta.Text = MethodBox.SelectedItem as string == "GET" ? "GET 요청은 본문을 보내지 않습니다" : "";

    // ── 보내기 ─────────────────────────────────────────────────

    // 처리하지 못한 예외는 async void 를 거쳐 앱 공통 처리(알림)로 간다
    private async void UrlText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SendAsync();
    }

    private async void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.Control) return;
        e.Handled = true;
        await SendAsync();
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendAsync();

    private async Task SendAsync()
    {
        // 보내는 중에 다시 누르면 취소
        if (_sending != null)
        {
            _sending.Cancel();
            return;
        }

        string method = MethodBox.SelectedItem as string ?? "GET";
        string url = UrlText.Text.Trim();
        Uri? uri = url.Length == 0 ? null : RestClient.NormalizeUrl(RestClient.BuildUrl(url, _params));
        if (uri is null)
        {
            ShowStatus("주소를 확인해 주십시오", "DangerBrush");
            ResponseMeta.Text = "http 또는 https 주소를 입력해 주십시오";
            return;
        }

        using var sending = new CancellationTokenSource();
        _sending = sending;
        SendText.Text = "취소";
        ShowStatus("보내는 중…", "TextMutedBrush");
        ResponseMeta.Text = uri.ToString();
        try
        {
            RestResponse response = await RestClient.SendAsync(method, uri, _headers, BodyText.Text, sending.Token);
            ShowStatus($"{response.StatusCode} {response.Reason}".Trim(), response.StatusCode switch
            {
                < 300 => "SuccessBrush",
                < 400 => "PrimaryBrush",
                < 500 => "WarningBrush",
                _ => "DangerBrush",
            });
            ResponseMeta.Text = $"{response.ElapsedMs:N0}ms · {FormatSize(response.Bytes)}";
            ShowBody(response.Body);
        }
        catch (OperationCanceledException) when (sending.IsCancellationRequested)
        {
            ShowStatus("취소했습니다", "TextMutedBrush");
            ShowBody("");
        }
        catch (OperationCanceledException)
        {
            ShowStatus("시간 초과", "DangerBrush");
            ShowBody("30초 안에 응답이 오지 않았습니다.");
        }
        catch (Exception ex) // 취소 말고는 모두 실패로 보인다(깨진 gzip 본문 같은 예외도 상태가 '보내는 중'에 머물지 않게)
        {
            ShowStatus("요청 실패", "DangerBrush");
            ShowBody(ex.InnerException is { } inner ? $"{ex.Message}\n{inner.Message}" : ex.Message);
        }
        finally
        {
            _sending = null;
            SendText.Text = "보내기";
        }
    }

    private void ShowStatus(string text, string brushKey)
    {
        StatusText.Text = text;
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        StatusChip.Visibility = Visibility.Visible;
    }

    // 아주 큰 응답은 앞부분만 보여 화면이 멈추지 않게 한다(복사는 전체)
    private void ShowBody(string body)
    {
        _response = body;
        ResponseText.Text = body.Length > RestClient.MaxShownChars
            ? body[..RestClient.MaxShownChars] + $"\n\n… 앞 {RestClient.MaxShownChars:N0}자만 표시합니다. 복사하면 전체가 복사됩니다."
            : body;
    }

    internal static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes}B" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#}KB" : $"{bytes / 1024.0 / 1024.0:0.#}MB";

    private void CopyResponse_Click(object sender, RoutedEventArgs e) => Ui.Copy(_response);

    // ── 저장한 요청 ────────────────────────────────────────────

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string url = UrlText.Text.Trim();
        if (url.Length == 0)
        {
            Ui.Toast("저장할 주소를 먼저 입력해 주십시오");
            return;
        }

        string method = MethodBox.SelectedItem as string ?? "GET";
        var request = new SavedRequest
        {
            Name = NameFor(url),
            Method = method,
            Url = url,
            Headers = _headers.Where(h => h.Key.Length > 0 || h.Value.Length > 0).Select(h => h.Clone()).ToList(),
            Params = _params.Where(p => p.Key.Length > 0 || p.Value.Length > 0).Select(p => p.Clone()).ToList(),
            Body = BodyText.Text,
        };

        int existing = _saved.ToList().FindIndex(s => s.Method == method && s.Url == url);
        if (existing >= 0) _saved[existing] = request;
        else _saved.Insert(0, request);
        SavedList.SelectedItem = request;
        UpdateSavedEmpty();
        Ui.Toast(!RestStore.Save(_saved) ? "요청을 저장하지 못했습니다" : existing >= 0 ? "저장한 요청을 수정했습니다" : "요청을 저장했습니다");
    }

    /// <summary>목록에 보일 이름: scheme·쿼리를 뺀 호스트와 경로.</summary>
    internal static string NameFor(string url)
    {
        int scheme = url.IndexOf("://", StringComparison.Ordinal);
        string name = scheme >= 0 ? url[(scheme + 3)..] : url;
        int query = name.IndexOfAny(['?', '#']);
        if (query >= 0) name = name[..query];
        name = name.TrimEnd('/');
        return name.Length > 0 ? name : url;
    }

    private void SavedItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject)) return; // 삭제 단추는 불러오지 않는다
        if ((sender as ListBoxItem)?.DataContext is not SavedRequest request) return;

        MethodBox.SelectedItem = RestClient.Methods.Contains(request.Method) ? request.Method : "GET";
        UrlText.Text = request.Url;
        BodyText.Text = request.Body;
        Fill(_headers, request.Headers);
        Fill(_params, request.Params);
    }

    private static void Fill(ObservableCollection<KeyValueRow> target, IEnumerable<KeyValueRow> rows)
    {
        target.Clear();
        foreach (KeyValueRow row in rows) target.Add(row.Clone());
        if (target.Count == 0) target.Add(new KeyValueRow());
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        for (DependencyObject? node = source; node is not null and not ListBoxItem;
             node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is ButtonBase) return true;
        }
        return false;
    }

    private void DeleteSaved_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SavedRequest request) return;
        _saved.Remove(request);
        UpdateSavedEmpty();
        if (!RestStore.Save(_saved)) Ui.Toast("목록을 저장하지 못했습니다");
    }

    private void UpdateSavedEmpty() => SavedEmpty.Visibility = _saved.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}
