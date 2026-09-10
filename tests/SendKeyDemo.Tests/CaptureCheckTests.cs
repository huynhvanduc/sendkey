using SendKeyDemo;
using Xunit;

namespace SendKeyDemo.Tests;

public class CaptureCheckTests
{
    const string File1 = @"C:\proj\Program.cs";
    const string File2 = @"C:\proj\Steps.cs";
    const int Line = 19;
    const string Tc = "TC-017";

    static DebugSnapshot Snap(
        bool inBreak = true,
        string hitFile = File1,
        int hitLine = Line,
        int bpInFile = 1,
        bool exprValid = true,
        string exprValue = "0",
        int pid = 1234)
        => new(true, inBreak, hitFile, hitLine, bpInFile, exprValid, exprValue, pid);

    static CheckResult Run(DebugSnapshot s, LastCapture? prev = null, string expr = "rc")
        => CaptureCheck.Evaluate(s, Tc, File1, Line, expr, prev);

    // ---------- so giá trị ----------

    [Theory]
    [InlineData("0", "0", true)]
    [InlineData(" 0 ", "0", true)]
    [InlineData("\"abc\"", "abc", true)]
    [InlineData("'x'", "x", true)]
    [InlineData("True", "true", true)]
    [InlineData("0", "1", false)]
    [InlineData("\"abc\"", "abd", false)]
    public void ValuesMatch_cases(string actual, string expected, bool match)
        => Assert.Equal(match, CaptureCheck.ValuesMatch(actual, expected));

    // ---------- chặn cứng (lỗi thao tác cơ học) ----------

    [Fact]
    public void Blocks_when_vs_state_unreadable()
    {
        var r = Run(DebugSnapshot.Unavailable("VS đã đóng"));
        Assert.Equal(CheckLevel.Block, r.Level);
        Assert.Contains("VS đã đóng", r.Message);
    }

    [Fact]
    public void Blocks_when_not_in_break_mode()
    {
        var r = Run(Snap(inBreak: false));
        Assert.Equal(CheckLevel.Block, r.Level);
        Assert.Contains("Chưa dừng ở breakpoint", r.Message);
    }

    [Fact]
    public void Blocks_when_stopped_but_not_by_a_breakpoint()
    {
        var r = Run(Snap(hitFile: "", hitLine: 0));
        Assert.Equal(CheckLevel.Block, r.Level);
        Assert.Contains("không phải do breakpoint", r.Message);
    }

    [Fact]
    public void Blocks_when_stopped_at_wrong_line()
    {
        var r = Run(Snap(hitLine: 24));
        Assert.Equal(CheckLevel.Block, r.Level);
        Assert.Contains("Program.cs:24", r.Message);
        Assert.Contains("TC-017", r.Message);
    }

    [Fact]
    public void Blocks_when_stopped_in_wrong_file()
    {
        var r = Run(Snap(hitFile: File2));
        Assert.Equal(CheckLevel.Block, r.Level);
        Assert.Contains("Steps.cs", r.Message);
    }

    [Fact]
    public void Blocks_when_expression_cannot_be_evaluated()
    {
        var r = Run(Snap(exprValid: false, exprValue: ""));
        Assert.Equal(CheckLevel.Block, r.Level);
        Assert.Contains("Không đọc được giá trị", r.Message);
    }

    // ---------- hỏi lại (nghi chưa reset biến) ----------

    [Fact]
    public void Confirms_when_value_identical_to_previous_capture_in_same_session()
    {
        var prev = new LastCapture(1234, "TC-016", "rc", "0");
        var r = Run(Snap(exprValue: "0", pid: 1234), prev);
        Assert.Equal(CheckLevel.Confirm, r.Level);
        Assert.Contains("y hệt lần chụp TC-016", r.Message);
    }

    [Fact]
    public void Same_value_after_restart_is_not_suspicious()
    {
        // pid khác = đã Shift+F5 rồi F5 lại -> giá trị trùng là bình thường
        var prev = new LastCapture(1111, "TC-016", "rc", "0");
        var r = Run(Snap(exprValue: "0", pid: 2222), prev);
        Assert.Equal(CheckLevel.Ok, r.Level);
    }

    [Fact]
    public void Recapturing_the_same_test_case_is_not_suspicious()
    {
        var prev = new LastCapture(1234, "TC-017", "rc", "0");
        var r = Run(Snap(exprValue: "0", pid: 1234), prev);
        Assert.Equal(CheckLevel.Ok, r.Level);
    }

    // ---------- cho qua ----------

    [Fact]
    public void Ok_when_everything_matches()
    {
        var r = Run(Snap());
        Assert.Equal(CheckLevel.Ok, r.Level);
        Assert.Contains("TC-017", r.Message);
        Assert.Contains("rc = 0", r.Message);
        Assert.Contains("chụp được", r.Message);
    }

    [Fact]
    public void Ok_with_no_watch_expression_at_all()
    {
        var r = Run(Snap(exprValid: false, exprValue: ""), expr: "");
        Assert.Equal(CheckLevel.Ok, r.Level);
    }

    [Fact]
    public void Ok_but_mentions_leftover_breakpoints_in_the_file()
    {
        var r = Run(Snap(bpInFile: 3));
        Assert.Equal(CheckLevel.Ok, r.Level);
        Assert.Contains("còn 3 breakpoint", r.Message);
    }

    [Fact]
    public void Path_comparison_ignores_separators_and_case()
    {
        var r = CaptureCheck.Evaluate(
            Snap(hitFile: @"C:\proj\PROGRAM.CS"), Tc,
            @"C:\proj\.\Program.cs", Line, "rc", null);
        Assert.Equal(CheckLevel.Ok, r.Level);
    }
}
