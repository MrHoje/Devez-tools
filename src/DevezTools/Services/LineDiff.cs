namespace DevezTools.Services;

public enum DiffKind { Equal, Added, Removed }

/// <summary>나란히 비교의 한 행. 줄 번호는 1부터, 그 쪽에 없는 줄이면 0.</summary>
public readonly record struct DiffRow(DiffKind Kind, int LeftLine, int RightLine, string Text);

/// <summary>
/// 줄 단위 비교(talkRemind WPF 의 MdLineDiff 이식). 같은 앞·뒤 줄을 먼저 걷어 내고 남은 가운데를 LCS 표로 맞춘다.
/// 가운데가 표로 다루기에 너무 크면 양쪽에 한 번씩만 나오는 줄을 기준점으로 잘라 나눠 비교하고(patience diff),
/// 기준점도 없으면 그 구간만 통째로 삭제+추가로 표시해 메모리 폭증을 막는다.
/// </summary>
public static class LineDiff
{
    private const long LcsCellCap = 4_000_000;
    private const int MaxDepth = 64;

    public static List<DiffRow> Compute(string original, string modified)
    {
        string[] a = Split(original);
        string[] b = Split(modified);
        var rows = new List<DiffRow>(Math.Max(a.Length, b.Length));
        Diff(a, 0, a.Length, b, 0, b.Length, rows, 0);
        return rows;
    }

    private static void Diff(string[] a, int aStart, int aEnd, string[] b, int bStart, int bEnd, List<DiffRow> rows, int depth)
    {
        while (aStart < aEnd && bStart < bEnd && a[aStart] == b[bStart])
        {
            rows.Add(new DiffRow(DiffKind.Equal, aStart + 1, bStart + 1, a[aStart]));
            aStart++;
            bStart++;
        }

        int suffix = 0;
        while (aEnd - suffix > aStart && bEnd - suffix > bStart && a[aEnd - suffix - 1] == b[bEnd - suffix - 1]) suffix++;
        aEnd -= suffix;
        bEnd -= suffix;

        if ((long)(aEnd - aStart) * (bEnd - bStart) <= LcsCellCap)
        {
            Lcs(a, aStart, aEnd, b, bStart, bEnd, rows);
        }
        else if (depth < MaxDepth && UniqueAnchors(a, aStart, aEnd, b, bStart, bEnd) is { Count: > 0 } anchors)
        {
            foreach ((int x, int y) in anchors)
            {
                Diff(a, aStart, x, b, bStart, y, rows, depth + 1);
                rows.Add(new DiffRow(DiffKind.Equal, x + 1, y + 1, a[x]));
                aStart = x + 1;
                bStart = y + 1;
            }
            Diff(a, aStart, aEnd, b, bStart, bEnd, rows, depth + 1);
        }
        else
        {
            for (int i = aStart; i < aEnd; i++) rows.Add(new DiffRow(DiffKind.Removed, i + 1, 0, a[i]));
            for (int j = bStart; j < bEnd; j++) rows.Add(new DiffRow(DiffKind.Added, 0, j + 1, b[j]));
        }

        for (int k = 0; k < suffix; k++) rows.Add(new DiffRow(DiffKind.Equal, aEnd + k + 1, bEnd + k + 1, a[aEnd + k]));
    }

    private static void Lcs(string[] a, int aStart, int aEnd, string[] b, int bStart, int bEnd, List<DiffRow> rows)
    {
        int n = aEnd - aStart, m = bEnd - bStart;
        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                lcs[i, j] = a[aStart + i] == b[bStart + j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[aStart + x] == b[bStart + y])
            {
                rows.Add(new DiffRow(DiffKind.Equal, aStart + x + 1, bStart + y + 1, a[aStart + x]));
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                rows.Add(new DiffRow(DiffKind.Removed, aStart + x + 1, 0, a[aStart + x]));
                x++;
            }
            else
            {
                rows.Add(new DiffRow(DiffKind.Added, 0, bStart + y + 1, b[bStart + y]));
                y++;
            }
        }
        for (; x < n; x++) rows.Add(new DiffRow(DiffKind.Removed, aStart + x + 1, 0, a[aStart + x]));
        for (; y < m; y++) rows.Add(new DiffRow(DiffKind.Added, 0, bStart + y + 1, b[bStart + y]));
    }

    /// <summary>양쪽 구간에 딱 한 번씩 나오는 줄 가운데 순서가 어긋나지 않는 가장 긴 줄(LIS)을 기준점으로 고른다.</summary>
    private static List<(int A, int B)> UniqueAnchors(string[] a, int aStart, int aEnd, string[] b, int bStart, int bEnd)
    {
        // 줄 → 위치, 두 번 이상 나오면 -1
        var inA = new Dictionary<string, int>(aEnd - aStart);
        for (int i = aStart; i < aEnd; i++) inA[a[i]] = inA.ContainsKey(a[i]) ? -1 : i;
        var inB = new Dictionary<string, int>(bEnd - bStart);
        for (int j = bStart; j < bEnd; j++) inB[b[j]] = inB.ContainsKey(b[j]) ? -1 : j;

        var pairs = new List<(int A, int B)>();
        for (int i = aStart; i < aEnd; i++)
            if (inA[a[i]] == i && inB.TryGetValue(a[i], out int j) && j >= 0) pairs.Add((i, j));

        // a 순서로 놓인 짝에서 b 위치가 커지는 가장 긴 부분 수열(patience sorting)
        var tails = new List<int>();
        var previous = new int[pairs.Count];
        for (int p = 0; p < pairs.Count; p++)
        {
            int lo = 0, hi = tails.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (pairs[tails[mid]].B < pairs[p].B) lo = mid + 1;
                else hi = mid;
            }
            previous[p] = lo > 0 ? tails[lo - 1] : -1;
            if (lo == tails.Count) tails.Add(p);
            else tails[lo] = p;
        }

        var anchors = new List<(int A, int B)>(tails.Count);
        for (int p = tails.Count > 0 ? tails[^1] : -1; p >= 0; p = previous[p]) anchors.Add(pairs[p]);
        anchors.Reverse();
        return anchors;
    }

    // 빈 글은 줄이 하나도 없는 것으로 본다(빈 쪽과 비교하면 모두 추가·삭제로 나오게).
    // 세로 탭도 TextBox 는 줄바꿈으로 세므로 같이 나눠야 강조가 한 줄씩 밀리지 않는다.
    private static string[] Split(string text) => text.Length == 0 ? [] : text.ReplaceLineEndings("\n").Replace('\v', '\n').Split('\n');
}
