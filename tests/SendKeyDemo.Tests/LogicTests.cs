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

    static readonly string[] _labelBlock =
    {
        "    CHECK_INPUT:",                          // 1
        "        inputFile = \"orders.csv\";",       // 2
        "        rc = inputFile.Length > 3 ? 0 : 1;",// 3
        "        Console.WriteLine(rc);",            // 4
        "        if (rc != 0) goto ERR;",            // 5
        "",                                          // 6
        "    NEXT_STEP:",                            // 7
        "        if (rc != 0) goto ERR;",            // 8  (không được chạm — đã sang nhãn khác)
    };

    [Fact]
    public void FindLabelLine_plain_identifier_anchors_at_label_first_line()
    {
        var r = Mapping.FindLabelLineInText(_labelBlock, "CHECK_INPUT", "rc");
        Assert.Equal(LabelLineKind.Ok, r.Kind);
        Assert.Equal(2, r.Line);
    }

    [Fact]
    public void FindLabelLine_expression_anchor_jumps_to_matching_line()
    {
        var r = Mapping.FindLabelLineInText(_labelBlock, "CHECK_INPUT", "rc != 0");
        Assert.Equal(LabelLineKind.Ok, r.Kind);
        Assert.Equal(5, r.Line);
    }

    [Fact]
    public void FindLabelLine_expression_anchor_matches_despite_spacing()
    {
        var src = new[] { "  L:", "  a = 1;", "  if(rc!=0) return;" };
        var r = Mapping.FindLabelLineInText(src, "L", "rc != 0");
        Assert.Equal(LabelLineKind.Ok, r.Kind);
        Assert.Equal(3, r.Line);
    }

    [Fact]
    public void FindLabelLine_expression_anchor_not_found_stops_at_next_label()
    {
        var src = new[] { "  L:", "  a = 1;", "  M:", "  x == 9;" };
        var r = Mapping.FindLabelLineInText(src, "L", "x == 9");
        Assert.Equal(LabelLineKind.AnchorNotFound, r.Kind);
    }

    static readonly string[] _prefixBlock =
    {
        "    CHECK_INPUT:",                        // 1
        "        rc = 0;",                         // 2
        "    CHECK_INPUT_aa:",                     // 3
        "        rc = 1;",                         // 4
        "    CHECK_INPUT_aa2:",                    // 5
        "        goto CHECK_INPUT_aa;",            // 6  (không phải khai báo label)
        "    END_PROC:",                           // 7
        "        return;",                         // 8
    };

    [Fact]
    public void FindLabelsByPrefix_matches_exact_name_and_suffixed_names_in_line_order()
    {
        var hits = Mapping.FindLabelsByPrefix(_prefixBlock, "CHECK_INPUT");
        Assert.Equal(new[] { 1, 3, 5 }, hits.Select(h => h.LabelLine));
        Assert.Equal(new[] { "CHECK_INPUT", "CHECK_INPUT_aa", "CHECK_INPUT_aa2" }, hits.Select(h => h.Name));
    }

    [Fact]
    public void FindLabelsByPrefix_single_match_gives_the_real_label_name()
    {
        var hit = Assert.Single(Mapping.FindLabelsByPrefix(_prefixBlock, "CHECK_INPUT_aa2"));
        Assert.Equal(new LabelHit(5, 6, "CHECK_INPUT_aa2"), hit);
    }

    static readonly string[] _execBlock =
    {
        "    L_aa1: rc = 0;",                      // 1  lệnh ngay sau dấu hai chấm
        "    L_aa2:",                              // 2
        "",                                        // 3
        "        // ghi chú",                      // 4
        "        {",                               // 5
        "        rc = 1;",                         // 6
        "    L_aa3:",                              // 7  sau nhãn không còn dòng thực thi
        "        // hết",                          // 8
    };

    [Fact]
    public void FindLabelsByPrefix_exec_line_is_the_first_runnable_line_after_the_label()
    {
        // breakpoint phải rơi vào ExecLine; đặt ở dòng nhãn thì VS không bind, chương trình chạy thẳng
        var hits = Mapping.FindLabelsByPrefix(_execBlock, "L_aa");
        Assert.Equal(new[] { 1, 2, 7 }, hits.Select(h => h.LabelLine));
        Assert.Equal(new[] { 1, 6, 0 }, hits.Select(h => h.ExecLine));
    }

    [Fact]
    public void FindLabelsByPrefix_no_match_and_empty_prefix_give_nothing()
    {
        Assert.Empty(Mapping.FindLabelsByPrefix(_prefixBlock, "NOSUCH"));
        Assert.Empty(Mapping.FindLabelsByPrefix(_prefixBlock, ""));
        Assert.Empty(Mapping.FindLabelsByPrefix(_prefixBlock, "   "));
    }

    static readonly string[] _searchBlock =
    {
        "    L:",                                  // 1
        "        rc = 0;",                         // 2
        "        // if (rc != 0) goto SKIP;",      // 3  comment thường
        "        /* rc != 0 */",                   // 4  comment khối 1 dòng
        "        if (rc != 0) goto A;",            // 5
        "        x = 1;",                          // 6
        "        if(rc!=0) goto B;",               // 7  khác khoảng trắng, vẫn khớp
        "    M:",                                  // 8
        "        if (rc != 0) goto C;",            // 9  đã sang label khác
    };

    [Fact]
    public void FindInLabel_returns_every_matching_line_in_the_label()
        => Assert.Equal(new[] { 5, 7 }, Mapping.FindInLabel(_searchBlock, 1, "rc != 0"));

    [Fact]
    public void FindInLabel_stops_before_the_next_label()
        => Assert.Equal(new[] { 9 }, Mapping.FindInLabel(_searchBlock, 8, "rc != 0"));

    [Fact]
    public void FindInLabel_ignores_matches_inside_comments()
    {
        var src = new[] { "  L:", "  // rc != 0", "  /*", "  rc != 0", "  */", "  a = 1;" };
        Assert.Empty(Mapping.FindInLabel(src, 1, "rc != 0"));
    }

    static readonly string[] _wordBlock =
    {
        "    L:",                          // 1
        "        rc = 0;",                 // 2
        "        rc2 = 5;",                // 3  tên dài hơn — KHÔNG được dính khi tìm "rc"
        "        rcTotal = rc + rc2;",     // 4
        "        _rc = 1;",                // 5  có tiền tố — cũng không dính
        "        if (rc != 0) goto E;",    // 6
    };

    [Fact]
    public void FindInLabel_matches_a_plain_identifier_on_word_boundaries()
    {
        Assert.Equal(new[] { 2, 4, 6 }, Mapping.FindInLabel(_wordBlock, 1, "rc"));
        Assert.Equal(new[] { 3, 4 }, Mapping.FindInLabel(_wordBlock, 1, "rc2"));
    }

    [Fact]
    public void FindInLabel_still_matches_an_expression_as_plain_text()
        => Assert.Equal(new[] { 6 }, Mapping.FindInLabel(_wordBlock, 1, "rc != 0"));

    [Fact]
    public void FindInLabel_no_match_or_empty_expression_gives_nothing()
    {
        Assert.Empty(Mapping.FindInLabel(_searchBlock, 1, "zz == 9"));
        Assert.Empty(Mapping.FindInLabel(_searchBlock, 1, ""));
        Assert.Empty(Mapping.FindInLabel(_searchBlock, 99, "rc != 0"));
    }

    [Theory]
    // dòng if → mệnh đề, kể cả khi có goto cùng dòng
    [InlineData("        if (rc != 0) goto HANDLE_ERROR;", "rc", new[] { "rc != 0" })]
    [InlineData("        if (Check(rc) && (a || b)) { return; }", "rc", new[] { "Check(rc) && (a || b)" })]
    // COND không nhắc tới biến → về luật chung
    [InlineData("        if (x != 0) goto E;", "rc", new[] { "rc" })]
    // dòng gán → biến + vế phải
    [InlineData("        rc = inputFile.EndsWith(\".csv\") ? 0 : 12;", "rc",
        new[] { "rc", "inputFile.EndsWith(\".csv\") ? 0 : 12" })]
    [InlineData("        int rc = a + b;", "rc", new[] { "rc", "a + b" })]
    [InlineData("        rc = a + b;   // ghi chú", "rc", new[] { "rc", "a + b" })]
    // vế phải LUÔN vào Watch, kể cả hằng
    [InlineData("        rc = 0;", "rc", new[] { "rc", "0" })]
    [InlineData("        inputFile = \"orders.csv\";", "inputFile", new[] { "inputFile", "\"orders.csv\"" })]
    [InlineData("        decimal discount = 0m;", "discount", new[] { "discount", "0m" })]
    [InlineData("        ok = true;", "ok", new[] { "ok", "true" })]
    [InlineData("        s = null;", "s", new[] { "s", "null" })]
    // không phải phép gán
    [InlineData("        rc == a + b;", "rc", new[] { "rc" })]
    [InlineData("        rc += a + b;", "rc", new[] { "rc" })]
    [InlineData("        a => a + 1", "a", new[] { "a" })]
    [InlineData("        Console.WriteLine($\"CHECK_INPUT: inputFile={inputFile}, rc={rc}\");", "rc", new[] { "rc" })]
    // vế trái là biến khác
    [InlineData("        ok = rc == 0;", "rc", new[] { "rc" })]
    // vòng lặp không áp luật if
    [InlineData("        while (rc != 0) { if (rc > 1) break; }", "rc", new[] { "rc" })]
    [InlineData("        for (int i = 0; rc == 0; i++)", "rc", new[] { "rc" })]
    public void WatchFor_picks_expressions_by_the_shape_of_the_line(string line, string varName, string[] expected)
        => Assert.Equal(expected, Mapping.WatchFor(line, varName));

    [Fact]
    public void WatchFor_keeps_the_text_exactly_as_the_file_has_it()
    {
        // SetWatch bôi đen biểu thức nguyên văn trong file .cs — chuẩn hoá khoảng trắng là tìm không ra.
        Assert.Equal(new[] { "rc", "a  ?  0  :  12" }, Mapping.WatchFor("    rc = a  ?  0  :  12;", "rc"));
        Assert.Equal(new[] { "rc!=0" }, Mapping.WatchFor("    if (rc!=0) goto E;", "rc"));
    }

    [Theory]
    // Chuỗi giữ nguyên cả tiền tố @ / $ lẫn cặp nháy.
    [InlineData("        a = @$\"{a}mc/m\";", "a", "@$\"{a}mc/m\"")]
    [InlineData("        path = @\"C:\\a\\b\";", "path", "@\"C:\\a\\b\"")]
    // "//" trong nháy không phải comment đuôi
    [InlineData("        url = \"http://x\";", "url", "\"http://x\"")]
    // "=" trong nháy không phải dấu gán
    [InlineData("        msg = $\"a=b\";", "msg", "$\"a=b\"")]
    // ";" trong nháy không phải kết câu
    [InlineData("        s = \"a;b\";", "s", "\"a;b\"")]
    // nháy escape trong chuỗi verbatim
    [InlineData("        q = @\"say \"\"hi\"\"\";", "q", "@\"say \"\"hi\"\"\"")]
    public void WatchFor_scans_string_literals_before_cutting(string line, string varName, string rhs)
        => Assert.Equal(new[] { varName, rhs }, Mapping.WatchFor(line, varName));

    [Fact]
    public void WatchFor_ignores_a_statement_that_continues_on_the_next_line()
    {
        Assert.Equal(new[] { "rc" }, Mapping.WatchFor("    rc = a +", "rc"));
        Assert.Equal(new[] { "rc" }, Mapping.WatchFor("    if (rc != 0 &&", "rc"));
    }

    [Fact]
    public void WatchFor_without_a_variable_gives_nothing()
    {
        Assert.Empty(Mapping.WatchFor("    rc = 0;", ""));
        Assert.Empty(Mapping.WatchFor("    rc = 0;", "   "));
    }

    [Theory]
    [InlineData("rc", false)]
    [InlineData("order.Total", false)]
    [InlineData("rc != 0", true)]
    [InlineData("a && b", true)]
    [InlineData("", false)]
    public void IsExpression_cases(string s, bool expected)
        => Assert.Equal(expected, Mapping.IsExpression(s));

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
}

// ==================== CopiedTextTests ====================

public class CopiedTextTests
{
    // ---------- chuẩn hóa ----------

    [Theory]
    [InlineData("％ＲＣ％", "%RC%")]                       // ASCII full-width -> nửa chiều rộng
    [InlineData("ＣＨＥＣＫ＿ＩＮＰＵＴ", "CHECK_INPUT")]
    [InlineData("A　B", "A B")]                             // khoảng trắng Nhật U+3000
    [InlineData("a\r\nb", "a b")]
    [InlineData("a\tb", "a b")]
    [InlineData("  a   b  ", "a b")]
    [InlineData("：", ":")]
    public void Normalize_cases(string raw, string expected)
        => Assert.Equal(expected, CopiedText.Normalize(raw));

    [Fact]
    public void Normalize_keeps_japanese_brackets()
        => Assert.Equal("「%RC%」", CopiedText.Normalize("「％ＲＣ％」"));

    [Theory]
    [InlineData("\"a\nb\"", "a\nb")]
    [InlineData("\"say \"\"hi\"\"\"", "say \"hi\"")]
    [InlineData("khong co nhay", "khong co nhay")]
    public void UnquoteExcel_cases(string raw, string expected)
        => Assert.Equal(expected, CopiedText.UnquoteExcel(raw));

    // ---------- bóc ngoặc ----------

    [Theory]
    [InlineData("「%RC%」", "%RC%")]
    [InlineData("『rc != 0』", "rc != 0")]
    [InlineData("処理結果「%RC%」が0であること", "%RC%")]
    [InlineData("「 %RC% 」", "%RC%")]
    public void ExtractBracket_cases(string raw, string expected)
        => Assert.Equal(expected, CopiedText.ExtractBracket(raw));

    [Fact]
    public void ExtractBracket_takes_the_first_pair_only()
        => Assert.Equal("%RC%", CopiedText.ExtractBracket("「%RC%」が「0」であること"));

    [Fact]
    public void ExtractBracket_null_when_no_bracket()
        => Assert.Null(CopiedText.ExtractBracket("CHECK_INPUT"));

    // ---------- phân loại ----------

    [Fact]
    public void Bracketed_text_is_a_variable()
    {
        var p = CopiedText.Classify("「%RC%」");
        Assert.NotNull(p);
        Assert.True(p!.IsVar);
        Assert.Equal("%RC%", p.Value);
    }

    [Fact]
    public void Fullwidth_variable_is_normalized()
    {
        var p = CopiedText.Classify("「％ＲＣ％」");
        Assert.True(p!.IsVar);
        Assert.Equal("%RC%", p.Value);
    }

    [Fact]
    public void Bracketed_clause_keeps_inner_spaces_and_quotes()
    {
        var p = CopiedText.Classify("条件『if \"%RC%\" NEQ \"0\"』を満たすこと");
        Assert.True(p!.IsVar);
        Assert.Equal("if \"%RC%\" NEQ \"0\"", p.Value);
    }

    [Fact]
    public void Variable_wins_even_with_japanese_text_around_it()
    {
        var p = CopiedText.Classify("処理結果　「%RC%」　が 0 であること");
        Assert.True(p!.IsVar);
        Assert.Equal("%RC%", p.Value);
    }

    [Fact]
    public void Unbracketed_text_is_a_label()
    {
        var p = CopiedText.Classify("CHECK_INPUT");
        Assert.NotNull(p);
        Assert.False(p!.IsVar);
        Assert.Equal("CHECK_INPUT", p.Value);
    }

    [Theory]
    [InlineData(":CHECK_INPUT")]
    [InlineData("：CHECK_INPUT")]
    [InlineData("CHECK_INPUT:")]
    [InlineData("  CHECK_INPUT  ")]
    public void Label_strips_colons_and_spaces(string raw)
        => Assert.Equal("CHECK_INPUT", CopiedText.Classify(raw)!.Value);

    [Fact]
    public void Excel_cell_with_newline_becomes_one_line_label()
    {
        var p = CopiedText.Classify("\"CHECK_INPUT\nCHECK_INPUT\"");
        Assert.False(p!.IsVar);
        Assert.Equal("CHECK_INPUT CHECK_INPUT", p.Value);
    }

    [Fact]
    public void Long_unbracketed_prose_is_ignored()
    {
        var prose = new string('あ', CopiedText.MaxLabelLength + 1);
        Assert.Null(CopiedText.Classify(prose));
    }

    [Fact]
    public void Empty_or_whitespace_is_ignored()
    {
        Assert.Null(CopiedText.Classify(""));
        Assert.Null(CopiedText.Classify("   \r\n  "));
        Assert.Null(CopiedText.Classify(null));
    }

    [Fact]
    public void Empty_brackets_are_ignored()
    {
        Assert.Null(CopiedText.Classify("「」"));
        Assert.Null(CopiedText.Classify("「　」"));
    }
}

// ==================== CopyGroupTests ====================

public class CopyGroupTests
{
    [Fact]
    public void ClassifyAll_takes_every_batch_variable_in_one_cell()
    {
        var ps = CopiedText.ClassifyAll("「%RC%」と「％ＴＡＸ％」を確認");
        Assert.Equal(new[] { "%RC%", "%TAX%" }, ps.Select(p => p.Value));
        Assert.All(ps, p => Assert.True(p.IsVar));
    }

    [Fact]
    public void ClassifyAll_skips_expected_value_brackets()
        => Assert.Equal(new[] { "%RC%" }, CopiedText.ClassifyAll("「%RC%」が「0」であること").Select(p => p.Value));

    [Fact]
    public void ClassifyAll_keeps_first_when_nothing_looks_like_a_variable()
        => Assert.Equal(new[] { "rc" }, CopiedText.ClassifyAll("「rc」が「0」").Select(p => p.Value));

    [Fact]
    public void ClassifyAll_single_bracket_or_label_same_as_Classify()
    {
        Assert.Equal("rc != 0", Assert.Single(CopiedText.ClassifyAll("『rc != 0』")).Value);
        var label = Assert.Single(CopiedText.ClassifyAll(":CHECK_INPUT"));
        Assert.False(label.IsVar);
        Assert.Equal("CHECK_INPUT", label.Value);
        Assert.Empty(CopiedText.ClassifyAll("「」"));
        Assert.Empty(CopiedText.ClassifyAll(null));
    }

    [Theory]
    [InlineData("goto :END_PROC", "END_PROC")]
    [InlineData("GOTO END_PROC", "END_PROC")]
    [InlineData("goto:END_PROC", "END_PROC")]
    [InlineData("%RC%", null)]
    [InlineData("if \"%RC%\" NEQ \"0\" goto :ERR", null)]
    public void GotoTarget_cases(string inner, string? expected)
        => Assert.Equal(expected, CopiedText.GotoTarget(inner));

    [Fact]
    public void Fullwidth_goto_is_recognized()
        => Assert.Equal("END_PROC",
            CopiedText.GotoTarget(Assert.Single(CopiedText.ClassifyAll("「ｇｏｔｏ　：ＥＮＤ＿ＰＲＯＣ」")).Value));

    [Fact]
    public void FindLabelLine_reports_label_line()
    {
        var r = Mapping.FindLabelLineInText(new[] { "x();", "  L:", "  // c", "  a();", "  if (rc != 0) b();" }, "L", "rc != 0");
        Assert.Equal(LabelLineKind.Ok, r.Kind);
        Assert.Equal(5, r.Line);
        Assert.Equal(2, r.LabelLine);
    }

    [Fact]
    public void GroupStops_same_line_shares_one_shot_and_orders_by_line()
    {
        var stops = Mapping.GroupStops(new[]
        {
            new StopTarget(@"C:\p\Program.cs", 40, 35, "total", "%TOTAL%"),
            new StopTarget(@"C:\p\Program.cs", 27, 25, "rc", "%RC%"),
            new StopTarget(@"c:\P\program.cs", 40, 35, "tax", "%TAX%"),
            new StopTarget(@"C:\p\Program.cs", 92, 91, "", "goto :END_PROC"),
        });
        Assert.Equal(new[] { 27, 40, 92 }, stops.Select(s => s.Line));
        Assert.Equal(new[] { "total", "tax" }, stops[1].Watch);
        Assert.Empty(stops[2].Watch);
    }

    [Fact]
    public void GroupStops_keeps_file_order_of_first_appearance()
    {
        var stops = Mapping.GroupStops(new[]
        {
            new StopTarget(@"C:\p\Steps.cs", 50, 48, "amount", "%AMT%"),
            new StopTarget(@"C:\p\Program.cs", 10, 9, "rc", "%RC%"),
        });
        Assert.Equal(new[] { "Steps.cs", "Program.cs" }, stops.Select(s => Path.GetFileName(s.File)));
    }

    [Fact]
    public void WatchMismatch_null_when_same_ignoring_spaces_and_order()
        => Assert.Null(CaptureCheck.WatchMismatch(new[] { "rc", "rc!=0" }, new[] { "rc != 0", "rc" }));

    [Fact]
    public void WatchMismatch_reports_missing_extra_and_duplicates()
        => Assert.Equal("Watch thiếu tax · dư rc, total.",
            CaptureCheck.WatchMismatch(new[] { "rc", "rc", "total" }, new[] { "rc", "tax" }));

    [Fact]
    public void WatchMismatch_goto_stop_wants_empty_watch()
    {
        Assert.Null(CaptureCheck.WatchMismatch(Array.Empty<string>(), Array.Empty<string>()));
        Assert.Equal("Watch dư rc.", CaptureCheck.WatchMismatch(new[] { "rc" }, Array.Empty<string>()));
    }

    // ---------- bypass mệnh đề if ----------

    [Fact]
    public void GroupStops_if_and_goto_on_same_line_are_two_shots_by_column()
    {
        var stops = Mapping.GroupStops(new[]
        {
            new StopTarget(@"C:\p\Program.cs", 33, 29, "rc != 0", "if", Column: 22),
            new StopTarget(@"C:\p\Program.cs", 33, 29, "rc != 0", "if", Condition: "rc != 0"),
        });
        Assert.Equal(new[] { 0, 22 }, stops.Select(s => s.Column));
        Assert.Equal("rc != 0", stops[0].Condition);   // dòng if: chụp xong thì set cho mệnh đề đúng
        Assert.Equal("", stops[1].Condition);          // lệnh đầu nhánh: chỉ chụp
    }

    [Fact]
    public void Picks_on_one_line_accumulate_per_item_and_end_up_in_one_stop()
    {
        // BigSample dòng 34 `rc = inputFile.EndsWith(".csv") ? 0 : 12;` nhắc cả hai biến của test case.
        const string file = @"C:\p\Program.cs";
        var picked = new List<StopTarget>();
        void Pick(int line, string item, params string[] watches)
        {
            picked.RemoveAll(p => Mapping.SamePick(p, file, line, item));
            foreach (var w in watches) picked.Add(new StopTarget(file, line, 32, w, item));
        }

        Pick(34, "%RC%", "rc", "inputFile.EndsWith(\".csv\") ? 0 : 12");
        Pick(34, "%INPUT_FILE%", "inputFile");        // biến KHÁC cùng dòng → cộng dồn, không xoá phần của rc
        var stop = Assert.Single(Mapping.GroupStops(picked));
        Assert.Equal(new[] { "rc", "inputFile.EndsWith(\".csv\") ? 0 : 12", "inputFile" }, stop.Watch);
        Assert.Equal(new[] { "%RC%", "%RC%", "%INPUT_FILE%" }, stop.Items);

        Pick(34, "%RC%", "rc", "inputFile.EndsWith(\".csv\") ? 0 : 12");   // bấm lại CÙNG biến → không nhân đôi
        stop = Assert.Single(Mapping.GroupStops(picked));
        Assert.Equal(3, stop.Watch.Count);
        Assert.Contains("inputFile", stop.Watch);
    }

    [Theory]
    [InlineData("if \"%RC%\" NEQ \"0\"", "RC", "1")]
    [InlineData("if \"%RC%\" NEQ \"8\"", "RC", "0")]
    [InlineData("if \"%MODE%\" EQU \"PROD\"", "MODE", "PROD")]
    [InlineData("if \"%MODE%\"==\"PROD\"", "MODE", "PROD")]
    [InlineData("if /i \"%MODE%\" == \"prod\"", "MODE", "prod")]
    [InlineData("if %CNT% GTR 3", "CNT", "4")]
    [InlineData("if %CNT% LSS 3", "CNT", "2")]
    [InlineData("if %CNT% GEQ 3", "CNT", "3")]
    [InlineData("if %CNT% LEQ 3", "CNT", "3")]
    [InlineData("if not \"%RC%\"==\"0\"", "RC", "1")]
    [InlineData("if not %CNT% GTR 3", "CNT", "3")]
    [InlineData("IF \"%RC%\" NEQ \"0\" goto :ERR", "RC", "1")]
    public void IfClause_suggests_value_that_makes_clause_true(string clause, string var, string value)
        => Assert.Equal(new IfBypass(var, value), IfClause.Suggest(clause));

    [Theory]
    [InlineData("if exist \"%IN_FILE%\"")]
    [InlineData("if defined RC")]
    [InlineData("if errorlevel 1")]
    [InlineData("if %CNT% GTR abc")]
    [InlineData("%RC%")]
    public void IfClause_unrecognized_gives_no_suggestion(string clause)
        => Assert.Null(IfClause.Suggest(clause));

    [Theory]
    [InlineData("for %%i in (*.csv) do call :PROC %%i", true)]
    [InlineData("FOR /L %%n IN (1,1,3) DO echo %%n", true)]
    [InlineData("while (rc == 0)", true)]
    [InlineData("do echo x", true)]
    [InlineData("if \"%RC%\" NEQ \"0\"", false)]
    [InlineData("forfiles /p C:\\tmp", false)]
    [InlineData("%RC%", false)]
    [InlineData("", false)]
    public void IsLoop_only_catches_loop_clauses(string item, bool expected)
        => Assert.Equal(expected, IfClause.IsLoop(item));

    [Fact]
    public void IfClause_gives_no_bypass_for_a_loop_clause()
    {
        // vòng lặp chỉ điều hướng + chụp; đừng sinh SET dù bên trong có mệnh đề so sánh
        Assert.Null(IfClause.Suggest("for %%i in (1 2) do if \"%RC%\" NEQ \"0\" goto :ERR"));
        Assert.Null(IfClause.Suggest("do if \"%RC%\" NEQ \"0\" goto :ERR"));
    }

    [Fact]
    public void IfClause_statement_fills_template()
        => Assert.Equal("SET(\"RC\", \"1\")", IfClause.Statement("SET(\"{var}\", \"{value}\")", new IfBypass("RC", "1")));

    [Fact]
    public void BranchStart_goto_on_same_line_gives_its_column()
        => Assert.Equal<(int, int)?>((1, 22), Mapping.BranchStart(new[] { "        if (rc != 0) goto HANDLE_ERROR;" }, 1));

    [Fact]
    public void BranchStart_nested_parens_and_brace_on_same_line()
        => Assert.Equal<(int, int)?>((1, 30), Mapping.BranchStart(new[] { "if (Check(rc) && (a || b)) { return; }" }, 1));

    [Fact]
    public void BranchStart_block_on_next_lines_gives_first_statement_line()
        => Assert.Equal<(int, int)?>((3, 0), Mapping.BranchStart(new[] { "    if (rc != 0)", "    {", "        goto X;", "    }" }, 1));

    [Fact]
    public void BranchStart_null_when_not_an_if_or_condition_spans_lines()
    {
        Assert.Null(Mapping.BranchStart(new[] { "rc = 0;" }, 1));
        Assert.Null(Mapping.BranchStart(new[] { "if (rc != 0 &&", "    x)", "goto X;" }, 1));
    }
}

// ==================== CaptureCheckTests ====================

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

// ==================== AppSettingsTests ====================

public class AppSettingsTests
{
    [Fact]
    public void Save_then_Load_round_trips()
    {
        var p = Path.GetTempFileName();
        new AppSettings { MappingPath = @"C:\m.csv", TargetCsPath = @"C:\a.cs", TopMost = false }.Save(p);
        var s = AppSettings.Load(p);
        File.Delete(p);
        Assert.Equal(@"C:\m.csv", s.MappingPath);
        Assert.Equal(@"C:\a.cs", s.TargetCsPath);
        Assert.False(s.TopMost);
    }

    [Fact]
    public void Load_missing_file_returns_defaults_with_topmost_true()
    {
        var s = AppSettings.Load(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid() + ".json"));
        Assert.Null(s.MappingPath);
        Assert.True(s.TopMost);
    }

    [Fact]
    public void Load_corrupt_json_returns_defaults()
    {
        var p = Path.GetTempFileName();
        File.WriteAllText(p, "{ not json");
        var s = AppSettings.Load(p);
        File.Delete(p);
        Assert.True(s.TopMost);
        Assert.Null(s.MappingPath);
    }

    [Fact]
    public void Load_json_without_topmost_key_defaults_to_true()
    {
        var p = Path.GetTempFileName();
        File.WriteAllText(p, "{\"MappingPath\":\"x\"}");
        var s = AppSettings.Load(p);
        File.Delete(p);
        Assert.Equal("x", s.MappingPath);
        Assert.True(s.TopMost);
    }
}

// ==================== khung chụp cỡ cố định ====================

public class CaptureFrameTests
{
    static readonly Size Frame = new(1000, 500);
    static readonly Rectangle Screen1 = new(0, 0, 1920, 1080);

    [Fact]
    public void Frame_is_centered_on_the_cursor()
        => Assert.Equal(new Rectangle(460, 290, 1000, 500),
            ScreenCapture.PlaceFixed(new Point(960, 540), Frame, Screen1));

    [Fact]
    public void Frame_is_pushed_back_inside_the_screen_near_edges()
    {
        Assert.Equal(new Rectangle(0, 0, 1000, 500),
            ScreenCapture.PlaceFixed(new Point(10, 10), Frame, Screen1));
        Assert.Equal(new Rectangle(920, 580, 1000, 500),
            ScreenCapture.PlaceFixed(new Point(1915, 1075), Frame, Screen1));
    }

    [Fact]
    public void Frame_stays_on_a_second_monitor_with_offset()
        => Assert.Equal(new Rectangle(1920, 0, 1000, 500),
            ScreenCapture.PlaceFixed(new Point(1925, 5), Frame, new Rectangle(1920, 0, 1920, 1080)));
}
