using System.IO;
using System.Linq;
using SendKeyDemo;
using Xunit;

namespace SendKeyDemo.Tests;

// Kiểm tra bộ fixture samples/BigSample khớp nhau: mapping.csv (4 & 5 cột trộn) tra được
// mọi label trong Program.cs / Steps.cs. Chạy cùng logic mà app dùng ở "Kiểm tra mapping.csv".
public class BigSampleFixtureTests
{
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
}
