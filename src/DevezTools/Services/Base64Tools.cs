using System.Text;

namespace DevezTools.Services;

public static class Base64Tools
{
    // 깨진 바이트를 '?'(U+FFFD)로 몰래 바꾸지 않고 예외로 알린다(다시 인코딩하면 원본과 달라지므로)
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    /// <summary>표준·URL 안전 형식, 줄바꿈, 빠진 '=' 를 모두 받아 UTF-8 글자로 푼다.
    /// Base64 형식이 틀리면 FormatException, UTF-8 글자가 아니면 DecoderFallbackException.</summary>
    public static string Decode(string base64)
    {
        var s = new StringBuilder(base64.Length + 2);
        foreach (char c in base64)
        {
            if (char.IsWhiteSpace(c)) continue;
            s.Append(c switch { '-' => '+', '_' => '/', _ => c });
        }
        while (s.Length % 4 != 0) s.Append('=');
        return StrictUtf8.GetString(Convert.FromBase64String(s.ToString()));
    }
}
