using DevezTools.Services;

namespace DevezTools.Tests;

public class LineDiffTests
{
    [Fact]
    public void Same_text_is_all_equal()
    {
        List<DiffRow> rows = LineDiff.Compute("a\nb\nc", "a\r\nb\r\nc");
        Assert.All(rows, r => Assert.Equal(DiffKind.Equal, r.Kind));
        Assert.Equal([1, 2, 3], rows.Select(r => r.LeftLine));
        Assert.Equal([1, 2, 3], rows.Select(r => r.RightLine));
    }

    [Fact]
    public void Changed_line_is_removed_then_added_with_line_numbers()
    {
        List<DiffRow> rows = LineDiff.Compute("a\nb\nc", "a\nB\nc\nd");
        Assert.Equal(
            [
                new DiffRow(DiffKind.Equal, 1, 1, "a"),
                new DiffRow(DiffKind.Removed, 2, 0, "b"),
                new DiffRow(DiffKind.Added, 0, 2, "B"),
                new DiffRow(DiffKind.Equal, 3, 3, "c"),
                new DiffRow(DiffKind.Added, 0, 4, "d"),
            ],
            rows);
    }

    [Fact]
    public void Lines_moved_in_the_middle_keep_the_longest_common_part()
    {
        List<DiffRow> rows = LineDiff.Compute("1\n2\n3\n4\n5", "1\n3\n4\n2\n5");
        Assert.Equal(3 + 1, rows.Count(r => r.Kind == DiffKind.Equal)); // 1,3,4,5 같음
        Assert.Single(rows, r => r.Kind == DiffKind.Removed && r.Text == "2");
        Assert.Single(rows, r => r.Kind == DiffKind.Added && r.Text == "2");
    }

    [Fact]
    public void Vertical_tab_breaks_lines_like_the_text_box()
    {
        List<DiffRow> rows = LineDiff.Compute("a\vb\vc", "a\vB\vc");
        Assert.Equal([2], rows.Where(r => r.Kind == DiffKind.Removed).Select(r => r.LeftLine));
        Assert.Equal(3, rows[^1].LeftLine);
    }

    [Fact]
    public void Empty_side_has_no_lines()
    {
        Assert.Empty(LineDiff.Compute("", ""));
        List<DiffRow> rows = LineDiff.Compute("", "x\ny");
        Assert.All(rows, r => Assert.Equal(DiffKind.Added, r.Kind));
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Big_text_with_scattered_edits_marks_only_changed_lines()
    {
        // 끝에 줄을 더해 같은 뒷줄이 없고 가운데가 LCS 표 상한을 넘는 경우: 기준점으로 나눠 실제 바뀐 줄만 표시해야 한다
        string big = string.Join('\n', Enumerable.Range(0, 20000).Select(i => $"line {i}: value = {i * 7};"));
        string edited = big.Replace("line 5000:", "line 5000 (edited):").Replace("line 15000:", "LINE 15000:") + "\nappended";

        List<DiffRow> rows = LineDiff.Compute(big, edited);

        Assert.Equal([5001, 15001], rows.Where(r => r.Kind == DiffKind.Removed).Select(r => r.LeftLine));
        Assert.Equal([5001, 15001, 20001], rows.Where(r => r.Kind == DiffKind.Added).Select(r => r.RightLine));
        Assert.Equal(19998, rows.Count(r => r.Kind == DiffKind.Equal));
    }

    [Fact]
    public void Huge_middle_falls_back_to_remove_and_add_without_table()
    {
        string left = string.Join('\n', Enumerable.Range(0, 2500).Select(i => "L" + i));
        string right = string.Join('\n', Enumerable.Range(0, 2500).Select(i => "R" + i));
        List<DiffRow> rows = LineDiff.Compute("same\n" + left + "\nend", "same\n" + right + "\nend");
        Assert.Equal(2500, rows.Count(r => r.Kind == DiffKind.Removed));
        Assert.Equal(2500, rows.Count(r => r.Kind == DiffKind.Added));
        Assert.Equal(DiffKind.Equal, rows[0].Kind);
        Assert.Equal(DiffKind.Equal, rows[^1].Kind);
        Assert.Equal(2502, rows[^1].LeftLine);
    }
}
