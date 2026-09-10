using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Media;
using System.Runtime.InteropServices;

namespace SendKeyDemo;

/// <summary>
/// Một đợt chụp bằng chứng. Nghe clipboard để bắt cặp label / 「biến」 copy từ file test case,
/// hiện cặp C# tương ứng (chưa có thì cho gõ tay rồi ghi vào mapping.csv), bám sự kiện VS dừng
/// để tự chấm, và gác cổng phím chụp — sai thao tác cơ học thì KHÔNG cho ra ảnh.
/// </summary>
public sealed class EvidenceSession : IDisposable
{
    readonly MainForm _host;
    readonly AppSettings _settings;
    readonly EvidenceBarForm _bar = new();

    ClipboardWatcher? _clip;
    VsAutomation.BreakWatcher? _watcher;     // phải giữ field, xem chú thích trong BreakWatcher
    LastCapture? _last;
    bool _active;
    int _shotCount;

    string _cmdLabel = "";
    string _cmdVar = "";

    // Đích đã tra được — dùng để đối chiếu với chỗ VS thật sự dừng.
    string _targetFile = "";
    int _targetLine;
    string _watchExpr = "";

    public EvidenceSession(MainForm host, AppSettings settings)
    {
        _host = host;
        _settings = settings;
        _bar.RunRequested += Run;
    }

    public bool Active => _active;
    public EvidenceBarForm Bar => _bar;

    string Progress => $"{_shotCount} ảnh";

    /// <summary>Tên cặp đang chụp, chỉ để ghi vào thông báo — vd "CHECK_INPUT/%RC%".</summary>
    string TcId => _cmdVar.Length > 0 ? $"{_cmdLabel}/{_cmdVar}" : _cmdLabel;

    // ---------------- vòng đời ----------------

    /// <summary>Bắt đầu đợt: hiện thanh, nghe clipboard, bám sự kiện VS dừng.</summary>
    public void Start()
    {
        Stop(keepBar: true);
        _active = true;
        _shotCount = 0;

        _bar.PlaceAt(_settings.StripX, _settings.StripY);
        _bar.Show();

        AttachWatcher();

        _clip = new ClipboardWatcher();
        _clip.TextCopied += OnCopied;
        _cmdLabel = _cmdVar = "";
        _bar.SetCmd("", "");
        _bar.SetCs("", "");
        SetStatus(StripState.Pending, "Copy label trong file test case (Excel) để bắt đầu.");
        _host.Log("Chụp bằng chứng: bắt đầu — copy label rồi 「biến」 từ Excel.");
    }

    public void Stop() => Stop(keepBar: false);

    void Stop(bool keepBar)
    {
        _watcher?.Dispose();
        _watcher = null;

        if (_clip != null)
        {
            _clip.TextCopied -= OnCopied;
            _clip.Dispose();
            _clip = null;
        }

        if (_active)
        {
            _settings.StripX = _bar.Location.X;
            _settings.StripY = _bar.Location.Y;
            _settings.Save();
        }

        _active = false;
        _last = null;
        if (!keepBar) _bar.Hide();
    }

    /// <summary>Gắn lại sự kiện break khi đổi instance VS (hoặc VS vừa mở lại).</summary>
    public void AttachWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (_host.CurrentDte() is not { } dte) return;

        try { _watcher = new VsAutomation.BreakWatcher(dte, OnEnterBreak); }
        catch (Exception ex) { _host.Log("Không bám được sự kiện dừng của VS: " + ex.Message); }
    }

    void OnEnterBreak()
    {
        if (!_bar.IsHandleCreated) return;
        try { _bar.BeginInvoke(new Action(() => Recheck(fromEvent: true))); }
        catch (InvalidOperationException) { /* form đang đóng */ }
    }

    // ---------------- nhận cái vừa copy ----------------

    void OnCopied(string raw)
    {
        if (!_bar.IsHandleCreated) return;
        try { _bar.BeginInvoke(new Action(() => ApplyCopied(raw))); }
        catch (InvalidOperationException) { }
    }

    void ApplyCopied(string raw)
    {
        if (!_active) return;

        // Quy tắc duy nhất: trong 「 」 là biến/mệnh đề, ngoài ngoặc là label.
        if (CopiedText.Classify(raw) is not { } piece) return;

        if (piece.IsVar) _cmdVar = piece.Value;
        else { _cmdLabel = piece.Value; _cmdVar = ""; }

        Prefill();
    }

    /// <summary>Tra mapping.csv để điền sẵn cặp C#; chưa có thì để trống cho người dùng gõ.</summary>
    void Prefill()
    {
        _bar.SetCmd(_cmdLabel, _cmdVar);
        _targetLine = 0;

        var rows = _host.GetMapRows();
        if (rows == null)
        {
            _bar.SetCs("", "");
            SetStatus(StripState.Block, "Chưa nạp được mapping.csv — kiểm tra đường dẫn ở cửa sổ cấu hình.");
            return;
        }

        if (_cmdLabel.Length == 0) { _bar.SetCs("", ""); return; }

        var label = Mapping.NormalizeLabel(_cmdLabel);
        var sameLabel = rows.Where(r => Mapping.NormalizeLabel(r.CmdLabel) == label).ToList();

        if (_cmdVar.Length == 0)
        {
            // Mới copy label, chưa copy 「 」 — chỉ hiện csharpLabel để đối chiếu.
            _bar.SetCs(sameLabel.Count > 0 ? sameLabel[0].CsharpLabel : "", "");
            SetStatus(StripState.Pending, sameLabel.Count > 0
                ? $"Label đã có trong mapping. Copy tiếp phần trong 「 」."
                : $"Label \"{_cmdLabel}\" chưa có trong mapping. Copy tiếp 「 」 rồi điền cặp C#.");
            return;
        }

        var res = Mapping.Resolve(rows, _cmdLabel, _cmdVar);
        switch (res.Kind)
        {
            case LookupKind.Ok:
                _bar.SetCs(res.Row!.CsharpLabel, res.Row.CsharpVar);
                SetStatus(StripState.Pending, $"Đã có trong mapping — bấm {_settings.GotoCurrentHotkey} để đặt breakpoint.");
                break;

            case LookupKind.Duplicate:
                _bar.SetCs("", "");
                SetStatus(StripState.Block,
                    $"mapping.csv trùng ở dòng {string.Join(", ", res.DuplicateLines!)} — sửa file rồi thử lại.");
                break;

            case LookupKind.NeedPickVar:
                // Label đã biết, biến thì chưa: điền sẵn csharpLabel, để trống csharpVar cho người dùng gõ.
                _bar.SetCs(sameLabel.Count > 0 ? sameLabel[0].CsharpLabel : "", "");
                SetStatus(StripState.Confirm,
                    $"Biến \"{_cmdVar}\" chưa có — gõ csharpVar rồi Enter (sẽ ghi thêm vào mapping.csv).");
                break;

            default:   // NotFoundLabel
                _bar.SetCs("", "");
                SetStatus(StripState.Confirm,
                    $"Chưa có trong mapping — gõ csharpLabel + csharpVar rồi Enter (sẽ ghi thêm vào mapping.csv).");
                break;
        }
    }

    // ---------------- chạy: ghi mapping nếu cần, rồi đặt breakpoint ----------------

    /// <summary>Enter trong thanh, nút ⏎ Chạy, hoặc hotkey GotoCurrent.</summary>
    public void Run()
    {
        if (!_active) return;

        if (_cmdLabel.Length == 0)
        {
            SetStatus(StripState.Block, "Chưa copy label từ file test case.");
            Beep(StripState.Block);
            return;
        }

        var csLabel = _bar.CsLabel;
        var csVar = _bar.CsVar;
        if (csLabel.Length == 0 || csVar.Length == 0)
        {
            SetStatus(StripState.Confirm, "Điền nốt csharpLabel / csharpVar rồi Enter.");
            _bar.FocusFirstEmptyCs();
            Beep(StripState.Confirm);
            return;
        }

        var rows = _host.GetMapRows();
        if (rows == null) { SetStatus(StripState.Block, "Chưa nạp được mapping.csv."); Beep(StripState.Block); return; }

        var cmdVarForLookup = _cmdVar.Length > 0 ? _cmdVar : null;
        var res = Mapping.Resolve(rows, _cmdLabel, cmdVarForLookup);

        if (res.Kind is LookupKind.NotFoundLabel or LookupKind.NeedPickVar)
        {
            var err = _host.AddMapping(_cmdLabel, _cmdVar, csLabel, csVar);
            if (err != null)
            {
                SetStatus(StripState.Block, err);
                _bar.FocusFirstEmptyCs();
                Beep(StripState.Block);
                return;
            }
            rows = _host.GetMapRows();
            if (rows == null) { SetStatus(StripState.Block, "Chưa nạp lại được mapping.csv."); return; }
            res = Mapping.Resolve(rows, _cmdLabel, cmdVarForLookup);
        }

        if (res.Kind != LookupKind.Ok || res.Row is not { } row)
        {
            SetStatus(StripState.Block, res.Kind == LookupKind.Duplicate
                ? $"mapping.csv trùng ở dòng {string.Join(", ", res.DuplicateLines!)}."
                : "Tra mapping không ra sau khi ghi — kiểm tra mapping.csv.");
            Beep(StripState.Block);
            return;
        }

        // Người dùng sửa ô C# nhưng cặp cmd đã có sẵn dòng khác -> dùng dòng trong file, báo cho biết.
        if (row.CsharpLabel != csLabel || row.CsharpVar != csVar)
        {
            _bar.SetCs(row.CsharpLabel, row.CsharpVar);
            _host.Log($"Chụp: cặp này đã có trong mapping.csv ({row.CsharpLabel}/{row.CsharpVar}) — " +
                      "dùng dòng có sẵn. Muốn đổi thì sửa thẳng mapping.csv.");
        }

        var csp = _host.EffectiveCsPath(row);
        if (string.IsNullOrWhiteSpace(csp) || !File.Exists(csp))
        {
            SetStatus(StripState.Block, $"Không thấy file .cs: {csp}");
            Beep(StripState.Block);
            return;
        }

        var ll = Mapping.FindLabelLine(csp, row.CsharpLabel, row.CsharpVar);
        if (ll.Kind != LabelLineKind.Ok)
        {
            SetStatus(StripState.Block, ll.Kind switch
            {
                LabelLineKind.NotFound => $"Không thấy \"{row.CsharpLabel}:\" trong {Path.GetFileName(csp)}.",
                LabelLineKind.Multiple => $"\"{row.CsharpLabel}:\" xuất hiện ở dòng {string.Join(", ", ll.MatchLines!)}.",
                LabelLineKind.NoExecutableLine => $"Sau \"{row.CsharpLabel}:\" không còn dòng thực thi.",
                _ => $"Không thấy biểu thức \"{row.CsharpVar}\" sau \"{row.CsharpLabel}:\".",
            });
            Beep(StripState.Block);
            return;
        }

        _targetFile = csp;
        _targetLine = ll.Line;
        _watchExpr = row.CsharpVar;

        // Lỗ hổng âm thầm: định danh thuần thì breakpoint rơi vào dòng đầu sau label bất kể
        // biến gán ở đâu -> giá trị có thể chưa được gán tại dòng này.
        if (!Mapping.IsExpression(_watchExpr) && LineMentions(csp, ll.Line, _watchExpr) == false)
            _host.Log($"Chụp: ⚠ dòng {Path.GetFileName(csp)}:{ll.Line} không nhắc tới \"{_watchExpr}\" — " +
                      "giá trị có thể chưa được gán ở đây.");

        if (_host.CurrentDte() is not { } dte)
        {
            SetStatus(StripState.Block, "Chưa chọn instance VS — mở cửa sổ cấu hình bấm Refresh.");
            Beep(StripState.Block);
            return;
        }

        try
        {
            _host.Log("Chụp: " + VsAutomation.SetOnlyBreakpoint(dte, csp, ll.Line));
            _host.Log("Chụp: " + VsAutomation.GoToLine(dte, csp, ll.Line));
            if (_clip != null) _clip.IgnoreText = _watchExpr;   // đừng tự nhận lại biểu thức mình vừa copy
            try { Clipboard.SetText(_watchExpr); } catch { /* clipboard bận */ }
        }
        catch (Exception ex)
        {
            SetStatus(StripState.Block, "Lỗi thao tác VS: " + ex.Message);
            Beep(StripState.Block);
            return;
        }

        SetStatus(StripState.Pending,
            $"▶ {Path.GetFileName(csp)}:{ll.Line} · F5 → dừng → Ctrl+V vào Watch");
    }

    // ---------------- chấm + chụp ----------------

    public CheckResult? Recheck(bool fromEvent = false)
    {
        if (!_active || _targetLine == 0) return null;

        if (_host.CurrentDte() is not { } dte)
        {
            SetStatus(StripState.Block, "Chưa chọn instance VS.");
            return new CheckResult(CheckLevel.Block, "Chưa chọn instance VS.");
        }

        var snap = VsAutomation.ReadDebugState(dte, _targetFile, _watchExpr);
        var result = CaptureCheck.Evaluate(snap, TcId, _targetFile, _targetLine, _watchExpr, _last);

        if (fromEvent && !snap.InBreakMode) return result;   // chưa tới lúc, im lặng

        SetStatus(LevelToState(result.Level), Prefix(result.Level) + result.Message);
        if (fromEvent) Beep(LevelToState(result.Level));
        return result;
    }

    /// <summary>Phím chụp: chấm trước, đỏ thì không ra ảnh.</summary>
    public void CaptureCurrent()
    {
        if (!_active) return;

        if (_targetLine == 0)
        {
            SetStatus(StripState.Block, $"Chưa đặt breakpoint — bấm {_settings.GotoCurrentHotkey} trước.");
            Beep(StripState.Block);
            return;
        }

        if (_host.SavedRegion is not { } region)
        {
            SetStatus(StripState.Block, $"Chưa khoanh vùng chụp — bấm {_settings.DefineRegionHotkey} một lần.");
            Beep(StripState.Block);
            return;
        }

        var tcId = TcId;
        var result = Recheck();
        if (result == null) return;

        if (result.Level == CheckLevel.Block)
        {
            Beep(StripState.Block);
            _host.Log("Chụp BỊ CHẶN: " + result.Message);
            return;
        }

        if (result.Level == CheckLevel.Confirm)
        {
            Beep(StripState.Confirm);
            var answer = MessageBox.Show(_bar, result.Message, "Xác nhận chụp",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                SetStatus(StripState.Confirm, "Đã hủy chụp — " + result.Message);
                _host.Log("Chụp: người dùng hủy — " + result.Message);
                return;
            }
        }

        // Không lưu PNG: ảnh vào thẳng clipboard để dán vào tài liệu bằng chứng.
        // Ẩn thanh nổi + dời chuột ra ngoài vùng trước khi chụp để ảnh không dính thanh / tooltip.
        var shot = ScreenCapture.GrabClean(region, _settings, _bar);
        if (!shot.Ok)
        {
            SetStatus(StripState.Block, shot.Message);
            Beep(StripState.Block);
            _host.Log("Chụp: " + shot.Message);
            return;
        }

        if (_host.CurrentDte() is { } dteNow)
        {
            var snapNow = VsAutomation.ReadDebugState(dteNow, _targetFile, _watchExpr);
            _last = new LastCapture(snapNow.ProcessId, tcId, _watchExpr, snapNow.ExprValue);
        }

        _shotCount++;
        _host.Log($"Chụp XONG {tcId} → clipboard (Ctrl+V để dán). {Progress}");
        Beep(StripState.Ok);

        SetStatus(StripState.Ok, "✓ Đã chụp — Ctrl+V để dán. Copy cặp tiếp theo từ Excel.");
    }

    // ---------------- trợ giúp ----------------

    static bool? LineMentions(string file, int line, string ident)
    {
        try
        {
            var lines = File.ReadAllLines(file);
            if (line < 1 || line > lines.Length) return null;
            return lines[line - 1].Contains(ident, StringComparison.Ordinal);
        }
        catch { return null; }
    }

    static StripState LevelToState(CheckLevel level) => level switch
    {
        CheckLevel.Ok => StripState.Ok,
        CheckLevel.Confirm => StripState.Confirm,
        _ => StripState.Block,
    };

    static string Prefix(CheckLevel level) => level switch
    {
        CheckLevel.Ok => "✅ ",
        CheckLevel.Confirm => "⚠ ",
        _ => "❌ ",
    };

    void SetStatus(StripState state, string text) => _bar.SetStatus(state, text, Progress);

    // Phản hồi bằng âm thanh: mắt dev đang ở VS hoặc Excel, không ở thanh này.
    static void Beep(StripState state)
    {
        switch (state)
        {
            case StripState.Ok: SystemSounds.Asterisk.Play(); break;
            case StripState.Confirm: SystemSounds.Exclamation.Play(); break;
            case StripState.Block: SystemSounds.Hand.Play(); break;
        }
    }

    public void Dispose()
    {
        Stop();
        _bar.Dispose();
    }
}

// ==================== EvidenceBarForm ====================

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

// ==================== ClipboardWatcher ====================

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
