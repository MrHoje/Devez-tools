using System.Text.Json;
using System.Xml;
using DevezTools.Services;

namespace DevezTools.Tests;

public class FormatAndRegexTests
{
    [Fact]
    public void Json_is_indented_with_lf_or_compacted()
    {
        Assert.Equal("{\n  \"a\": [\n    1,\n    true\n  ],\n  \"b\": \"한글\"\n}", JsonTools.Format("""{"a":[1,true],"b":"한글"}"""));
        Assert.Equal("""{"a":[1,true]}""", JsonTools.Format("{ \"a\" : [ 1 , true ] }", indented: false));
    }

    [Fact]
    public void Json_error_points_to_one_based_line()
    {
        JsonException ex = Assert.ThrowsAny<JsonException>(() => JsonTools.Format("{\n  \"a\": 1,\n}"));
        string message = JsonTools.DescribeError(ex);
        Assert.StartsWith("3번째 줄", message);
        Assert.DoesNotContain("LineNumber", message);
    }

    [Fact]
    public void Json_tokens_rebuild_the_original_and_know_their_kinds()
    {
        const string json = "{\n  \"key\": \"v\\\"x\",\n  \"n\": -1.5e3,\n  \"ok\": null\n}";
        List<JsonToken> tokens = JsonTools.Tokenize(json).ToList();

        Assert.Equal(json, string.Concat(tokens.Select(t => t.Text)));
        Assert.Contains(new JsonToken(JsonTokenKind.Property, "\"key\""), tokens);
        Assert.Contains(new JsonToken(JsonTokenKind.String, "\"v\\\"x\""), tokens);
        Assert.Contains(new JsonToken(JsonTokenKind.Number, "-1.5e3"), tokens);
        Assert.Contains(new JsonToken(JsonTokenKind.Literal, "null"), tokens);
    }

    [Fact]
    public void Xml_keeps_declaration_and_indents()
    {
        string formatted = XmlTools.Format("<?xml version=\"1.0\" encoding=\"utf-8\"?><a><b>1</b></a>");
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", formatted);
        Assert.Contains("  <b>1</b>", formatted);
    }

    [Fact]
    public void Xml_error_has_position_once()
    {
        XmlException ex = Assert.Throws<XmlException>(() => XmlTools.Format("<a>\n<b></a>"));
        string message = XmlTools.DescribeError(ex);
        Assert.StartsWith("2번째 줄", message);
        Assert.DoesNotContain("Line 2, position", message);
    }

    [Fact]
    public void Regex_lists_matches_with_groups()
    {
        RegexRunResult result = RegexRunner.Run(@"(\d{3})-(?<tail>\d{4})", "call 010-1234 or 999-0000", ignoreCase: false, multiline: false);

        Assert.Null(result.Error);
        Assert.Equal(2, result.Total);
        Assert.Equal(new RegexMatchRow(1, 5, 8, "010-1234", "1: 010  tail: 1234"), result.Rows[0]);
        Assert.Equal(17, result.Rows[1].Position);
    }

    [Fact]
    public void Regex_options_and_invalid_pattern()
    {
        Assert.Equal(1, RegexRunner.Run("^b", "a\nb", ignoreCase: false, multiline: true).Total);
        Assert.Equal(0, RegexRunner.Run("^b", "a\nb", ignoreCase: false, multiline: false).Total);
        Assert.Equal(2, RegexRunner.Run("a", "aA", ignoreCase: true, multiline: false).Total);

        RegexRunResult bad = RegexRunner.Run("(", "x", false, false);
        Assert.StartsWith("패턴 오류", bad.Error);
        Assert.Empty(bad.Rows);
    }

    [Fact]
    public void Regex_shows_only_first_rows_but_counts_all()
    {
        RegexRunResult result = RegexRunner.Run("x", new string('x', 1200), false, false);
        Assert.Equal(1200, result.Total);
        Assert.Equal(RegexRunner.MaxRows, result.Rows.Count);
    }

    [Fact]
    public void Regex_display_shows_line_breaks_and_empty_matches()
    {
        Assert.Equal("a↵b→c", new RegexMatchRow(1, 0, 5, "a\r\nb\tc", "").Display);
        Assert.Equal("(빈 일치)", new RegexMatchRow(1, 0, 0, "", "").Display);
    }
}
