using System;
using System.IO;
using SendKeyDemo;
using Xunit;

namespace SendKeyDemo.Tests;

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
