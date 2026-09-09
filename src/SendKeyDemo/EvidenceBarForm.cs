using System.Drawing.Drawing2D;
using System.Drawing.Text;
using QuickShot;

namespace SendKeyDemo;

public enum StripState { Idle, Pending, Ok, Confirm, Block }

/// <summary>
/// Thanh làm việc luôn nổi trên cùng trong lúc chụp bằng chứng.
/// Hàng 1: cặp vừa copy từ Excel (cmdLabel / cmdVar trong 「 」).
/// Hàng 2: cặp C# tương ứng — tự điền nếu mapping.csv đã có, KHÔNG có thì để trống
///          cho người dùng gõ rồi Enter (ghi thêm dòng vào mapping.csv rồi chạy luôn).
/// Hàng 3: đèn tín hiệu + tiến độ.
/// Cố ý không cướp focus khi hiện/cập nhật — dev đang gõ trong VS hoặc Excel.
/// </summary>
public sealed class EvidenceBarForm : Form
{
    readonly TextBox _cmdLabel = new();
    readonly TextBox _cmdVar = new();
    readonly TextBox _csLabel = new();
    readonly TextBox _csVar = new();
    readonly Button _run = new() { Text = "⏎ Chạy", AutoSize = true };
    readonly Panel _status = new() { Dock = DockStyle.Bottom, Height = 30 };

    StripState _state = StripState.Idle;
    string _statusText = "";
    string _progress = "";

    public event Action? RunRequested;
    public event Action? OpenConfigRequested;

    protected override bool ShowWithoutActivation => true;

    public string CmdLabel => _cmdLabel.Text.Trim();
    public string CmdVar => _cmdVar.Text.Trim();
    public string CsLabel => _csLabel.Text.Trim();
    public string CsVar => _csVar.Text.Trim();

    public EvidenceBarForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(780, 116);
        BackColor = Theme.PanelBackground;
        DoubleBuffered = true;
        KeyPreview = true;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2,
            Padding = new Padding(10, 8, 10, 4), BackColor = Theme.PanelBackground,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));   // nhãn hàng
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));    // label
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16));   // 「
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));    // var
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 16));   // 」
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // nút
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        foreach (var tb in new[] { _cmdLabel, _cmdVar, _csLabel, _csVar })
        {
            tb.Dock = DockStyle.Fill;
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.BackColor = Color.FromArgb(30, 33, 41);
            tb.ForeColor = Theme.TextPrimary;
            tb.Font = new Font("Consolas", 9.5f);
            tb.Margin = new Padding(3, 4, 3, 4);
            tb.KeyDown += OnFieldKeyDown;
        }

        // Hàng cmd chỉ để xem — clipboard điền vào, sửa tay không có tác dụng gì thêm.
        _cmdLabel.ReadOnly = _cmdVar.ReadOnly = true;
        _cmdLabel.ForeColor = _cmdVar.ForeColor = Theme.TextSecondary;
        _cmdVar.PlaceholderText = "copy phần trong 「 」";
        _cmdLabel.PlaceholderText = "copy label (dòng trên)";
        _csLabel.PlaceholderText = "csharpLabel — gõ nếu chưa có";
        _csVar.PlaceholderText = "csharpVar — gõ nếu chưa có";

        grid.Controls.Add(RowCaption("cmd"), 0, 0);
        grid.Controls.Add(_cmdLabel, 1, 0);
        grid.Controls.Add(Bracket("「"), 2, 0);
        grid.Controls.Add(_cmdVar, 3, 0);
        grid.Controls.Add(Bracket("」"), 4, 0);

        grid.Controls.Add(RowCaption("C#"), 0, 1);
        grid.Controls.Add(_csLabel, 1, 1);
        grid.Controls.Add(Bracket(""), 2, 1);
        grid.Controls.Add(_csVar, 3, 1);
        grid.Controls.Add(Bracket(""), 4, 1);

        _run.Margin = new Padding(6, 4, 0, 4);
        _run.FlatStyle = FlatStyle.Flat;
        _run.BackColor = Color.FromArgb(45, 50, 62);
        _run.ForeColor = Theme.TextPrimary;
        _run.FlatAppearance.BorderColor = Theme.AccentStart;
        _run.Click += (_, _) => RunRequested?.Invoke();
        grid.Controls.Add(_run, 5, 1);

        _status.Paint += PaintStatus;
        _status.DoubleClick += (_, _) => OpenConfigRequested?.Invoke();

        Controls.Add(grid);
        Controls.Add(_status);
    }

    static Label RowCaption(string text) => new()
    {
        Text = text, Dock = DockStyle.Fill, ForeColor = Theme.TextSecondary,
        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft,
    };

    static Label Bracket(string text) => new()
    {
        Text = text, Dock = DockStyle.Fill, ForeColor = Theme.AccentStart,
        Font = new Font("Segoe UI", 11f), TextAlign = ContentAlignment.MiddleCenter,
    };

    void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is not (Keys.Enter or Keys.Return)) return;
        e.SuppressKeyPress = true;
        RunRequested?.Invoke();
    }

    /// <summary>Điền cặp vừa copy. <paramref name="cmdVar"/> null = giữ nguyên ô cũ.</summary>
    public void SetCmd(string cmdLabel, string? cmdVar)
    {
        _cmdLabel.Text = cmdLabel;
        if (cmdVar != null) _cmdVar.Text = cmdVar;
    }

    /// <summary>Điền cặp C# tra được. Chuỗi rỗng = chưa có trong mapping.csv, tô sáng cho người dùng gõ.</summary>
    public void SetCs(string csLabel, string csVar)
    {
        _csLabel.Text = csLabel;
        _csVar.Text = csVar;
        Highlight(_csLabel, csLabel.Length == 0);
        Highlight(_csVar, csVar.Length == 0);
    }

    static void Highlight(TextBox tb, bool needsInput)
        => tb.BackColor = needsInput ? Color.FromArgb(58, 44, 20) : Color.FromArgb(30, 33, 41);

    /// <summary>Đưa con trỏ vào ô C# đầu tiên còn trống (nếu có) để gõ ngay.</summary>
    public void FocusFirstEmptyCs()
    {
        var target = _csLabel.Text.Trim().Length == 0 ? _csLabel
                   : _csVar.Text.Trim().Length == 0 ? _csVar
                   : null;
        if (target == null) return;
        Activate();
        target.Focus();
        target.SelectionStart = target.TextLength;
    }

    public void SetStatus(StripState state, string text, string progress = "")
    {
        _state = state;
        _statusText = text ?? "";
        _progress = progress ?? "";
        _status.Invalidate();
    }

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
        StripState.Ok => Color.FromArgb(74, 222, 128),
        StripState.Confirm => Color.FromArgb(250, 204, 21),
        StripState.Block => Color.FromArgb(248, 113, 113),
        StripState.Pending => Theme.AccentStart,
        _ => Color.FromArgb(148, 152, 164),
    };

    void PaintStatus(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.PanelBackground);

        using (var dot = new SolidBrush(DotColor))
            g.FillEllipse(dot, 12, _status.Height / 2f - 5, 10, 10);

        using var font = new Font("Segoe UI", 9f);
        using var progressFont = new Font("Consolas", 8.5f, FontStyle.Bold);

        float rightPad = 12;
        if (_progress.Length > 0)
        {
            var size = g.MeasureString(_progress, progressFont);
            using var pb = new SolidBrush(Theme.TextSecondary);
            g.DrawString(_progress, progressFont, pb,
                _status.Width - size.Width - 12, (_status.Height - size.Height) / 2f);
            rightPad = size.Width + 24;
        }

        using var tb = new SolidBrush(Theme.TextPrimary);
        using var fmt = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        g.DrawString(_statusText, font, tb,
            new RectangleF(30, 0, _status.Width - 30 - rightPad, _status.Height), fmt);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var body = new RectangleF(0.5f, 0.5f, ClientSize.Width - 1, ClientSize.Height - 1);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.RoundedRect(body, 8f);
        using var pen = new Pen(Color.FromArgb(80, DotColor), 1.5f);
        e.Graphics.DrawPath(pen, path);
    }

    // Kéo thanh bằng vùng trống (ô nhập vẫn bấm vào gõ được bình thường).
    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTCLIENT = 1, HTCAPTION = 2;

        base.WndProc(ref m);
        if (m.Msg == WM_NCHITTEST && m.Result.ToInt32() == HTCLIENT)
            m.Result = new IntPtr(HTCAPTION);
    }
}
