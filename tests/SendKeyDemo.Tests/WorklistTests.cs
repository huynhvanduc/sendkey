using System.Linq;
using SendKeyDemo;
using Xunit;

namespace SendKeyDemo.Tests;

public class WorklistTests
{
    [Theory]
    [InlineData("TC-01\tCHECK_INPUT\t%RC%\t0", new[] { "TC-01", "CHECK_INPUT", "%RC%", "0" })]
    [InlineData("TC-01  CHECK_INPUT  %RC%  0", new[] { "TC-01", "CHECK_INPUT", "%RC%", "0" })]
    [InlineData("TC-01\tCHECK_INPUT", new[] { "TC-01", "CHECK_INPUT" })]
    [InlineData("VALIDATE_DATE", new[] { "VALIDATE_DATE" })]
    public void SplitColumns_cases(string raw, string[] expected)
        => Assert.Equal(expected, Worklist.SplitColumns(raw, 4));

    [Fact]
    public void SplitColumns_last_column_keeps_inner_spaces()
    {
        var c = Worklist.SplitColumns("TC-01\tL\t%RC%\thello world here", 4);
        Assert.Equal("hello world here", c[3]);
    }

    [Fact]
    public void SplitColumns_respects_max_and_does_not_over_split()
    {
        var c = Worklist.SplitColumns("a\tb\tc\td\te", 4);
        Assert.Equal(4, c.Length);
        Assert.Equal("d\te", c[3]);
    }

    [Fact]
    public void Parse_full_four_columns()
    {
        var items = Worklist.Parse("TC-01\tCHECK_INPUT\t%RC%\t0");
        var i = Assert.Single(items);
        Assert.Equal("TC-01", i.TcId);
        Assert.Equal("CHECK_INPUT", i.CmdLabel);
        Assert.Equal("%RC%", i.CmdVar);
        Assert.Equal("0", i.Expected);
    }

    [Fact]
    public void Parse_single_column_autonumbers_tcid()
    {
        var items = Worklist.Parse("CHECK_INPUT\nVALIDATE_DATE");
        Assert.Equal(new[] { "01", "02" }, items.Select(x => x.TcId));
        Assert.Equal("CHECK_INPUT", items[0].CmdLabel);
        Assert.Equal("", items[0].CmdVar);
    }

    [Fact]
    public void Parse_skips_blank_and_comment_lines()
    {
        var items = Worklist.Parse("# ghi chú\n\nTC-01\tL\n   \n# nữa\nTC-02\tM");
        Assert.Equal(2, items.Count);
        Assert.Equal("TC-01", items[0].TcId);
        Assert.Equal("TC-02", items[1].TcId);
    }

    [Fact]
    public void Parse_missing_optional_columns_become_empty()
    {
        var items = Worklist.Parse("TC-01\tCHECK_INPUT");
        Assert.Equal("", items[0].CmdVar);
        Assert.Equal("", items[0].Expected);
    }

    [Fact]
    public void ToLines_round_trips_through_Parse()
    {
        var original = Worklist.Parse("TC-01\tL\t%RC%\t0\nTC-02\tM\t\t");
        var list = new Worklist(original);
        var again = Worklist.Parse(string.Join("\n", list.ToLines()));
        Assert.Equal(original, again);
    }

    [Fact]
    public void MoveNextPending_skips_items_already_done()
    {
        var list = new Worklist(Worklist.Parse("TC-01\tA\nTC-02\tB\nTC-03\tC"));
        list.MarkDone("TC-02");

        Assert.True(list.MoveNextPending());
        Assert.Equal("TC-03", list.Current!.TcId);
    }

    [Fact]
    public void MoveNextPending_false_when_nothing_left()
    {
        var list = new Worklist(Worklist.Parse("TC-01\tA\nTC-02\tB"));
        list.MarkDone("TC-02");
        Assert.False(list.MoveNextPending());
        Assert.Equal("TC-01", list.Current!.TcId);   // giữ nguyên vị trí
    }

    [Fact]
    public void MarkDone_is_idempotent_and_counts_once()
    {
        var list = new Worklist(Worklist.Parse("TC-01\tA"));
        list.MarkDone("TC-01");
        list.MarkDone("TC-01");
        Assert.Equal(1, list.DoneCount);
        Assert.True(list.IsDone("tc-01"));           // không phân biệt hoa thường
    }

    [Fact]
    public void MoveTo_clamps_into_range()
    {
        var list = new Worklist(Worklist.Parse("TC-01\tA\nTC-02\tB"));
        list.MoveTo(99);
        Assert.Equal("TC-02", list.Current!.TcId);
        list.MoveTo(-5);
        Assert.Equal("TC-01", list.Current!.TcId);
    }

    [Fact]
    public void Empty_worklist_has_no_current()
    {
        var list = new Worklist(Worklist.Parse("   \n# chỉ có chú thích"));
        Assert.True(list.IsEmpty);
        Assert.Null(list.Current);
        Assert.False(list.MoveNextPending());
    }

    [Fact]
    public void Describe_omits_var_when_empty()
    {
        Assert.Equal("CHECK_INPUT", new WorkItem("T", "CHECK_INPUT", "", "").Describe());
        Assert.Equal("CHECK_INPUT/%RC%", new WorkItem("T", "CHECK_INPUT", "%RC%", "").Describe());
    }
}
