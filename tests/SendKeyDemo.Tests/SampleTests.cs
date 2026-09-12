using Xunit.Abstractions;

namespace SendKeyDemo.Tests;

// Test trên bộ mẫu samples/BigSample, chạy đúng logic app dùng (bỏ phần cần VS đang chạy):
// - mapping.csv (4 & 5 cột trộn) tra được mọi label trong Program.cs / Steps.cs — như nút "Kiểm tra".
// - tc-input-jp.txt: mỗi khối lấy dòng dưới "ラベル" làm label, dòng dưới "確認値" làm biến, cho qua
//   CopiedText.Classify rồi tra mapping.csv — giống hệt lúc app nghe clipboard.
public class BigSampleTests
{
    readonly ITestOutputHelper _out;
    public BigSampleTests(ITestOutputHelper o) => _out = o;

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

    // Luồng điều hướng thật: 「%RC%」 trong CHECK_INPUT rơi vào 3 dòng, mỗi dòng một hình dạng khác nhau
    // nên Watch của từng dòng cũng phải khác nhau.
    [Fact]
    public void Rc_in_CHECK_INPUT_lands_on_three_lines_each_with_its_own_watch()
    {
        var lines = File.ReadAllLines(Path.Combine(Dir, "Program.cs"));
        var label = Assert.Single(Mapping.FindLabelsByPrefix(lines, "CHECK_INPUT"));
        Assert.Equal(new[] { 34, 35, 36 }, Mapping.FindInLabel(lines, label.LabelLine, "rc"));

        Assert.Equal(new[] { "rc", "inputFile.EndsWith(\".csv\") ? 0 : 12" }, Mapping.WatchFor(lines[33], "rc"));
        Assert.Equal(new[] { "rc" }, Mapping.WatchFor(lines[34], "rc"));            // dòng log
        Assert.Equal(new[] { "rc != 0" }, Mapping.WatchFor(lines[35], "rc"));       // dòng if
    }

    // Nhãn NAME_* trong BigSample có rc / rc2 / rcTotal cạnh nhau — tìm "rc" không được dính tên dài hơn.
    [Fact]
    public void Plain_identifier_search_does_not_catch_a_longer_name()
    {
        var lines = File.ReadAllLines(Path.Combine(Dir, "Program.cs"));
        var nameSet = Assert.Single(Mapping.FindLabelsByPrefix(lines, "NAME_SET"));

        var rc = Mapping.FindInLabel(lines, nameSet.LabelLine, "rc").Select(ln => lines[ln - 1].Trim()).ToList();
        Assert.Contains("rc = 0;", rc);
        Assert.DoesNotContain("rc2 = 5;", rc);

        var rc2 = Mapping.FindInLabel(lines, nameSet.LabelLine, "rc2").Select(ln => lines[ln - 1].Trim()).ToList();
        Assert.Contains("rc2 = 5;", rc2);
        Assert.DoesNotContain("rc = 0;", rc2);
    }

    // ---------- mapping.csv khớp code ----------

    [Fact]
    public void Mapping_csv_loads_with_mixed_column_counts()
    {
        var rows = Mapping.Load(Path.Combine(Dir, "mapping.csv"));
        Assert.Equal(28, rows.Count);
        Assert.Equal("", rows.First(r => r.CmdLabel == "INIT").CsharpFile);
        Assert.Equal("Steps.cs", rows.First(r => r.CmdLabel == "RECALC").CsharpFile);
    }

    [Fact]
    public void Every_row_resolves_to_an_executable_line()
    {
        var rows = Mapping.Load(Path.Combine(Dir, "mapping.csv"));
        var res = Mapping.Validate(rows, r =>
        {
            var p = r.CsharpFile.Length == 0
                ? Path.Combine(Dir, "Program.cs")
                : Path.Combine(Dir, r.CsharpFile);
            return File.Exists(p) ? File.ReadAllLines(p) : null;
        });
        Assert.Empty(res.Problems);
        Assert.Equal(rows.Count, res.Ok);       // trỏ đúng file cho từng dòng thì không dòng nào bị bỏ sót
        Assert.Equal(0, res.Unchecked);
    }

    // ---------- tc-input-jp.txt: luồng copy từ Excel ----------

    record Block(string Tc, string LabelLine, string VarLine);

    static List<Block> ReadBlocks()
    {
        var lines = File.ReadAllLines(Path.Combine(Dir, "tc-input-jp.txt"));
        var blocks = new List<Block>();
        string tc = "", label = "";

        for (int i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("TC-")) { tc = t.Split(' ')[0]; continue; }
            if (t == "ラベル") { label = NextNonEmpty(lines, i); continue; }
            if (t == "確認値" && label.Length > 0)
            {
                blocks.Add(new Block(tc, label, NextNonEmpty(lines, i)));
                label = "";
            }
        }
        return blocks;
    }

    static string NextNonEmpty(string[] lines, int from)
    {
        for (int i = from + 1; i < lines.Length; i++)
            if (lines[i].Trim().Length > 0) return lines[i].Trim();
        return "";
    }

    [Fact]
    public void File_has_all_eighteen_blocks()
        => Assert.Equal(18, ReadBlocks().Count);

    [Fact]
    public void Label_line_classifies_as_label_and_var_line_as_variable()
    {
        foreach (var b in ReadBlocks())
        {
            var lbl = CopiedText.Classify(b.LabelLine);
            Assert.True(lbl is { IsVar: false }, $"{b.Tc}: dòng label không nhận ra — \"{b.LabelLine}\"");

            var v = CopiedText.Classify(b.VarLine);
            Assert.True(v is { IsVar: true }, $"{b.Tc}: dòng 確認値 phải bóc ra biến — \"{b.VarLine}\"");
        }
    }

    [Fact]
    public void Fifteen_pairs_resolve_and_three_are_deliberately_missing()
    {
        var rows = Mapping.Load(Path.Combine(Dir, "mapping.csv"));
        var missing = new List<string>();
        int ok = 0;
        string? clauseLine = null, fullWidthLine = null, plainRcLine = null;

        foreach (var b in ReadBlocks())
        {
            var label = CopiedText.Classify(b.LabelLine)!.Value;
            var cmdVar = CopiedText.Classify(b.VarLine)!.Value;

            var res = Mapping.Resolve(rows, label, cmdVar);
            if (res.Kind != LookupKind.Ok)
            {
                _out.WriteLine($"[CHƯA CÓ] {b.Tc}  {label} / {cmdVar} — {res.Kind}");
                missing.Add(b.Tc);
                continue;
            }

            var row = res.Row!;
            var csFile = row.CsharpFile.Length == 0 ? "Program.cs" : row.CsharpFile;
            var ll = Mapping.FindLabelLine(Path.Combine(Dir, csFile), row.CsharpLabel, row.CsharpVar);
            Assert.True(ll.Kind == LabelLineKind.Ok, $"{b.Tc}: {row.CsharpLabel} không ra dòng — {ll.Kind}");

            _out.WriteLine($"[OK] {b.Tc}  {label} / {cmdVar}  → {csFile}:{ll.Line}  (watch: {row.CsharpVar})");
            ok++;

            if (b.Tc == "TC-04") plainRcLine = $"{csFile}:{ll.Line}";
            if (b.Tc == "TC-11") fullWidthLine = $"{csFile}:{ll.Line}";
            if (b.Tc == "TC-12") clauseLine = $"{csFile}:{ll.Line}";
        }

        Assert.Equal(15, ok);
        Assert.Equal(new[] { "TC-16", "TC-17", "TC-18" }, missing);

        // Full-width ％ＲＣ％ phải ra đúng chỗ như %RC% thường.
        Assert.Equal(plainRcLine, fullWidthLine);
        // Mệnh đề trong 『 』 phải anchor tới dòng "if (rc != 0) …", không phải đầu label.
        Assert.Equal("Program.cs:36", clauseLine);
        Assert.NotEqual(plainRcLine, clauseLine);
    }

    // Phần E của file hướng dẫn gõ tay csharpLabel/csharpVar — kiểm để hướng dẫn không lạc hậu
    // khi Program.cs đổi.
    [Theory]
    [InlineData("RECALC_VIA_HELPER", "total")]
    [InlineData("ROLLBACK_VIA_HELPER", "rc")]
    [InlineData("END_PROC", "rc")]
    public void Suggested_csharp_pairs_validate_against_the_code(string csLabel, string csVar)
    {
        var ll = Mapping.FindLabelLine(Path.Combine(Dir, "Program.cs"), csLabel, csVar);
        Assert.Equal(LabelLineKind.Ok, ll.Kind);
    }

    [Fact]
    public void Wrong_suggestion_in_the_file_really_fails()
    {
        // File bảo gõ NOSUCH để xem báo đỏ — đảm bảo nó vẫn hỏng thật.
        var ll = Mapping.FindLabelLine(Path.Combine(Dir, "Program.cs"), "NOSUCH", "rc");
        Assert.Equal(LabelLineKind.NotFound, ll.Kind);
    }

    [Fact]
    public void Section_F_lines_are_ignored_by_the_clipboard_watcher()
    {
        var text = File.ReadAllText(Path.Combine(Dir, "tc-input-jp.txt"));
        var prose = text.Split('\n')
            .Select(l => l.Trim())
            .First(l => l.StartsWith("本テストケース"));

        Assert.Null(CopiedText.Classify(prose));    // câu mô tả dài, không ngoặc
        Assert.Null(CopiedText.Classify("「」"));
        Assert.Null(CopiedText.Classify("「　」"));
    }
}
