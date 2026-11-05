using System.Text.Json;

namespace DevezTools.Services;

/// <summary>서명은 검증하지 않고 헤더·페이로드만 풀어 보는 디버그용 디코더.</summary>
public static class JwtDecoder
{
    public const string DecodeFailed = "[디코드 실패]";

    public sealed record Result(string Header, string Payload, string Signature, string? Algorithm, DateTimeOffset? Expires, DateTimeOffset? IssuedAt);

    /// <summary>점(.)으로 나눈 조각이 2~3개가 아니면 null.</summary>
    public static Result? Decode(string token)
    {
        string[] parts = token.Trim().Split('.');
        if (token.Trim().Length == 0 || parts.Length is < 2 or > 3) return null;

        return new Result(
            DecodeSegment(parts[0]),
            DecodeSegment(parts[1]),
            parts.Length == 3 ? parts[2] : "",
            ReadClaim(parts[0], "alg") is { ValueKind: JsonValueKind.String } alg ? alg.GetString() : null,
            ToTime(ReadClaim(parts[1], "exp")),
            ToTime(ReadClaim(parts[1], "iat")));
    }

    // 초 단위 숫자를 시각으로 읽는다(RFC 7519 NumericDate 는 소수도 허용, 밀리초처럼 범위를 벗어난 값은 버린다).
    private static DateTimeOffset? ToTime(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.Number } number && number.TryGetDouble(out double seconds)
        && seconds >= DateTimeOffset.MinValue.ToUnixTimeSeconds() && seconds <= DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            ? DateTimeOffset.FromUnixTimeSeconds((long)Math.Floor(seconds))
            : null;

    public static string DecodeSegment(string segment)
    {
        try
        {
            return JsonTools.Format(Base64Tools.Decode(segment));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return DecodeFailed;
        }
    }

    /// <summary>조각을 풀어 최상위 객체의 값 하나를 꺼낸다. 없거나 풀리지 않으면 null.</summary>
    private static JsonElement? ReadClaim(string segment, string claim)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(Base64Tools.Decode(segment));
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(claim, out JsonElement value)
                ? value.Clone()
                : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
