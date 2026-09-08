# VS SendKey Automation Demo — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** App WinForms .NET 8 điều khiển Visual Studio 2022/2026 đang chạy (qua EnvDTE + SendKeys) để Go To Line, Toggle Breakpoint theo file+line, và Add Watch một biểu thức.

**Architecture:** Một form duy nhất dựng bằng code. Toàn bộ COM interop (quét Running Object Table, thao tác DTE, message filter, SendKeys) gộp trong `VsAutomation.cs`. Kèm project console `SampleTarget` để mở trong VS mà debug, và một project xUnit chỉ test hàm parse moniker (logic thuần).

**Tech Stack:** .NET 8, WinForms (`net8.0-windows`), NuGet `envdte`, xUnit, P/Invoke `ole32.dll` / `user32.dll`.

## Global Constraints

- Mọi project target `.NET 8`. `SendKeyDemo` và `SendKeyDemo.Tests` dùng `net8.0-windows`; `SampleTarget` dùng `net8.0`.
- `SendKeyDemo`: `OutputType=WinExe`, `UseWindowsForms=true`, `Nullable=enable`, `ImplicitUsings=enable`, `PlatformTarget=x64`, `NoWarn=NU1701`.
- Ưu tiên ít file / ít dòng. Không thêm interface, DI, abstraction ngoài những gì plan này liệt kê. Không tạo file `.Designer.cs`.
- Chuỗi hiển thị cho người dùng (log, nút, nhãn) bằng tiếng Việt như trong code mẫu.
- Commit sau mỗi task bằng đúng lệnh `git` ghi trong task.

---

## File Structure

| File | Trách nhiệm |
|---|---|
| `SendKeyDemo.sln` | Gom 3 project |
| `.gitignore` | Bỏ qua `bin/`, `obj/`, `.vs/` |
| `src/SendKeyDemo/SendKeyDemo.csproj` | Project app, tham chiếu `envdte` |
| `src/SendKeyDemo/Program.cs` | Entry point `[STAThread]` |
| `src/SendKeyDemo/VsAutomation.cs` | `VsInstance`, `ParseMoniker`, `FindVisualStudios`, `GoToLine`, `ToggleBreakpoint`, `AddWatch`, `OleMessageFilter` |
| `src/SendKeyDemo/MainForm.cs` | UI dựng bằng code + xử lý sự kiện |
| `samples/SampleTarget/SampleTarget.csproj` | Console net8.0 |
| `samples/SampleTarget/Program.cs` | Vòng lặp + biến cục bộ để debug |
| `tests/SendKeyDemo.Tests/SendKeyDemo.Tests.csproj` | xUnit, tham chiếu `SendKeyDemo` |
| `tests/SendKeyDemo.Tests/MonikerParseTests.cs` | Test `ParseMoniker` |
| `README.md` | Cách chạy + checklist test thủ công |

---

## Task 1: Scaffold solution, 3 project, SampleTarget chạy được

**Files:**
- Create: `SendKeyDemo.sln`, `.gitignore`
- Create: `src/SendKeyDemo/SendKeyDemo.csproj`, `src/SendKeyDemo/Program.cs`, `src/SendKeyDemo/MainForm.cs`
- Create: `samples/SampleTarget/SampleTarget.csproj`, `samples/SampleTarget/Program.cs`
- Create: `tests/SendKeyDemo.Tests/SendKeyDemo.Tests.csproj`

**Interfaces:**
- Consumes: —
- Produces: namespace `SendKeyDemo`; class `MainForm : Form` (ctor rỗng tham số); `Program.Main`.

- [ ] **Step 1: Tạo 3 project + solution + gitignore**

Chạy từ `D:\LearnCode\sendkey`:

```bash
dotnet new gitignore
dotnet new winforms -o src/SendKeyDemo -n SendKeyDemo
dotnet new console  -o samples/SampleTarget -n SampleTarget
dotnet new xunit    -o tests/SendKeyDemo.Tests -n SendKeyDemo.Tests
dotnet new sln -n SendKeyDemo
dotnet sln add src/SendKeyDemo samples/SampleTarget tests/SendKeyDemo.Tests
dotnet add tests/SendKeyDemo.Tests reference src/SendKeyDemo
dotnet add src/SendKeyDemo package envdte
```

- [ ] **Step 2: Ghi đè `src/SendKeyDemo/SendKeyDemo.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <PlatformTarget>x64</PlatformTarget>
    <NoWarn>NU1701</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="envdte" Version="17.13.40008" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Ghi đè `src/SendKeyDemo/Program.cs`**

```csharp
namespace SendKeyDemo;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
```

- [ ] **Step 4: Tạo `src/SendKeyDemo/MainForm.cs` (placeholder tối thiểu, sẽ hoàn thiện ở Task 5)**

```csharp
namespace SendKeyDemo;

public class MainForm : Form
{
    public MainForm()
    {
        Text = "VS SendKey Automation Demo";
        Width = 720;
        Height = 480;
    }
}
```

- [ ] **Step 5: Xóa file thừa do template sinh ra**

Xóa nếu tồn tại: `src/SendKeyDemo/Form1.cs`, `src/SendKeyDemo/Form1.Designer.cs`, `src/SendKeyDemo/Form1.resx`, `tests/SendKeyDemo.Tests/UnitTest1.cs`.

- [ ] **Step 6: Ghi đè `samples/SampleTarget/Program.cs`**

```csharp
Console.WriteLine("SampleTarget khởi động — F5 trong VS để debug.");

for (int i = 0; i < 100; i++)
{
    int counter = i * i;
    string label = $"iteration {i}";
    Console.WriteLine($"{label}: counter={counter}");
    Thread.Sleep(200);
}
```

- [ ] **Step 7: Build cả solution**

Run: `dotnet build SendKeyDemo.sln`
Expected: `Build succeeded`, 0 error. Cảnh báo NU1701 đã bị chặn; các cảnh báo khác không được có.

- [ ] **Step 8: Chạy thử SampleTarget**

Run: `dotnet run --project samples/SampleTarget`
Expected: in ra `SampleTarget khởi động...` rồi `iteration 0: counter=0`, `iteration 1: counter=1`, ... (Ctrl+C để dừng.)

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "chore: scaffold solution, sample target, test project"
```

---

## Task 2: `ParseMoniker` (TDD)

**Files:**
- Create: `src/SendKeyDemo/VsAutomation.cs`
- Modify: `tests/SendKeyDemo.Tests/MonikerParseTests.cs` (tạo mới, thay `UnitTest1.cs` đã xóa)

**Interfaces:**
- Consumes: —
- Produces: `public static (string Version, int ProcessId)? SendKeyDemo.VsAutomation.ParseMoniker(string? displayName)` — trả tuple khi tên khớp `!VisualStudio.DTE.<major>.<minor>:<pid>`, ngược lại `null`.

- [ ] **Step 1: Viết test thất bại — `tests/SendKeyDemo.Tests/MonikerParseTests.cs`**

```csharp
using SendKeyDemo;
using Xunit;

public class MonikerParseTests
{
    [Theory]
    [InlineData("!VisualStudio.DTE.17.0:12345", "17.0", 12345)]
    [InlineData("!VisualStudio.DTE.18.0:9", "18.0", 9)]
    public void Parses_valid_moniker(string input, string version, int pid)
    {
        var r = VsAutomation.ParseMoniker(input);
        Assert.NotNull(r);
        Assert.Equal(version, r!.Value.Version);
        Assert.Equal(pid, r.Value.ProcessId);
    }

    [Theory]
    [InlineData("!VisualStudio.DTE")]
    [InlineData("!VisualStudio.DTE.17.0")]
    [InlineData("")]
    [InlineData("random string")]
    [InlineData("!VisualStudio.DTE.17.0:notanumber")]
    public void Rejects_invalid_moniker(string input)
    {
        Assert.Null(VsAutomation.ParseMoniker(input));
    }
}
```

- [ ] **Step 2: Chạy test để xác nhận fail**

Run: `dotnet test tests/SendKeyDemo.Tests`
Expected: FAIL — lỗi biên dịch `'VsAutomation' does not exist` (chưa có type).

- [ ] **Step 3: Tạo `src/SendKeyDemo/VsAutomation.cs` với đủ hàm `ParseMoniker`**

```csharp
using System.Text.RegularExpressions;

namespace SendKeyDemo;

public static class VsAutomation
{
    public static (string Version, int ProcessId)? ParseMoniker(string? displayName)
    {
        var m = Regex.Match(displayName ?? "", @"^!VisualStudio\.DTE\.(\d+\.\d+):(\d+)$");
        return m.Success ? (m.Groups[1].Value, int.Parse(m.Groups[2].Value)) : null;
    }
}
```

- [ ] **Step 4: Chạy test để xác nhận pass**

Run: `dotnet test tests/SendKeyDemo.Tests`
Expected: PASS — 7 test passed.

- [ ] **Step 5: Commit**

```bash
git add src/SendKeyDemo/VsAutomation.cs tests/SendKeyDemo.Tests/MonikerParseTests.cs
git commit -m "feat: parse VisualStudio.DTE moniker into version + pid"
```

---

## Task 3: Quét Running Object Table + `VsInstance` + `OleMessageFilter`

**Files:**
- Modify: `src/SendKeyDemo/VsAutomation.cs`

**Interfaces:**
- Consumes: `ParseMoniker` (Task 2).
- Produces:
  - `public record VsInstance(string Version, int ProcessId, string SolutionName, EnvDTE.DTE Dte)` với `ToString()` → `"VS 17.0 — Foo.sln (pid 123)"`.
  - `public static IEnumerable<VsInstance> VsAutomation.FindVisualStudios()`.
  - `public static class VsAutomation.OleMessageFilter` với `static void Register()`.

- [ ] **Step 1: Thay toàn bộ nội dung `src/SendKeyDemo/VsAutomation.cs`**

```csharp
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
```

- [ ] **Step 2: Build + chạy lại test parse (không được hồi quy)**

Run: `dotnet build SendKeyDemo.sln && dotnet test tests/SendKeyDemo.Tests`
Expected: `Build succeeded`; 7 test vẫn PASS.

- [ ] **Step 3: Commit**

```bash
git add src/SendKeyDemo/VsAutomation.cs
git commit -m "feat: enumerate running Visual Studio instances via ROT + OLE message filter"
```

---

## Task 4: Thao tác DTE — GoToLine, ToggleBreakpoint, AddWatch

**Files:**
- Modify: `src/SendKeyDemo/VsAutomation.cs`

**Interfaces:**
- Consumes: `EnvDTE.DTE` từ `VsInstance.Dte` (Task 3).
- Produces (tất cả `public static string`, trả chuỗi trạng thái tiếng Việt để log):
  - `GoToLine(DTE dte, string file, int line)`
  - `ToggleBreakpoint(DTE dte, string file, int line)`
  - `AddWatch(DTE dte, string expression)`

- [ ] **Step 1: Thêm `using` và các method vào `VsAutomation.cs`**

Thêm vào đầu file, sau các `using` hiện có:

```csharp
using System.Text;
```

Thêm 3 method + 2 helper vào trong `public static class VsAutomation`, ngay sau `FindVisualStudios()`:

```csharp
public static string GoToLine(DTE dte, string file, int line)
{
    if (!File.Exists(file)) return $"file không tồn tại: {file}";
    var win = dte.ItemOperations.OpenFile(file, Constants.vsViewKindTextView);
    win.Activate();
    var sel = (TextSelection)dte.ActiveDocument.Selection;
    sel.GotoLine(line, false);
    dte.MainWindow.Activate();
    return $"đã tới {Path.GetFileName(file)}:{line}";
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
    SetForegroundWindow(new IntPtr(dte.MainWindow.HWnd));
    Thread.Sleep(150);
    dte.ExecuteCommand("Debug.AddWatch");
    Thread.Sleep(150);
    SendKeys.SendWait(EscapeSendKeys(expression) + "{ENTER}");
    return dte.Debugger.CurrentMode == dbgDebugMode.dbgDesignMode
        ? $"đã gửi \"{expression}\" vào Watch (VS chưa debug — giá trị sẽ hiện khi F5 và dừng ở breakpoint)"
        : $"đã gửi \"{expression}\" vào Watch";
}

static string EscapeSendKeys(string s)
{
    var sb = new StringBuilder();
    foreach (var c in s)
        sb.Append("+^%~(){}[]".Contains(c) ? "{" + c + "}" : c.ToString());
    return sb.ToString();
}

[DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
```

- [ ] **Step 2: Build**

Run: `dotnet build SendKeyDemo.sln`
Expected: `Build succeeded`, 0 error, 0 warning (ngoài NU1701 đã chặn).

- [ ] **Step 3: Commit**

```bash
git add src/SendKeyDemo/VsAutomation.cs
git commit -m "feat: DTE operations for goto-line, toggle breakpoint, add watch"
```

---

## Task 5: `MainForm` — UI đầy đủ + nối sự kiện

**Files:**
- Modify: `src/SendKeyDemo/MainForm.cs`

**Interfaces:**
- Consumes: `VsAutomation.FindVisualStudios()`, `VsAutomation.OleMessageFilter.Register()`, `VsAutomation.GoToLine/ToggleBreakpoint/AddWatch`, `VsInstance`.
- Produces: `MainForm` hoàn chỉnh (form demo dùng được).

- [ ] **Step 1: Thay toàn bộ nội dung `src/SendKeyDemo/MainForm.cs`**

```csharp
using System.Runtime.InteropServices;
using EnvDTE;

namespace SendKeyDemo;

public class MainForm : Form
{
    readonly ComboBox _instances = new() { Width = 560, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Button _refresh = new() { Text = "Refresh", AutoSize = true };
    readonly TextBox _file = new() { Dock = DockStyle.Fill };
    readonly Button _browse = new() { Text = "Browse...", AutoSize = true };
    readonly NumericUpDown _line = new() { Minimum = 1, Maximum = 1_000_000, Value = 1, Width = 100 };
    readonly TextBox _watch = new() { Dock = DockStyle.Fill };
    readonly Button _goto = new() { Text = "Go To Line", AutoSize = true };
    readonly Button _bp = new() { Text = "Toggle Breakpoint", AutoSize = true };
    readonly Button _addWatch = new() { Text = "Add Watch", AutoSize = true };
    readonly TextBox _log = new()
    {
        Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f)
    };

    public MainForm()
    {
        Text = "VS SendKey Automation Demo";
        Width = 760;
        Height = 520;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 0) };
        top.Controls.Add(_instances);
        top.Controls.Add(_refresh);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true, Padding = new Padding(8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label { Text = "File", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        grid.Controls.Add(_file, 1, 0);
        grid.Controls.Add(_browse, 2, 0);
        grid.Controls.Add(new Label { Text = "Line", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        grid.Controls.Add(_line, 1, 1);
        grid.Controls.Add(new Label { Text = "Watch", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        grid.Controls.Add(_watch, 1, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 0, 8, 8) };
        buttons.Controls.Add(_goto);
        buttons.Controls.Add(_bp);
        buttons.Controls.Add(_addWatch);

        var logHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        logHost.Controls.Add(_log);

        Controls.Add(logHost);
        Controls.Add(buttons);
        Controls.Add(grid);
        Controls.Add(top);

        _refresh.Click += (_, _) => LoadInstances();
        _browse.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Filter = "C# files (*.cs)|*.cs|All files (*.*)|*.*" };
            if (d.ShowDialog(this) == DialogResult.OK) _file.Text = d.FileName;
        };
        _goto.Click += (_, _) => Run("Go To Line", dte => VsAutomation.GoToLine(dte, _file.Text, (int)_line.Value));
        _bp.Click += (_, _) => Run("Toggle Breakpoint", dte => VsAutomation.ToggleBreakpoint(dte, _file.Text, (int)_line.Value));
        _addWatch.Click += (_, _) => Run("Add Watch", dte => VsAutomation.AddWatch(dte, _watch.Text));

        Load += (_, _) =>
        {
            VsAutomation.OleMessageFilter.Register();
            LoadInstances();
        };
    }

    void LoadInstances()
    {
        _instances.Items.Clear();
        try
        {
            foreach (var vs in VsAutomation.FindVisualStudios())
                _instances.Items.Add(vs);
        }
        catch (Exception ex)
        {
            Log("LỖI khi quét VS: " + ex.Message);
        }

        var any = _instances.Items.Count > 0;
        if (any)
            _instances.SelectedIndex = 0;
        else
            _instances.Items.Add("(không có instance VS đang chạy)");

        _goto.Enabled = _bp.Enabled = _addWatch.Enabled = any;
        Log(any ? $"Tìm thấy {_instances.Items.Count} instance VS." : "Không tìm thấy VS nào đang chạy.");
    }

    void Run(string label, Func<DTE, string> action)
    {
        if (_instances.SelectedItem is not VsInstance vs)
        {
            Log(label + ": chưa chọn instance.");
            return;
        }
        try
        {
            Log($"{label}: {action(vs.Dte)}");
        }
        catch (InvalidComObjectException)
        {
            Log($"{label}: instance đã đóng — bấm Refresh.");
        }
        catch (Exception ex)
        {
            Log($"{label} LỖI: {ex.Message}");
        }
    }

    void Log(string msg) =>
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}{Environment.NewLine}");
}
```

- [ ] **Step 2: Build**

Run: `dotnet build SendKeyDemo.sln`
Expected: `Build succeeded`, 0 error/warning.

- [ ] **Step 3: Chạy app, xác nhận form hiện**

Run: `dotnet run --project src/SendKeyDemo`
Expected: cửa sổ "VS SendKey Automation Demo" mở ra; ô log ghi 1 dòng — hoặc "Không tìm thấy VS nào đang chạy." (nếu chưa mở VS) hoặc "Tìm thấy N instance VS." Đóng cửa sổ để kết thúc.

- [ ] **Step 4: Commit**

```bash
git add src/SendKeyDemo/MainForm.cs
git commit -m "feat: full MainForm UI wired to VS automation actions"
```

---

## Task 6: README + kiểm thử thủ công với Visual Studio thật

**Files:**
- Create: `README.md`

**Interfaces:**
- Consumes: toàn bộ app.
- Produces: —

- [ ] **Step 1: Tạo `README.md`**

```markdown
# VS SendKey Automation Demo

App WinForms .NET 8 điều khiển Visual Studio 2022 / 2026 đang chạy: **Go To Line**,
**Toggle Breakpoint** theo file + line, **Add Watch** một biểu thức.
Breakpoint và goto-line đi qua EnvDTE (chính xác theo file+line); Add Watch dùng
EnvDTE mở dòng watch rồi SendKeys gõ biểu thức.

## Chạy

```
dotnet run --project src/SendKeyDemo
```

Yêu cầu: Windows, .NET 8 SDK, có Visual Studio 2022/2026 đang mở sẵn một solution.

## Cách dùng

1. Mở `samples/SampleTarget` bằng Visual Studio.
2. Chạy app demo. Chọn instance VS ở dropdown trên cùng (bấm **Refresh** nếu mở VS sau).
3. **File**: đường dẫn đầy đủ tới `samples/SampleTarget/Program.cs` (nút Browse).
4. **Line**: dòng có `int counter = i * i;`.
5. Bấm **Go To Line** / **Toggle Breakpoint** / **Add Watch**. Kết quả in ở ô log.

## Kiểm thử thủ công

| # | Thao tác | Kỳ vọng |
|---|---|---|
| 1 | Mở `samples/SampleTarget` trong VS; chạy app; xem dropdown | Có 1 dòng `VS 17.x — SampleTarget.sln (pid ...)` |
| 2 | File = `...\SampleTarget\Program.cs`, Line = dòng `int counter`, bấm **Go To Line** | Con trỏ VS nhảy tới đúng dòng đó; log `đã tới Program.cs:<n>` |
| 3 | Bấm **Toggle Breakpoint** | Chấm đỏ hiện ở dòng đó; log `đã đặt breakpoint...` |
| 4 | Bấm **Toggle Breakpoint** lần nữa | Chấm đỏ biến mất; log `đã xóa breakpoint...` |
| 5 | Đặt lại breakpoint, F5 debug `SampleTarget`, đợi dừng ở breakpoint; Watch = `counter`, bấm **Add Watch** | Dòng `counter` xuất hiện trong cửa sổ Watch kèm giá trị |
| 6 | Watch = `label`, bấm **Add Watch** khi vẫn đang dừng | Dòng `label` xuất hiện trong Watch kèm giá trị chuỗi |
| 7 | Đóng VS, bấm một nút bất kỳ | Log báo `instance đã đóng — bấm Refresh`; app không crash |

## Ghi chú

- App build x64 để khớp tiến trình 64-bit của VS.
- Nếu VS đang bận (đang build/gỡ lỗi), lời gọi tự động retry tối đa ~10 giây.
- Add Watch cần cửa sổ VS lên foreground trong ~0,3s; đừng thao tác chuột/bàn phím lúc đó.
```

- [ ] **Step 2: Xác minh build + test toàn bộ lần cuối**

Run: `dotnet build SendKeyDemo.sln && dotnet test SendKeyDemo.sln`
Expected: `Build succeeded`; `Passed! - Failed: 0` (7 test).

- [ ] **Step 3: Kiểm thử thủ công với Visual Studio**

Mở `samples/SampleTarget/SampleTarget.csproj` bằng Visual Studio 2022 hoặc 2026, rồi làm lần lượt 7 dòng trong bảng "Kiểm thử thủ công" của `README.md`. Tất cả phải đúng kỳ vọng. Ghi lại (trong phần mô tả commit hoặc PR) máy đã test trên VS phiên bản nào.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: README with usage and manual test checklist"
```

---

## Self-Review Notes

- **Spec coverage:** Go To Line → Task 4/5; Toggle Breakpoint theo file+line → Task 4/5; Add Watch (DTE + SendKeys) → Task 4/5; chọn instance qua dropdown + Refresh → Task 5; quét ROT không dùng `Marshal.GetActiveObject` → Task 3; `OleMessageFilter` retry VS bận → Task 3/5; build x64 + `NoWarn NU1701` → Task 1 (csproj) + Global Constraints; SampleTarget console → Task 1; test `ParseMoniker` (4+ case rác) → Task 2; các mục xử lý lỗi trong spec (file sai, line quá lớn do `GotoLine` tự kẹp, chưa break mode, instance đóng, COMException) → `GoToLine`/`ToggleBreakpoint`/`AddWatch`/`Run` trong Task 4–5. Không còn mục spec nào thiếu task.
- **Placeholder scan:** `MainForm.cs` ở Task 1 là bản tối thiểu có chủ đích, được thay toàn bộ ở Task 5 Step 1 (không phải placeholder treo).
- **Type consistency:** `ParseMoniker` trả `(string Version, int ProcessId)?` — dùng nhất quán ở Task 2 test và Task 3 (`p.Version`, `p.ProcessId`). `VsInstance.Dte` kiểu `EnvDTE.DTE` — khớp chữ ký `Func<DTE,string>` ở `MainForm.Run` và 3 method trong Task 4. Tên method `GoToLine` / `ToggleBreakpoint` / `AddWatch` đồng nhất giữa Task 4 và Task 5.
