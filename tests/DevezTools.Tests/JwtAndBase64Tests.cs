using System.Text;
using DevezTools.Services;

namespace DevezTools.Tests;

public class JwtAndBase64Tests
{
    private static string Segment(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static readonly string Header = Segment("""{"alg":"HS256","typ":"JWT"}""");
    private static readonly string Payload = Segment("""{"sub":"42","name":"데브즈 도구","iat":1516239022,"exp":4102444800}""");

    [Fact]
    public void Decodes_header_payload_and_times()
    {
        JwtDecoder.Result? result = JwtDecoder.Decode($"{Header}.{Payload}.c2lnbmF0dXJl");

        Assert.NotNull(result);
        Assert.Equal("HS256", result.Algorithm);
        Assert.Contains("\"name\": \"데브즈 도구\"", result.Payload); // 한글을 \uXXXX 로 바꾸지 않는다
        Assert.Contains("\n  \"sub\": \"42\"", result.Payload);
        Assert.Equal("c2lnbmF0dXJl", result.Signature);
        Assert.Equal(new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero), result.Expires);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1516239022), result.IssuedAt);
    }

    [Fact]
    public void Two_parts_mean_no_signature()
    {
        JwtDecoder.Result? result = JwtDecoder.Decode($" {Header}.{Payload} ");
        Assert.NotNull(result);
        Assert.Equal("", result.Signature);
    }

    [Theory]
    [InlineData("")]
    [InlineData("onlyone")]
    [InlineData("a.b.c.d")]
    public void Wrong_part_count_is_not_a_token(string token) => Assert.Null(JwtDecoder.Decode(token));

    [Fact]
    public void Broken_segment_is_marked_and_bad_times_are_ignored()
    {
        string payload = Segment("""{"exp":1700000000000,"iat":"yesterday"}"""); // 밀리초 exp, 문자열 iat
        JwtDecoder.Result? result = JwtDecoder.Decode($"%%%.{payload}");

        Assert.NotNull(result);
        Assert.Equal(JwtDecoder.DecodeFailed, result.Header);
        Assert.Null(result.Algorithm);
        Assert.Null(result.Expires);
        Assert.Null(result.IssuedAt);
    }

    [Fact]
    public void Fractional_expiry_is_read_as_seconds()
    {
        JwtDecoder.Result? result = JwtDecoder.Decode($"{Header}.{Segment("""{"exp":1700000000.75}""")}");
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000000), result?.Expires);
    }

    [Fact]
    public void Base64_reports_bytes_that_are_not_utf8() =>
        Assert.Throws<DecoderFallbackException>(() => Base64Tools.Decode("//4=")); // 0xFF 0xFE

    [Fact]
    public void Base64_round_trips_korean()
    {
        string encoded = Base64Tools.Encode("안녕하세요 devez");
        Assert.Equal("안녕하세요 devez", Base64Tools.Decode(encoded));
    }

    [Fact]
    public void Base64_accepts_url_safe_missing_padding_and_line_breaks()
    {
        // "??>" 는 표준 형식에서 "Pz8+" 이다
        Assert.Equal("??>", Base64Tools.Decode("Pz8-"));
        Assert.Equal("ab", Base64Tools.Decode("YW\r\nI"));
    }

    [Fact]
    public void Base64_rejects_invalid_text() => Assert.Throws<FormatException>(() => Base64Tools.Decode("a"));
}
