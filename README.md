# SendKey Evidence

App WinForms .NET 8 chạy nền ở khay hệ thống, dùng để **chụp bằng chứng Unit Test** cho code batch
(`nhãn:` + `goto`) đã migrate sang C#. Copy label / 「biến」 từ file test case (Excel), app tra
`mapping.csv`, đặt breakpoint đúng dòng trong Visual Studio; khi chương trình dừng thì tự điền Watch đúng biến.
App kiểm tra xong mới cho chụp.

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
Excel:  Ctrl+C label (không có ngoặc)  →  Ctrl+C từng phần 「%RC%」 (1 ô có nhiều 「」 cũng được)
        → Ctrl+Shift+G
          ├─ đã có trong mapping.csv → app đặt breakpoint cho từng dòng cần chụp, mở đúng tab,
          │                            cuộn cho dòng label nằm đầu vùng nhìn, đưa VS lên trước
          └─ chưa có → thanh nổi mở ô gõ csharpLabel / csharpVar, Enter
                       (app kiểm tra với code trước, ghi thêm dòng rồi chạy luôn)
        chương trình (đang debug sẵn) chạy tới breakpoint thì dừng
VS:     app xoá Watch cũ, thêm đúng biến của dòng đó, kiểm tra (ding = được, buzz = lỗi)
        Ctrl+Shift+S → ảnh vào clipboard → cửa sổ Excel tự nổi lại → Ctrl+V
```

- App **không bao giờ tự chạy hay khởi động lại phiên debug**. Mở tool khi chương trình đang debug sẵn; breakpoint đặt
  bằng `Ctrl+Shift+G` sẽ dừng khi chương trình chạy tới.
- Các 「」 copy sau 1 label, cho tới lúc chụp, gom thành 1 nhóm. Copy label mới, hoặc chụp đủ, là sang nhóm mới.
- Biến rơi vào nhiều dòng code thì **mỗi dòng 1 ảnh**: thanh hiện `1/2`, chụp xong cho chương trình chạy tiếp tới dòng sau
  rồi chụp tiếp: Ctrl+V ảnh vừa chụp vào Excel TRƯỚC (clipboard chỉ giữ 1 ảnh), rồi bấm F5 trong VS — đang dừng thì F5 chỉ
  chạy tiếp, không khởi động lại.
- `「goto :END_PROC」`: dừng ở dòng thực thi đầu tiên của label `END_PROC`, Watch để trống. Chưa có trong mapping thì
  chỉ phải gõ csharpLabel.
- Watch được điền bằng cách bôi đen biểu thức trong file `.cs` rồi gọi lệnh Add Watch của VS, không gõ phím. Biểu thức
  không có nguyên văn trong file thì app copy sẵn vào clipboard, bạn Ctrl+V vào Watch. Cần VS giao diện tiếng Anh
  (app tìm cửa sổ tên `Watch 1`).

Quy tắc duy nhất khi copy: **trong 「 」 là biến, ngoài ngoặc là label**. App tự xử lý chữ full-width
(`％`→`%`), ngoặc `『 』` và ô Excel có bọc nháy. Đoạn văn dài không có ngoặc thì bị bỏ qua. Một ô có nhiều 「」 thì
chỉ lấy cặp trông như biến batch (`%X%`, `!X!`, `if …`, `goto …`), nên 「0」 trong `「%RC%」が「0」` bị bỏ qua.

| Hotkey | Việc |
|---|---|
| `Ctrl+Shift+R` | Khoanh vùng chụp |
| `Ctrl+Shift+G` | Đặt breakpoint cho nhóm đang hiện trên thanh (thiếu mapping thì mở ô gõ C#) |
| `Ctrl+Shift+S` | Trong đợt: chụp bằng chứng vào clipboard. Ngoài đợt: chụp vùng đã nhớ, lưu PNG |
| `Ctrl+Shift+F` | Chụp toàn màn hình, lưu PNG |
| `Ctrl+Shift+W` | Chụp cửa sổ đang active, lưu PNG |

Phím của app là phím toàn cục: lúc app chạy, nó đè phím cùng tổ hợp của VS — `Ctrl+Shift+S` (Save All),
`Ctrl+Shift+F` (Find in Files). Đổi phím trong `settings.json` nếu cần.

Khi bấm `Ctrl+Shift+S`, app điền lại Watch (để VS tính lại giá trị) và kiểm tra trạng thái VS rồi mới chụp. Lúc chụp, app tạm ẩn thanh nổi và dời chuột
ra ngoài vùng chụp (để ảnh không dính tooltip giá trị biến), chụp xong trả chuột về chỗ cũ.

| Tình huống | Xử lý |
|---|---|
| Chưa dừng ở breakpoint, hoặc dừng không phải do breakpoint | 🚫 chặn |
| Dừng sai dòng / sai file | 🚫 chặn |
| Không đọc được biểu thức Watch | 🚫 chặn |
| Watch dư hoặc thiếu biến so với dòng đang chụp (app không tự điền được) | 🚫 chặn |
| Giá trị y hệt lần chụp trước, trong cùng phiên debug | ⚠️ bấm `Ctrl+Shift+S` lần nữa mới chụp (nghi chưa reset biến) |
| Không thấy dòng label trong editor dù đã cuộn | ⚠️ bấm `Ctrl+Shift+S` lần nữa mới chụp |

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
| 6 | Copy `CHECK_INPUT` rồi copy `「%RC%」` | Thanh hiện `CHECK_INPUT 「%RC%」 → rc · Program.cs:36`, chấm xanh dương |
| 7 | Copy `「％ＲＣ％」` | Vẫn là `%RC%`, không bị thêm lần 2 |
| 8 | Copy một đoạn văn dài không có 「 」 | Thanh không đổi gì |
| 9 | Bấm `Ctrl+Shift+S` khi chưa `Ctrl+Shift+G` | Buzz, không có ảnh |
| 10 | Bấm `Ctrl+Shift+G` | Trong file chỉ còn 1 breakpoint; VS nổi lên, tab `Program.cs`, dòng `CHECK_INPUT:` ở đầu vùng nhìn |
| 11 | Bấm `Ctrl+Shift+S` khi chưa chạy | Buzz, không có ảnh, báo `❌ Chưa dừng ở breakpoint…` |
| 12 | Cho chương trình (đang debug sẵn) chạy tới dòng `CHECK_INPUT` | Khi dừng Watch 1 chỉ còn đúng `rc` (dòng cũ bị xoá); app kêu ding; chấm xanh lá. App không tự chạy / khởi động lại debug |
| 13 | Bấm `Ctrl+Shift+S` | Ding; cửa sổ lúc copy nổi lên; Ctrl+V ra ảnh; thanh `✓ đủ 1 ảnh` |
| 14 | Copy `KHONGCO` rồi `「%RC%」` | Chấm vàng `「%RC%」 chưa có trong mapping.csv — bấm Ctrl+Shift+G…`; focus vẫn ở chỗ đang copy |
| 15 | Bấm `Ctrl+Shift+G`, gõ `NOSUCH` / `rc`, Enter | Báo đỏ `Không thấy "NOSUCH:"…`; `mapping.csv` không đổi; ô nhập vẫn mở |
| 16 | Sửa ô trái thành `END_PROC`, Enter | `mapping.csv` có thêm dòng; VS đặt breakpoint |
| 17 | Mở `mapping.csv` bằng Excel, lặp bước 16 với cặp khác | Báo đỏ `Không ghi được mapping.csv (đang mở trong Excel?)…` |
| 18 | **Công cụ khác**: File = `Program.cs` của BigSample, Line = một dòng lệnh, bấm **Toggle Breakpoint** 2 lần | Breakpoint hiện ra rồi mất đi |
| 19 | Khi đang dừng ở breakpoint: Watch = `rc`, bấm **Add Watch** | Cửa sổ Watch nổi lên, Ctrl+V ra `rc`; file `.cs` không bị sửa |
| 20 | Copy `CHECK_INPUT`, rồi `「%INPUT_FILE%」「%RC%」` (1 lần), rồi `「if "%RC%" NEQ "0"」`, bấm `Ctrl+Shift+G` | 2 breakpoint (dòng 36, 39); thanh `inputFile, rc · Program.cs:36 · 1/2` |
| 21 | Chạy tới dòng 36 → `Ctrl+Shift+S` → Ctrl+V vào Excel → F5 trong VS tới dòng 39 → `Ctrl+Shift+S` | Lần 1 Watch = `inputFile`, `rc`; lần 2 Watch chỉ còn `rc != 0`; thanh `✓ đủ 2 ảnh` |
| 22 | Copy `「goto :END_PROC」`, bấm `Ctrl+Shift+G`, cho chương trình chạy tới | Breakpoint dòng 92; khi dừng Watch trống; `Ctrl+Shift+S` chụp được |
