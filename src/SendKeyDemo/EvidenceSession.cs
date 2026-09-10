using System.Media;

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
        var shot = ScreenCapture.Grab(region, _settings, saveFile: false);
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
