using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
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
        return list;
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

            // rejectType: 1 = SERVERCALL_REJECTED, 2 = SERVERCALL_RETRYLATER
            public int RetryRejectedCall(IntPtr callee, int tickCount, int rejectType)
                => (rejectType is 1 or 2) && tickCount < 10_000 ? 100 : -1;

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
