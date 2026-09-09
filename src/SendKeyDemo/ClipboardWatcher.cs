using System.Runtime.InteropServices;

namespace SendKeyDemo;

/// <summary>
/// Nghe clipboard bằng WM_CLIPBOARDUPDATE (không polling) để bắt cái vừa copy từ Excel.
/// Cửa sổ vô hình, giống HotkeyWindow.
/// </summary>
public sealed class ClipboardWatcher : NativeWindow, IDisposable
{
    const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)] static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    /// <summary>Text mà chính app vừa đẩy vào clipboard — bỏ qua để không tự kích hoạt mình.</summary>
    public string? IgnoreText { get; set; }

    public event Action<string>? TextCopied;

    public ClipboardWatcher()
    {
        CreateHandle(new CreateParams());
        AddClipboardFormatListener(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_CLIPBOARDUPDATE) Handle_ClipboardUpdate();
        base.WndProc(ref m);
    }

    void Handle_ClipboardUpdate()
    {
        string text;
        try
        {
            // Ảnh chụp màn hình của chính app cũng bắn sự kiện này -> chỉ quan tâm text.
            if (!Clipboard.ContainsText()) return;
            text = Clipboard.GetText();
        }
        catch (ExternalException) { return; }   // app khác đang giữ clipboard
        catch (ThreadStateException) { return; }

        if (string.IsNullOrWhiteSpace(text)) return;
        if (IgnoreText != null && string.Equals(text, IgnoreText, StringComparison.Ordinal)) return;

        TextCopied?.Invoke(text);
    }

    public void Dispose()
    {
        try { RemoveClipboardFormatListener(Handle); } catch { /* handle đã chết */ }
        DestroyHandle();
    }
}
