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

## Ghi chú

- App build x64 để khớp tiến trình 64-bit của VS.
- Nếu VS đang bận (đang build/gỡ lỗi), lời gọi tự động retry tối đa ~10 giây.
- Add Watch cần cửa sổ VS lên foreground trong ~0,3s; đừng thao tác chuột/bàn phím lúc đó.
- Nếu Visual Studio chạy với quyền Administrator mà app demo thì không, Windows (UIPI) sẽ chặn `SetForegroundWindow` và `SendKeys` — Add Watch sẽ "im lặng không tác dụng". Chạy cả hai cùng mức quyền.
