using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Media;
using System.Runtime.InteropServices;

namespace SendKeyDemo;

/// <summary>
/// Một đợt chụp bằng chứng. Nghe clipboard để gom label + các 「」 copy từ file test case, tra mapping.csv
/// (chưa có thì hỏi C# ngay trên thanh rồi ghi thêm), đặt breakpoint cho từng dòng cần chụp, tự điền Watch
/// khi VS dừng, và gác cổng phím chụp — sai thao tác cơ học thì KHÔNG cho ra ảnh.
/// Mỗi dòng dừng = 1 ảnh; Watch mỗi lần chỉ gồm biến của dòng đó.
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

    // Nhóm đang chụp: label + nội dung các 「」 đã copy, theo thứ tự.
    string _cmdLabel = "";
    readonly List<string> _items = new();
    string? _askingItem;                     // 「」 đang chờ gõ C# trên thanh

    // Sau G: các dòng cần chụp (đã đặt breakpoint), dòng nào chụp xong, dòng VS đang dừng.
    List<StopPoint> _stops = new();
    readonly HashSet<int> _done = new();
    int _currentStop = -1;
    List<string> _bpFiles = new();
    string? _confirmed;                      // lý do ⚠ đã báo — bấm chụp lần nữa mới ra ảnh
    IntPtr _sourceWindow;                    // cửa sổ lúc copy (Excel) — chụp xong đưa lên lại

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

    public EvidenceSession(MainForm host, AppSettings settings)
    {
        _host = host;
        _settings = settings;
        _bar.InputSubmitted += OnInputSubmitted;
    }

    public bool Active => _active;
    public EvidenceBarForm Bar => _bar;

    /// <summary>Tên nhóm đang chụp, chỉ để ghi vào thông báo — vd "CHECK_INPUT「%RC%」「%TAX%」".</summary>
    string TcId => _cmdLabel + string.Concat(_items.Select(i => $"「{i}」"));

    // ---------------- vòng đời ----------------

    /// <summary>Bắt đầu đợt: hiện thanh, nghe clipboard, bám sự kiện VS dừng.</summary>
    public void Start()
    {
        Stop(keepBar: true);
        _active = true;

        _bar.PlaceAt(_settings.StripX, _settings.StripY);
        _bar.Show();

        AttachWatcher();

        _clip = new ClipboardWatcher();
        _clip.TextCopied += OnCopied;
        _cmdLabel = "";
        _items.Clear();
        ResetStops();
        _bar.SetPair("", _items, "");
        _bar.SetStatus(StripState.Idle, null);
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

        // Quy tắc duy nhất: trong 「 」 là biến/mệnh đề (1 ô có thể nhiều cặp), ngoài ngoặc là label.
        var pieces = CopiedText.ClassifyAll(raw);
        if (pieces.Count == 0) return;
        _sourceWindow = GetForegroundWindow();

        // Label mới, hoặc nhóm trước đã chụp đủ → bắt đầu nhóm mới.
        if (!pieces[0].IsVar) { _cmdLabel = pieces[0].Value; _items.Clear(); }
        else if (_stops.Count > 0 && _done.Count == _stops.Count) _items.Clear();

        foreach (var p in pieces.Where(p => p.IsVar))
            if (!_items.Contains(p.Value, StringComparer.OrdinalIgnoreCase)) _items.Add(p.Value);

        ResetStops();
        Resolve(ask: false);   // chỉ báo; không cướp focus khỏi Excel lúc dev còn đang copy
    }

    /// <summary>
    /// Tra mọi 「」 trong nhóm ra các dòng cần chụp. Có cái chưa có trong mapping.csv thì báo vàng — và nếu
    /// <paramref name="ask"/> thì mở ô gõ C# ngay trên thanh. Lỗi khác báo đỏ. Không tra đủ → null.
    /// </summary>
    List<StopPoint>? Resolve(bool ask)
    {
        _bar.SetPair(_cmdLabel, _items, "");
        if (_items.Count == 0) { _bar.SetStatus(StripState.Idle, null); return null; }

        var rows = _host.GetMapRows();
        if (rows == null) return Fail("Chưa nạp được mapping.csv — kiểm tra đường dẫn ở cửa sổ cấu hình.");

        var targets = new List<StopTarget>();
        foreach (var item in _items)
        {
            // 「goto :X」 → dừng ở dòng đầu label X, không cần Watch; còn lại tra theo label vừa copy.
            var gotoLabel = CopiedText.GotoTarget(item);
            var label = gotoLabel ?? _cmdLabel;
            if (label.Length == 0) return Fail($"Chưa copy label cho 「{item}」.");

            var res = Mapping.Resolve(rows, label, gotoLabel == null ? item : null);
            if (res.Kind == LookupKind.Duplicate)
                return Fail($"mapping.csv trùng ở dòng {string.Join(", ", res.DuplicateLines!)} — sửa file rồi thử lại.");
            if (res.Row is not { } row)
            {
                var known = rows.FirstOrDefault(r => Mapping.NormalizeLabel(r.CmdLabel) == Mapping.NormalizeLabel(label));
                if (ask)
                {
                    _askingItem = item;
                    _bar.AskInput(gotoLabel != null, item, known?.CsharpLabel ?? "", "");
                }
                _bar.SetStatus(StripState.Confirm, ask
                    ? $"「{item}」 chưa có trong mapping.csv — gõ C# rồi Enter."
                    : $"「{item}」 chưa có trong mapping.csv — bấm {_settings.GotoCurrentHotkey} để gõ C#.");
                return null;
            }

            var csp = _host.EffectiveCsPath(row);
            if (string.IsNullOrWhiteSpace(csp) || !File.Exists(csp)) return Fail($"Không thấy file .cs: {csp}");

            var watch = gotoLabel == null ? row.CsharpVar : "";
            var ll = Mapping.FindLabelLine(csp, row.CsharpLabel, watch);
            if (ll.Kind != LabelLineKind.Ok)
                return Fail(ll.Kind switch
                {
                    LabelLineKind.NotFound => $"Không thấy \"{row.CsharpLabel}:\" trong {Path.GetFileName(csp)}.",
                    LabelLineKind.Multiple => $"\"{row.CsharpLabel}:\" xuất hiện ở dòng {string.Join(", ", ll.MatchLines!)}.",
                    LabelLineKind.NoExecutableLine => $"Sau \"{row.CsharpLabel}:\" không còn dòng thực thi.",
                    _ => $"Không thấy biểu thức \"{watch}\" sau \"{row.CsharpLabel}:\".",
                });
            targets.Add(new StopTarget(csp, ll.Line, ll.LabelLine, watch, item));

            // Mệnh đề if: ảnh 1 ở dòng if (giá trị thật) → chụp xong app SET cho mệnh đề ĐÚNG → ảnh 2 ở lệnh đầu nhánh.
            if (gotoLabel == null && IfClause.IsIf(item) && Mapping.IsExpression(watch))
            {
                var bypass = IfClause.Suggest(item);
                targets[^1] = targets[^1] with
                {
                    SetStatement = bypass == null ? "" : IfClause.Statement(_settings.IfSetStatement, bypass),
                    Condition = watch,
                };
                if (Mapping.BranchStart(File.ReadAllLines(csp), ll.Line) is { } branch)
                    targets.Add(new StopTarget(csp, branch.Line, ll.LabelLine, watch, item, branch.Column));
                else
                    _host.Log($"Chụp: không tìm được lệnh đầu nhánh của {Path.GetFileName(csp)}:{ll.Line} — chỉ chụp dòng if.");
            }
        }

        var stops = Mapping.GroupStops(targets);
        _bar.SetPair(_cmdLabel, _items, TargetText(stops, 0));
        _bar.SetStatus(StripState.Pending, null);
        return stops;
    }

    /// <summary>Enter trong ô C# trên thanh: kiểm với code, ghi thêm dòng vào mapping.csv rồi chạy luôn như G.</summary>
    void OnInputSubmitted(string csLabel, string csVar)
    {
        if (!_active || _askingItem is not { } item) return;

        var gotoLabel = CopiedText.GotoTarget(item);
        var err = gotoLabel != null
            ? _host.AddMapping(gotoLabel, "", csLabel, "")
            : _host.AddMapping(_cmdLabel, item, csLabel, csVar);
        if (err != null)
        {
            _bar.SetStatus(StripState.Block, err);   // ô nhập vẫn mở để sửa rồi Enter lại
            Beep(StripState.Block);
            return;
        }

        _askingItem = null;
        Run();
    }

    // ---------------- G: đặt breakpoint ----------------

    /// <summary>Hotkey G, hoặc Enter sau khi gõ C#: đặt breakpoint cho mọi dòng cần chụp, mở đúng tab, đưa VS lên.</summary>
    public void Run()
    {
        if (!_active) return;
        if (_items.Count == 0) { Block("Chưa copy 「…」 từ file test case."); return; }
        if (Resolve(ask: true) is not { } stops) { Beep(StripState.Block); return; }
        if (_host.CurrentDte() is not { } dte) { Block("Chưa chọn instance VS — mở cửa sổ cấu hình bấm Refresh."); return; }

        try
        {
            // Xoá breakpoint cũ ở mọi file đã đụng tới rồi đặt đúng 1 breakpoint cho mỗi dòng cần chụp.
            foreach (var f in _bpFiles.Concat(stops.Select(s => s.File)).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
                VsAutomation.ClearBreakpointsInFile(dte, f);
            foreach (var s in stops)
                _host.Log("Chụp: " + VsAutomation.EnsureBreakpoint(dte, s.File, s.Line, s.Column));
            VsAutomation.ShowLabel(dte, stops[0].File, stops[0].LabelLine, stops[0].Line);
            VsAutomation.BringToFront(dte);
        }
        catch (Exception ex) { Block("Lỗi thao tác VS: " + ex.Message); return; }

        ResetStops();
        _stops = stops;
        _bpFiles = stops.Select(s => s.File).ToList();

        // Định danh thuần thì breakpoint rơi vào dòng đầu sau label — giá trị có thể chưa được gán ở đó.
        foreach (var s in stops)
            foreach (var w in s.Watch.Where(w => !Mapping.IsExpression(w) && LineMentions(s.File, s.Line, w) == false))
                _host.Log($"Chụp: ⚠ dòng {Path.GetFileName(s.File)}:{s.Line} không nhắc tới \"{w}\" — " +
                          "giá trị có thể chưa được gán ở đây.");

        _bar.SetPair(_cmdLabel, _items, TargetText(stops, 0));
        _bar.SetStatus(StripState.Pending, null);
    }

    // ---------------- chấm + chụp ----------------

    /// <summary>
    /// Chấm trạng thái VS. Lúc VS vừa dừng đúng 1 dòng cần chụp (<paramref name="fromEvent"/>) hoặc lúc bấm
    /// chụp (<paramref name="refresh"/>) thì đặt lại Watch = đúng biến của dòng đó — không dư biến lần trước,
    /// không thiếu, và VS phải tính lại giá trị — rồi cuộn cho label hiện ra.
    /// </summary>
    public CheckResult? Recheck(bool fromEvent = false, bool refresh = false)
    {
        if (!_active || _stops.Count == 0) return null;

        if (_host.CurrentDte() is not { } dte)
        {
            _bar.SetStatus(StripState.Block, "Chưa chọn instance VS.");
            return new CheckResult(CheckLevel.Block, "Chưa chọn instance VS.");
        }

        var hit = VsAutomation.ReadDebugState(dte, "", Array.Empty<string>());
        if (fromEvent && !hit.InBreakMode) return null;   // chưa tới lúc, im lặng

        // Đang dừng ở dòng nào trong nhóm; không khớp dòng nào thì so với dòng chưa chụp đầu tiên để báo sai dòng.
        // if + goto cùng dòng: stop khớp đúng cột breakpoint vừa dừng được ưu tiên, rồi mới tới stop theo dòng (cột 0).
        var open = Enumerable.Range(0, _stops.Count).Where(i => !_done.Contains(i) && IsAt(_stops[i], hit)).ToList();
        _currentStop = open.FirstOrDefault(i => _stops[i].Column > 0 && _stops[i].Column == hit.HitColumn,
                           open.FirstOrDefault(i => _stops[i].Column == 0, -1));
        var stop = _stops[_currentStop >= 0 ? _currentStop : NextStop()];

        string note = "";
        bool labelShown = true;
        if (_currentStop >= 0 && (fromEvent || refresh))
        {
            try
            {
                var missed = VsAutomation.SetWatch(dte, stop.File, stop.LabelLine, stop.Watch);
                if (missed is not { Count: 0 })
                {
                    var manual = missed ?? stop.Watch.ToList();
                    CopyForManualWatch(manual);
                    if (manual.Count > 0) note = $" Đã copy {string.Join(", ", manual)} — Ctrl+V vào Watch.";
                }
                labelShown = VsAutomation.ShowLabel(dte, stop.File, stop.LabelLine, stop.Line);
            }
            catch (Exception ex) { note = " Lỗi điền Watch: " + ex.Message; }
        }

        var snap = VsAutomation.ReadDebugState(dte, stop.File, stop.Watch);
        var result = CaptureCheck.Evaluate(snap, TcId, stop.File, stop.Line, string.Join("; ", stop.Watch), _last);

        // Watch phải có đúng biến của dòng này — lý do bị review trả ảnh nhiều nhất.
        if (result.Level != CheckLevel.Block && VsAutomation.ReadWatchNames(dte) is { } names &&
            CaptureCheck.WatchMismatch(names, stop.Watch) is { } mismatch)
            result = new CheckResult(CheckLevel.Block, mismatch + note);
        else if (result.Level == CheckLevel.Ok && !labelShown)
            result = new CheckResult(CheckLevel.Confirm,
                $"Không thấy dòng label (dòng {stop.LabelLine}) trong editor — nới cửa sổ code để ảnh thấy label.");

        _bar.SetPair(_cmdLabel, _items, TargetText(_stops, _stops.IndexOf(stop)));
        _bar.SetStatus(LevelToState(result.Level), result.Level == CheckLevel.Ok ? null : result.Message);
        if (fromEvent) Beep(LevelToState(result.Level));
        return result;
    }

    /// <summary>Phím chụp: làm mới Watch rồi chấm; đỏ thì không ra ảnh, vàng thì bấm lần nữa mới chụp.</summary>
    public void CaptureCurrent()
    {
        if (!_active) return;

        if (_stops.Count == 0) { Block($"Chưa đặt breakpoint — bấm {_settings.GotoCurrentHotkey} trước."); return; }
        if (_host.SavedRegion is not { } region) { Block($"Chưa khoanh vùng chụp — bấm {_settings.DefineRegionHotkey} một lần."); return; }

        // Chụp xong lần trước app đưa Excel lên, có thể đang che VS → đưa VS lên trước khi điền Watch và chụp vùng.
        if (_host.CurrentDte() is { } front) VsAutomation.BringToFront(front);

        var result = Recheck(refresh: true);
        if (result == null) return;

        if (result.Level == CheckLevel.Block || _currentStop < 0)
        {
            Beep(StripState.Block);
            _host.Log("Chụp BỊ CHẶN: " + result.Message);
            return;
        }

        if (result.Level == CheckLevel.Confirm && _confirmed != result.Message)
        {
            _confirmed = result.Message;
            _bar.SetStatus(StripState.Confirm, $"{result.Message} Bấm {_settings.CaptureRegionHotkey} lần nữa để vẫn chụp.");
            Beep(StripState.Confirm);
            return;
        }
        _confirmed = null;

        // Không lưu PNG: ảnh vào thẳng clipboard để dán vào tài liệu bằng chứng.
        // Ẩn thanh nổi + dời chuột ra ngoài vùng trước khi chụp để ảnh không dính thanh / tooltip.
        var shot = ScreenCapture.GrabClean(region, _settings, _bar);
        if (!shot.Ok)
        {
            Block(shot.Message);
            _host.Log("Chụp: " + shot.Message);
            return;
        }

        var stop = _stops[_currentStop];
        if (_host.CurrentDte() is { } dteNow)
        {
            var snapNow = VsAutomation.ReadDebugState(dteNow, stop.File, stop.Watch);
            _last = new LastCapture(snapNow.ProcessId, TcId, string.Join("; ", stop.Watch), snapNow.ExprValue);
        }
        _done.Add(_currentStop);
        Beep(StripState.Ok);
        _host.Log($"Chụp XONG {TcId} · {Path.GetFileName(stop.File)}:{stop.Line} → clipboard (Ctrl+V để dán).");

        // Mệnh đề if: đã chụp giá trị thật → ép mệnh đề ĐÚNG để F5 đi vào nhánh (ảnh sau ở lệnh đầu nhánh).
        // Làm trước khi đưa Excel lên; app KHÔNG tự chạy tiếp.
        string? bypassNote = null;
        var bypassState = StripState.Pending;
        if (stop.Condition.Length > 0)
        {
            if (stop.SetStatement.Length == 0)
                (bypassNote, bypassState) = ($"「{stop.Items[0]}」: tự set giá trị cho mệnh đề này rồi F5.", StripState.Confirm);
            else if (_host.CurrentDte() is not { } dteSet)
                (bypassNote, bypassState) = ($"Mất kết nối VS — chưa chạy được {stop.SetStatement}.", StripState.Block);
            else if (VsAutomation.RunIfBypass(dteSet, stop.File, stop.LabelLine, stop.SetStatement, stop.Condition) is { } err)
                (bypassNote, bypassState) = (err, StripState.Block);
            else
                _host.Log($"Chụp: đã chạy {stop.SetStatement} — {stop.Condition} = true.");
        }

        bool all = _done.Count == _stops.Count;
        // Chưa đủ nhóm: nhắc dán ảnh này trước (clipboard chỉ giữ 1 ảnh) rồi mới F5 trong VS — app không tự chạy tiếp.
        _bar.SetPair(_cmdLabel, _items, all
            ? $"✓ đủ {_stops.Count} ảnh — copy test case tiếp"
            : $"Ctrl+V rồi F5 trong VS → {TargetText(_stops, NextStop())}");
        _bar.SetStatus(bypassNote != null ? bypassState : all ? StripState.Ok : StripState.Pending, bypassNote);
        if (bypassNote != null) Beep(bypassState);

        // Đưa lại cửa sổ vừa copy (Excel) để Ctrl+V luôn.
        if (_sourceWindow != IntPtr.Zero) SetForegroundWindow(_sourceWindow);
    }

    // ---------------- trợ giúp ----------------

    /// <summary>Phần sau mũi tên trên thanh, vd "rc, tax · Program.cs:42 · 1/2".</summary>
    static string TargetText(IReadOnlyList<StopPoint> stops, int index)
    {
        if (stops.Count == 0) return "";
        int i = Math.Clamp(index, 0, stops.Count - 1);
        var s = stops[i];
        var what = s.Watch.Count > 0 ? string.Join(", ", s.Watch) : "goto";
        var col = s.Column > 0 ? $":{s.Column}" : "";
        return $"{what} · {Path.GetFileName(s.File)}:{s.Line}{col}" + (stops.Count > 1 ? $" · {i + 1}/{stops.Count}" : "");
    }

    void ResetStops()
    {
        _stops = new();
        _done.Clear();
        _currentStop = -1;
        _confirmed = null;
        _askingItem = null;
    }

    int NextStop()
    {
        for (int i = 0; i < _stops.Count; i++)
            if (!_done.Contains(i)) return i;
        return Math.Max(0, _stops.Count - 1);
    }

    static bool IsAt(StopPoint s, DebugSnapshot hit)
        => hit.HitLine == s.Line && hit.HitFile.Length > 0 &&
           string.Equals(Path.GetFullPath(hit.HitFile), Path.GetFullPath(s.File), StringComparison.OrdinalIgnoreCase);

    /// <summary>Không tự điền được Watch: để sẵn biểu thức trong clipboard cho dev dán tay.</summary>
    void CopyForManualWatch(IReadOnlyList<string> exprs)
    {
        if (exprs.Count == 0) return;
        var text = string.Join(Environment.NewLine, exprs);
        if (_clip != null) _clip.IgnoreText = text;   // đừng tự nhận lại biểu thức mình vừa copy
        try { Clipboard.SetText(text); } catch { /* clipboard bận */ }
    }

    List<StopPoint>? Fail(string reason)
    {
        _bar.SetStatus(StripState.Block, reason);
        return null;
    }

    void Block(string reason)
    {
        _bar.SetStatus(StripState.Block, reason);
        Beep(StripState.Block);
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
/// Màu chấm (và viền thanh): xám = chờ copy · xanh dương = chờ chương trình dừng ở breakpoint · xanh lá = chụp được · vàng = ⚠ · đỏ = ❌.
/// Dòng 2 chỉ hiện khi vàng/đỏ, đúng 1 câu lý do. Chưa có mapping thì ô nhập C# hiện ngay sau mũi tên
/// (ca goto chỉ 1 ô csharpLabel); Enter = <see cref="InputSubmitted"/>.
/// Không cướp focus khi hiện/cập nhật — chỉ lấy focus lúc cần gõ C# (<see cref="AskInput"/>); ô sửa giá trị SET
/// của mệnh đề if (<see cref="AskValue"/>) KHÔNG lấy focus.
/// Kéo thanh ở bất kỳ chỗ nào trừ ô nhập; double-click = mở cửa sổ cấu hình.
/// </summary>
public sealed class EvidenceBarForm : Form
{
    const int BarWidth = 640;   // đơn vị 96-dpi, quy đổi theo màn hình lúc hiện

    static readonly Color InputBack = Color.FromArgb(58, 44, 20);

    readonly Label _dot = new() { Text = "●", AutoSize = true, Margin = new Padding(0, 1, 6, 0) };
    readonly Label _cmd = new() { AutoSize = true, Margin = new Padding(0, 4, 6, 0) };
    readonly Label _arrow = new() { Text = "→", AutoSize = true, Margin = new Padding(0, 4, 6, 0) };
    // Không AutoSize: rộng = phần còn trống của dòng (xem Fit), chữ dài thì hiện "…".
    readonly Label _target = new() { AutoSize = false, AutoEllipsis = true, Margin = new Padding(0, 4, 0, 0) };
    readonly TextBox _csLabel = new() { Visible = false, Margin = new Padding(0, 2, 6, 0) };
    readonly TextBox _csVar = new() { Visible = false, Margin = new Padding(0, 2, 0, 0) };
    // Ô sửa giá trị đã SET cho mệnh đề if, vd "RC = [1]" — xem AskValue.
    readonly Label _valueName = new() { AutoSize = true, Visible = false, Margin = new Padding(8, 4, 4, 0) };
    readonly TextBox _value = new() { Visible = false, Margin = new Padding(0, 2, 0, 0) };
    readonly Label _reason = new() { AutoSize = true, Visible = false, Margin = new Padding(22, 3, 0, 1) };

    readonly TableLayoutPanel _body;

    StripState _state = StripState.Idle;
    bool _labelOnly;

    /// <summary>Enter trong ô nhập: (csharpLabel, csharpVar); ca goto thì csharpVar = "".</summary>
    public event Action<string, string>? InputSubmitted;
    /// <summary>Enter trong ô giá trị (<see cref="AskValue"/>): giá trị mới để chạy lại lệnh SET.</summary>
    public event Action<string>? ValueSubmitted;
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
        Padding = new Padding(2);                      // chừa viền màu trạng thái
        BackColor = Theme.PanelBackground;
        DoubleBuffered = true;

        var text = new Font("Segoe UI", 10f);
        _dot.Font = new Font("Segoe UI", 12f);
        _cmd.Font = _arrow.Font = _target.Font = _reason.Font = text;
        _cmd.ForeColor = _arrow.ForeColor = Theme.TextSecondary;
        _target.ForeColor = _reason.ForeColor = Theme.TextPrimary;
        _valueName.Font = text;
        _valueName.ForeColor = Theme.TextSecondary;

        foreach (var tb in new[] { _csLabel, _csVar, _value })
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
        line1.Controls.AddRange(new Control[] { _dot, _cmd, _arrow, _target, _valueName, _value, _csLabel, _csVar });

        _body = new TableLayoutPanel
        {
            ColumnCount = 1, Dock = DockStyle.Fill,
            Padding = new Padding(10, 4, 12, 4), BackColor = Theme.PanelBackground,
        };
        _body.Controls.Add(line1, 0, 0);
        _body.Controls.Add(_reason, 0, 1);
        Controls.Add(_body);

        foreach (var c in new Control[] { this, _body, line1, _dot, _cmd, _arrow, _target, _valueName, _reason })
            c.MouseDown += DragOrOpenConfig;

        SetPair("", Array.Empty<string>(), "");
        SetStatus(StripState.Idle, null);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Đặt cỡ lúc hiện (đã biết DPI của màn hình), không dùng px cứng trong constructor.
        int w = LogicalToDeviceUnits(BarWidth);
        ClientSize = new Size(w, ClientSize.Height);
        _reason.MaximumSize = new Size(w - LogicalToDeviceUnits(50), 0);
        Fit();
    }

    /// <summary>
    /// Chia chiều ngang dòng 1 (ô target / 2 ô nhập ăn phần còn trống) rồi đặt chiều cao theo nội dung —
    /// form không AutoSize vì khung Dock=Fill không báo lên khi dòng 2 bật/tắt.
    /// </summary>
    void Fit()
    {
        if (!IsHandleCreated) return;   // OnLoad sẽ gọi lại
        int inner = ClientSize.Width - Padding.Horizontal - _body.Padding.Horizontal;
        int used = 0;
        foreach (var c in new Control[] { _dot, _cmd, _arrow })
            if (c.Visible) used += c.PreferredSize.Width + c.Margin.Horizontal;
        if (_value.Visible)
        {
            _value.Width = LogicalToDeviceUnits(90);
            used += _valueName.PreferredSize.Width + _valueName.Margin.Horizontal + _value.Width + _value.Margin.Horizontal;
        }
        int room = Math.Max(LogicalToDeviceUnits(80), inner - used);

        _target.Width = room - _target.Margin.Horizontal;
        _target.Height = _target.PreferredHeight;
        if (_labelOnly)
            _csLabel.Width = Math.Min(room - _csLabel.Margin.Horizontal, LogicalToDeviceUnits(260));
        else
            _csLabel.Width = _csVar.Width =
                Math.Min((room - _csLabel.Margin.Horizontal - _csVar.Margin.Horizontal) / 2, LogicalToDeviceUnits(220));

        int h = _body.GetPreferredSize(new Size(ClientSize.Width - Padding.Horizontal, 0)).Height;
        ClientSize = new Size(ClientSize.Width, h + Padding.Vertical);
    }

    // ---------------- API cho EvidenceSession ----------------

    /// <summary>Cái vừa copy + đích đã tra (vd "rc, tax · Program.cs:42 · 1/2"). Gọi hàm này cũng ẩn ô nhập.</summary>
    public void SetPair(string cmdLabel, IReadOnlyList<string> cmdItems, string target)
    {
        var cmd = $"{cmdLabel} {string.Concat(cmdItems.Select(i => $"「{i}」"))}".Trim();
        _cmd.Text = cmd.Length > 0 ? Clip(cmd, 45) : "Copy label trong Excel để bắt đầu";
        _target.Text = Clip(target, 70);
        _labelOnly = false;
        _csLabel.Text = _csVar.Text = "";
        _valueName.Visible = _value.Visible = false;
        SetInputVisible(false);
    }

    /// <summary>
    /// Hiện ô nhập C# ngay sau mũi tên (labelOnly = ca goto: chỉ 1 ô csharpLabel), lấy focus vào ô trống đầu tiên.
    /// <paramref name="forItem"/> là 「」 đang thiếu mapping, hiện làm gợi ý trong ô.
    /// </summary>
    public void AskInput(bool labelOnly, string forItem, string csLabel, string csVar)
    {
        _valueName.Visible = _value.Visible = false;
        FillInput(labelOnly, forItem, csLabel, csVar);
        SetInputVisible(true);

        Activate();
        var box = !labelOnly && _csLabel.Text.Trim().Length > 0 ? _csVar : _csLabel;
        box.Focus();
        box.SelectionStart = box.TextLength;
    }

    /// <summary>
    /// Hiện ô sửa giá trị đã SET cho mệnh đề if, vd "RC = [1]"; Enter = <see cref="ValueSubmitted"/>.
    /// KHÔNG lấy focus: hàm này được gọi ngay sau khi chụp, lúc app vừa đưa Excel lên cho dev Ctrl+V —
    /// lấy focus thì Ctrl+V sẽ dán vào ô này. Muốn sửa giá trị thì click vào ô.
    /// </summary>
    public void AskValue(string varName, string value)
    {
        _valueName.Text = $"{varName} =";
        _value.Text = value;
        _valueName.Visible = _value.Visible = true;
        Fit();
    }

    /// <summary>reason chỉ hiện ở dòng 2 khi state là Confirm/Block; null/rỗng = không hiện dòng 2.</summary>
    public void SetStatus(StripState state, string? reason)
    {
        _state = state;
        _dot.ForeColor = DotColor;
        bool show = (state is StripState.Confirm or StripState.Block) && !string.IsNullOrWhiteSpace(reason);
        _reason.Text = show ? (state == StripState.Block ? "❌  " : "⚠  ") + reason : "";
        _reason.Visible = show;
        Fit();          // dòng 2 bật/tắt thì đổi chiều cao
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
        Fit();
    }

    void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is not (Keys.Enter or Keys.Return)) return;
        e.SuppressKeyPress = true;
        if (sender == _value) { ValueSubmitted?.Invoke(_value.Text.Trim()); return; }
        InputSubmitted?.Invoke(_csLabel.Text.Trim(), _labelOnly ? "" : _csVar.Text.Trim());
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
