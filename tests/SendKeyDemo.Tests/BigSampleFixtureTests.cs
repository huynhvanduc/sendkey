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

    // Chạy đúng vòng lặp của BatchDialog (lọc #/blank → SplitBatchLine → Resolve → FindLabelLine)
    // trên CHÍNH file samples/BigSample/cmd-input.txt, in ra bảng [OK]/[LỖI] mà form sẽ hiện.
    // Bỏ phần EnsureBreakpoint/GoToLine (cần DTE sống).
    [Fact]
    public void Batch_mode_over_cmd_input_txt()
    {
        var rows = Mapping.Load(Path.Combine(Dir, "mapping.csv"));
        var raw = File.ReadAllLines(Path.Combine(Dir, "cmd-input.txt"))
            .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#"))
            .ToArray();

        int ok = 0, fail = 0;
        string? ifClauseLine = null;
        foreach (var line in raw)
        {
            var (lbl, v) = Mapping.SplitBatchLine(line);
            var res = Mapping.Resolve(rows, lbl, v.Length == 0 ? null : v);
            if (res.Kind != LookupKind.Ok)
            {
                _out.WriteLine($"[LỖI] {line,-34} — {res.Kind}");
                fail++;
                continue;
            }
            var row = res.Row!;
            var csFile = row.CsharpFile.Length == 0 ? "Program.cs" : row.CsharpFile;
            var ll = Mapping.FindLabelLine(Path.Combine(Dir, csFile), row.CsharpLabel, row.CsharpVar);
            if (ll.Kind != LabelLineKind.Ok)
            {
                _out.WriteLine($"[LỖI] {line,-34} — {ll.Kind} @ {csFile}");
                fail++;
                continue;
            }
            _out.WriteLine($"[OK]  {line,-34} → {csFile}:{ll.Line}  (watch: {row.CsharpVar})");
            if (v.Contains("NEQ")) ifClauseLine = $"{csFile}:{ll.Line}";
            ok++;
        }
        _out.WriteLine($"\nTổng: {ok} OK, {fail} lỗi / {raw.Length} dòng dữ liệu.");

        Assert.Equal(18, ok);
        Assert.Equal(2, fail);
        Assert.Equal("Program.cs:39", ifClauseLine);   // mệnh đề if -> anchor tới "if (rc != 0) …"
    }
}
