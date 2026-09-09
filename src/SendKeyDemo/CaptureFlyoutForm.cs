using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace QuickShot;

/// <summary>
/// Hiệu ứng sau khi chụp: viền gradient lóe lên (fade-in) đúng khung vùng vừa chụp để
/// xác nhận, rồi ảnh co dần về một khung nhỏ ở góc dưới-phải màn hình (ease-out cubic),
/// "thở" bằng glow nhẹ trong lúc giữ, phóng to khi rê chuột vào (tạm dừng đếm giờ mờ dần),
/// rồi tự mờ dần biến mất. Click lúc đang giữ để mở file ảnh đã lưu.
///
/// Vẽ thủ công qua layered window (UpdateLayeredWindow) thay vì BackgroundImage/Opacity
/// mặc định của Form: mỗi frame chỉ có 1 lệnh set vị trí+kích thước+nội dung, rẻ hơn
/// nhiều so với SetWindowPos + WM_PAINT riêng lẻ — đây là nguyên nhân chính gây giật đo
/// được ở bản trước (frame-time 7-45ms thay vì đều ~15ms).
/// </summary>
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

    private Bitmap? _original;    // ảnh full-res: Highlight (1:1, không cần scale) + nguồn cho hover-zoom
    private Bitmap? _smallSource; // prescale ~2x kích thước nghỉ, dùng cho Fly/Hold — rẻ để scale lại mỗi tick
    private Bitmap? _hoverSource; // prescale đúng kích thước hover, dựng lười lúc hover lần đầu

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

    // Phóng to neo tại góc dưới-phải (điểm cố định của thumbnail) để không tràn ra ngoài màn hình.
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

                // Cross-fade: viền highlight mờ dần trong 100ms đầu, viền glow của thumbnail hiện dần thay thế
                // — thay cho cú cắt cứng Invalidate() ở bản trước.
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

    // Nhân alpha toàn frame theo opacity — cần cho pha Fade vì UpdateLayeredWindow không
    // có tham số "opacity toàn cục" riêng như Form.Opacity, phải áp trực tiếp vào từng pixel.
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

    // Ẩn khỏi Alt+Tab, không cướp focus của cửa sổ đang dùng, và bật layered window
    // để UpdateLayeredWindow điều khiển alpha per-pixel.
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
