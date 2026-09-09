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
4. **Line**: `5` (dòng `int counter = i * i;`). Dòng `string label` là `6`.
5. Bấm **Go To Line** / **Toggle Breakpoint** / **Add Watch**. Kết quả in ở ô log.

> **Add Watch chỉ chạy khi VS đang ở break mode** (đã F5 và DỪNG tại breakpoint).
> Ngoài break mode, lệnh `Debug.AddWatch` của VS không tồn tại — app sẽ báo nhắc.
> **Toggle Breakpoint** phải trỏ vào dòng có lệnh thực thi (không phải dòng trống /
> comment / `using` / `{`), và file phải thuộc solution đang mở trong VS.

## Mapping mode (tra `mapping.csv` → tự đặt breakpoint)

Dùng khi chụp Unit Test cho code batch đã migrate sang C# (giữ `nhãn:` + `goto`).

1. **mapping.csv** — 4 cột, 1 dòng header: `cmdLabel,cmdVar,csharpLabel,csharpVar`.
   - `cmdVar`: token biến batch (`%RC%`) hoặc nguyên mệnh đề `if`.
   - `csharpVar`: định danh C# hoặc biểu thức C# đã dịch sẵn (cho mệnh đề `if`).
   - Field chứa `,` hoặc `"` phải bọc `"…"`, dấu `"` bên trong viết `""` (RFC 4180).
   - Đặt nhãn C# trên **dòng riêng**; breakpoint sẽ nằm ở dòng thực thi kế tiếp.
2. Trỏ ô **mapping.csv** và **target .cs** (nút Browse). App nhớ 2 đường dẫn cho lần sau.
3. Paste **cmdLabel** + **cmdVar** từ file test case UT. Để trống **cmdVar** = chỉ đặt
   điểm dừng ở đầu label (không add watch).
4. Bấm **Tra & Chạy** (hoặc `Ctrl+Enter` khi con trỏ ở ô cmdLabel/cmdVar). Tool tra CSV,
   nhảy tới label trong `.cs`, đặt breakpoint, và điền sẵn **Line** + **Watch**.
5. **F5** trong VS, để chương trình **dừng ở breakpoint**, rồi bấm **Add Watch**.

Không khớp `cmdVar` → tool hiện danh sách biến của label đó để chọn (không tự đoán),
kèm nút **+ Thêm biến mới…**. Không thấy `cmdLabel` → tool mở form nhập dòng mapping
mới (2 ô `cmd` điền sẵn, gõ `csharpLabel` + `csharpVar`). Tool kiểm `csharpLabel:` có
trong file `.cs` rồi mới ghi thêm 1 dòng vào `mapping.csv` và chạy tiếp. Nếu `mapping.csv`
đang mở trong Excel thì không ghi được — đóng Excel rồi thử lại (tool vẫn chạy tiếp lần này).
Checkbox **Luôn nổi trên cùng** giữ form không bị trình duyệt / Excel che.

Tiện ích thêm:
- **Kiểm tra mapping.csv** — soát toàn bộ dòng: cặp `cmdLabel+cmdVar` trùng, và `csharpLabel`
  không tra được trong file `.cs` đích (thiếu / trùng / sau label không có lệnh). Kết quả in ở log.
- **Chỉ tra (không cần VS)** — checkbox: Tra & Chạy chỉ điền `File`/`Line`/`Watch` + log vị trí,
  không cần chọn instance VS, không goto/breakpoint. Dùng để soạn trước hoặc đối chiếu mapping.
- **Xóa BP file này** — xóa mọi breakpoint trong file đang ở ô `File` (dọn giữa các test case;
  `Tra & Chạy` chỉ thêm breakpoint, không tự xóa).
- **Copy Watch** — copy biểu thức ở ô `Watch` vào clipboard để tự `Ctrl+V` vào cửa sổ Watch
  (dùng khi `Add Watch` qua SendKeys bị chặn: VS chạy admin, mất foreground…).

## Kiểm thử thủ công

| # | Thao tác | Kỳ vọng |
|---|---|---|
| 1 | Mở `samples/SampleTarget` trong VS; chạy app; xem dropdown | Có 1 dòng `VS 17.x — SampleTarget.sln (pid ...)` |
| 2 | File = `...\SampleTarget\Program.cs`, Line = `5`, bấm **Go To Line** | Con trỏ VS nhảy tới dòng 5 (`int counter = i * i;`); log `đã tới Program.cs:5` |
| 3 | Bấm **Toggle Breakpoint** (Line vẫn `5`) | Chấm đỏ hiện ở dòng 5; log `đã đặt breakpoint tại Program.cs:5` |
| 4 | Bấm **Toggle Breakpoint** lần nữa | Chấm đỏ biến mất; log `đã xóa breakpoint tại Program.cs:5` |
| 5 | Đặt lại breakpoint ở dòng 5, **F5** debug `SampleTarget`, đợi chương trình **dừng** ở dòng 5; Watch = `counter`, bấm **Add Watch** | Dòng `counter` xuất hiện trong cửa sổ Watch kèm giá trị |
| 6 | Watch = `label`, bấm **Add Watch** khi vẫn đang dừng | Dòng `label` xuất hiện trong Watch kèm giá trị chuỗi |
| 7 | Bấm **Add Watch** khi KHÔNG ở break mode | Log `Chưa ở break mode — F5 chạy chương trình...`; không có lỗi thô |
| 8 | Đóng VS, bấm một nút bất kỳ | Log `... instance đã đóng — bấm Refresh`; app không crash |
| 9 | Mapping mode: trỏ `mapping.csv` + `samples\SampleTarget\Program.cs`; đóng/mở lại app | 2 đường dẫn còn nguyên; form nổi trên cùng |
| 10 | Paste `CHECK_INPUT` + `%RC%`, bấm **Tra & Chạy** (hoặc Ctrl+Enter) | `File`/`Line`/`Watch` tự điền (`Watch=rc`); VS nhảy tới dòng `rc = inputExists ? 0 : 1;`; chấm đỏ hiện; log `mapping: CHECK_INPUT/%RC% → Program.cs:<n>, watch "rc" …` |
| 11 | Để trống `cmdVar`, cmdLabel = `VALIDATE_DATE`, **Tra & Chạy** | goto + breakpoint ở dòng `inDate = "2026-09-08";`; `Watch` không đổi; log `… breakpoint sẵn sàng` |
| 12 | cmdLabel = `CHECK_INPUT`, cmdVar = `%SAI%`, **Tra & Chạy** | Hiện danh sách `%INPUT_FILE%`, `%RC%`, `if "%RC%" NEQ "0"` để chọn |
| 13 | cmdLabel = `KHONGCO`, cmdVar = `%RC%`, **Tra & Chạy** → ở form "Thêm mapping mới" gõ `csharpLabel = NOSUCH`, `csharpVar = rc` → OK | Form báo đỏ `không thấy "NOSUCH:"`; `mapping.csv` không đổi |
| 14 | Sửa `csharpLabel = END_PROC` (giữ `csharpVar = rc`) → OK | `mapping.csv` có thêm dòng `KHONGCO,%RC%,END_PROC,rc`; log `ĐÃ THÊM mapping…`; VS goto + breakpoint ở `END_PROC`; `Watch = rc` |
| 15 | Sau bước 10: **F5** debug `SampleTarget`, đợi dừng ở breakpoint, bấm **Add Watch** | Dòng `rc` xuất hiện trong cửa sổ Watch kèm giá trị `0` |

## Ghi chú

- App build x64 để khớp tiến trình 64-bit của VS.
- Nếu VS đang bận (đang build/gỡ lỗi), lời gọi tự động retry tối đa ~10 giây.
- Add Watch cần cửa sổ VS lên foreground trong ~0,3s; đừng thao tác chuột/bàn phím lúc đó.
- Nếu Visual Studio chạy với quyền Administrator mà app demo thì không, Windows (UIPI) sẽ chặn `SetForegroundWindow` và `SendKeys` — Add Watch sẽ "im lặng không tác dụng". Chạy cả hai cùng mức quyền.
