using System.Text.RegularExpressions;

namespace DevezTools.Services;

public sealed record RegexMatchRow(int Index, int Position, int Length, string Value, string Groups)
{
    /// <summary>목록 한 줄에 보이는 값: 줄바꿈은 ↵, 탭은 →, 빈 일치는 따로 표시.</summary>
    public string Display => Value.Length == 0 ? "(빈 일치)" : Value.ReplaceLineEndings("↵").Replace('\t', '→');
}

/// <summary>Rows 는 앞에서부터 최대 표시 수까지만 담고, Total 은 전체 일치 수다. Error 가 있으면 둘 다 비어 있다.</summary>
public sealed record RegexRunResult(IReadOnlyList<RegexMatchRow> Rows, int Total, string? Error);

public static class RegexRunner
{
    public const int MaxRows = 500;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    public static RegexRunResult Run(string pattern, string input, bool ignoreCase, bool multiline)
    {
        var options = RegexOptions.None;
        if (ignoreCase) options |= RegexOptions.IgnoreCase;
        if (multiline) options |= RegexOptions.Multiline;

        Regex regex;
        try
        {
            regex = new Regex(pattern, options, Timeout);
        }
        catch (ArgumentException ex)
        {
            return new RegexRunResult([], 0, "패턴 오류: " + ex.Message);
        }

        try
        {
            string[] groupNames = regex.GetGroupNames().Where(n => n != "0").ToArray();
            var rows = new List<RegexMatchRow>();
            int total = 0;
            // 제한 시간은 일치 한 번마다 걸리므로, 일치가 많을 때를 위해 전체 시간도 따로 잰다
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (Match m = regex.Match(input); m.Success; m = m.NextMatch())
            {
                if (watch.Elapsed > Timeout) throw new RegexMatchTimeoutException();
                total++;
                if (rows.Count < MaxRows)
                {
                    string groups = string.Join("  ", groupNames
                        .Where(n => m.Groups[n].Success)
                        .Select(n => $"{n}: {m.Groups[n].Value}"));
                    rows.Add(new RegexMatchRow(total, m.Index, m.Length, m.Value, groups));
                }
            }
            return new RegexRunResult(rows, total, null);
        }
        catch (RegexMatchTimeoutException)
        {
            return new RegexRunResult([], 0, $"시간 초과: {Timeout.TotalSeconds:0}초 안에 찾기를 끝내지 못했습니다. 패턴을 확인해 주십시오.");
        }
    }
}
