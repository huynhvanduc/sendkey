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
}
