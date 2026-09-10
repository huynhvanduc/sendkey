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
/// Thanh nổi 1 dòng, luôn trên cùng trong lúc chụp bằng chứng:
///   ●  CHECK_INPUT 「%RC%」「%TAX%」  →  rc, tax · Program.cs:42 · 1/2
/// Màu chấm (và viền thanh): xám = chờ copy · xanh dương = chờ F5 · xanh lá = chụp được · vàng = ⚠ · đỏ = ❌.
/// Dòng 2 chỉ hiện khi vàng/đỏ, đúng 1 câu lý do. Chưa có mapping thì ô nhập C# hiện ngay sau mũi tên
/// (ca goto chỉ 1 ô csharpLabel); Enter = <see cref="InputSubmitted"/>.
/// Không cướp focus khi hiện/cập nhật — chỉ lấy focus lúc cần gõ (<see cref="AskInput"/>).
/// Kéo thanh ở bất kỳ chỗ nào trừ ô nhập; double-click = mở cửa sổ cấu hình.
/// </summary>
public sealed class EvidenceBarForm : Form
{
    const int BarWidth = 640;   // đơn vị 96-dpi, quy đổi theo màn hình lúc hiện

    static readonly Color InputBack = Color.FromArgb(58, 44, 20);

    readonly Label _dot = new() { Text = "●", AutoSize = true, Margin = new Padding(0, 1, 6, 0) };
    readonly Label _cmd = new() { AutoSize = true, Margin = new Padding(0, 4, 6, 0) };
    readonly Label _arrow = new() { Text = "→", AutoSize = true, Margin = new Padding(0, 4, 6, 0) };
    readonly Label _target = new() { AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
    readonly TextBox _csLabel = new() { Visible = false, Margin = new Padding(0, 2, 6, 0) };
    readonly TextBox _csVar = new() { Visible = false, Margin = new Padding(0, 2, 0, 0) };
    readonly Label _reason = new() { AutoSize = true, Visible = false, Margin = new Padding(22, 3, 0, 1) };

    StripState _state = StripState.Idle;
    bool _labelOnly;

    /// <summary>Enter trong ô nhập: (csharpLabel, csharpVar); ca goto thì csharpVar = "".</summary>
    public event Action<string, string>? InputSubmitted;
    public event Action? RunRequested;
    public event Action? OpenConfigRequested;

    protected override bool ShowWithoutActivation => true;

    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    public EvidenceBarForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(2);                      // chừa viền màu trạng thái
        BackColor = Theme.PanelBackground;
        DoubleBuffered = true;

        var text = new Font("Segoe UI", 10f);
        _dot.Font = new Font("Segoe UI", 12f);
        _cmd.Font = _arrow.Font = _target.Font = _reason.Font = text;
        _cmd.ForeColor = _arrow.ForeColor = Theme.TextSecondary;
        _target.ForeColor = _reason.ForeColor = Theme.TextPrimary;

        foreach (var tb in new[] { _csLabel, _csVar })
        {
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.BackColor = InputBack;
            tb.ForeColor = Theme.TextPrimary;
            tb.Font = new Font("Consolas", 10f);
            tb.KeyDown += OnFieldKeyDown;
        }

        var line1 = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            Margin = new Padding(0), BackColor = Theme.PanelBackground,
        };
        line1.Controls.AddRange(new Control[] { _dot, _cmd, _arrow, _target, _csLabel, _csVar });

        var body = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill,
            Padding = new Padding(10, 4, 12, 4), BackColor = Theme.PanelBackground,
        };
        body.Controls.Add(line1, 0, 0);
        body.Controls.Add(_reason, 0, 1);
        Controls.Add(body);

        foreach (var c in new Control[] { this, body, line1, _dot, _cmd, _arrow, _target, _reason })
            c.MouseDown += DragOrOpenConfig;

        SetPair("", Array.Empty<string>(), "");
        SetStatus(StripState.Idle, null);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Đặt cỡ lúc hiện (đã biết DPI của màn hình), không dùng px cứng trong constructor.
        int w = LogicalToDeviceUnits(BarWidth);
        MinimumSize = new Size(w, 0);
        MaximumSize = new Size(w, 0);
        _csLabel.Width = LogicalToDeviceUnits(180);
        _csVar.Width = LogicalToDeviceUnits(220);
        _reason.MaximumSize = new Size(w - LogicalToDeviceUnits(50), 0);
    }

    // ---------------- API cho EvidenceSession ----------------

    /// <summary>Cái vừa copy + đích đã tra (vd "rc, tax · Program.cs:42 · 1/2"). Gọi hàm này cũng ẩn ô nhập.</summary>
    public void SetPair(string cmdLabel, IReadOnlyList<string> cmdItems, string target)
    {
        var cmd = $"{cmdLabel} {string.Concat(cmdItems.Select(i => $"「{i}」"))}".Trim();
        _cmd.Text = cmd.Length > 0 ? Clip(cmd, 60) : "Copy label trong Excel để bắt đầu";
        _target.Text = Clip(target, 70);
        _labelOnly = false;
        _csLabel.Text = _csVar.Text = "";
        SetInputVisible(false);
    }

    /// <summary>
    /// Hiện ô nhập C# ngay sau mũi tên (labelOnly = ca goto: chỉ 1 ô csharpLabel), lấy focus vào ô trống đầu tiên.
    /// <paramref name="forItem"/> là 「」 đang thiếu mapping, hiện làm gợi ý trong ô.
    /// </summary>
    public void AskInput(bool labelOnly, string forItem, string csLabel, string csVar)
    {
        FillInput(labelOnly, forItem, csLabel, csVar);
        SetInputVisible(true);

        Activate();
        var box = !labelOnly && _csLabel.Text.Trim().Length > 0 ? _csVar : _csLabel;
        box.Focus();
        box.SelectionStart = box.TextLength;
    }

    /// <summary>reason chỉ hiện ở dòng 2 khi state là Confirm/Block; null/rỗng = không hiện dòng 2.</summary>
    public void SetStatus(StripState state, string? reason)
    {
        _state = state;
        _dot.ForeColor = DotColor;
        bool show = (state is StripState.Confirm or StripState.Block) && !string.IsNullOrWhiteSpace(reason);
        _reason.Text = show ? (state == StripState.Block ? "❌  " : "⚠  ") + reason : "";
        _reason.Visible = show;
        Invalidate();   // viền đổi màu theo chấm
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

    // ---------------- bên trong ----------------

    void FillInput(bool labelOnly, string forItem, string csLabel, string csVar)
    {
        _labelOnly = labelOnly;
        _csLabel.Text = csLabel;
        _csVar.Text = labelOnly ? "" : csVar;
        _csLabel.PlaceholderText = labelOnly && forItem.Length > 0 ? $"csharpLabel cho 「{forItem}」" : "csharpLabel";
        _csVar.PlaceholderText = forItem.Length > 0 ? $"csharpVar cho 「{forItem}」" : "csharpVar";
    }

    void SetInputVisible(bool visible)
    {
        _csLabel.Visible = visible;
        _csVar.Visible = visible && !_labelOnly;
        _target.Visible = !visible && _target.Text.Length > 0;
        _arrow.Visible = visible || _target.Text.Length > 0;
    }

    void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is not (Keys.Enter or Keys.Return)) return;
        e.SuppressKeyPress = true;
        if (InputSubmitted != null) InputSubmitted(_csLabel.Text.Trim(), _labelOnly ? "" : _csVar.Text.Trim());
        else RunRequested?.Invoke();   // TẠM: EvidenceSession hiện tại vẫn nghe RunRequested + đọc CsLabel/CsVar
    }

    // Kéo thanh ở bất kỳ chỗ nào (trừ ô nhập); double-click = mở cửa sổ cấu hình.
    void DragOrOpenConfig(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (e.Clicks >= 2) { OpenConfigRequested?.Invoke(); return; }
        const int WM_NCLBUTTONDOWN = 0xA1, HTCAPTION = 2;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    Color DotColor => _state switch
    {
        StripState.Ok => Color.FromArgb(74, 222, 128),
        StripState.Confirm => Color.FromArgb(250, 204, 21),
        StripState.Block => Color.FromArgb(248, 113, 113),
        StripState.Pending => Theme.AccentStart,
        _ => Color.FromArgb(148, 152, 164),
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(DotColor, 2f);
        e.Graphics.DrawRectangle(pen, 1, 1, ClientSize.Width - 2, ClientSize.Height - 2);
    }

    // ---------------- TẠM: API cũ mà EvidenceSession hiện tại còn gọi ----------------
    // sendkey-4d chuyển EvidenceSession sang SetPair / AskInput / SetStatus(state, reason) xong thì xoá cả khối này.

    string _oldLabel = "", _oldVar = "";

    public string CsLabel => _csLabel.Text.Trim();
    public string CsVar => _csVar.Text.Trim();

    public void SetCmd(string cmdLabel, string? cmdVar)
    {
        _oldLabel = cmdLabel;
        if (cmdVar != null) _oldVar = cmdVar;
        SetPair(_oldLabel, _oldVar.Length > 0 ? new[] { _oldVar } : Array.Empty<string>(), "");
    }

    public void SetCs(string csLabel, string csVar)
    {
        FillInput(false, _oldVar, csLabel, csVar);
        if (csLabel.Length > 0 && csVar.Length > 0)
        {
            _target.Text = Clip($"{csLabel} / {csVar}", 70);
            SetInputVisible(false);            // giữ chữ trong ô (ẩn) vì Run() cũ đọc CsLabel/CsVar
        }
        else if (_oldLabel.Length > 0)
            SetInputVisible(true);
    }

    public void FocusFirstEmptyCs()
    {
        if (!_csLabel.Visible) return;
        var box = _csLabel.Text.Trim().Length == 0 ? _csLabel
                : _csVar.Visible && _csVar.Text.Trim().Length == 0 ? _csVar
                : null;
        if (box == null) return;
        Activate();
        box.Focus();
        box.SelectionStart = box.TextLength;
    }

    public void SetStatus(StripState state, string text, string progress)
    {
        SetStatus(state, text);
        if (state is StripState.Confirm or StripState.Block) return;
        // Trạng thái bình thường: hiện câu hướng dẫn cũ ở chỗ đích để bản chuyển tiếp vẫn đọc được.
        _target.Text = Clip(progress.Length > 0 ? $"{text} · {progress}" : text, 90);
        SetInputVisible(_csLabel.Visible);
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
