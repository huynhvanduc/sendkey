using System.IO;
using SendKeyDemo;
using Xunit;

namespace SendKeyDemo.Tests;

public class MappingTests
{
    [Fact]
    public void ParseCsv_plain_rows()
    {
        var r = Mapping.ParseCsv("a,b,c\n1,2,3\n");
        Assert.Equal(2, r.Count);
        Assert.Equal(new[] { "a", "b", "c" }, r[0]);
        Assert.Equal(new[] { "1", "2", "3" }, r[1]);
    }

    [Fact]
    public void ParseCsv_quoted_field_with_comma_and_escaped_quote()
    {
        var r = Mapping.ParseCsv("x,\"if \"\"%RC%\"\"==\"\"0\"\"\",y\n");
        Assert.Single(r);
        Assert.Equal(new[] { "x", "if \"%RC%\"==\"0\"", "y" }, r[0]);
    }

    [Fact]
    public void ParseCsv_handles_crlf_and_missing_final_newline()
    {
        var r = Mapping.ParseCsv("a,b\r\n1,2");
        Assert.Equal(2, r.Count);
        Assert.Equal(new[] { "1", "2" }, r[1]);
    }

    [Fact]
    public void ParseCsv_blank_line_yields_single_empty_field()
    {
        var r = Mapping.ParseCsv("a\n\nb\n");
        Assert.Equal(3, r.Count);
        Assert.Equal(new[] { "" }, r[1]);
    }

    [Fact]
    public void Load_skips_header_and_blank_lines_and_sets_source_line()
    {
        var p = Path.GetTempFileName();
        File.WriteAllText(p,
            "cmdLabel,cmdVar,csharpLabel,csharpVar\n" +
            "CHECK_INPUT,%RC%,CHECK_INPUT,rc\n" +
            "\n" +
            "VALIDATE_DATE,%IN_DATE%,VALIDATE_DATE,inDate\n");
        var rows = Mapping.Load(p);
        File.Delete(p);

        Assert.Equal(2, rows.Count);
        Assert.Equal("CHECK_INPUT", rows[0].CmdLabel);
        Assert.Equal("%RC%", rows[0].CmdVar);
        Assert.Equal(2, rows[0].SourceLine);
        Assert.Equal("VALIDATE_DATE", rows[1].CmdLabel);
        Assert.Equal(4, rows[1].SourceLine);
    }

    [Fact]
    public void Load_throws_with_line_number_on_wrong_column_count()
    {
        var p = Path.GetTempFileName();
        File.WriteAllText(p,
            "cmdLabel,cmdVar,csharpLabel,csharpVar\n" +
            "CHECK_INPUT,%RC%,CHECK_INPUT\n");
        var ex = Assert.Throws<MappingFormatException>(() => Mapping.Load(p));
        File.Delete(p);
        Assert.Equal(2, ex.LineNumber);
    }

    [Fact]
    public void Load_trims_each_field()
    {
        var p = Path.GetTempFileName();
        File.WriteAllText(p,
            "cmdLabel,cmdVar,csharpLabel,csharpVar\n" +
            " CHECK_INPUT , %RC% , CHECK_INPUT , rc \n");
        var rows = Mapping.Load(p);
        File.Delete(p);
        Assert.Equal("CHECK_INPUT", rows[0].CmdLabel);
        Assert.Equal("rc", rows[0].CsharpVar);
    }

    [Theory]
    [InlineData(" :CHECK_INPUT ", "check_input")]
    [InlineData("check_input", "check_input")]
    [InlineData("CHECK   INPUT", "check input")]
    [InlineData(": CHECK_INPUT", "check_input")]
    public void NormalizeLabel_cases(string raw, string expected)
        => Assert.Equal(expected, Mapping.NormalizeLabel(raw));

    [Theory]
    [InlineData("%RC%", "%rc%")]
    [InlineData("  if  %RC%  ", "%rc%")]
    [InlineData("goto END_PROC", "end_proc")]
    [InlineData("IF \"%RC%\"==\"0\"", "\"%rc%\"==\"0\"")]
    public void NormalizeVar_cases(string raw, string expected)
        => Assert.Equal(expected, Mapping.NormalizeVar(raw));

    static IReadOnlyList<MapRow> SampleRows() => new List<MapRow>
    {
        new("CHECK_INPUT",  "%INPUT_FILE%", "CHECK_INPUT",   "inputFile", 2),
        new("CHECK_INPUT",  "%RC%",         "CHECK_INPUT",   "rc",        3),
        new("VALIDATE_DATE","%IN_DATE%",    "VALIDATE_DATE", "inDate",    4),
        new("VALIDATE_DATE","%RC%",         "VALIDATE_DATE", "rc",        5),
    };

    [Fact]
    public void Resolve_exact_match_returns_row()
    {
        var r = Mapping.Resolve(SampleRows(), " check_input ", "%rc%");
        Assert.Equal(LookupKind.Ok, r.Kind);
        Assert.Equal("rc", r.Row!.CsharpVar);
        Assert.Equal("CHECK_INPUT", r.Row!.CsharpLabel);
    }

    [Fact]
    public void Resolve_unknown_label()
        => Assert.Equal(LookupKind.NotFoundLabel,
            Mapping.Resolve(SampleRows(), "NOPE", "%rc%").Kind);

    [Fact]
    public void Resolve_unknown_var_returns_pick_list_for_that_label()
    {
        var r = Mapping.Resolve(SampleRows(), "CHECK_INPUT", "%WAT%");
        Assert.Equal(LookupKind.NeedPickVar, r.Kind);
        Assert.Equal(new[] { "%INPUT_FILE%", "%RC%" }, r.VarChoices);
    }

    [Fact]
    public void Resolve_empty_var_is_label_only_mode()
    {
        var r = Mapping.Resolve(SampleRows(), "VALIDATE_DATE", null);
        Assert.Equal(LookupKind.Ok, r.Kind);
        Assert.Equal("VALIDATE_DATE", r.Row!.CsharpLabel);
        Assert.Null(r.Warning);
    }

    [Fact]
    public void Resolve_duplicate_pair_reports_source_lines()
    {
        var rows = new List<MapRow>
        {
            new("L", "%X%", "L", "x",  2),
            new("L", "%X%", "L", "x2", 7),
        };
        var r = Mapping.Resolve(rows, "L", "%x%");
        Assert.Equal(LookupKind.Duplicate, r.Kind);
        Assert.Equal(new[] { 2, 7 }, r.DuplicateLines);
    }

    [Fact]
    public void Resolve_label_only_warns_when_csharp_label_differs()
    {
        var rows = new List<MapRow>
        {
            new("L", "%A%", "LabelA", "a", 2),
            new("L", "%B%", "LabelB", "b", 3),
        };
        var r = Mapping.Resolve(rows, "L", null);
        Assert.Equal(LookupKind.Ok, r.Kind);
        Assert.NotNull(r.Warning);
    }
}
