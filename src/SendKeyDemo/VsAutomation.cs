using EnvDTE;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.RegularExpressions;
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

    /// <summary>select = bôi đen cả dòng; activate = false thì chỉ đổi tab trong VS, không raise VS đè lên Excel.</summary>
    public static string GoToLine(DTE dte, string file, int line, bool select = false, bool activate = true)
    {
        if (!File.Exists(file)) return $"file không tồn tại: {file}";
        var win = dte.ItemOperations.OpenFile(file, Constants.vsViewKindTextView);
        win.Activate();
        var sel = (TextSelection)win.Document.Selection;
        sel.GotoLine(line, select);
        var reached = sel.CurrentLine;
        if (activate) dte.MainWindow.Activate();
        return reached == line
            ? $"đã tới {Path.GetFileName(file)}:{line}"
            : $"đã tới {Path.GetFileName(file)}:{reached} (file chỉ có {reached} dòng)";
    }

    /// <summary>File .cs đang mở trong VS. Đi thẳng ActiveDocument: ActiveWindow trả null khi cửa sổ Watch đang active.</summary>
    public static string? ActiveFile(DTE dte)
    {
        try { return dte.ActiveDocument?.FullName; }
        catch { return null; }
    }

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

    public static string EnsureBreakpoint(DTE dte, string file, int line, int column = 0)
    {
        if (!File.Exists(file)) return $"file không tồn tại: {file}";
        var where = column > 0 ? $"{Path.GetFileName(file)}:{line}:{column}" : $"{Path.GetFileName(file)}:{line}";
        foreach (Breakpoint bp in dte.Debugger.Breakpoints)
        {
            if (string.Equals(bp.File, file, StringComparison.OrdinalIgnoreCase) && bp.FileLine == line &&
                (column > 0 ? bp.FileColumn == column : bp.FileColumn <= 1))
                return $"breakpoint đã có tại {where}";
        }
        try
        {
            dte.Debugger.Breakpoints.Add("", file, line, column > 0 ? column : 1);
        }
        catch (COMException)
        {
            return $"không đặt được breakpoint tại {Path.GetFileName(file)}:{line} — dòng phải là lệnh " +
                   "thực thi (không phải dòng trống / comment / khai báo), và file phải thuộc solution đang mở.";
        }
        return $"đã đặt breakpoint tại {Path.GetFileName(file)}:{line}";
    }

    public static string SetOnlyBreakpoint(DTE dte, string file, int line)
    {
        if (!File.Exists(file)) return $"file không tồn tại: {file}";
        ClearBreakpointsInFile(dte, file);
        return EnsureBreakpoint(dte, file, line);
    }

    public static DebugSnapshot ReadDebugState(DTE dte, string csFile, IReadOnlyList<string> exprs)
    {
        try
        {
            var dbg = dte.Debugger;
            bool inBreak = dbg.CurrentMode == dbgDebugMode.dbgBreakMode;

            // Breakpoint nào vừa làm chương trình dừng — chính xác hơn nhiều so với đọc vị trí con trỏ,
            // vì con trỏ có thể đã bị người dùng click đi chỗ khác.
            string hitFile = "";
            int hitLine = 0, hitColumn = 0;
            if (inBreak)
            {
                try
                {
                    var hit = dbg.BreakpointLastHit;
                    if (hit != null) { hitFile = hit.File ?? ""; hitLine = hit.FileLine; hitColumn = hit.FileColumn; }
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

            return new DebugSnapshot(true, inBreak, hitFile, hitLine, bpInFile, exprValid, exprValue, pid,
                BadExpr: badExpr, HitColumn: hitColumn);
        }
        catch (Exception ex)
        {
            return DebugSnapshot.Unavailable("Không đọc được trạng thái VS: " + ex.Message);
        }
    }

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

    public static string AddWatch(DTE dte, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return "biểu thức trống";
        if (dte.Debugger.CurrentMode != dbgDebugMode.dbgBreakMode)
            return "Chưa ở break mode — cho chương trình chạy và DỪNG lại ở breakpoint, rồi mới Add Watch.";

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


    public static string? RunIfBypass(DTE dte, string statement, string condition)
    {
        var dbg = dte.Debugger;
        if (dbg.CurrentMode != dbgDebugMode.dbgBreakMode) return $"VS không còn dừng — chưa chạy được {statement}.";
        try
        {
            var r = dbg.GetExpression(statement, true, 5000);
            if (!r.IsValidValue) return $"{statement} lỗi: {r.Value}";
            var c = dbg.GetExpression(condition, true, 2000);
            if (!c.IsValidValue) return $"Đã chạy {statement} nhưng không đọc được {condition}: {c.Value}";
            if (!string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase))
                return $"Đã chạy {statement} nhưng {condition} vẫn = {c.Value} — sửa giá trị rồi Enter.";
        }
        catch (COMException ex) { return $"{statement} lỗi: {ex.Message}"; }
        return null;
    }

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

    public static void BringToFront(DTE dte)
    {
        var hwnd = new IntPtr(dte.MainWindow.HWnd);
        if (hwnd != IntPtr.Zero) SetForegroundWindow(hwnd);
    }

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

    public static class OleMessageFilter
    {
        [DllImport("ole32.dll")]
        static extern int CoRegisterMessageFilter(IOleMessageFilter newFilter, out IOleMessageFilter oldFilter);

        public static void Register() => CoRegisterMessageFilter(new Filter(), out _);

        class Filter : IOleMessageFilter
        {
            public int HandleInComingCall(int callType, IntPtr caller, int tickCount, IntPtr info) => 0;

            public int RetryRejectedCall(IntPtr callee, int tickCount, int rejectType)
                => rejectType == 2 && tickCount < 10_000 ? 100 : -1;

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
public enum CheckLevel
{
    Ok,
    Confirm,
    Block,
}

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
    string BadExpr = "",
    int HitColumn = 0)
{
    public static DebugSnapshot Unavailable(string error) =>
        new(false, false, "", 0, 0, false, "", 0, error);
}

public record LastCapture(int ProcessId, string TcId, string Expr, string Value);

public record CheckResult(CheckLevel Level, string Message);

public static class CaptureCheck
{
    public static bool ValuesMatch(string? actual, string? expected)
        => string.Equals(Unquote(actual), Unquote(expected), StringComparison.OrdinalIgnoreCase);

    static string Unquote(string? s)
    {
        var t = (s ?? "").Trim();
        if (t.Length >= 2 && ((t[0] == '"' && t[^1] == '"') || (t[0] == '\'' && t[^1] == '\'')))
            t = t[1..^1];
        return t.Trim();
    }

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
                "Chưa dừng ở breakpoint — đợi chương trình chạy tới breakpoint rồi mới chụp.");

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
