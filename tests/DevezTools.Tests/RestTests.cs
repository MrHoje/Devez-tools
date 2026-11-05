using DevezTools.Infrastructure;
using DevezTools.Services;
using DevezTools.Views;

namespace DevezTools.Tests;

public class RestTests
{
    private static KeyValueRow Row(string key, string value, bool enabled = true) => new() { Key = key, Value = value, Enabled = enabled };

    [Theory]
    [InlineData("https://x.io/a", "https://x.io/a?q=%ED%95%9C%20%EA%B8%80&n=1")]
    [InlineData("https://x.io/a?z=0", "https://x.io/a?z=0&q=%ED%95%9C%20%EA%B8%80&n=1")]
    [InlineData("https://x.io/a?", "https://x.io/a?q=%ED%95%9C%20%EA%B8%80&n=1")]
    [InlineData("https://x.io/a#top", "https://x.io/a?q=%ED%95%9C%20%EA%B8%80&n=1#top")]
    public void BuildUrl_appends_enabled_params(string url, string expected)
    {
        KeyValueRow[] rows = [Row("q", "한 글"), Row("off", "x", enabled: false), Row("", "no key"), Row("n", "1")];
        Assert.Equal(expected, RestClient.BuildUrl(url, rows));
    }

    [Fact]
    public void BuildUrl_without_params_keeps_url() => Assert.Equal("http://a/b?c", RestClient.BuildUrl("http://a/b?c", []));

    [Theory]
    [InlineData("localhost:5000/api", "http://localhost:5000/api")]
    [InlineData("  https://x.io  ", "https://x.io/")]
    public void NormalizeUrl_adds_http(string input, string expected) => Assert.Equal(expected, RestClient.NormalizeUrl(input)?.ToString());

    [Theory]
    [InlineData("ftp://x.io/file")]
    [InlineData("http://")]
    public void NormalizeUrl_rejects_non_http(string input) => Assert.Null(RestClient.NormalizeUrl(input));

    [Fact]
    public void NormalizeUrl_ignores_scheme_text_inside_query() =>
        Assert.Equal("http://localhost:8080/cb?next=http://x", RestClient.NormalizeUrl("localhost:8080/cb?next=http://x")?.ToString());

    [Fact]
    public async Task Bad_header_name_is_reported_not_dropped()
    {
        // 보내기 전에 헤더를 붙이다 실패하므로 서버가 없어도 된다
        KeyValueRow[] headers = [Row("X Bad", "1")];
        FormatException ex = await Assert.ThrowsAsync<FormatException>(() =>
            RestClient.SendAsync("GET", new Uri("http://127.0.0.1:9/"), headers, "", CancellationToken.None));
        Assert.Contains("X Bad", ex.Message);
    }

    [Fact]
    public void Json_responses_are_pretty_others_untouched()
    {
        Assert.Equal("{" + Environment.NewLine + "  \"a\": 1" + Environment.NewLine + "}", RestClient.PrettyIfJson("""{"a":1}"""));
        Assert.Equal("{not json", RestClient.PrettyIfJson("{not json"));
        Assert.Equal("<html/>", RestClient.PrettyIfJson("<html/>"));
    }

    [Theory]
    [InlineData("https://api.x.io/users/?page=2", "api.x.io/users")]
    [InlineData("localhost:8080/health#x", "localhost:8080/health")]
    [InlineData("https://", "https://")]
    public void Saved_request_name_is_host_and_path(string url, string expected) => Assert.Equal(expected, RestView.NameFor(url));

    [Theory]
    [InlineData(512L, "512B")]
    [InlineData(1536L, "1.5KB")]
    [InlineData(3L * 1024 * 1024, "3MB")]
    public void Sizes_are_readable(long bytes, string expected) => Assert.Equal(expected, RestView.FormatSize(bytes));

    [Fact]
    public void Card_stats_count_lines_and_chars()
    {
        Assert.Equal("", Ui.Stats(""));
        Assert.Equal("2줄 · 3자", Ui.Stats("a\nb"));
        Assert.Equal("1줄 · 1,200자", Ui.Stats(new string('x', 1200)));
    }
}
