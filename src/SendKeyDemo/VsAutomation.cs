using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
using EnvDTE;
using UIA = System.Windows.Automation;

namespace SendKeyDemo;

public record VsInstance(string Version, int ProcessId, string SolutionName, DTE Dte)
{
    public override string ToString() => $"VS {Version} — {SolutionName} (pid {ProcessId})";
}

public static class VsAutomation
{
    public static (string Version, int ProcessId)? ParseMoniker(string? displayName)
    {
        var m = Regex.Match(displayName ?? "", @"^!VisualStudio\.DTE\.(\d+\.\d+):(\d+)$");
        return m.Success ? (m.Groups[1].Value, int.Parse(m.Groups[2].Value)) : null;
    }

    [DllImport("ole32.dll")] static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);
    [DllImport("ole32.dll")] static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable prot);

    public static IEnumerable<VsInstance> FindVisualStudios()
    {
        var list = new List<VsInstance>();
        if (GetRunningObjectTable(0, out var rot) != 0) return list;
        rot.EnumRunning(out var e);
        CreateBindCtx(0, out var ctx);

        var monikers = new IMoniker[1];
        while (e.Next(1, monikers, IntPtr.Zero) == 0)
        {
            try
            {
                monikers[0].GetDisplayName(ctx, null, out var name);
                if (ParseMoniker(name) is not { } p) continue;
                if (rot.GetObject(monikers[0], out var obj) != 0) continue;
                if (obj is not DTE dte) continue;

                var sln = "(chưa mở solution)";
                try
                {
                    var full = dte.Solution?.FullName;
                    if (!string.IsNullOrEmpty(full)) sln = Path.GetFileName(full);
                }
                catch { /* solution đang load */ }

                list.Add(new VsInstance(p.Version, p.ProcessId, sln, dte));
            }
            catch (COMException) { continue; }
        }
        return list;
    }

    public static string GoToLine(DTE dte, string file, int line)
    {
        if (!File.Exists(file)) return $"file không tồn tại: {file}";
        var win = dte.ItemOperations.OpenFile(file, Constants.vsViewKindTextView);
        win.Activate();
        var sel = (TextSelection)win.Document.Selection;
        sel.GotoLine(line, false);
        var reached = sel.CurrentLine;
        dte.MainWindow.Activate();
        return reached == line
            ? $"đã tới {Path.GetFileName(file)}:{line}"
            : $"đã tới {Path.GetFileName(file)}:{reached} (file chỉ có {reached} dòng)";
    }

    /// <summary>
    /// Mở đúng tab file .cs, đặt con trỏ ở dòng dừng và cuộn cho dòng label nằm đầu vùng nhìn (ảnh chụp
    /// phải thấy label cần kiểm chứng), rồi đưa VS lên trước. Trả false nếu cuộn rồi vẫn không thấy label.
    /// </summary>
    public static bool ShowLabel(DTE dte, string file, int labelLine, int stopLine)
    {
        var win = dte.ItemOperations.OpenFile(file, Constants.vsViewKindTextView);
        win.Activate();
        ((TextSelection)win.Document.Selection).GotoLine(stopLine, false);
        bool visible = true;
        if (labelLine > 0 && win.Object is TextWindow tw)
        {
            var doc = (TextDocument)win.Document.Object("TextDocument");
            var top = doc.CreateEditPoint();
            top.MoveToLineAndOffset(labelLine, 1);
            var bottom = doc.CreateEditPoint();
            bottom.MoveToLineAndOffset(stopLine, 1);
            tw.ActivePane.TryToShow(top, vsPaneShowHow.vsPaneShowTop, bottom);
            visible = tw.ActivePane.IsVisible(top, bottom);
        }
        dte.MainWindow.Activate();
        return visible;
    }

    public static string ToggleBreakpoint(DTE dte, string file, int line)
    {
        if (!File.Exists(file)) return $"file không tồn tại: {file}";
        foreach (Breakpoint bp in dte.Debugger.Breakpoints)
        {
            if (string.Equals(bp.File, file, StringComparison.OrdinalIgnoreCase) && bp.FileLine == line)
            {
                bp.Delete();
                return $"đã xóa breakpoint tại {Path.GetFileName(file)}:{line}";
            }
        }
        try
        {
            dte.Debugger.Breakpoints.Add("", file, line);
        }
        catch (COMException)
        {
            return $"không đặt được breakpoint tại {Path.GetFileName(file)}:{line} — dòng phải là lệnh " +
                   "thực thi (không phải dòng trống / comment / using / khai báo), và file phải thuộc " +
                   "solution đang mở trong VS.";
        }
        return $"đã đặt breakpoint tại {Path.GetFileName(file)}:{line}";
    }

    public static string ClearBreakpointsInFile(DTE dte, string file)
    {
        if (string.IsNullOrWhiteSpace(file)) return "chưa có file để xóa breakpoint";
        var bps = dte.Debugger.Breakpoints;
        int n = 0;
        for (int i = bps.Count; i >= 1; i--)   // duyệt ngược vì Delete() đổi collection
        {
            Breakpoint bp = bps.Item(i);
            if (string.Equals(bp.File, file, StringComparison.OrdinalIgnoreCase)) { bp.Delete(); n++; }
        }
        return n == 0
            ? $"không có breakpoint nào ở {Path.GetFileName(file)}"
            : $"đã xóa {n} breakpoint ở {Path.GetFileName(file)}";
    }

    public static string EnsureBreakpoint(DTE dte, string file, int line)
    {
        if (!File.Exists(file)) return $"file không tồn tại: {file}";
        foreach (Breakpoint bp in dte.Debugger.Breakpoints)
        {
            if (string.Equals(bp.File, file, StringComparison.OrdinalIgnoreCase) && bp.FileLine == line)
                return $"breakpoint đã có tại {Path.GetFileName(file)}:{line}";
        }
        try
        {
            dte.Debugger.Breakpoints.Add("", file, line);
        }
        catch (COMException)
        {
            return $"không đặt được breakpoint tại {Path.GetFileName(file)}:{line} — dòng phải là lệnh " +
                   "thực thi (không phải dòng trống / comment / khai báo), và file phải thuộc solution đang mở.";
        }
        return $"đã đặt breakpoint tại {Path.GetFileName(file)}:{line}";
    }

    /// <summary>Xóa mọi breakpoint trong file rồi đặt đúng 1 cái ở <paramref name="line"/> — dùng cho chế độ chụp.</summary>
    public static string SetOnlyBreakpoint(DTE dte, string file, int line)
    {
        if (!File.Exists(file)) return $"file không tồn tại: {file}";
        ClearBreakpointsInFile(dte, file);
        return EnsureBreakpoint(dte, file, line);
    }

    /// <summary>
    /// Đọc trạng thái debugger để gác cổng trước khi chụp. Toàn read-only, không đổi gì phía VS.
    /// </summary>
    public static DebugSnapshot ReadDebugState(DTE dte, string csFile, IReadOnlyList<string> exprs)
    {
        try
        {
            var dbg = dte.Debugger;
            bool inBreak = dbg.CurrentMode == dbgDebugMode.dbgBreakMode;

            // Breakpoint nào vừa làm chương trình dừng — chính xác hơn nhiều so với đọc vị trí con trỏ,
            // vì con trỏ có thể đã bị người dùng click đi chỗ khác.
            string hitFile = "";
            int hitLine = 0;
            if (inBreak)
            {
                try
                {
                    var hit = dbg.BreakpointLastHit;
                    if (hit != null) { hitFile = hit.File ?? ""; hitLine = hit.FileLine; }
                }
                catch (COMException) { /* dừng không do breakpoint */ }
            }

            int bpInFile = 0;
            try
            {
                foreach (Breakpoint bp in dbg.Breakpoints)
                {
                    try
                    {
                        if (string.Equals(bp.File, csFile, StringComparison.OrdinalIgnoreCase)) bpInFile++;
                    }
                    catch (COMException) { /* breakpoint kiểu hàm — không có File */ }
                }
            }
            catch (COMException) { /* chưa có collection */ }

            // Đọc từng biểu thức; hỏng cái nào thì nhớ tên cái đầu tiên để báo cho dev.
            bool exprValid = inBreak;
            string badExpr = "";
            var values = new List<string>();
            foreach (var expr in inBreak ? exprs : Array.Empty<string>())
            {
                try
                {
                    var e = dbg.GetExpression(expr, true, 2000);
                    if (e.IsValidValue) { values.Add(e.Value ?? ""); continue; }
                }
                catch (COMException) { /* biểu thức không evaluate được ở frame hiện tại */ }
                if (badExpr.Length == 0) badExpr = expr;
                exprValid = false;
            }
            var exprValue = string.Join("; ", values);

            int pid = 0;
            try { pid = dbg.CurrentProcess?.ProcessID ?? 0; }
            catch (COMException) { /* chưa chạy */ }

            return new DebugSnapshot(true, inBreak, hitFile, hitLine, bpInFile, exprValid, exprValue, pid, BadExpr: badExpr);
        }
        catch (Exception ex)
        {
            return DebugSnapshot.Unavailable("Không đọc được trạng thái VS: " + ex.Message);
        }
    }

    /// <summary>
    /// Bám sự kiện VS dừng ở breakpoint để tự chấm mà không cần người dùng bấm gì.
    /// QUAN TRỌNG: phải giữ instance này trong một field — thả ra là GC dọn mất
    /// đối tượng events và sự kiện im lặng ngừng bắn.
    /// </summary>
    public sealed class BreakWatcher : IDisposable
    {
        readonly DebuggerEvents _events;                                        // giữ tham chiếu, đừng để GC dọn
        readonly _dispDebuggerEvents_OnEnterBreakModeEventHandler _onBreak;

        public BreakWatcher(DTE dte, Action onEnterBreak)
        {
            _events = dte.Events.DebuggerEvents;
            _onBreak = (dbgEventReason reason, ref dbgExecutionAction action) => onEnterBreak();
            _events.OnEnterBreakMode += _onBreak;
        }

        public void Dispose()
        {
            try { _events.OnEnterBreakMode -= _onBreak; }
            catch (Exception) { /* VS đã đóng */ }
        }
    }

    // Watch window kind GUID (EnvDTE.Constants.vsWindowKindWatch)
    const string WatchWindowKind = "{90243340-BD7A-11D0-93EF-00A0C90F2734}";

    /// <summary>
    /// Copy biểu thức vào clipboard và mở/kích hoạt cửa sổ Watch. KHÔNG gõ phím tự động:
    /// SendKeys gõ mù có thể rơi vào editor và sửa file .cs. Người dùng bấm Ctrl+V rồi Enter.
    /// </summary>
    public static string AddWatch(DTE dte, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return "biểu thức trống";
        if (dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
            return "Chưa ở break mode — F5 chạy chương trình và để nó DỪNG lại ở breakpoint, rồi mới Add Watch.";

        var copied = false;
        try { Clipboard.SetText(expression); copied = true; } catch { /* clipboard đang bận */ }

        try
        {
            var win = dte.Windows.Item(WatchWindowKind);
            win.Visible = true;
            win.Activate();
        }
        catch (COMException)
        {
            // Chưa từng mở cửa sổ Watch nào -> Windows.Item ném. Gọi lệnh menu để VS tự tạo Watch 1.
            try { dte.ExecuteCommand("Debug.Watch1"); }
            catch (COMException) { /* vẫn không mở được — người dùng tự mở */ }
        }

        return copied
            ? $"đã copy \"{expression}\" + mở cửa sổ Watch — bấm Ctrl+V rồi Enter (không gõ tự động để tránh sửa nhầm file .cs)."
            : $"không copy được clipboard — tự gõ \"{expression}\" vào cửa sổ Watch.";
    }

    // ---- Watch tự điền: không gõ phím nào vào VS ----
    // Đã thử trên VS 2022: Debug.AddWatch KHÔNG nhận tham số — nó lấy chữ đang bôi đen trong editor, và editor
    // phải đang active (focus ở Watch thì VS nhân bản dòng watch đang chọn). Chỉ chạy khi VS đang dừng.
    // DTE không có API đọc/xoá Watch và ActiveWindow trả null khi Watch active, nên đọc/xoá qua UI Automation.

    /// <summary>
    /// Đặt Watch 1 = đúng <paramref name="exprs"/>: xoá hết dòng cũ, rồi thêm từng biểu thức bằng cách bôi đen nó
    /// trong file .cs (tìm từ <paramref name="fromLine"/>, không thấy thì tìm cả file) và gọi Debug.AddWatch.
    /// Trả về biểu thức không tự thêm được; null nếu không xoá được Watch cũ (hoặc VS chưa dừng).
    /// </summary>
    public static List<string>? SetWatch(DTE dte, string file, int fromLine, IReadOnlyList<string> exprs)
    {
        if (dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode) return null;
        try { dte.ExecuteCommand("Debug.Watch1"); } catch (COMException) { return null; }
        if (WatchTree(dte) is not { } tree || !ClearWatch(dte, tree)) return null;

        var missed = new List<string>();
        foreach (var expr in exprs)
        {
            try
            {
                var win = dte.ItemOperations.OpenFile(file, Constants.vsViewKindTextView);
                win.Activate();
                var sel = (TextSelection)win.Document.Selection;
                int flags = (int)vsFindOptions.vsFindOptionsMatchCase |
                            (Mapping.IsExpression(expr) ? 0 : (int)vsFindOptions.vsFindOptionsMatchWholeWord);
                sel.MoveToLineAndOffset(Math.Max(1, fromLine), 1);
                if (!sel.FindText(expr, flags) && !sel.FindText(expr, flags | (int)vsFindOptions.vsFindOptionsFromStart))
                {
                    missed.Add(expr);
                    continue;
                }
                dte.ExecuteCommand("Debug.AddWatch");
            }
            catch (COMException) { missed.Add(expr); }
        }
        return missed;
    }

    /// <summary>Biểu thức các dòng đang có trong Watch 1; null nếu không đọc được.</summary>
    public static List<string>? ReadWatchNames(DTE dte)
    {
        try { return WatchTree(dte) is { } tree ? WatchItems(tree).Select(i => i.Current.Name).ToList() : null; }
        catch (Exception) { return null; }
    }

    static UIA.AutomationElement? WatchTree(DTE dte)
    {
        var hwnd = new IntPtr(dte.MainWindow.HWnd);
        if (hwnd == IntPtr.Zero) return null;
        return UIA.AutomationElement.FromHandle(hwnd).FindFirst(UIA.TreeScope.Descendants, new UIA.AndCondition(
            new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.Tree),
            new UIA.PropertyCondition(UIA.AutomationElement.NameProperty, "Watch 1")));
    }

    static List<UIA.AutomationElement> WatchItems(UIA.AutomationElement tree)
        => tree.FindAll(UIA.TreeScope.Children,
                new UIA.PropertyCondition(UIA.AutomationElement.ControlTypeProperty, UIA.ControlType.TreeItem))
            .Cast<UIA.AutomationElement>().ToList();

    /// <summary>
    /// Xoá từng dòng Watch: chọn dòng bằng UI Automation rồi Edit.Delete — CHỈ khi focus đang nằm trong cây Watch,
    /// để lệnh Delete không bao giờ rơi vào editor. Mỗi lần xoá được 1 dòng nên lặp tới hết.
    /// </summary>
    static bool ClearWatch(DTE dte, UIA.AutomationElement tree)
    {
        for (int guard = 0; guard < 100; guard++)
        {
            var items = WatchItems(tree);
            if (items.Count == 0) return true;

            items[0].SetFocus();
            ((UIA.SelectionItemPattern)items[0].GetCurrentPattern(UIA.SelectionItemPattern.Pattern)).Select();
            if (!FocusInside(tree)) return false;

            dte.ExecuteCommand("Edit.Delete");
            if (WatchItems(tree).Count >= items.Count) return false;   // không xoá được — dừng, đừng lặp mãi
        }
        return false;
    }

    static bool FocusInside(UIA.AutomationElement element)
    {
        var walker = UIA.TreeWalker.ControlViewWalker;
        for (var f = UIA.AutomationElement.FocusedElement; f != null; f = walker.GetParent(f))
            if (UIA.Automation.Compare(f, element)) return true;
        return false;
    }

    // ---- COM message filter: tự retry khi VS đang bận ----
    public static class OleMessageFilter
    {
        [DllImport("ole32.dll")]
        static extern int CoRegisterMessageFilter(IOleMessageFilter newFilter, out IOleMessageFilter oldFilter);

        public static void Register() => CoRegisterMessageFilter(new Filter(), out _);

        class Filter : IOleMessageFilter
        {
            // SERVERCALL_ISHANDLED
            public int HandleInComingCall(int callType, IntPtr caller, int tickCount, IntPtr info) => 0;

            // chỉ RETRYLATER (2) mới retry được; REJECTED (1) thì hủy luôn
            public int RetryRejectedCall(IntPtr callee, int tickCount, int rejectType)
                => rejectType == 2 && tickCount < 10_000 ? 100 : -1;

            // PENDINGMSG_WAITDEFPROCESS
            public int MessagePending(IntPtr callee, int tickCount, int pendingType) => 2;
        }
    }

    [ComImport, Guid("00000016-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOleMessageFilter
    {
        [PreserveSig] int HandleInComingCall(int callType, IntPtr caller, int tickCount, IntPtr info);
        [PreserveSig] int RetryRejectedCall(IntPtr callee, int tickCount, int rejectType);
        [PreserveSig] int MessagePending(IntPtr callee, int tickCount, int pendingType);
    }
}

// ==================== CaptureCheck ====================

/// <summary>Mức gác cổng trước khi chụp bằng chứng.</summary>
public enum CheckLevel
{
    /// <summary>Mọi thứ khớp — chụp thẳng.</summary>
    Ok,
    /// <summary>Nghi chưa reset biến — hỏi lại rồi mới chụp.</summary>
    Confirm,
    /// <summary>Sai thao tác cơ học — không chụp.</summary>
    Block,
}

/// <summary>Ảnh chụp trạng thái debugger của VS tại một thời điểm. Đọc bởi <see cref="VsAutomation.ReadDebugState"/>.</summary>
public record DebugSnapshot(
    bool Available,
    bool InBreakMode,
    string HitFile,
    int HitLine,
    int BreakpointsInFile,
    bool ExprValid,
    string ExprValue,
    int ProcessId,
    string? Error = null,
    string BadExpr = "")
{
    public static DebugSnapshot Unavailable(string error) =>
        new(false, false, "", 0, 0, false, "", 0, error);
}

/// <summary>Lần chụp gần nhất — để phát hiện "giá trị y hệt lần trước, chưa reset".</summary>
public record LastCapture(int ProcessId, string TcId, string Expr, string Value);

public record CheckResult(CheckLevel Level, string Message);

/// <summary>
/// Chấm điểm "có được phép chụp bằng chứng không". Logic thuần, không đụng COM —
/// nhận <see cref="DebugSnapshot"/> đã đọc sẵn nên test được.
/// </summary>
public static class CaptureCheck
{
    /// <summary>So hai giá trị debugger đọc được, bỏ qua nháy bao ngoài và hoa/thường.</summary>
    public static bool ValuesMatch(string? actual, string? expected)
        => string.Equals(Unquote(actual), Unquote(expected), StringComparison.OrdinalIgnoreCase);

    static string Unquote(string? s)
    {
        var t = (s ?? "").Trim();
        if (t.Length >= 2 && ((t[0] == '"' && t[^1] == '"') || (t[0] == '\'' && t[^1] == '\'')))
            t = t[1..^1];
        return t.Trim();
    }

    /// <summary>
    /// So các dòng đang có trong Watch với biến cần chụp (bỏ qua khoảng trắng). Khớp → null;
    /// lệch → "Watch thiếu …· dư …" (dòng lặp lại cũng tính là dư).
    /// </summary>
    public static string? WatchMismatch(IReadOnlyList<string> inWatch, IReadOnlyList<string> expected)
    {
        static string Key(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var have = inWatch.Select(Key).ToList();
        var want = expected.Select(Key).ToList();
        var missing = expected.Where(e => !have.Contains(Key(e))).ToList();
        var extra = inWatch.Where((w, i) => !want.Contains(have[i]) || have.IndexOf(have[i]) != i).Distinct().ToList();
        if (missing.Count == 0 && extra.Count == 0) return null;

        var parts = new List<string>();
        if (missing.Count > 0) parts.Add("thiếu " + string.Join(", ", missing));
        if (extra.Count > 0) parts.Add("dư " + string.Join(", ", extra));
        return "Watch " + string.Join(" · ", parts) + ".";
    }

    static bool SameFile(string a, string b)
        => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
           string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Chặn cứng khi sai thao tác cơ học (chưa break / dừng nhầm dòng / không đọc được biểu thức);
    /// chỉ hỏi lại khi nghi chưa reset biến. <paramref name="tcId"/> chỉ để ghi vào thông báo.
    /// </summary>
    public static CheckResult Evaluate(
        DebugSnapshot s,
        string tcId,
        string expectedFile,
        int expectedLine,
        string watchExpr,
        LastCapture? previous)
    {
        if (!s.Available)
            return new CheckResult(CheckLevel.Block, s.Error ?? "Không đọc được trạng thái VS — bấm Refresh chọn lại instance.");

        if (!s.InBreakMode)
            return new CheckResult(CheckLevel.Block,
                "Chưa dừng ở breakpoint — F5 và để chương trình DỪNG lại rồi mới chụp.");

        if (string.IsNullOrEmpty(s.HitFile) || s.HitLine <= 0)
            return new CheckResult(CheckLevel.Block,
                "Dừng nhưng không phải do breakpoint (Break All / exception?) — không chụp.");

        if (!SameFile(s.HitFile, expectedFile) || s.HitLine != expectedLine)
            return new CheckResult(CheckLevel.Block,
                $"Dừng ở {Path.GetFileName(s.HitFile)}:{s.HitLine} — không phải dòng của {tcId} " +
                $"({Path.GetFileName(expectedFile)}:{expectedLine}).");

        bool hasExpr = !string.IsNullOrWhiteSpace(watchExpr);

        if (hasExpr && !s.ExprValid)
            return new CheckResult(CheckLevel.Block,
                $"Không đọc được giá trị \"{(s.BadExpr.Length > 0 ? s.BadExpr : watchExpr)}\" ở dòng này — sai biểu thức hoặc biến chưa vào scope.");

        var bpNote = s.BreakpointsInFile > 1 ? $" (còn {s.BreakpointsInFile} breakpoint trong file)" : "";

        if (hasExpr && previous is { } prev &&
            prev.ProcessId == s.ProcessId && prev.ProcessId != 0 &&
            !string.Equals(prev.TcId, tcId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(prev.Expr, watchExpr, StringComparison.OrdinalIgnoreCase) &&
            ValuesMatch(prev.Value, s.ExprValue))
        {
            return new CheckResult(CheckLevel.Confirm,
                $"{watchExpr} = {s.ExprValue} y hệt lần chụp {prev.TcId}, cùng phiên debug — " +
                "biến đã được gán lại chưa? Vẫn chụp?");
        }

        var valuePart = hasExpr ? $" · {watchExpr} = {s.ExprValue}" : "";

        return new CheckResult(CheckLevel.Ok,
            $"{tcId} · {Path.GetFileName(expectedFile)}:{expectedLine}{valuePart} · chụp được{bpNote}");
    }
}
