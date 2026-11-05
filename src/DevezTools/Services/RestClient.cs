using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevezTools.Services;

/// <summary>헤더·파라미터 표의 한 행. 모두 선택 체크박스처럼 코드에서 바꿔도 화면이 따라오게 알림을 보낸다.</summary>
public sealed class KeyValueRow : INotifyPropertyChanged
{
    private bool _enabled = true;
    private string _key = "";
    private string _value = "";

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string Key { get => _key; set => Set(ref _key, value); }
    public string Value { get => _value; set => Set(ref _value, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public KeyValueRow Clone() => new() { Enabled = Enabled, Key = Key, Value = Value };

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class SavedRequest
{
    public string Name { get; set; } = "";
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = "";
    public List<KeyValueRow> Headers { get; set; } = [];
    public List<KeyValueRow> Params { get; set; } = [];
    public string Body { get; set; } = "";
}

public sealed record RestResponse(int StatusCode, string Reason, long ElapsedMs, long Bytes, string Body);

public static class RestClient
{
    public static readonly string[] Methods = ["GET", "POST", "PUT", "PATCH", "DELETE"];
    public const int MaxShownChars = 1_000_000;

    // 쿠키는 저장하지 않는다: 표에 보이지 않는 쿠키가 다음 요청에 몰래 실리면 로그아웃·권한 시험 결과가 틀어진다.
    private static readonly HttpClient Http = new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All, UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(30),
        MaxResponseContentBufferSize = 50 * 1024 * 1024,
    };

    /// <summary>주소에 켜진 파라미터를 붙인다. 이미 ? 가 있으면 & 로 잇고, # 조각은 맨 뒤에 둔다.</summary>
    public static string BuildUrl(string url, IEnumerable<KeyValueRow> parameters)
    {
        string[] pairs = parameters.Where(p => p.Enabled && p.Key.Length > 0)
            .Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))
            .ToArray();
        if (pairs.Length == 0) return url;

        int hash = url.IndexOf('#');
        string fragment = hash >= 0 ? url[hash..] : "";
        string head = hash >= 0 ? url[..hash] : url;
        string separator = head.Contains('?') ? (head.EndsWith('?') || head.EndsWith('&') ? "" : "&") : "?";
        return head + separator + string.Join("&", pairs) + fragment;
    }

    /// <summary>scheme 이 없으면 http:// 를 붙인다. http·https 절대 주소가 아니면 null.</summary>
    public static Uri? NormalizeUrl(string url)
    {
        url = url.Trim();
        // 쿼리 안의 "://"(예: ?next=http://x) 는 scheme 이 아니므로 맨 앞만 본다
        if (!Regex.IsMatch(url, @"^[a-zA-Z][a-zA-Z0-9+.\-]*://")) url = "http://" + url;
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;
    }

    public static async Task<RestResponse> SendAsync(string method, Uri uri, IEnumerable<KeyValueRow> headers, string body, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), uri);
        KeyValueRow[] active = headers.Where(h => h.Enabled && h.Key.Trim().Length > 0).ToArray();

        if (method != "GET" && body.Length > 0)
        {
            string? contentType = active.LastOrDefault(h => h.Key.Trim().Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value;
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var parsed)
                ? parsed
                : new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }

        foreach (KeyValueRow h in active)
        {
            // 문서에서 "Authorization:" 처럼 콜론까지 복사해 와도 받아 준다
            string key = h.Key.Trim().TrimEnd(':').Trim();
            if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            if (!request.Headers.TryAddWithoutValidation(key, h.Value) && request.Content?.Headers.TryAddWithoutValidation(key, h.Value) != true)
                throw new FormatException($"헤더 이름 '{key}' 은(는) 보낼 수 없습니다. 공백이나 특수 문자를 확인해 주십시오.");
        }
        if (!request.Headers.Contains("User-Agent")) request.Headers.TryAddWithoutValidation("User-Agent", "devezTools/1.0");

        // 응답 글자 변환·JSON 정렬(최대 50MB)은 화면 스레드가 아닌 곳에서 한다
        var watch = Stopwatch.StartNew();
        using HttpResponseMessage response = await Http.SendAsync(request, cancel).ConfigureAwait(false);
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancel).ConfigureAwait(false);
        watch.Stop();

        Encoding encoding = Encoding.UTF8;
        try
        {
            if (response.Content.Headers.ContentType?.CharSet is { Length: > 0 } charset) encoding = Encoding.GetEncoding(charset.Trim('"'));
        }
        catch (ArgumentException)
        {
            // 모르는 문자 집합은 UTF-8 로 읽는다.
        }

        return new RestResponse((int)response.StatusCode, response.ReasonPhrase ?? "", watch.ElapsedMilliseconds, bytes.LongLength,
            PrettyIfJson(encoding.GetString(bytes)));
    }

    /// <summary>JSON 응답은 보기 좋게 정렬하고, 아니면 그대로 둔다.</summary>
    public static string PrettyIfJson(string body)
    {
        string trimmed = body.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                return JsonTools.Format(body).ReplaceLineEndings();
            }
            catch (JsonException)
            {
                // JSON 처럼 보였지만 아니면 원문 그대로
            }
        }
        return body;
    }
}

/// <summary>%APPDATA%\devezTools\rest-requests.json 에 저장한 요청 목록.</summary>
public static class RestStore
{
    private static readonly string FilePath = Path.Combine(AppSettings.DataFolder, "rest-requests.json");

    // 읽지 못한 파일을 빈 목록으로 덮어써 기존 요청을 잃지 않도록, 그때는 이번 실행 동안 저장하지 않는다
    private static bool _readOnly;

    /// <summary>읽지 못하면 빈 목록과 함께 problem 에 이유를 돌려준다(깨진 파일은 옆에 옮겨 둔다).</summary>
    public static List<SavedRequest> Load(out string? problem)
    {
        problem = null;
        if (!File.Exists(FilePath)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<SavedRequest>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (JsonException)
        {
            string backup = $"{FilePath}.broken-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Move(FilePath, backup);
                problem = $"저장한 요청 파일이 깨져 {Path.GetFileName(backup)} 로 옮겨 두었습니다.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _readOnly = true;
                problem = "저장한 요청 파일이 깨져 읽지 못했습니다. 파일을 보호하기 위해 이번에는 저장하지 않습니다.";
            }
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _readOnly = true;
            problem = "저장한 요청 파일을 열 수 없습니다. 파일을 보호하기 위해 이번에는 저장하지 않습니다.";
            return [];
        }
    }

    /// <summary>저장하지 못하면 false.</summary>
    public static bool Save(IEnumerable<SavedRequest> requests)
    {
        if (_readOnly) return false;
        try
        {
            SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(requests, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
