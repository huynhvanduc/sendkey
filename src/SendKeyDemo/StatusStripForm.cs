using System.Drawing.Drawing2D;
using System.Drawing.Text;
using QuickShot;

namespace SendKeyDemo;

public enum StripState { Idle, Pending, Ok, Confirm, Block }

/// <summary>
/// Thanh mỏng luôn nổi trên cùng, hiện test case đang làm + đèn tín hiệu.
/// Cố ý KHÔNG nhận focus: dev đang gõ trong VS, thanh này chỉ để liếc.
/// </summary>
public sealed class StatusStripForm : Form
{
    StripState _state = StripState.Idle;
    string _text = "";
    string _progress = "";

    public event Action? OpenConfigRequested;

    // Không cướp focus khỏi Visual Studio khi hiện lên / cập nhật.
    protected override bool ShowWithoutActivation => true;

    public StatusStripForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(620, 42);
        BackColor = Theme.PanelBackground;
        DoubleBuffered = true;
        DoubleClick += (_, _) => OpenConfigRequested?.Invoke();
    }

    public void SetState(StripState state, string text, string progress = "")
    {
        _state = state;
        _text = text ?? "";
        _progress = progress ?? "";
        Invalidate();
    }

    /// <summary>Đặt thanh ở vị trí đã nhớ, không có thì canh giữa mép trên màn hình chứa chuột.</summary>
    public void PlaceAt(int? x, int? y)
    {
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        if (x is { } px && y is { } py &&
            Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(px, py, Width, Height))))
        {
            Location = new Point(px, py);
            return;
        }
        Location = new Point(screen.X + (screen.Width - Width) / 2, screen.Y + 8);
    }

    Color DotColor => _state switch
    {
        StripState.Ok => Color.FromArgb(74, 222, 128),        // xanh lá
        StripState.Confirm => Color.FromArgb(250, 204, 21),   // vàng
        StripState.Block => Color.FromArgb(248, 113, 113),    // đỏ
        StripState.Pending => Theme.AccentStart,
        _ => Color.FromArgb(148, 152, 164),
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var body = new RectangleF(0.5f, 0.5f, ClientSize.Width - 1, ClientSize.Height - 1);
        using (var path = Theme.RoundedRect(body, 8f))
        using (var bg = new SolidBrush(Theme.PanelBackground))
        using (var pen = new Pen(Color.FromArgb(70, DotColor), 1.5f))
        {
            g.FillPath(bg, path);
            g.DrawPath(pen, path);
        }

        using (var dot = new SolidBrush(DotColor))
            g.FillEllipse(dot, 14, ClientSize.Height / 2f - 5, 10, 10);

        using var font = new Font("Segoe UI", 9.5f);
        using var progressFont = new Font("Consolas", 9f, FontStyle.Bold);

        float rightPad = 12;
        if (_progress.Length > 0)
        {
            var size = g.MeasureString(_progress, progressFont);
            using var pb = new SolidBrush(Theme.TextSecondary);
            g.DrawString(_progress, progressFont, pb,
                ClientSize.Width - size.Width - 12, (ClientSize.Height - size.Height) / 2f);
            rightPad = size.Width + 24;
        }

        var textRect = new RectangleF(34, 0, ClientSize.Width - 34 - rightPad, ClientSize.Height);
        using var tb = new SolidBrush(Theme.TextPrimary);
        using var fmt = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        g.DrawString(_text, font, tb, textRect, fmt);
    }

    // Kéo được cả thanh bằng cách coi toàn bộ vùng client là thanh tiêu đề.
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTCLIENT = 1, HTCAPTION = 2;

        base.WndProc(ref m);
        if (m.Msg == WM_NCHITTEST && m.Result.ToInt32() == HTCLIENT)
            m.Result = new IntPtr(HTCAPTION);
    }
}
