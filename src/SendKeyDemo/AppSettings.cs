using System.Text.Json;

namespace SendKeyDemo;

public record AppSettings
{
    public string? MappingPath { get; init; }
    public string? TargetCsPath { get; init; }
    public bool TopMost { get; init; } = true;
    public string[] RecentLookups { get; init; } = System.Array.Empty<string>();   // "cmdLabel\tcmdVar", mới nhất đầu

    static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load() => Load(DefaultPath);

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save() => Save(DefaultPath);

    public void Save(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // cấu hình không lưu được thì bỏ qua, không làm hỏng app
        }
    }
}
