using System.IO;
using System.Linq;
using SendKeyDemo;
using Xunit;
using Xunit.Abstractions;

namespace SendKeyDemo.Tests;

// Kiểm tra bộ fixture samples/BigSample khớp nhau: mapping.csv (4 & 5 cột trộn) tra được
// mọi label trong Program.cs / Steps.cs. Chạy cùng logic mà app dùng ở "Kiểm tra mapping.csv".
public class BigSampleFixtureTests
{
    readonly ITestOutputHelper _out;
    public BigSampleFixtureTests(ITestOutputHelper o) => _out = o;

    static string Dir
    {
        get
        {
            var d = AppContext.BaseDirectory;
            while (d != null && !File.Exists(Path.Combine(d, "SendKeyDemo.sln")))
                d = Path.GetDirectoryName(d);
            return Path.Combine(d!, "samples", "BigSample");
        }
    }

    [Fact]
    public void Mapping_csv_loads_with_mixed_column_counts()
    {
        var rows = Mapping.Load(Path.Combine(Dir, "mapping.csv"));
        Assert.Equal(17, rows.Count);
        Assert.Equal("", rows.First(r => r.CmdLabel == "INIT").CsharpFile);
        Assert.Equal("Steps.cs", rows.First(r => r.CmdLabel == "RECALC").CsharpFile);
    }

    [Fact]
    public void Every_row_resolves_to_an_executable_line()
    {
        var rows = Mapping.Load(Path.Combine(Dir, "mapping.csv"));
        var problems = Mapping.Validate(rows, r =>
        {
            var p = r.CsharpFile.Length == 0
                ? Path.Combine(Dir, "Program.cs")
                : Path.Combine(Dir, r.CsharpFile);
            return File.Exists(p) ? File.ReadAllLines(p) : null;
        });
        Assert.Empty(problems);
    }

    // Mô phỏng đúng vòng lặp của BatchDialog (SplitBatchLine → Resolve → FindLabelLine),
    // in ra bảng [OK]/[LỖI] mà form sẽ hiện. Bỏ phần EnsureBreakpoint/GoToLine (cần DTE).
    [Fact]
    public void Batch_mode_over_BigSample_paste()
    {
        var rows = Mapping.Load(Path.Combine(Dir, "mapping.csv"));

        var paste = new[]
        {
            "# khối test — dòng này bị bỏ qua",  // comment -> bỏ qua
            "",                                   // dòng trống -> bỏ qua
            "CHECK_INPUT\t%RC%",
            "VALIDATE_DETAIL\t%LINE_CNT%",
            "CALC_TOTAL   %TOTAL%",              // ngăn bằng ≥2 dấu cách
            "APPLY_DISCOUNT",                    // label-only
            "RECALC\t%AMT%",                     // sang Steps.cs qua csharpFile
            "AUDIT_LOG\t%MSG%",                  // sang Steps.cs
            "CHECK_INPUT\tif \"%RC%\" NEQ \"0\"",// mệnh đề if -> anchor tới dòng "if (rc != 0)"
            "NOPE\t%RC%",                        // label không có -> LỖI
            "CHECK_INPUT\t%WRONGVAR%",           // var không khớp -> LỖI
        };

        int ok = 0, fail = 0;
        foreach (var raw in paste)
        {
            if (raw.Trim().Length == 0 || raw.TrimStart().StartsWith("#")) continue;
            var (lbl, v) = Mapping.SplitBatchLine(raw);
            var res = Mapping.Resolve(rows, lbl, v.Length == 0 ? null : v);
            if (res.Kind != LookupKind.Ok)
            {
                _out.WriteLine($"[LỖI] {raw,-28} — {res.Kind}");
                fail++;
                continue;
            }
            var row = res.Row!;
            var csFile = row.CsharpFile.Length == 0 ? "Program.cs" : row.CsharpFile;
            var ll = Mapping.FindLabelLine(Path.Combine(Dir, csFile), row.CsharpLabel, row.CsharpVar);
            if (ll.Kind != LabelLineKind.Ok)
            {
                _out.WriteLine($"[LỖI] {raw,-34} — {ll.Kind} @ {csFile}");
                fail++;
                continue;
            }
            _out.WriteLine($"[OK]  {raw,-34} → {csFile}:{ll.Line}  (watch: {row.CsharpVar})");
            ok++;
        }
        _out.WriteLine($"\nTổng: {ok} OK, {fail} lỗi / {paste.Length} dòng.");

        Assert.Equal(7, ok);
        Assert.Equal(2, fail);
    }
}
