using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace QuickShot;

/// <summary>
/// Overlay phủ TOÀN BỘ các màn hình (virtual screen): nền tối có vignette nhẹ (đậm
/// dần ra rìa) thay vì màu đen phẳng, fade-in nhanh lúc mở, cho user kéo chuột chọn
/// một khung chữ nhật viền gradient. Trả về Rectangle theo tọa độ màn hình thật.
/// </summary>
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

    // Kết quả: null nếu user hủy (Esc / click không kéo)
    public Rectangle? Result { get; private set; }

    public RegionSelector()
    {
        // Phủ hết mọi màn hình, kể cả tọa độ âm (màn bên trái màn chính)
        var vs = SystemInformation.VirtualScreen;
        StartPosition = FormStartPosition.Manual;
        Bounds = vs;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        Opacity = 0.0;
        BackColor = Color.Black;
        Cursor = Cursors.Cross;
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
        _fadeStopwatch.Start();
        _fadeTimer.Start();
    }

    // Vẽ 1 lần lúc mở: PathGradientBrush tâm sáng hơn rìa một chút. Vì cả overlay
    // đã bị Opacity của Form nhân xuống ~0.42 nên chênh lệch RGB ở đây vẫn giữ được
    // cảm giác "đậm dần ra rìa" chứ không cần alpha khác nhau theo từng điểm.
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

    private void OnMouseDown(object? s, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _fadeTimer.Stop();
        Opacity = TargetOverlayOpacity;
        _dragging = true;
        _start = e.Location;
        _selection = new Rectangle(e.Location, Size.Empty);
    }

    private void OnMouseMove(object? s, MouseEventArgs e)
    {
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
        string label = $"{_selection.Width} x {_selection.Height}";
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
