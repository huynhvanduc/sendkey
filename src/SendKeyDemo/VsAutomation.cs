using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using EnvDTE;

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
        dte.Debugger.Breakpoints.Add("", file, line);
        return $"đã đặt breakpoint tại {Path.GetFileName(file)}:{line}";
    }

    public static string AddWatch(DTE dte, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return "biểu thức trống";
        if (dte.Debugger.CurrentMode == dbgDebugMode.dbgDesignMode)
            return "VS chưa debug — F5 và dừng ở breakpoint rồi Add Watch.";
        SetForegroundWindow(new IntPtr(dte.MainWindow.HWnd));
        System.Threading.Thread.Sleep(150);
        dte.ExecuteCommand("Debug.AddWatch");
        System.Threading.Thread.Sleep(150);
        SendKeys.SendWait(EscapeSendKeys(expression) + "{ENTER}");
        return $"đã gửi \"{expression}\" vào Watch";
    }

    static string EscapeSendKeys(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
            sb.Append("+^%~(){}[]".Contains(c) ? "{" + c + "}" : c.ToString());
        return sb.ToString();
    }

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

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
