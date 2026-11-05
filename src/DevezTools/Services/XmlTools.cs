using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace DevezTools.Services;

public static partial class XmlTools
{
    /// <summary>들여쓴 XML(선언이 있으면 유지). 문법 오류면 XmlException.</summary>
    public static string Format(string xml)
    {
        XDocument doc = XDocument.Parse(xml);
        string body = doc.ToString();
        return doc.Declaration is null ? body : doc.Declaration + Environment.NewLine + body;
    }

    [GeneratedRegex(@"\s*Line \d+, position \d+\.\s*$")]
    private static partial Regex PositionSuffix();

    public static string DescribeError(XmlException ex)
    {
        string message = PositionSuffix().Replace(ex.Message, "");
        return ex.LineNumber > 0 ? $"{ex.LineNumber}번째 줄 {ex.LinePosition}번째 글자: {message}" : message;
    }
}
