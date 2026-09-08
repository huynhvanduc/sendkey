# VS SendKey Automation Demo — Design

**Ngày:** 2026-09-08
**Trạng thái:** Đã chốt, chờ implement

## Mục tiêu

App .NET 8 (WinForms) demo điều khiển Visual Studio 2022 / 2026 đang chạy từ một
tiến trình bên ngoài, làm được 3 việc:

1. **Add Watch** một biểu thức.
2. **Toggle Breakpoint** tại file + line chỉ định.
3. **Go To Line** — nhảy con trỏ editor tới line chỉ định.

## Nguyên tắc

- **Ưu tiên dễ dùng.** Một form, vài ô nhập, vài nút, một ô log.
- **Không cần maintain.** Code càng ít file / ít dòng càng tốt; hạn chế abstraction,
  không tách interface, không DI.
- **Đúng thì tin cậy.** Breakpoint và goto-line đi qua EnvDTE API (chính xác theo
  file+line). Chỉ Add Watch mới cần SendKeys hỗ trợ.

## Hướng kỹ thuật

- **EnvDTE (COM automation) làm chính**, SendKeys hỗ trợ cho Add Watch.
- NuGet `Microsoft.VisualStudio.Interop` để có type `DTE` (chạy được trên .NET 8).
- Không dùng `Marshal.GetActiveObject` (không có trong .NET Core) → tự P/Invoke
  vào Running Object Table (ROT).
- App build **x64** để khớp tiến trình 64-bit của VS2022/2026 khi marshaling COM.

## Bố cục (tối giản)

```
sendkey/
  SendKeyDemo.sln
  README.md
  src/SendKeyDemo/
    SendKeyDemo.csproj        # net8.0-windows, WinForms, x64
    Program.cs                # entry point [STAThread]
    MainForm.cs               # UI + xử lý sự kiện (code, không dùng Designer)
    VsAutomation.cs           # ROT scan + VsInstance + thao tác DTE + OleMessageFilter + SendKeys
  samples/SampleTarget/
    SampleTarget.csproj       # net8.0 console
    Program.cs                # vòng lặp + biến cục bộ để debug
  tests/SendKeyDemo.Tests/
    SendKeyDemo.Tests.csproj  # xUnit
    MonikerParseTests.cs      # test parse moniker (logic thuần)
  docs/superpowers/specs/2026-09-08-vs-sendkey-automation-design.md
```

MainForm dựng bằng code trong `MainForm.cs` (không file `.Designer.cs`) để giảm số dòng.

## Thành phần

### `VsAutomation.cs` (gộp mọi thứ liên quan COM)

- **`record VsInstance(string Version, int ProcessId, string SolutionName, DTE Dte)`**
  - `ToString()` → `"VS 17.14 — MyApp.sln (pid 12345)"` để hiện trong dropdown.
- **`static IEnumerable<VsInstance> FindVisualStudios()`**
  - `GetRunningObjectTable` → `EnumRunning` → với mỗi moniker lấy display name.
  - Lọc tên khớp regex `^!VisualStudio\.DTE\.(\d+\.\d+):(\d+)$`.
  - `ParseMoniker(string)` tách ra `(version, pid)`; tên không khớp → `null`
    (đây là hàm được unit-test).
  - Bind moniker → ép kiểu `DTE`; đọc `dte.Solution.FullName` lấy tên solution
    (rỗng nếu chưa mở solution).
- **Thao tác (mỗi cái là 1 method static nhận `DTE` + tham số):**
  - `GoToLine(DTE dte, string file, int line)`
    → `dte.ItemOperations.OpenFile(file)`; `(TextSelection)dte.ActiveDocument.Selection`
    → `sel.GotoLine(line, false)`; `dte.ActiveWindow.Activate()`.
  - `ToggleBreakpoint(DTE dte, string file, int line)`
    → duyệt `dte.Debugger.Breakpoints`, nếu có bp cùng `File` + `FileLine` thì
    `.Delete()`, ngược lại `dte.Debugger.Breakpoints.Add("", file, line)`.
  - `AddWatch(DTE dte, string expression)`
    → `SetForegroundWindow((IntPtr)dte.MainWindow.HWnd)`; `Thread.Sleep(150)`;
    `dte.ExecuteCommand("Debug.AddWatch")`; `SendKeys.SendWait(Escape(expression) + "{ENTER}")`.
- **`OleMessageFilter`** — `IOleMessageFilter` + `CoRegisterMessageFilter`.
  `HandleInComingCall` → `SERVERCALL_ISHANDLED`; `RetryRejectedCall` → nếu
  `RPC_E_CALL_REJECTED`/`SERVERCALL_RETRYLATER` và đã đợi < 10s thì trả `100` (retry
  sau 100ms), hết thì `-1` (hủy). `Register()` gọi 1 lần khi form load.

### `MainForm.cs`

Layout dọc, `AutoSize` panel:

```
[ ComboBox instance ........................ ] [ Refresh ]
File     [ TextBox path .................... ] [ Browse ]
Line     [ NumericUpDown  1..1_000_000 ]
Watch    [ TextBox expression ............................ ]
[ Go To Line ] [ Toggle Breakpoint ] [ Add Watch ]
[ TextBox log, multiline, readonly, dock fill ]
status: <label>
```

- `Load` / `Refresh_Click` → `FindVisualStudios().ToList()` đổ vào combo; rỗng →
  thêm item `"(không có instance)"`, disable 3 nút hành động.
- Mỗi nút: lấy `VsInstance` đang chọn, gọi method tương ứng trong `try/catch`,
  `Log(...)` kết quả hoặc `Log("LỖI: " + ex.Message)`. Chạy thẳng trên UI thread (STA).
- `Log(string)` → append `HH:mm:ss  msg\r\n` vào ô log, scroll xuống cuối.

### `samples/SampleTarget/Program.cs`

```csharp
for (int i = 0; i < 100; i++)
{
    int counter = i * i;
    string label = $"iteration {i}";
    Console.WriteLine($"{label}: {counter}");
    Thread.Sleep(200);
}
```

Đủ để đặt breakpoint trong vòng lặp, watch `counter` / `label`.

## Xử lý lỗi

| Tình huống | Xử lý |
|---|---|
| Không có VS | Combo `(không có instance)`, disable nút hành động |
| VS bận (`RPC_E_CALL_REJECTED` / `RETRYLATER`) | `OleMessageFilter` retry ~10s rồi báo lỗi |
| File không thuộc solution / path sai | DTE ném → catch, ghi log, không crash |
| Line > số dòng | `GotoLine` tự kẹp; ghi cảnh báo |
| Add Watch khi chưa break mode | Vẫn thêm dòng watch (giá trị "not available"); log nhắc chạy khi đang debug |
| Instance bị đóng giữa chừng | Bắt `InvalidComObjectException` → xóa khỏi combo, nhắc Refresh |
| `COMException` khác | Log HRESULT + message |

## Test

- **xUnit — `MonikerParseTests`:** chỉ test `ParseMoniker`:
  - `"!VisualStudio.DTE.17.0:12345"` → `("17.0", 12345)`
  - `"!VisualStudio.DTE.18.0:9"` → `("18.0", 9)`
  - `"!VisualStudio.DTE"` , `""` , `"random"` → `null`
- **Thủ công (README checklist):**
  1. Mở `samples/SampleTarget` bằng VS2022 → chạy `SendKeyDemo`.
  2. Combo hiện instance → chọn.
  3. File = `...\SampleTarget\Program.cs`, Line = dòng có `int counter = i * i;`
     → **Go To Line**: editor nhảy đúng dòng.
  4. **Toggle Breakpoint** tại line đó: chấm đỏ hiện ra; bấm lại → mất.
  5. Đặt lại breakpoint, F5 debug `SampleTarget`, dừng ở break → Watch = `counter`
     → **Add Watch**: dòng `counter` xuất hiện trong Watch window kèm giá trị.

## Ngoài phạm vi (YAGNI)

- Không có file kịch bản JSON / "Run All".
- Không hỗ trợ VS Code, không hỗ trợ attach theo tên process.
- Không toggle được breakpoint điều kiện / tracepoint.
- Không đóng gói installer; chạy trực tiếp bằng `dotnet run` hoặc từ VS.
