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

    [Fact]
    public void FindLabelLine_first_executable_line_after_label()
    {
        var src = new[]
        {
            "static void Main() {",
            "    CHECK_INPUT:",
            "",
            "        // set rc",
            "        rc = 0;",
            "        goto END;",
        };
        var r = Mapping.FindLabelLineInText(src, "CHECK_INPUT");
        Assert.Equal(LabelLineKind.Ok, r.Kind);
        Assert.Equal(5, r.Line);
    }

    [Fact]
    public void FindLabelLine_skips_lone_brace_and_block_comment()
    {
        var src = new[] { "  L:", "  {", "  /* a", "     b */", "  DoThing();" };
        var r = Mapping.FindLabelLineInText(src, "L");
        Assert.Equal(LabelLineKind.Ok, r.Kind);
        Assert.Equal(5, r.Line);
    }

    [Fact]
    public void FindLabelLine_skips_preprocessor_line()
    {
        var src = new[] { "  L:", "#pragma warning restore CS0164", "  rc = 0;" };
        var r = Mapping.FindLabelLineInText(src, "L");
        Assert.Equal(LabelLineKind.Ok, r.Kind);
        Assert.Equal(3, r.Line);
    }

    [Fact]
    public void FindLabelLine_statement_on_same_line_as_label()
    {
        var r = Mapping.FindLabelLineInText(new[] { "  L: rc = 0;", "  goto END;" }, "L");
        Assert.Equal(LabelLineKind.Ok, r.Kind);
        Assert.Equal(1, r.Line);
    }

    [Fact]
    public void FindLabelLine_not_found()
        => Assert.Equal(LabelLineKind.NotFound,
            Mapping.FindLabelLineInText(new[] { "x", "y" }, "L").Kind);

    [Fact]
    public void FindLabelLine_multiple_matches()
    {
        var r = Mapping.FindLabelLineInText(new[] { "L:", "  a();", "L:", "  b();" }, "L");
        Assert.Equal(LabelLineKind.Multiple, r.Kind);
        Assert.Equal(new[] { 1, 3 }, r.MatchLines);
    }

    [Fact]
    public void FindLabelLine_no_executable_line_after_label()
        => Assert.Equal(LabelLineKind.NoExecutableLine,
            Mapping.FindLabelLineInText(new[] { "  a();", "  L:", "" }, "L").Kind);

    [Fact]
    public void FindLabelLine_ignores_goto_and_string_occurrences()
    {
        var src = new[]
        {
            "  goto CHECK_INPUT;",
            "  Console.WriteLine(\"CHECK_INPUT: hi\");",
            "  CHECK_INPUT:",
            "  rc = 1;",
        };
        var r = Mapping.FindLabelLineInText(src, "CHECK_INPUT");
        Assert.Equal(LabelLineKind.Ok, r.Kind);
        Assert.Equal(4, r.Line);
    }

    [Theory]
    [InlineData(" :Check Input : ", "Check Input")]
    [InlineData("CHECK_INPUT", "CHECK_INPUT")]
    [InlineData("  END_PROC  ", "END_PROC")]
    [InlineData("A\t B", "A B")]
    public void CleanLabel_trims_strips_colons_keeps_case(string raw, string expected)
        => Assert.Equal(expected, Mapping.CleanLabel(raw));

    [Fact]
    public void AppendRow_keeps_existing_rows_and_adds_one()
    {
        var p = Path.GetTempFileName();
        File.WriteAllText(p,
            "cmdLabel,cmdVar,csharpLabel,csharpVar\n" +
            "CHECK_INPUT,%RC%,CHECK_INPUT,rc\n");
        Mapping.AppendRow(p, new MapRow("NEW_LABEL", "%X%", "NEW_LABEL", "x", 0));
        var rows = Mapping.Load(p);
        File.Delete(p);

        Assert.Equal(2, rows.Count);
        Assert.Equal("CHECK_INPUT", rows[0].CmdLabel);
        Assert.Equal("NEW_LABEL", rows[1].CmdLabel);
        Assert.Equal("x", rows[1].CsharpVar);
    }

    [Fact]
    public void AppendRow_adds_newline_when_file_has_none()
    {
        var p = Path.GetTempFileName();
        File.WriteAllText(p,
            "cmdLabel,cmdVar,csharpLabel,csharpVar\n" +
            "CHECK_INPUT,%RC%,CHECK_INPUT,rc");          // không có newline cuối
        Mapping.AppendRow(p, new MapRow("L2", "%Y%", "L2", "y", 0));
        var rows = Mapping.Load(p);
        File.Delete(p);

        Assert.Equal(2, rows.Count);
        Assert.Equal("rc", rows[0].CsharpVar);           // dòng cũ không bị dính
        Assert.Equal("L2", rows[1].CmdLabel);
    }

    [Fact]
    public void AppendRow_quotes_fields_with_comma_or_quote_roundtrip()
    {
        var p = Path.GetTempFileName();
        File.WriteAllText(p, "cmdLabel,cmdVar,csharpLabel,csharpVar\n");
        Mapping.AppendRow(p, new MapRow("CHECK", "if \"%RC%\" NEQ \"0\"", "CHECK", "rc != 0", 0));
        var rows = Mapping.Load(p);
        File.Delete(p);

        Assert.Single(rows);
        Assert.Equal("if \"%RC%\" NEQ \"0\"", rows[0].CmdVar);
        Assert.Equal("rc != 0", rows[0].CsharpVar);
    }

    [Fact]
    public void Validate_clean_rows_no_problems()
    {
        var rows = new List<MapRow>
        {
            new("CHECK_INPUT", "%RC%", "CHECK_INPUT", "rc", 2),
        };
        var cs = new[] { "  CHECK_INPUT:", "  rc = 0;" };
        Assert.Empty(Mapping.Validate(rows, _ => cs));
    }

    [Fact]
    public void Validate_flags_duplicate_pair_with_source_lines()
    {
        var rows = new List<MapRow>
        {
            new("L", "%X%", "L", "x",  2),
            new("L", " %x% ", "L", "x2", 6),
        };
        var p = Mapping.Validate(rows, _ => null);
        Assert.Single(p);
        Assert.Contains("2, 6", p[0]);
    }

    [Fact]
    public void Validate_flags_label_missing_in_cs()
    {
        var rows = new List<MapRow> { new("L", "%X%", "NOSUCH", "x", 3) };
        var cs = new[] { "  L:", "  rc = 0;" };
        var p = Mapping.Validate(rows, _ => cs);
        Assert.Single(p);
        Assert.Contains("dòng 3", p[0]);
        Assert.Contains("NOSUCH", p[0]);
    }

    [Fact]
    public void Validate_null_lines_skips_label_checks()
    {
        var rows = new List<MapRow> { new("L", "%X%", "NOSUCH", "x", 3) };
        Assert.Empty(Mapping.Validate(rows, _ => null));
    }

    [Fact]
    public void Load_accepts_optional_fifth_csharpFile_column()
    {
        var p = Path.GetTempFileName();
        File.WriteAllText(p,
            "cmdLabel,cmdVar,csharpLabel,csharpVar,csharpFile\n" +
            "A,%X%,A,x,sub/Foo.cs\n" +
            "B,%Y%,B,y\n");                              // dòng 4 cột vẫn hợp lệ
        var rows = Mapping.Load(p);
        File.Delete(p);

        Assert.Equal(2, rows.Count);
        Assert.Equal("sub/Foo.cs", rows[0].CsharpFile);
        Assert.Equal("", rows[1].CsharpFile);
    }

    [Fact]
    public void Load_throws_on_six_columns()
    {
        var p = Path.GetTempFileName();
        File.WriteAllText(p,
            "cmdLabel,cmdVar,csharpLabel,csharpVar,csharpFile\n" +
            "A,%X%,A,x,f.cs,extra\n");
        var ex = Assert.Throws<MappingFormatException>(() => Mapping.Load(p));
        File.Delete(p);
        Assert.Equal(2, ex.LineNumber);
    }

    [Fact]
    public void AppendRow_writes_five_columns_when_csharpFile_set()
    {
        var p = Path.GetTempFileName();
        File.WriteAllText(p, "cmdLabel,cmdVar,csharpLabel,csharpVar,csharpFile\n");
        Mapping.AppendRow(p, new MapRow("A", "%X%", "A", "x", 0, "sub/Foo.cs"));
        var rows = Mapping.Load(p);
        File.Delete(p);

        Assert.Single(rows);
        Assert.Equal("sub/Foo.cs", rows[0].CsharpFile);
        Assert.Equal("x", rows[0].CsharpVar);
    }

    [Theory]
    [InlineData("CHECK_INPUT\t%RC%", "CHECK_INPUT", "%RC%")]
    [InlineData("CHECK_INPUT   %RC%", "CHECK_INPUT", "%RC%")]
    [InlineData("  VALIDATE_DATE  ", "VALIDATE_DATE", "")]
    [InlineData("L\tif \"%RC%\" NEQ \"0\"", "L", "if \"%RC%\" NEQ \"0\"")]
    public void SplitBatchLine_cases(string raw, string label, string var)
    {
        var (l, v) = Mapping.SplitBatchLine(raw);
        Assert.Equal(label, l);
        Assert.Equal(var, v);
    }
}
