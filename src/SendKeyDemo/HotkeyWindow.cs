using System.ComponentModel;
using System.Runtime.InteropServices;

namespace QuickShot;

/// <summary>
/// Cửa sổ VÔ HÌNH chỉ để nhận message WM_HOTKEY của Windows.
/// RegisterHotKey cần một handle cửa sổ để gửi message tới — NativeWindow
/// cho ta handle đó mà không cần vẽ Form thật.
/// </summary>
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

    /// <summary>Đăng ký 1 tổ hợp phím. Trả về true nếu thành công.</summary>
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
