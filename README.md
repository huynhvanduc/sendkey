# SendKey Evidence

App WinForms .NET 8 chạy nền ở khay hệ thống, dùng để **chụp bằng chứng Unit Test** cho code batch
(`nhãn:` + `goto`) đã migrate sang C#. Copy label / 「biến」 từ file test case (Excel), app tra
`mapping.csv`, đặt breakpoint đúng dòng trong Visual Studio. Sau khi F5, app kiểm tra xong mới cho chụp.

## Chạy

```
dotnet run --project src/SendKeyDemo
```

Yêu cầu: Windows, .NET SDK 8 trở lên, Visual Studio 2022/2026 đang mở solution cần debug.

Bản 1 file cho team (không cần cài .NET), ra `publish\SendKeyDemo.exe`:

```
dotnet publish src/SendKeyDemo/SendKeyDemo.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

- App chỉ chạy 1 bản. Mở lần nữa thì cửa sổ bản đang chạy nổi lên.
- Bấm `X` là thu về tray. Muốn thoát hẳn: chuột phải icon → **Thoát**.
- Cấu hình (hotkey, `SaveFolder`, `ClipboardWidthInches`/`ClipboardHeightInches` = cỡ ảnh khi dán vào Excel)
  nằm trong `settings.json` cạnh exe, lần đầu chạy tự tạo.

## mapping.csv

Mỗi máy một file riêng. 1 dòng header, mỗi dòng 4 hoặc 5 cột:

```
cmdLabel,cmdVar,csharpLabel,csharpVar[,csharpFile]
```

- `cmdVar`: biến batch (`%RC%`) hoặc nguyên mệnh đề `if "%RC%" NEQ "0"`.
- `csharpVar`:
  - tên biến (`rc`): breakpoint ở dòng thực thi đầu tiên sau `csharpLabel:`
  - biểu thức (`rc != 0`): breakpoint ở dòng đầu tiên trong label có chứa biểu thức đó
- `csharpFile` (tùy chọn): file `.cs` riêng cho dòng đó, đường dẫn tuyệt đối hoặc tương đối theo thư mục của
  `mapping.csv`. Để trống thì dùng ô **target .cs**.
- Field có `,` hoặc `"` thì bọc trong `"…"`; dấu `"` bên trong viết thành `""`.

Nút **Kiểm tra** cạnh ô mapping.csv soát cặp bị trùng và label không tìm thấy trong code.

## Chụp bằng chứng

1. Trỏ **mapping.csv** + **target .cs**, chọn instance VS.
2. Bấm **▶ Bắt đầu chụp bằng chứng**. Cửa sổ thu về tray, chỉ còn thanh nổi trên màn hình.
3. Sắp cửa sổ VS sao cho thấy tab tên file, dòng code và cửa sổ Watch, rồi bấm `Ctrl+Shift+R` khoanh vùng chụp.
   Chỉ làm 1 lần cho cả đợt. Nếu `settings.json` có `ClipboardWidthInches` / `ClipboardHeightInches` thì hiện
   **khung cỡ cố định** (inch × 96 px) chạy theo chuột: đưa tới chỗ rồi click để chốt, Esc để huỷ.
   Như vậy ảnh nào cũng cùng một cỡ, dán vào Excel không bị méo.

Mỗi test case:

```
Excel:  Ctrl+C label (không có ngoặc)  →  Ctrl+C phần 「%RC%」
        ├─ đã có trong mapping.csv → Ctrl+Shift+G
        └─ chưa có → gõ csharpLabel / csharpVar vào 2 ô tô vàng rồi Enter
                     (app kiểm tra với code trước, xong mới ghi thêm dòng)
        → app xoá breakpoint cũ trong file, đặt 1 breakpoint, nhảy tới dòng, copy biểu thức Watch
VS:     F5, Ctrl+V vào Watch → chương trình dừng → app tự kiểm tra (ding = được, buzz = lỗi)
        Ctrl+Shift+S → ảnh vào clipboard → Ctrl+V dán vào tài liệu
```

Quy tắc duy nhất khi copy: **trong 「 」 là biến, ngoài ngoặc là label**. App tự xử lý chữ full-width
(`％`→`%`), ngoặc `『 』` và ô Excel có bọc nháy. Đoạn văn dài không có ngoặc thì bị bỏ qua.

| Hotkey | Việc |
|---|---|
| `Ctrl+Shift+R` | Khoanh vùng chụp |
| `Ctrl+Shift+G` | Đặt breakpoint cho cặp đang hiện trên thanh |
| `Ctrl+Shift+S` | Trong đợt: chụp bằng chứng vào clipboard. Ngoài đợt: chụp vùng đã nhớ, lưu PNG |
| `Ctrl+Shift+F` | Chụp toàn màn hình, lưu PNG |
| `Ctrl+Shift+W` | Chụp cửa sổ đang active, lưu PNG |

Khi bấm `Ctrl+Shift+S`, app kiểm tra trạng thái VS trước rồi mới chụp. Lúc chụp, app tạm ẩn thanh nổi và dời chuột
ra ngoài vùng chụp (để ảnh không dính tooltip giá trị biến), chụp xong trả chuột về chỗ cũ.

| Tình huống | Xử lý |
|---|---|
| Chưa dừng ở breakpoint, hoặc dừng không phải do breakpoint | 🚫 chặn |
| Dừng sai dòng / sai file | 🚫 chặn |
| Không đọc được biểu thức Watch | 🚫 chặn |
| Giá trị y hệt lần chụp trước, trong cùng phiên debug | ⚠️ hỏi lại (nghi chưa reset biến) |

## Công cụ khác

Bấm **▸ Công cụ khác** để mở ra:
- ô **File** / **Line** / **Watch**
- nút **Go To Line**, **Toggle Breakpoint**, **Xóa BP file này**
- nút **Add Watch**: chỉ chạy khi chương trình đang dừng. App copy biểu thức và mở cửa sổ Watch, bạn tự Ctrl+V.
- nút **Copy Watch**

App không bao giờ tự gõ phím vào VS, để khỏi gõ nhầm vào file `.cs`.

## Mẫu để thử: samples/BigSample

1. Mở `samples/BigSample/BigSample.csproj` trong VS.
2. Trỏ app vào `samples/BigSample/mapping.csv` và `samples/BigSample/Program.cs`.
3. Mở `samples/BigSample/tc-input-jp.txt` (test case kiểu Excel tiếng Nhật) rồi copy theo hướng dẫn trong file.

## Kiểm thử thủ công (cần VS thật)

| # | Thao tác | Kỳ vọng |
|---|---|---|
| 1 | Mở BigSample trong VS, chạy app | Dropdown có instance VS; bấm `X` thì về tray, double-click icon tray mở lại |
| 2 | Mở exe lần nữa | Cửa sổ bản đang chạy nổi lên, không có bản thứ 2 |
| 3 | Bấm **Kiểm tra** | `Kiểm tra mapping.csv: OK — 17 dòng, không thấy vấn đề.` |
| 4 | Bấm **▶ Bắt đầu chụp bằng chứng** | Về tray; thanh nổi hiện `Copy label…`; hộp thoại nhắc khoanh vùng |
| 5 | `Ctrl+Shift+R`, kéo chọn vùng | Log `Đã nhớ vùng chụp …` |
| 6 | Copy `CHECK_INPUT` rồi copy `「%RC%」` | Hàng cmd hiện cặp (không có ngoặc); hàng C# tự điền `CHECK_INPUT` / `rc` |
| 7 | Copy `「％ＲＣ％」` | Vẫn ra `%RC%` |
| 8 | Copy một đoạn văn dài không có 「 」 | Thanh không đổi gì |
| 9 | Bấm `Ctrl+Shift+S` khi chưa `Ctrl+Shift+G` | Buzz, không có ảnh |
| 10 | Bấm `Ctrl+Shift+G` | Trong file chỉ còn 1 breakpoint; VS nhảy tới dòng; clipboard chứa `rc` |
| 11 | Bấm `Ctrl+Shift+S` khi chưa F5 | Buzz, không có ảnh, báo `❌ Chưa dừng ở breakpoint…` |
| 12 | F5, đợi chương trình dừng | App tự kêu ding; thanh xanh `✅ … rc = … · chụp được` |
| 13 | Bấm `Ctrl+Shift+S` | Ding; Ctrl+V vào Word/Excel ra ảnh; thanh đếm `1 ảnh` |
| 14 | Copy `KHONGCO` rồi `「%RC%」` | 2 ô C# trống, tô vàng |
| 15 | Gõ `NOSUCH` / `rc` vào 2 ô, Enter | Báo đỏ `Không thấy "NOSUCH:"…`; `mapping.csv` không đổi |
| 16 | Sửa ô trái thành `END_PROC`, Enter | Log `ĐÃ THÊM mapping…`; `mapping.csv` có thêm dòng; VS đặt breakpoint |
| 17 | Mở `mapping.csv` bằng Excel, lặp bước 16 với cặp khác | Báo đỏ `Không ghi được mapping.csv (đang mở trong Excel?)…` |
| 18 | **Công cụ khác**: File = `Program.cs` của BigSample, Line = một dòng lệnh, bấm **Toggle Breakpoint** 2 lần | Breakpoint hiện ra rồi mất đi |
| 19 | Khi đang dừng ở breakpoint: Watch = `rc`, bấm **Add Watch** | Cửa sổ Watch nổi lên, Ctrl+V ra `rc`; file `.cs` không bị sửa |
