using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SendKeyDemo;

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

    public static Rectangle CursorScreenBounds() => Screen.FromPoint(Cursor.Position).Bounds;

    public static Rectangle? ActiveWindowBounds()
    {
        IntPtr hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero || IsIconic(hWnd)) return null;

        bool got = DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<RECT>()) == 0
                   || GetWindowRect(hWnd, out r);
        if (!got) return null;

        var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        return rect.Width > 0 && rect.Height > 0 ? rect : null;
    }

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

    public static Result GrabClean(Rectangle region, AppSettings settings, Form? hide)
    {
        var cursor = Cursor.Position;
        bool hidden = hide is { Visible: true };
        try
        {
            if (hidden) hide!.Hide();
            Cursor.Position = OutsidePoint(region);
            Thread.Sleep(250);   // đủ cho tooltip của VS tắt và DWM vẽ lại chỗ thanh nổi vừa ẩn
            return Grab(region, settings, saveFile: false);
        }
        finally
        {
            Cursor.Position = cursor;
            if (hidden) hide!.Show();   // thanh có ShowWithoutActivation nên không cướp focus
        }
    }

    static Point OutsidePoint(Rectangle region)
    {
        var r = Rectangle.Inflate(region, 40, 40);
        var mid = new Point(region.X + region.Width / 2, region.Y + region.Height / 2);
        Point[] candidates = { new(r.Right, mid.Y), new(r.Left, mid.Y), new(mid.X, r.Bottom), new(mid.X, r.Top) };
        foreach (var p in candidates)
            if (Screen.AllScreens.Any(s => s.Bounds.Contains(p))) return p;
        var vs = SystemInformation.VirtualScreen;
        return new Point(vs.Right - 1, vs.Bottom - 1);   // vùng chụp phủ kín mọi màn hình — đành ra góc
    }

    public static Rectangle PlaceFixed(Point center, Size size, Rectangle bounds)
    {
        int x = center.X - size.Width / 2, y = center.Y - size.Height / 2;
        x = Math.Max(bounds.Left, Math.Min(x, bounds.Right - size.Width));
        y = Math.Max(bounds.Top, Math.Min(y, bounds.Bottom - size.Height));
        return new Rectangle(x, y, size.Width, size.Height);
    }

    static void SetClipboardImage(Bitmap bmp)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetImage(bmp); return; }
            catch (ExternalException) when (attempt < 4) { Thread.Sleep(80); }
        }
    }

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

// ==================== RegionSelector ====================

public sealed class RegionSelector : Form
{
    private const int FadeInMs = 120;
    private const float TargetOverlayOpacity = 0.42f;

    private Point _start;
    private Rectangle _selection;
    private bool _dragging;

    private readonly System.Windows.Forms.Timer _fadeTimer;
    private readonly Stopwatch _fadeStopwatch = new();
    private Brush? _vignetteBrush;

    private readonly Size? _fixedSize;   // null = kéo tự do

    public Rectangle? Result { get; private set; }

    public RegionSelector(Size? fixedSize = null)
    {
        _fixedSize = fixedSize;
        // Phủ hết mọi màn hình, kể cả tọa độ âm (màn bên trái màn chính)
        var vs = SystemInformation.VirtualScreen;
        StartPosition = FormStartPosition.Manual;
        Bounds = vs;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        Opacity = 0.0;
        BackColor = Color.Black;
        Cursor = fixedSize == null ? Cursors.Cross : Cursors.SizeAll;
        DoubleBuffered = true;

        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { Result = null; Close(); } };
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;

        _fadeTimer = new System.Windows.Forms.Timer { Interval = 12 };
        _fadeTimer.Tick += (_, _) =>
        {
            double t = Math.Min(1.0, _fadeStopwatch.ElapsedMilliseconds / (double)FadeInMs);
            Opacity = TargetOverlayOpacity * Theme.EaseOutQuad(t);
            if (t >= 1.0) _fadeTimer.Stop();
        };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _vignetteBrush = BuildVignetteBrush(ClientRectangle);
        if (_fixedSize != null) PlaceFrame(PointToClient(Cursor.Position));
        _fadeStopwatch.Start();
        _fadeTimer.Start();
    }

    private static Brush BuildVignetteBrush(Rectangle bounds)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(bounds.X - bounds.Width * 0.2f, bounds.Y - bounds.Height * 0.2f,
            bounds.Width * 1.4f, bounds.Height * 1.4f);
        return new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(255, 18, 20, 26),
            SurroundColors = new[] { Color.FromArgb(255, 4, 5, 8) },
        };
    }

    private void PlaceFrame(Point client)
    {
        var screen = RectangleToClient(Screen.FromPoint(PointToScreen(client)).Bounds);
        _selection = ScreenCapture.PlaceFixed(client, _fixedSize!.Value, screen);
        Invalidate();
    }

    private void OnMouseDown(object? s, MouseEventArgs e)
    {
        if (_fixedSize != null)
        {
            // Khung cỡ cố định: click trái = chốt ở chỗ đang đứng, click phải = huỷ.
            PlaceFrame(e.Location);
            Result = e.Button == MouseButtons.Left
                ? new Rectangle(PointToScreen(_selection.Location), _selection.Size)
                : null;
            Close();
            return;
        }

        if (e.Button != MouseButtons.Left) return;
        _fadeTimer.Stop();
        Opacity = TargetOverlayOpacity;
        _dragging = true;
        _start = e.Location;
        _selection = new Rectangle(e.Location, Size.Empty);
    }

    private void OnMouseMove(object? s, MouseEventArgs e)
    {
        if (_fixedSize != null) { PlaceFrame(e.Location); return; }
        if (!_dragging) return;
        // Chuẩn hóa để kéo được theo mọi hướng
        _selection = Rectangle.FromLTRB(
            Math.Min(_start.X, e.X), Math.Min(_start.Y, e.Y),
            Math.Max(_start.X, e.X), Math.Max(_start.Y, e.Y));
        Invalidate();
    }

    private void OnMouseUp(object? s, MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;

        if (_selection.Width < 2 || _selection.Height < 2)
        {
            Result = null;   // click hụt, coi như hủy
        }
        else
        {
            // Đổi từ tọa độ client (trong form) sang tọa độ màn hình thật
            var screenPt = PointToScreen(_selection.Location);
            Result = new Rectangle(screenPt, _selection.Size);
        }
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_vignetteBrush != null)
            e.Graphics.FillRectangle(_vignetteBrush, ClientRectangle);

        if (_selection.Width <= 0 || _selection.Height <= 0) return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var fillBrush = new SolidBrush(Color.FromArgb(50, Theme.AccentStart)))
            e.Graphics.FillRectangle(fillBrush, _selection);

        using (var borderBrush = Theme.AccentBrush(_selection))
        using (var pen = new Pen(borderBrush, 2f) { DashStyle = DashStyle.Dash })
            e.Graphics.DrawRectangle(pen, _selection);

        DrawSizeLabel(e.Graphics);
    }

    private void DrawSizeLabel(Graphics g)
    {
        string label = _fixedSize == null
            ? $"{_selection.Width} x {_selection.Height}"
            : $"{_selection.Width} x {_selection.Height}   ·   click để chốt, Esc để huỷ";
        using var font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        var textSize = g.MeasureString(label, font);
        var pillRect = new RectangleF(_selection.X, Math.Max(0, _selection.Y - textSize.Height - 14),
            textSize.Width + 16, textSize.Height + 8);

        using var pillPath = Theme.RoundedRect(pillRect, pillRect.Height / 2f);
        using var pillBrush = Theme.AccentBrush(pillRect);
        g.FillPath(pillBrush, pillPath);
        g.DrawString(label, font, Brushes.White, pillRect.X + 8, pillRect.Y + 4);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _fadeTimer.Dispose();
        _vignetteBrush?.Dispose();
    }

    // Ẩn khỏi Alt+Tab
    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x80;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW;
            return cp;
        }
    }
}


public sealed class CaptureFlyoutForm : Form
{
    private const int HighlightFadeMs = 150;
    private const int HighlightMs = 350;
    private const int FlyMs = 380;
    private const int HoldMs = 1500;
    private const int FadeMs = 320;
    private const int HoverAnimMs = 120;
    private const int PulsePeriodMs = 1600;
    private const int ThumbMaxW = 180;
    private const int ThumbMaxH = 120;
    private const int EdgeMargin = 12;
    private const float HoverScale = 1.6f;
    private const float CornerRadius = 10f;

    private enum Phase { Highlight, Fly, Hold, Fade }

    private readonly Rectangle _startBounds;
    private readonly Rectangle _restBounds;
    private readonly Rectangle _hoverBounds;
    private readonly string _filePath;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Stopwatch _stopwatch = new();

    private Bitmap? _original;
    private Bitmap? _smallSource;
    private Bitmap? _hoverSource;

    private Phase _phase = Phase.Highlight;
    private bool _isHovered;
    private double _hoverT;
    private double _holdRemainingMs = HoldMs;
    private long _lastFrameMs;

    public CaptureFlyoutForm(Bitmap image, Rectangle capturedBounds, string filePath)
    {
        _original = image;
        _filePath = filePath;
        _startBounds = capturedBounds;
        _restBounds = ComputeRestBounds(capturedBounds);
        _hoverBounds = ComputeHoverBounds(_restBounds);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = _startBounds;
        Cursor = Cursors.Hand;

        _smallSource = BuildScaled(_original, _restBounds.Width * 2, _restBounds.Height * 2);

        MouseDown += (_, _) => { if (_phase == Phase.Hold) OpenFileAndClose(); };
        MouseEnter += (_, _) => _isHovered = true;
        MouseLeave += (_, _) => _isHovered = false;

        _timer = new System.Windows.Forms.Timer { Interval = 15 };
        _timer.Tick += OnTick;
    }

    private static Rectangle ComputeRestBounds(Rectangle capturedBounds)
    {
        var screen = Screen.FromRectangle(capturedBounds);
        double scale = Math.Min(
            (double)ThumbMaxW / capturedBounds.Width,
            (double)ThumbMaxH / capturedBounds.Height);
        scale = Math.Min(scale, 1.0);

        int w = Math.Max(1, (int)(capturedBounds.Width * scale));
        int h = Math.Max(1, (int)(capturedBounds.Height * scale));

        var wa = screen.WorkingArea;
        return new Rectangle(wa.Right - w - EdgeMargin, wa.Bottom - h - EdgeMargin, w, h);
    }

    private static Rectangle ComputeHoverBounds(Rectangle rest)
    {
        int w = (int)(rest.Width * HoverScale);
        int h = (int)(rest.Height * HoverScale);
        return new Rectangle(rest.Right - w, rest.Bottom - h, w, h);
    }

    private static Bitmap BuildScaled(Bitmap source, int maxW, int maxH)
    {
        double scale = Math.Min((double)maxW / source.Width, (double)maxH / source.Height);
        scale = Math.Min(scale, 1.0);
        int w = Math.Max(1, (int)(source.Width * scale));
        int h = Math.Max(1, (int)(source.Height * scale));

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, w, h));
        return bmp;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _stopwatch.Start();
        _timer.Start();
        RenderFrame();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        long now = _stopwatch.ElapsedMilliseconds;
        long delta = now - _lastFrameMs;
        _lastFrameMs = now;

        switch (_phase)
        {
            case Phase.Highlight:
                if (now >= HighlightMs)
                {
                    _phase = Phase.Fly;
                    _stopwatch.Restart();
                    _lastFrameMs = 0;
                }
                break;

            case Phase.Fly:
                if (now >= FlyMs)
                {
                    _phase = Phase.Hold;
                    _stopwatch.Restart();
                    _lastFrameMs = 0;
                    _holdRemainingMs = HoldMs;
                }
                break;

            case Phase.Hold:
                UpdateHoverT(delta);
                if (!_isHovered) _holdRemainingMs -= delta;
                if (_holdRemainingMs <= 0 && !_isHovered)
                {
                    _phase = Phase.Fade;
                    _stopwatch.Restart();
                    _lastFrameMs = 0;
                }
                break;

            case Phase.Fade:
                UpdateHoverT(delta);
                if (_isHovered)
                {
                    // User rê chuột vào đúng lúc đang mờ: quay lại Hold, không để biến mất giữa chừng.
                    _phase = Phase.Hold;
                    _stopwatch.Restart();
                    _lastFrameMs = 0;
                    _holdRemainingMs = HoldMs;
                }
                else if (now >= FadeMs)
                {
                    CloseAndDispose();
                    return;
                }
                break;
        }

        RenderFrame();
    }

    private void UpdateHoverT(long delta)
    {
        double target = _isHovered ? 1.0 : 0.0;
        double step = delta / (double)HoverAnimMs;
        if (_hoverT < target) _hoverT = Math.Min(target, _hoverT + step);
        else if (_hoverT > target) _hoverT = Math.Max(target, _hoverT - step);

        if (_isHovered && _hoverSource == null && _original != null)
            _hoverSource = BuildScaled(_original, _hoverBounds.Width, _hoverBounds.Height);
    }

    private void RenderFrame()
    {
        if (_original == null || _smallSource == null) return;

        Rectangle bounds;
        Bitmap frame;
        double opacity = 1.0;

        switch (_phase)
        {
            case Phase.Highlight:
                {
                    bounds = _startBounds;
                    frame = new Bitmap(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height), PixelFormat.Format32bppArgb);
                    using var g = Graphics.FromImage(frame);
                    g.DrawImageUnscaled(_original, 0, 0);
                    double t = Math.Min(1.0, _stopwatch.ElapsedMilliseconds / (double)HighlightFadeMs);
                    DrawHighlightBorder(g, bounds, (float)t);
                    break;
                }

            case Phase.Fly:
                {
                    double t = Math.Min(1.0, _stopwatch.ElapsedMilliseconds / (double)FlyMs);
                    double eased = Theme.EaseOutCubic(t);
                    bounds = Lerp(_startBounds, _restBounds, eased);
                    frame = new Bitmap(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height), PixelFormat.Format32bppArgb);
                    using var g = Graphics.FromImage(frame);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    DrawClippedContent(g, _smallSource, bounds);

                    // Cross-fade 100ms: viền highlight mờ dần, viền glow của thumbnail hiện dần thay thế.
                    double crossT = Math.Min(1.0, _stopwatch.ElapsedMilliseconds / 100.0);
                    if (crossT < 1.0)
                        DrawHighlightBorder(g, new Rectangle(0, 0, bounds.Width, bounds.Height), (float)(1 - crossT));
                    Theme.DrawGlowBorder(g, new RectangleF(1, 1, bounds.Width - 2, bounds.Height - 2), CornerRadius, (float)crossT);
                    break;
                }

            case Phase.Hold:
                {
                    bounds = Lerp(_restBounds, _hoverBounds, Theme.EaseOutQuad(_hoverT));
                    var source = _hoverT > 0.01 && _hoverSource != null ? _hoverSource : _smallSource;
                    frame = new Bitmap(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height), PixelFormat.Format32bppArgb);
                    using var g = Graphics.FromImage(frame);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    DrawClippedContent(g, source, bounds);

                    double pulse = (Math.Sin(_stopwatch.ElapsedMilliseconds / (double)PulsePeriodMs * 2 * Math.PI) + 1) / 2;
                    float glowIntensity = _hoverT > 0.01 ? 1.0f : (float)(0.35 + 0.35 * pulse);
                    Theme.DrawGlowBorder(g, new RectangleF(1, 1, bounds.Width - 2, bounds.Height - 2), CornerRadius, glowIntensity);
                    break;
                }

            case Phase.Fade:
            default:
                {
                    bounds = _restBounds;
                    double t = Math.Min(1.0, _stopwatch.ElapsedMilliseconds / (double)FadeMs);
                    opacity = 1.0 - Theme.EaseInCubic(t);
                    frame = new Bitmap(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height), PixelFormat.Format32bppArgb);
                    using var g = Graphics.FromImage(frame);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    DrawClippedContent(g, _smallSource, bounds);
                    Theme.DrawGlowBorder(g, new RectangleF(1, 1, bounds.Width - 2, bounds.Height - 2), CornerRadius, 0.5f);
                    break;
                }
        }

        if (opacity < 1.0) ApplyOpacity(frame, opacity);
        LayeredSurface.Update(Handle, frame, bounds.Location);
        frame.Dispose();
    }

    private static void DrawClippedContent(Graphics g, Bitmap source, Rectangle destBounds)
    {
        var rect = new RectangleF(0, 0, destBounds.Width, destBounds.Height);
        using var path = Theme.RoundedRect(rect, CornerRadius);
        var oldClip = g.Clip;
        g.SetClip(path);
        g.DrawImage(source, rect.X, rect.Y, rect.Width, rect.Height);
        g.Clip = oldClip;
    }

    private static void DrawHighlightBorder(Graphics g, Rectangle localRect, float alpha)
    {
        if (alpha <= 0 || localRect.Width <= 4 || localRect.Height <= 4) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(2, 2, localRect.Width - 4, localRect.Height - 4);
        int a = (int)(Math.Clamp(alpha, 0f, 1f) * 255);
        var c1 = Color.FromArgb(a, Theme.AccentStart);
        var c2 = Color.FromArgb(a, Theme.AccentEnd);
        using var brush = new LinearGradientBrush(rect, c1, c2, 45f);
        using var pen = new Pen(brush, 3f) { DashStyle = DashStyle.Dash };
        using var path = Theme.RoundedRect(rect, 4f);
        g.DrawPath(pen, path);
    }

    private static void ApplyOpacity(Bitmap frame, double opacity)
    {
        var rect = new Rectangle(0, 0, frame.Width, frame.Height);
        var data = frame.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            byte[] buf = new byte[stride * frame.Height];
            Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            for (int i = 3; i < buf.Length; i += 4)
                buf[i] = (byte)(buf[i] * opacity);
            Marshal.Copy(buf, 0, data.Scan0, buf.Length);
        }
        finally
        {
            frame.UnlockBits(data);
        }
    }

    private static Rectangle Lerp(Rectangle a, Rectangle b, double t) => new(
        a.X + (int)((b.X - a.X) * t),
        a.Y + (int)((b.Y - a.Y) * t),
        a.Width + (int)((b.Width - a.Width) * t),
        a.Height + (int)((b.Height - a.Height) * t));

    private void OpenFileAndClose()
    {
        try
        {
            // Chụp bằng chứng không lưu file (chỉ clipboard) -> _filePath rỗng, không có gì để mở.
            if (!string.IsNullOrEmpty(_filePath))
                Process.Start(new ProcessStartInfo(_filePath) { UseShellExecute = true });
        }
        catch
        {
            // Không mở được thì thôi, không phải lỗi nghiêm trọng.
        }
        CloseAndDispose();
    }

    private void CloseAndDispose()
    {
        _timer.Stop();
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _timer.Dispose();
        _original?.Dispose();
        _original = null;
        _smallSource?.Dispose();
        _smallSource = null;
        _hoverSource?.Dispose();
        _hoverSource = null;
    }

    // Ẩn khỏi Alt+Tab, không cướp focus, và bật layered window cho alpha per-pixel.
    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x80;
            const int WS_EX_NOACTIVATE = 0x08000000;
            const int WS_EX_LAYERED = 0x00080000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;
}

// ==================== LayeredSurface ====================

internal static class LayeredSurface
{
    public static void Update(IntPtr hwnd, Bitmap frame, Point screenLocation)
    {
        int w = frame.Width, h = frame.Height;
        if (w <= 0 || h <= 0) return;

        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;
        try
        {
            var bmi = new Native.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // top-down DIB: khớp thứ tự hàng của BitmapData khi copy
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            };

            hBitmap = Native.CreateDIBSection(screenDc, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero) return;

            var srcData = frame.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                CopyPremultiplied(srcData, bits, w, h);
            }
            finally
            {
                frame.UnlockBits(srcData);
            }

            oldBitmap = Native.SelectObject(memDc, hBitmap);

            var ptDst = new Native.POINT(screenLocation.X, screenLocation.Y);
            var size = new Native.SIZE(w, h);
            var ptSrc = new Native.POINT(0, 0);
            var blend = new Native.BLENDFUNCTION
            {
                BlendOp = Native.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = Native.AC_SRC_ALPHA,
            };

            Native.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref ptDst, ref size, memDc, ref ptSrc, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero) Native.SelectObject(memDc, oldBitmap);
            if (hBitmap != IntPtr.Zero) Native.DeleteObject(hBitmap);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // GDI layered window cần alpha premultiplied, còn Format32bppArgb thì không — phải tự nhân.
    private static void CopyPremultiplied(BitmapData src, IntPtr destBits, int w, int h)
    {
        int srcStride = src.Stride;
        int destStride = w * 4;

        byte[] srcBuf = new byte[srcStride * h];
        Marshal.Copy(src.Scan0, srcBuf, 0, srcBuf.Length);
        byte[] destBuf = new byte[destStride * h];

        for (int y = 0; y < h; y++)
        {
            int sRow = y * srcStride;
            int dRow = y * destStride;
            for (int x = 0; x < w; x++)
            {
                int si = sRow + x * 4;
                int di = dRow + x * 4;
                byte b = srcBuf[si + 0];
                byte g = srcBuf[si + 1];
                byte r = srcBuf[si + 2];
                byte a = srcBuf[si + 3];

                destBuf[di + 0] = (byte)(b * a / 255);
                destBuf[di + 1] = (byte)(g * a / 255);
                destBuf[di + 2] = (byte)(r * a / 255);
                destBuf[di + 3] = a;
            }
        }

        Marshal.Copy(destBuf, 0, destBits, destBuf.Length);
    }
}

// ==================== Theme ====================

internal static class Theme
{
    public static readonly Color AccentStart = Color.FromArgb(59, 130, 246);   // #3B82F6
    public static readonly Color AccentEnd = Color.FromArgb(139, 92, 246);     // #8B5CF6
    public static readonly Color PanelBackground = Color.FromArgb(20, 22, 28); // #14161C
    public static readonly Color TextPrimary = Color.FromArgb(244, 244, 246);  // #F4F4F6
    public static readonly Color TextSecondary = Color.FromArgb(160, 164, 176);

    public static LinearGradientBrush AccentBrush(RectangleF rect, float angle = 45f) =>
        new(rect, AccentStart, AccentEnd, angle);

    public static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        if (d > rect.Width) d = Math.Max(0, rect.Width);
        if (d > rect.Height) d = Math.Max(0, rect.Height);

        if (d <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void DrawGlowBorder(Graphics g, RectangleF rect, float radius, float intensity, int layers = 4, float maxSpread = 6f)
    {
        if (rect.Width < 2 || rect.Height < 2 || intensity <= 0) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (int i = layers; i >= 1; i--)
        {
            float spread = maxSpread * i / layers;
            float alpha = intensity * (1f - (float)i / (layers + 1)) * 0.5f;
            var outer = RectangleF.Inflate(rect, spread, spread);
            using var path = RoundedRect(outer, radius + spread);
            using var pen = new Pen(Color.FromArgb((int)(Math.Clamp(alpha, 0f, 1f) * 255), AccentStart), 2f);
            g.DrawPath(pen, path);
        }

        using var borderPath = RoundedRect(rect, radius);
        using var borderBrush = AccentBrush(rect);
        using var borderPen = new Pen(borderBrush, 2f);
        g.DrawPath(borderPen, borderPath);
    }

    public static double EaseOutCubic(double t) => 1 - Math.Pow(1 - t, 3);
    public static double EaseInCubic(double t) => t * t * t;
    public static double EaseOutQuad(double t) => 1 - (1 - t) * (1 - t);
}

// ==================== Native ====================

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
        public POINT(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
        public SIZE(int cx, int cy) { this.cx = cx; this.cy = cy; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    public const uint ULW_ALPHA = 0x02;
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObj);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObj);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
}
