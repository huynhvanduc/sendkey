using System.Media;
using System.Runtime.InteropServices;

namespace SendKeyDemo;

public sealed class EvidenceSession : IDisposable
{
    readonly MainForm _host;
    readonly AppSettings _settings;
    readonly EvidenceBarForm _bar = new();

    ClipboardWatcher? _clip;
    VsAutomation.BreakWatcher? _watcher;
    LastCapture? _last;
    bool _active;

    string _cmdLabel = "";
    readonly List<string> _items = new();
    string? _askingItem;

    record NavHit(string Item, string Watch, string File, int LabelLine, int Line);

    string _navKey = "";
    string _navReason = "";
    List<NavHit> _nav = new();
    int _navIndex;
    DateTime _lastCopy;
    readonly List<StopTarget> _picked = new();

    List<StopPoint> _stops = new();
    readonly HashSet<int> _done = new();
    int _currentStop = -1;
    List<string> _bpFiles = new();
    string? _confirmed;
    IntPtr _sourceWindow;
    (StopPoint Stop, IfBypass Bypass)? _pendingBypass;

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

    public EvidenceSession(MainForm host, AppSettings settings)
    {
        _host = host;
        _settings = settings;
        _bar.InputSubmitted += OnInputSubmitted;
        _bar.ValueSubmitted += OnValueSubmitted;
    }

    public bool Active => _active;
    public EvidenceBarForm Bar => _bar;

    string TcId => _cmdLabel + string.Concat(_items.Select(i => $"「{i}」"));

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
        _picked.Clear();
        _nav = new();
        _navKey = _navReason = "";
        _navIndex = 0;
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

        // Copy CHỈ điều hướng; breakpoint chỉ đụng tới khi bấm G hoặc khi sang nhóm mới.
        var key = CopiedText.Normalize(CopiedText.UnquoteExcel(raw));
        var now = DateTime.UtcNow;
        if (key == _navKey && now - _lastCopy < TimeSpan.FromMilliseconds(400)) return;   // lỡ tay Ctrl+C 2 phát
        _lastCopy = now;

        bool same = key == _navKey;      // copy lại y hệt → sang kết quả kế tiếp
        _navKey = key;
        _navReason = "";
        _askingItem = null;

        if (!pieces[0].IsVar)
        {
            var label = pieces[0].Value;
            if (Mapping.NormalizeLabel(label) != Mapping.NormalizeLabel(_cmdLabel)) StartGroup();
            _cmdLabel = label;
            _nav = BuildLabelNav(label);
        }
        else
        {
            // Gộp mọi 「」 của lần copy này vào MỘT chuỗi duyệt: đi hết dòng của A rồi sang dòng của B.
            _nav = BuildVarNav(pieces.Where(p => p.IsVar).Select(p => p.Value).ToList());
        }

        ShowNav(same);
    }

    public void ClearBreakpoints()
    {
        if (!_active) return;

        int n = _stops.Count;
        string watchNote = "";
        if (_host.CurrentDte() is { } dte)
        {
            try { foreach (var f in _bpFiles) VsAutomation.ClearBreakpointsInFile(dte, f); }
            catch (Exception ex) { Block("Lỗi xoá breakpoint: " + ex.Message); return; }

            // Watch chỉ xoá được khi VS đang dừng, nên phải nói thật thay vì im lặng bỏ qua.
            watchNote = !VsAutomation.InBreakMode(dte) ? " · Watch chỉ xoá được khi đang dừng"
                : VsAutomation.ClearWatchAll(dte) ? " · đã làm sạch Watch"
                : " · không xoá được Watch, xoá tay giúp";
        }

        _picked.Clear();
        _items.Clear();
        ResetStops();
        _bpFiles.Clear();

        var msg = (n > 0 ? $"đã xoá {n} điểm dừng" : "không có điểm dừng nào để xoá") + watchNote;
        _bar.SetPair(_cmdLabel, _items, msg);
        _bar.SetStatus(StripState.Idle, null);
        _host.Log("Chụp: " + msg + " — copy lại label/biến rồi bấm " + _settings.GotoCurrentHotkey + ".");
    }

    void StartGroup()
    {
        _picked.Clear();
        _items.Clear();
        ResetStops();
        if (_bpFiles.Count > 0 && _host.CurrentDte() is { } dte)
            try { foreach (var f in _bpFiles) VsAutomation.ClearBreakpointsInFile(dte, f); }
            catch (Exception ex) { _host.Log("Chụp: không xoá được breakpoint nhóm cũ — " + ex.Message); }
        _bpFiles.Clear();
    }

    List<NavHit> BuildLabelNav(string label)
    {
        var nav = new List<NavHit>();
        if (_host.CurrentDte() is not { } dte) { _navReason = "Chưa chọn instance VS — mở cửa sổ cấu hình bấm Refresh."; return nav; }

        var row = _host.GetMapRows()?.FirstOrDefault(r => Mapping.NormalizeLabel(r.CmdLabel) == Mapping.NormalizeLabel(label));
        var csp = row != null ? _host.EffectiveCsPath(row) : VsAutomation.CurrentClassFile(dte);
        var prefix = row != null ? row.CsharpLabel : Mapping.CleanLabel(label);

        if (string.IsNullOrWhiteSpace(csp) || !File.Exists(csp))
        {
            _navReason = row != null ? $"Không thấy file .cs: {csp}" : "chưa mở file .cs trong VS, hoặc thêm dòng mapping";
            return nav;
        }

        foreach (var h in Mapping.FindLabelsByPrefix(File.ReadAllLines(csp), prefix))
            nav.Add(new NavHit("", "", csp, h.LabelLine, h.ExecLine));
        if (nav.Count == 0) _navReason = $"Không thấy nhãn bắt đầu bằng \"{prefix}\" trong {Path.GetFileName(csp)}.";
        return nav;
    }

    List<NavHit> BuildVarNav(IReadOnlyList<string> items)
    {
        var nav = new List<NavHit>();
        if (_cmdLabel.Length == 0) { _navReason = "Chưa copy label."; return nav; }

        var rows = _host.GetMapRows();
        if (rows == null) { _navReason = "Chưa nạp được mapping.csv — kiểm tra đường dẫn ở cửa sổ cấu hình."; return nav; }

        var cur = _navIndex >= 0 && _navIndex < _nav.Count ? _nav[_navIndex] : null;

        foreach (var item in items)
        {
            if (CopiedText.GotoTarget(item) is { } gotoLabel)
            {
                nav.AddRange(BuildLabelNav(gotoLabel).Select(h => h with { Item = item }));
                continue;
            }

            var res = Mapping.Resolve(rows, _cmdLabel, item);
            if (res.Kind == LookupKind.Duplicate)
            {
                _navReason = $"mapping.csv trùng ở dòng {string.Join(", ", res.DuplicateLines!)} — sửa file rồi thử lại.";
                continue;
            }
            if (res.Row is not { } row)
            {
                _askingItem = item;
                _navReason = $"「{item}」 chưa có trong mapping.csv — bấm {_settings.GotoCurrentHotkey} để gõ C#.";
                continue;
            }

            var csp = _host.EffectiveCsPath(row);
            if (string.IsNullOrWhiteSpace(csp) || !File.Exists(csp)) { _navReason = $"Không thấy file .cs: {csp}"; continue; }

            var lines = File.ReadAllLines(csp);
            var hits = Mapping.FindLabelsByPrefix(lines, row.CsharpLabel);
            if (hits.Count == 0) { _navReason = $"Không thấy \"{row.CsharpLabel}:\" trong {Path.GetFileName(csp)}."; continue; }

            // Đang đứng ở một nhãn khớp rồi thì tìm trong chính nhãn đó, đừng nhảy về nhãn đầu tiên.
            int labelLine = cur != null && string.Equals(cur.File, csp, StringComparison.OrdinalIgnoreCase)
                            && hits.Any(h => h.LabelLine == cur.LabelLine)
                ? cur.LabelLine
                : hits[0].LabelLine;

            var found = Mapping.FindInLabel(lines, labelLine, row.CsharpVar);
            if (found.Count == 0)
            {
                _navReason = $"Không thấy \"{row.CsharpVar}\" trong nhãn ở dòng {labelLine} của {Path.GetFileName(csp)}.";
                continue;
            }
            foreach (var ln in found) nav.Add(new NavHit(item, row.CsharpVar, csp, labelLine, ln));
        }
        return nav;
    }

    void ShowNav(bool same)
    {
        if (_nav.Count == 0)
        {
            _bar.SetPair(_cmdLabel, _items, "không thấy");
            _bar.SetStatus(StripState.Block, _navReason.Length > 0 ? _navReason : "Không tìm thấy.");
            return;
        }

        _navIndex = same ? (_navIndex + 1) % _nav.Count : 0;
        var h = _nav[_navIndex];

        // Biến → dòng lệnh; label → dòng khai báo nhãn (bôi đen cho user đọc được nhãn), nhưng G vẫn đặt BP ở h.Line.
        int line = h.Watch.Length > 0 ? h.Line : h.LabelLine;
        if (_host.CurrentDte() is { } dte)
            try { _host.Log("Điều hướng: " + VsAutomation.GoToLine(dte, h.File, line, select: true, activate: false)); }
            catch (Exception ex) { _host.Log("Điều hướng: lỗi thao tác VS — " + ex.Message); }

        var what = h.Watch.Length > 0 ? h.Watch : h.Item.Length > 0 ? h.Item : "label";
        var where = $"{what} · {Path.GetFileName(h.File)}:{line}";
        if (_nav.Count == 1)
        {
            _bar.SetPair(_cmdLabel, _items, $"1 dòng · {where}");
            _bar.SetStatus(StripState.Ok, null);
        }
        else
        {
            _bar.SetPair(_cmdLabel, _items, $"{_nav.Count} dòng – đang ở {_navIndex + 1} · {where}");
            _bar.SetStatus(StripState.Confirm, "copy lại để sang dòng kế tiếp");
        }
    }

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
        _navReason = "";
        _nav = BuildVarNav(new[] { item });   // vừa có dòng mapping → dựng lại chỗ dừng rồi mới đặt breakpoint
        ShowNav(same: false);
        Run();
    }

    // ---------------- G: đặt breakpoint ----------------

    public void Run()
    {
        if (!_active) return;

        // Hai loại đỏ khác nhau: thiếu mapping thì đây đúng là lúc mở ô gõ C#; không tìm thấy trong file thì chặn.
        if (_askingItem is { } asking)
        {
            var known = _host.GetMapRows()?.FirstOrDefault(r => Mapping.NormalizeLabel(r.CmdLabel) == Mapping.NormalizeLabel(_cmdLabel));
            _bar.AskInput(CopiedText.GotoTarget(asking) != null, asking, known?.CsharpLabel ?? "", "");
            _bar.SetStatus(StripState.Confirm, $"「{asking}」 chưa có trong mapping.csv — gõ C# rồi Enter.");
            return;
        }

        if (_nav.Count == 0) { Block("Chưa copy, hoặc không tìm thấy — copy lại label/biến."); return; }

        var h = _nav[_navIndex];
        if (h.Line == 0) { Block($"Sau nhãn ở dòng {h.LabelLine} không còn dòng thực thi."); return; }

        // Dòng gán cần thêm vế phải vì breakpoint dừng TRƯỚC khi dòng chạy, lúc đó biến còn giá trị cũ.
        var src = File.ReadAllLines(h.File);
        var watches = Mapping.WatchFor(h.Line >= 1 && h.Line <= src.Length ? src[h.Line - 1] : "", h.Watch);
        if (watches.Count == 0) watches = new List<string> { "" };   // điều hướng theo label: không Watch gì

        // Chọn lại CÙNG biến ở CÙNG dòng thì làm mới Watch của chính nó; biến khác ở cùng dòng thì cộng dồn.
        _picked.RemoveAll(p => Mapping.SamePick(p, h.File, h.Line, h.Item));

        int firstNew = _picked.Count;
        foreach (var w in watches)
            _picked.Add(new StopTarget(h.File, h.Line, h.LabelLine, w, h.Item));

        if (IfClause.IsLoop(h.Item))
            _host.Log($"Chụp: 「{h.Item}」 là vòng lặp — chỉ điều hướng + chụp, không sinh lệnh SET.");
        else if (IfClause.IsIf(h.Item) && Mapping.IsExpression(h.Watch))
        {
            // Mệnh đề if: ảnh 1 ở dòng if (giá trị thật) → chụp xong app SET cho mệnh đề ĐÚNG → ảnh 2 ở lệnh đầu nhánh.
            for (int i = firstNew; i < _picked.Count; i++)
                _picked[i] = _picked[i] with { Condition = h.Watch };
            if (Mapping.BranchStart(src, h.Line) is { } branch)
                _picked.Add(new StopTarget(h.File, branch.Line, h.LabelLine, h.Watch, h.Item, branch.Column));
            else
                _host.Log($"Chụp: không tìm được lệnh đầu nhánh của {Path.GetFileName(h.File)}:{h.Line} — chỉ chụp dòng if.");
        }

        var stops = Mapping.GroupStops(_picked);
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

        // TcId = label + 「items」 là khoá chống chụp trùng giá trị — quên cập nhật là cái gác đó hỏng âm thầm.
        _items.Clear();
        foreach (var it in _picked.Select(p => p.Item).Where(i => i.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            _items.Add(it);

        // Định danh thuần thì breakpoint rơi vào dòng đầu sau label — giá trị có thể chưa được gán ở đó.
        foreach (var s in stops)
            foreach (var w in s.Watch.Where(w => !Mapping.IsExpression(w) && LineMentions(s.File, s.Line, w) == false))
                _host.Log($"Chụp: ⚠ dòng {Path.GetFileName(s.File)}:{s.Line} không nhắc tới \"{w}\" — " +
                          "giá trị có thể chưa được gán ở đây.");

        // Điền Watch ngay, không đòi mũi tên vàng ở đúng dòng: biến local của Main vẫn trong scope.
        if (VsAutomation.InBreakMode(dte))
        {
            var pick = stops.FirstOrDefault(x => x.Line == h.Line && x.Column == 0 &&
                                                 string.Equals(x.File, h.File, StringComparison.OrdinalIgnoreCase))
                       ?? stops[0];
            try
            {
                var missed = VsAutomation.SetWatch(dte, pick.File, pick.LabelLine, pick.Watch);
                if (missed is not { Count: 0 })
                {
                    var manual = missed ?? pick.Watch.ToList();
                    CopyForManualWatch(manual);
                    if (manual.Count > 0) _host.Log($"Chụp: đã copy {string.Join(", ", manual)} — Ctrl+V vào Watch.");
                }
            }
            catch (Exception ex) { _host.Log("Chụp: lỗi điền Watch — " + ex.Message); }

            // Dừng khác dòng thì Evaluate trả Block, là bình thường lúc chưa chạy tới — đừng báo đỏ.
            if (IsAt(pick, VsAutomation.ReadDebugState(dte, "", Array.Empty<string>()))) { Recheck(); return; }

            _bar.SetPair(_cmdLabel, _items, $"{TargetText(stops, 0)} · Watch đã điền · chờ F5 tới dòng này");
            _bar.SetStatus(StripState.Pending, null);
            return;
        }

        // Thanh chỉ hiện dòng lý do khi vàng/đỏ, nên phải nói đang chờ gì ngay ở dòng 1.
        _bar.SetPair(_cmdLabel, _items, $"{TargetText(stops, 0)} · chờ F5, Watch tự điền khi dừng");
        _bar.SetStatus(StripState.Pending, null);
    }

    // ---------------- chấm + chụp ----------------

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
        if (fromEvent) _pendingBypass = null;              // đã chạy tới chỗ khác: ô giá trị của dòng if hết hiệu lực

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

        string? bypassNote = null;
        var bypassState = StripState.Pending;
        _pendingBypass = null;
        if (stop.Condition.Length > 0)
        {
            if (stop.Items.Where(IfClause.IsIf).Select(IfClause.Suggest).FirstOrDefault(b => b != null) is not { } bypass)
                (bypassNote, bypassState) = ($"「{stop.Items[0]}」: tự set giá trị cho mệnh đề này rồi F5.", StripState.Confirm);
            else
            {
                _pendingBypass = (stop, bypass);
                (bypassNote, bypassState) = ApplyBypass(stop, bypass);
            }
        }

        bool all = _done.Count == _stops.Count;
        _bar.SetPair(_cmdLabel, _items, all
            ? $"✓ đủ {_stops.Count} ảnh — copy test case tiếp"
            : $"Ctrl+V rồi F5 trong VS → {TargetText(_stops, NextStop())}");
        _bar.SetStatus(bypassNote != null ? bypassState : all ? StripState.Ok : StripState.Pending, bypassNote);
        if (_pendingBypass is { } pending) _bar.AskValue(pending.Bypass.Var, pending.Bypass.Value);   // sau SetPair (SetPair ẩn ô)
        if (bypassNote != null) Beep(bypassState);

        if (_sourceWindow != IntPtr.Zero) SetForegroundWindow(_sourceWindow);
    }

    (string? Note, StripState State) ApplyBypass(StopPoint stop, IfBypass bypass)
    {
        var statement = IfClause.Statement(_settings.IfSetStatement, bypass);
        if (_host.CurrentDte() is not { } dte) return ($"Mất kết nối VS — chưa chạy được {statement}.", StripState.Block);
        if (VsAutomation.RunIfBypass(dte, statement, stop.Condition) is { } err) return (err, StripState.Block);
        _host.Log($"Chụp: đã chạy {statement} — {stop.Condition} = true.");
        return (null, StripState.Pending);
    }

    void OnValueSubmitted(string value)
    {
        if (!_active || _pendingBypass is not { } pending || value.Length == 0) return;
        var bypass = pending.Bypass with { Value = value };
        _pendingBypass = (pending.Stop, bypass);
        var (note, state) = ApplyBypass(pending.Stop, bypass);
        _bar.SetStatus(note != null ? state : StripState.Pending, note);
        Beep(note != null ? state : StripState.Ok);
    }

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
        _pendingBypass = null;
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

    void CopyForManualWatch(IReadOnlyList<string> exprs)
    {
        if (exprs.Count == 0) return;
        var text = string.Join(Environment.NewLine, exprs);
        if (_clip != null) _clip.IgnoreText = text;   // đừng tự nhận lại biểu thức mình vừa copy
        try { Clipboard.SetText(text); } catch { /* clipboard bận */ }
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

    void Beep(StripState state)
    {
        if (_settings.Silent) return;

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


public enum StripState { Idle, Pending, Ok, Confirm, Block }

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

    public event Action<string, string>? InputSubmitted;
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
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0),
            BackColor = Theme.PanelBackground,
        };
        line1.Controls.AddRange(new Control[] { _dot, _cmd, _arrow, _target, _valueName, _value, _csLabel, _csVar });

        _body = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 4, 12, 4),
            BackColor = Theme.PanelBackground,
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

    public void AskValue(string varName, string value)
    {
        _valueName.Text = $"{varName} =";
        _value.Text = value;
        _valueName.Visible = _value.Visible = true;
        Fit();
    }

    public void SetStatus(StripState state, string? reason)
    {
        _state = state;
        _dot.ForeColor = DotColor;
        bool show = (state is StripState.Confirm or StripState.Block) && !string.IsNullOrWhiteSpace(reason);
        _reason.Text = show ? (state == StripState.Block ? "❌  " : "⚠  ") + reason : "";
        _reason.Visible = show;
        Fit();
        Invalidate();
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

    // ContextMenuStrip không kế thừa xuống control con, mà thanh phủ kín bởi Label nên phải gán đệ quy.
    public void AttachMenu(ContextMenuStrip menu)
    {
        ContextMenuStrip = menu;
        Attach(Controls);

        void Attach(Control.ControlCollection kids)
        {
            foreach (Control c in kids)
            {
                if (c is not TextBox) c.ContextMenuStrip = menu;
                Attach(c.Controls);
            }
        }
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

public sealed class ClipboardWatcher : NativeWindow, IDisposable
{
    const int WM_CLIPBOARDUPDATE = 0x031D;

    [DllImport("user32.dll", SetLastError = true)] static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

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
