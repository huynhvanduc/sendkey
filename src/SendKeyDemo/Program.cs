using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SendKeyDemo;

static class Program
{
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
            Native.ShowWindow(p.MainWindowHandle, 9);   // SW_RESTORE
            Native.SetForegroundWindow(p.MainWindowHandle);
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

    // Ctrl+Shift+Q chỉ đè Window.ActivateQuickLaunchPreviousCategory của VS, không đè phím nào của editor.
    public string MoveArrowHotkey { get; set; } = "Ctrl+Shift+Q";

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
        => Safe.Try<AppSettings?>(() => File.Exists(path) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) : null, null) ?? new AppSettings();

    public void Save() => Save(DefaultPath);

    // File này người dùng sửa tay (hotkey, SaveFolder) nên không escape "+" thành +.
    static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Cấu hình không lưu được thì bỏ qua, không làm hỏng app.
    public void Save(string path) => Safe.Try(() => File.WriteAllText(path, JsonSerializer.Serialize(this, _json)));

    [JsonIgnore]
    public string ResolvedSaveFolder => string.IsNullOrWhiteSpace(SaveFolder) ? Path.GetTempPath()
        : Safe.Try(() => { Directory.CreateDirectory(SaveFolder); return SaveFolder; }, Path.GetTempPath());

    const int ClipboardDpi = 96;   // DPI mặc định của GDI+/Office khi không có metadata khác

    [JsonIgnore]
    public Size? ClipboardPixelSize => ClipboardWidthInches <= 0 || ClipboardHeightInches <= 0 ? null
        : new Size(Math.Max(1, (int)Math.Round(ClipboardWidthInches * ClipboardDpi)), Math.Max(1, (int)Math.Round(ClipboardHeightInches * ClipboardDpi)));
}

// ==================== HotkeyWindow ====================

public sealed class HotkeyWindow : NativeWindow, IDisposable
{
    const int WM_HOTKEY = 0x0312;

    [Flags]
    enum Mod : uint { Alt = 0x1, Control = 0x2, Shift = 0x4, Win = 0x8, NoRepeat = 0x4000 }

    readonly Dictionary<int, Action> _handlers = new();
    int _nextId = 1;

    // Message-only window: ẩn hẳn, không lên taskbar.
    public HotkeyWindow() => CreateHandle(new CreateParams());

    // spec dạng "Ctrl+Shift+G"; sai cú pháp hoặc phím đã bị app khác chiếm thì trả false.
    public bool Register(string spec, Action onPressed)
    {
        var parts = (spec ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !Enum.TryParse(parts[^1], ignoreCase: true, out Keys key)) return false;

        var mods = Mod.NoRepeat;   // NoRepeat: giữ phím không bị bắn liên tục
        foreach (var part in parts[..^1])
        {
            Mod? m = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => Mod.Control,
                "alt" => Mod.Alt,
                "shift" => Mod.Shift,
                "win" or "windows" => Mod.Win,
                _ => null,
            };
            if (m is null) return false;
            mods |= m.Value;
        }

        int id = _nextId++;
        if (!Native.RegisterHotKey(Handle, id, (uint)mods, (uint)key)) return false;
        _handlers[id] = onPressed;
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && _handlers.TryGetValue(m.WParam.ToInt32(), out var action)) action();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys)
            Native.UnregisterHotKey(Handle, id);
        _handlers.Clear();
        DestroyHandle();
    }
}

// ==================== Safe ====================

// Cho các chỗ "lỗi thì thôi" (VS đã đóng, clipboard bận, file khoá…) khỏi lặp try/catch rỗng.
static class Safe
{
    public static T Try<T>(Func<T> f, T fallback) { try { return f(); } catch { return fallback; } }
    public static void Try(Action a) { try { a(); } catch { } }
}

// ==================== Native ====================

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx, cy; public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; } }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    public const uint ULW_ALPHA = 0x02;
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObj);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
}
