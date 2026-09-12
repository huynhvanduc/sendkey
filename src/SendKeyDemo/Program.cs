using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SendKeyDemo;

static class Program
{
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    const int SW_RESTORE = 9;

    [STAThread]
    static void Main()
    {
        using var single = new Mutex(true, @"Local\SendKeyDemo.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            FocusRunningInstance();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    static void FocusRunningInstance()
    {
        var me = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("SendKeyDemo"))
        {
            if (p.Id == me || p.MainWindowHandle == IntPtr.Zero) continue;
            ShowWindow(p.MainWindowHandle, SW_RESTORE);
            SetForegroundWindow(p.MainWindowHandle);
            return;
        }

        MessageBox.Show(
            "SendKey Evidence đang chạy sẵn ở khay hệ thống (góc dưới-phải, có thể phải bấm mũi tên \"^\").\n\n" +
            "Double-click icon đó để mở cửa sổ.",
            "Đã chạy rồi", MessageBoxButtons.OK, AppSettings.Load().MsgIcon(MessageBoxIcon.Information));
    }
}

public sealed class AppSettings
{
    // ---- nửa mapping ----
    public string? MappingPath { get; set; }
    public bool TopMost { get; set; } = true;

    // Mặc định im lặng; đặt false để bật lại ding/buzz.
    public bool Silent { get; set; } = true;

    // MessageBox tự phát system sound theo icon, nên Silent thì bỏ icon đi.
    public MessageBoxIcon MsgIcon(MessageBoxIcon wanted) => Silent ? MessageBoxIcon.None : wanted;

    // ---- nửa chụp màn hình ----
    public string DefineRegionHotkey { get; set; } = "Ctrl+Shift+R";
    public string CaptureRegionHotkey { get; set; } = "Ctrl+Shift+S";
    public string FullScreenHotkey { get; set; } = "Ctrl+Shift+F";
    public string ActiveWindowHotkey { get; set; } = "Ctrl+Shift+W";
    public string ClearBreakpointsHotkey { get; set; } = "Ctrl+Shift+D";
    public string SaveFolder { get; set; } = @"C:\Temp\shot";

    public double ClipboardWidthInches { get; set; } = 0;
    public double ClipboardHeightInches { get; set; } = 0;

    public string GotoCurrentHotkey { get; set; } = "Ctrl+Shift+G";

    public string IfSetStatement { get; set; } = "SET(\"{var}\", \"{value}\")";

    public int? StripX { get; set; }
    public int? StripY { get; set; }

    static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        bool firstRun = !File.Exists(DefaultPath);
        var s = Load(DefaultPath);
        if (firstRun) s.Save();
        return s;
    }

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

    // File này người dùng sửa tay (hotkey, SaveFolder) nên không escape "+" thành +.
    static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public void Save(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, _json));
        }
        catch
        {
            // cấu hình không lưu được thì bỏ qua, không làm hỏng app
        }
    }

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

// ==================== HotkeyParser ====================

public static class HotkeyParser
{
    public static bool TryParse(string spec, out HotkeyWindow.Mod modifiers, out Keys key)
    {
        modifiers = default;
        key = default;

        if (string.IsNullOrWhiteSpace(spec))
            return false;

        var parts = spec.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            HotkeyWindow.Mod? m = parts[i].ToLowerInvariant() switch
            {
                "ctrl" or "control" => HotkeyWindow.Mod.Control,
                "alt" => HotkeyWindow.Mod.Alt,
                "shift" => HotkeyWindow.Mod.Shift,
                "win" or "windows" => HotkeyWindow.Mod.Win,
                _ => null,
            };

            if (m is null)
                return false;

            modifiers |= m.Value;
        }

        return Enum.TryParse(parts[^1], ignoreCase: true, out key);
    }
}

// ==================== HotkeyWindow ====================

public sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // Modifier flags của Win32
    [Flags]
    public enum Mod : uint { Alt = 0x1, Control = 0x2, Shift = 0x4, Win = 0x8, NoRepeat = 0x4000 }

    // id -> callback tương ứng khi hotkey đó được nhấn
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public HotkeyWindow()
    {
        // Tạo một message-only window (ẩn hẳn, không lên taskbar)
        CreateHandle(new CreateParams());
    }

    public bool Register(Mod modifiers, Keys key, Action onPressed)
    {
        int id = _nextId++;
        // NoRepeat: giữ phím không bị bắn liên tục
        if (!RegisterHotKey(Handle, id, (uint)(modifiers | Mod.NoRepeat), (uint)key))
            return false;

        _handlers[id] = onPressed;
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            if (_handlers.TryGetValue(id, out var action))
                action.Invoke();
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys)
            UnregisterHotKey(Handle, id);
        _handlers.Clear();
        DestroyHandle();
    }
}
