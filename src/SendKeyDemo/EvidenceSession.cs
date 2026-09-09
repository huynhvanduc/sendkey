using System.Media;
using EnvDTE;

namespace SendKeyDemo;

/// <summary>
/// Một đợt chụp bằng chứng: giữ worklist, bám sự kiện VS dừng để tự chấm, và
/// gác cổng phím chụp — sai thao tác cơ học thì KHÔNG cho ra ảnh.
/// </summary>
public sealed class EvidenceSession : IDisposable
{
    public delegate (MapRow? Row, int Line, string? Error) ResolveFn(string label, string? cmdVar);

    readonly Func<DTE?> _getDte;
    readonly ResolveFn _resolve;
    readonly Func<MapRow, string> _csPathOf;
    readonly Func<Rectangle?> _getRegion;
    readonly Action<string> _log;
    readonly AppSettings _settings;

    readonly StatusStripForm _strip = new();
    VsAutomation.BreakWatcher? _watcher;      // phải giữ field, xem chú thích trong BreakWatcher
    Worklist? _list;
    LastCapture? _last;

    // Đích đã tra được của test case đang chọn — dùng để đối chiếu với chỗ VS thật sự dừng.
    string _targetFile = "";
    int _targetLine;
    string _watchExpr = "";

    public EvidenceSession(
        Func<DTE?> getDte,
        ResolveFn resolve,
        Func<MapRow, string> csPathOf,
        Func<Rectangle?> getRegion,
        Action<string> log,
        AppSettings settings)
    {
        _getDte = getDte;
        _resolve = resolve;
        _csPathOf = csPathOf;
        _getRegion = getRegion;
        _log = log;
        _settings = settings;
    }

    public bool Active => _list != null;
    public Worklist? List => _list;
    public StatusStripForm Strip => _strip;

    string Progress => _list == null ? "" : $"{_list.DoneCount}/{_list.Total}";

    // ---------------- vòng đời ----------------

    public void Start(Worklist list)
    {
        Stop(keepStrip: true);
        _list = list;

        _strip.PlaceAt(_settings.StripX, _settings.StripY);
        _strip.Show();

        AttachWatcher();
        ResolveCurrent(announce: true);
        _log($"Chụp bằng chứng: bắt đầu {list.Total} test case.");
    }

    public void Stop() => Stop(keepStrip: false);

    void Stop(bool keepStrip)
    {
        _watcher?.Dispose();
        _watcher = null;

        if (_list != null)
        {
            _settings.StripX = _strip.Location.X;
            _settings.StripY = _strip.Location.Y;
            _settings.Worklist = _list.ToLines();
            _settings.WorklistIndex = _list.Index;
            _settings.WorklistDone = _list.DoneIds();
            _settings.Save();
        }

        _list = null;
        _last = null;
        if (!keepStrip) _strip.Hide();
    }

    /// <summary>Gắn lại sự kiện break khi đổi instance VS (hoặc VS vừa mở lại).</summary>
    public void AttachWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (_getDte() is not { } dte) return;

        try { _watcher = new VsAutomation.BreakWatcher(dte, OnEnterBreak); }
        catch (Exception ex) { _log("Không bám được sự kiện dừng của VS: " + ex.Message); }
    }

    // Sự kiện tới từ COM — đẩy về UI thread trước khi đụng form.
    void OnEnterBreak()
    {
        if (!_strip.IsHandleCreated) return;
        try { _strip.BeginInvoke(new Action(() => Recheck(fromEvent: true))); }
        catch (InvalidOperationException) { /* form đang đóng */ }
    }

    // ---------------- thao tác ----------------

    /// <summary>Tra test case đang chọn → xóa breakpoint cũ trong file, đặt đúng 1 cái, copy biểu thức Watch.</summary>
    public void GotoCurrent()
    {
        if (_list?.Current is not { } item) { Beep(StripState.Block); return; }
        if (!ResolveCurrent(announce: true)) return;

        if (_getDte() is not { } dte)
        {
            SetStrip(StripState.Block, "Chưa chọn instance VS — bấm Refresh trong cửa sổ cấu hình.");
            Beep(StripState.Block);
            return;
        }

        try
        {
            _log("Chụp: " + VsAutomation.SetOnlyBreakpoint(dte, _targetFile, _targetLine));
            _log("Chụp: " + VsAutomation.GoToLine(dte, _targetFile, _targetLine));
            if (_watchExpr.Length > 0)
            {
                try { Clipboard.SetText(_watchExpr); } catch { /* clipboard bận */ }
            }
        }
        catch (Exception ex)
        {
            SetStrip(StripState.Block, "Lỗi thao tác VS: " + ex.Message);
            Beep(StripState.Block);
            return;
        }

        var hint = _watchExpr.Length > 0
            ? $"F5 → dừng → Ctrl+V vào Watch"
            : "F5 → để chương trình dừng lại";
        SetStrip(StripState.Pending,
            $"▶ {item.TcId} · {item.Describe()} · {Path.GetFileName(_targetFile)}:{_targetLine} · {hint}");
    }

    /// <summary>Chấm lại trạng thái hiện tại (tự gọi khi VS dừng, hoặc gọi tay).</summary>
    public CheckResult? Recheck(bool fromEvent = false)
    {
        if (_list?.Current is not { } item) return null;
        if (_targetLine == 0 && !ResolveCurrent(announce: false)) return null;

        if (_getDte() is not { } dte)
        {
            SetStrip(StripState.Block, "Chưa chọn instance VS.");
            return new CheckResult(CheckLevel.Block, "Chưa chọn instance VS.");
        }

        var snap = VsAutomation.ReadDebugState(dte, _targetFile, _watchExpr);
        var result = CaptureCheck.Evaluate(snap, item, _targetFile, _targetLine, _watchExpr, _last);

        // Chưa break mode mà tự chấm (do sự kiện) thì im lặng — chưa tới lúc.
        if (fromEvent && !snap.InBreakMode) return result;

        SetStrip(LevelToState(result.Level), Prefix(result.Level) + result.Message);
        if (fromEvent) Beep(LevelToState(result.Level));
        return result;
    }

    /// <summary>Phím chụp bằng chứng: chấm trước, đỏ thì không ra ảnh.</summary>
    public void CaptureCurrent()
    {
        if (_list?.Current is not { } item)
        {
            SetStrip(StripState.Block, "Worklist trống — mở cửa sổ cấu hình để dán danh sách.");
            Beep(StripState.Block);
            return;
        }

        if (_getRegion() is not { } region)
        {
            SetStrip(StripState.Block, $"Chưa khoanh vùng chụp — bấm {_settings.DefineRegionHotkey} một lần.");
            Beep(StripState.Block);
            return;
        }

        var result = Recheck();
        if (result == null) return;

        if (result.Level == CheckLevel.Block)
        {
            Beep(StripState.Block);
            _log("Chụp BỊ CHẶN: " + result.Message);
            return;
        }

        if (result.Level == CheckLevel.Confirm)
        {
            Beep(StripState.Confirm);
            var answer = MessageBox.Show(_strip, result.Message, $"Xác nhận chụp {item.TcId}",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                SetStrip(StripState.Confirm, "Đã hủy chụp — " + result.Message);
                _log("Chụp: người dùng hủy — " + result.Message);
                return;
            }
        }

        // Không lưu PNG: ảnh vào thẳng clipboard để dán vào tài liệu bằng chứng.
        var shot = ScreenCapture.Grab(region, _settings, saveFile: false);
        if (!shot.Ok)
        {
            SetStrip(StripState.Block, shot.Message);
            Beep(StripState.Block);
            _log("Chụp: " + shot.Message);
            return;
        }

        // Ghi lại giá trị vừa chụp để lần sau phát hiện "y hệt lần trước, chưa reset".
        if (_getDte() is { } dteNow)
        {
            var snapNow = VsAutomation.ReadDebugState(dteNow, _targetFile, _watchExpr);
            _last = new LastCapture(snapNow.ProcessId, item.TcId, _watchExpr, snapNow.ExprValue);
        }

        _list.MarkDone(item.TcId);
        _log($"Chụp XONG {item.TcId} → clipboard (Ctrl+V để dán). {Progress}");
        Beep(StripState.Ok);

        if (_list.MoveNextPending())
        {
            ResolveCurrent(announce: true);
            var next = _list.Current!;
            SetStrip(StripState.Pending,
                $"✓ {item.TcId} đã chụp — Ctrl+V để dán.   ▶ tiếp: {next.TcId} · {next.Describe()}");
        }
        else
        {
            SetStrip(StripState.Ok, $"✓ {item.TcId} đã chụp — HẾT danh sách. Ctrl+V để dán ảnh cuối.");
        }

        _settings.WorklistIndex = _list.Index;
        _settings.WorklistDone = _list.DoneIds();
        _settings.Save();
    }

    public void MoveTo(int index)
    {
        if (_list == null) return;
        _list.MoveTo(index);
        ResolveCurrent(announce: true);
    }

    // ---------------- trợ giúp ----------------

    /// <summary>Tra test case đang chọn ra (file, dòng, biểu thức watch). false = tra hỏng.</summary>
    bool ResolveCurrent(bool announce)
    {
        _targetFile = ""; _targetLine = 0; _watchExpr = "";
        if (_list?.Current is not { } item) return false;

        var (row, line, error) = _resolve(item.CmdLabel, item.CmdVar.Length == 0 ? null : item.CmdVar);
        if (error != null || row == null)
        {
            SetStrip(StripState.Block, $"✘ {item.TcId} · {item.Describe()} — {error}");
            _log($"Chụp: {item.TcId} tra hỏng — {error}");
            return false;
        }

        _targetFile = _csPathOf(row);
        _targetLine = line;
        _watchExpr = row.CsharpVar;

        // Cảnh báo lỗ hổng âm thầm: breakpoint ở dòng không hề nhắc tới biến -> giá trị có thể chưa được gán.
        if (_watchExpr.Length > 0 && !Mapping.IsExpression(_watchExpr) && LineMentions(_targetFile, _targetLine, _watchExpr) == false)
            _log($"Chụp: ⚠ {item.TcId} — dòng {Path.GetFileName(_targetFile)}:{_targetLine} không nhắc tới \"{_watchExpr}\"; " +
                 "giá trị có thể chưa được gán ở đây.");

        if (announce)
            SetStrip(StripState.Pending,
                $"▶ {item.TcId} · {item.Describe()} · {Path.GetFileName(_targetFile)}:{_targetLine}");
        return true;
    }

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

    void SetStrip(StripState state, string text) => _strip.SetState(state, text, Progress);

    // Phản hồi bằng âm thanh: mắt dev đang ở VS, không ở thanh trạng thái.
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
        _strip.Dispose();
    }
}
