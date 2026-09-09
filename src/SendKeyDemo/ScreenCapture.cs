using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using QuickShot;

namespace SendKeyDemo;

/// <summary>
/// Chụp một vùng màn hình vào Clipboard (và tùy chọn lưu PNG). Tách khỏi UI để
/// cả hotkey chụp thường lẫn chụp bằng chứng dùng chung một đường.
/// </summary>
public static class ScreenCapture
{
    public record Result(bool Ok, string Message, string FilePath);

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>Màn hình đang chứa con trỏ chuột (hỗ trợ nhiều màn hình).</summary>
    public static Rectangle CursorScreenBounds() => Screen.FromPoint(Cursor.Position).Bounds;

    /// <summary>Khung cửa sổ đang active; null nếu không có cửa sổ hợp lệ (desktop / đang thu nhỏ).</summary>
    public static Rectangle? ActiveWindowBounds()
    {
        IntPtr hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero || IsIconic(hWnd)) return null;

        // Ưu tiên DWM extended frame bounds: khớp viền nhìn thấy được, tránh phần viền ẩn
        // mà GetWindowRect cộng thêm ở cửa sổ maximized.
        bool got = DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) == 0
                   || GetWindowRect(hWnd, out r);
        if (!got) return null;

        var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        return rect.Width > 0 && rect.Height > 0 ? rect : null;
    }

    /// <summary>
    /// Chụp <paramref name="region"/> vào Clipboard. <paramref name="saveFile"/> = true thì lưu thêm PNG
    /// vào thư mục cấu hình (chụp bằng chứng không lưu file — dán thẳng vào tài liệu).
    /// </summary>
    public static Result Grab(Rectangle region, AppSettings settings, bool saveFile, bool showFlyout = true)
    {
        if (region.Width <= 0 || region.Height <= 0)
            return new Result(false, "vùng chụp không hợp lệ", "");

        try
        {
            using var bmp = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(region.Location, Point.Empty, region.Size);

            using (var forClipboard = BuildClipboardImage(bmp, settings))
                SetClipboardImage(forClipboard);

            string file = "";
            if (saveFile)
            {
                file = Path.Combine(settings.ResolvedSaveFolder,
                    $"QuickShot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
                bmp.Save(file, ImageFormat.Png);
            }

            if (showFlyout)
                new CaptureFlyoutForm(new Bitmap(bmp), region, file).Show();

            return new Result(true,
                saveFile ? $"đã chụp → {Path.GetFileName(file)}" : "đã chụp vào clipboard",
                file);
        }
        catch (Exception ex)
        {
            return new Result(false, "lỗi chụp: " + ex.Message, "");
        }
    }

    // Clipboard hay bận vì app khác đang giữ (Excel, trình duyệt) -> thử lại vài nhịp thay vì ném ngay.
    static void SetClipboardImage(Bitmap bmp)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetImage(bmp); return; }
            catch (ExternalException) when (attempt < 4) { Thread.Sleep(80); }
        }
    }

    // Kéo đúng ClipboardWidth/HeightInches (quy đổi pixel) nếu đã cấu hình, kể cả méo tỷ lệ,
    // để dán vào Excel/SharePoint ra sẵn đúng cỡ. Chưa cấu hình thì giữ nguyên gốc.
    static Bitmap BuildClipboardImage(Bitmap original, AppSettings settings)
    {
        if (settings.ClipboardPixelSize is not { } size) return new Bitmap(original);

        var resized = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(resized);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(original, new Rectangle(0, 0, size.Width, size.Height));
        return resized;
    }
}
