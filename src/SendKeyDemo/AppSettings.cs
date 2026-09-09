using System.Text.Json;
using System.Text.Json.Serialization;

namespace SendKeyDemo;

/// <summary>
/// Cấu hình chung cho cả 2 nửa của app: tra mapping / đặt breakpoint (SendKeyDemo cũ)
/// và chụp màn hình theo hotkey (QuickShot cũ). Một file settings.json cạnh .exe.
/// </summary>
public sealed class AppSettings
{
    // ---- nửa mapping ----
    public string? MappingPath { get; set; }
    public string? TargetCsPath { get; set; }
    public bool TopMost { get; set; } = true;
    public string[] RecentLookups { get; set; } = Array.Empty<string>();   // "cmdLabel\tcmdVar", mới nhất đầu

    // ---- nửa chụp màn hình ----
    public string DefineRegionHotkey { get; set; } = "Ctrl+Shift+R";
    public string CaptureRegionHotkey { get; set; } = "Ctrl+Shift+S";
    public string FullScreenHotkey { get; set; } = "Ctrl+Shift+F";
    public string ActiveWindowHotkey { get; set; } = "Ctrl+Shift+W";
    public string SaveFolder { get; set; } = @"C:\Temp\shot";

    // Kích thước cố định (inch) áp cho ảnh vào Clipboard, để dán vào Excel/SharePoint ra đúng
    // Width/Height mong muốn mà không phải kéo tay. 0 = giữ nguyên kích thước gốc.
    public double ClipboardWidthInches { get; set; } = 0;
    public double ClipboardHeightInches { get; set; } = 0;

    // ---- chế độ chụp bằng chứng ----
    /// <summary>Hotkey nhảy tới + đặt breakpoint cho test case đang chọn trong worklist.</summary>
    public string GotoCurrentHotkey { get; set; } = "Ctrl+Shift+G";

    /// <summary>Worklist đang làm dở (mỗi dòng "tcId\tcmdLabel\tcmdVar\texpected") + vị trí, để mở lại app là chạy tiếp.</summary>
    public string[] Worklist { get; set; } = Array.Empty<string>();
    public int WorklistIndex { get; set; }
    public string[] WorklistDone { get; set; } = Array.Empty<string>();   // các tcId đã chụp

    /// <summary>Vị trí thanh trạng thái mỏng người dùng đã kéo tới; null = canh giữa mép trên.</summary>
    public int? StripX { get; set; }
    public int? StripY { get; set; }

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

    /// <summary>Thư mục lưu ảnh thực tế: dùng SaveFolder nếu hợp lệ, ngược lại rơi về %TEMP%.</summary>
    [JsonIgnore]
    public string ResolvedSaveFolder
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SaveFolder)) return Path.GetTempPath();
            try
            {
                Directory.CreateDirectory(SaveFolder);
                return SaveFolder;
            }
            catch
            {
                return Path.GetTempPath();
            }
        }
    }

    const int ClipboardDpi = 96;   // DPI mặc định của GDI+/Office khi không có metadata khác

    /// <summary>Kích thước pixel quy đổi từ ClipboardWidth/HeightInches; null nếu chưa cấu hình.</summary>
    [JsonIgnore]
    public Size? ClipboardPixelSize
    {
        get
        {
            if (ClipboardWidthInches <= 0 || ClipboardHeightInches <= 0) return null;
            int w = Math.Max(1, (int)Math.Round(ClipboardWidthInches * ClipboardDpi));
            int h = Math.Max(1, (int)Math.Round(ClipboardHeightInches * ClipboardDpi));
            return new Size(w, h);
        }
    }
}
