using SendKeyDemo;
using Xunit;

namespace SendKeyDemo.Tests;

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
