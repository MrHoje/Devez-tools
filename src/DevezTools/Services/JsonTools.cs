using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevezTools.Services;

public enum JsonTokenKind { Text, Property, String, Number, Literal, Punctuation }

public readonly record struct JsonToken(JsonTokenKind Kind, string Text);

public static partial class JsonTools
{
    // 한글을 \uXXXX 로 바꾸지 않게 느슨한 인코더를 쓴다. 줄바꿈은 \n 하나로 통일한다.
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>들여쓴 JSON. 문법 오류면 JsonException.</summary>
    public static string Format(string json, bool indented = true)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement, indented ? Indented : Compact);
    }

    /// <summary>오류 위치를 사람이 읽는 말로(줄·글자는 1부터).</summary>
    public static string DescribeError(JsonException ex)
    {
        string message = ex.Message;
        int cut = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        if (cut > 0) message = message[..cut].TrimEnd(' ', '|');
        return ex.LineNumber is long line
            ? $"{line + 1}번째 줄 {(ex.BytePositionInLine ?? 0) + 1}번째 글자: {message}"
            : message;
    }

    [GeneratedRegex("""(?<key>"(?:\\.|[^"\\])*")(?=\s*:)|(?<string>"(?:\\.|[^"\\])*")|(?<number>-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)|(?<literal>\b(?:true|false|null)\b)|(?<punct>[{}\[\]:,])""")]
    private static partial Regex TokenRegex();

    /// <summary>문법 색칠용 조각. 이어 붙이면 원문과 같다.</summary>
    public static IEnumerable<JsonToken> Tokenize(string text)
    {
        int last = 0;
        foreach (Match m in TokenRegex().Matches(text))
        {
            if (m.Index > last) yield return new JsonToken(JsonTokenKind.Text, text[last..m.Index]);
            JsonTokenKind kind =
                m.Groups["key"].Success ? JsonTokenKind.Property :
                m.Groups["string"].Success ? JsonTokenKind.String :
                m.Groups["number"].Success ? JsonTokenKind.Number :
                m.Groups["literal"].Success ? JsonTokenKind.Literal : JsonTokenKind.Punctuation;
            yield return new JsonToken(kind, m.Value);
            last = m.Index + m.Length;
        }
        if (last < text.Length) yield return new JsonToken(JsonTokenKind.Text, text[last..]);
    }
}
