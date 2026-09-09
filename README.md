# VS SendKey Automation Demo

App WinForms .NET 8 chạy nền ở khay hệ thống, điều khiển Visual Studio 2022 / 2026 đang chạy:
**Go To Line**, **Toggle Breakpoint** theo file + line, **Add Watch** một biểu thức, và
**chụp bằng chứng Unit Test có gác cổng** (xem [Chụp bằng chứng](#chụp-bằng-chứng-gộp-quickshot-vào-app)
— gộp từ QuickShot: hotkey toàn cục + chụp vùng đã nhớ).
Breakpoint và goto-line đi qua EnvDTE (chính xác theo file+line). **Add Watch** copy
biểu thức vào clipboard và mở cửa sổ Watch của VS — bạn bấm **Ctrl+V** rồi Enter
(không gõ tự động: `SendKeys` gõ mù có thể rơi vào editor và sửa nhầm file `.cs`).

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
> Ngoài break mode app sẽ báo nhắc. Khi ở break mode: app copy biểu thức + đưa
> cửa sổ Watch lên, bạn bấm **Ctrl+V** + Enter vào ô biểu thức trống của Watch.
> **Toggle Breakpoint** phải trỏ vào dòng có lệnh thực thi (không phải dòng trống /
> comment / `using` / `{`), và file phải thuộc solution đang mở trong VS.

## Mapping mode (tra `mapping.csv` → tự đặt breakpoint)

Dùng khi chụp Unit Test cho code batch đã migrate sang C# (giữ `nhãn:` + `goto`).

1. **mapping.csv** — 4 hoặc 5 cột, 1 dòng header: `cmdLabel,cmdVar,csharpLabel,csharpVar[,csharpFile]`.
   - `cmdVar`: token biến batch (`%RC%`) hoặc nguyên mệnh đề `if`.
   - `csharpVar`: định danh C# **hoặc** biểu thức C# đã dịch sẵn (cho mệnh đề `if`).
     Nếu là biểu thức (có toán tử / khoảng trắng, vd `rc != 0`), tool quét từ dòng đầu
     label xuống, đặt breakpoint ở **dòng đầu tiên chứa biểu thức đó** (vd dòng
     `if (rc != 0) goto …`) thay vì dòng đầu label. Định danh thuần → dòng đầu label như cũ.
   - `csharpFile` (tùy chọn): đường dẫn file `.cs` cho riêng dòng đó — tuyệt đối, hoặc
     tương đối theo thư mục chứa `mapping.csv`. Trống = dùng ô **target .cs**. Dùng khi
     code migrate nằm rải nhiều file. Trộn dòng 4 cột và 5 cột trong cùng file được.
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
- **Copy Watch** — chỉ copy biểu thức ở ô `Watch` vào clipboard (không cần VS, không kiểm
  break mode). `Add Watch` = Copy Watch + kiểm break mode + đưa cửa sổ Watch lên.
- **Batch…** — mở form dán nhiều dòng, mỗi dòng `cmdLabel <Tab> cmdVar` (hoặc ≥2 dấu cách;
  trống `cmdVar` = chỉ đặt breakpoint ở label). Dòng trống hoặc bắt đầu bằng `#` bị bỏ qua.
  Bấm **Chạy** → tra từng dòng, đặt breakpoint cho mọi dòng hợp lệ (goto dòng đầu tiên),
  in bảng `[OK]/[LỖI]` + tổng kết. Tôn trọng checkbox **Chỉ tra** (khi bật: chỉ liệt kê vị trí,
  không đụng VS). Không popup chọn biến / thêm mapping giữa chừng — dòng nào hỏng thì báo lỗi và bỏ qua.
- **Gần đây ▾** — menu 20 lần Tra & Chạy gần nhất (`cmdLabel | cmdVar`); chọn để điền lại
  nhanh. Lưu trong `settings.json`.
- **Mở** (cạnh Browse) — mở `mapping.csv` / file `.cs` bằng ứng dụng mặc định của Windows.

### Fixture để thử: `samples/BigSample`

Project console lớn hơn `SampleTarget`: ~14 label + goto trong `Program.cs`, thêm 3 label
trong `Steps.cs`, chạy `Thread.Sleep(120s)` ở cuối để kịp thao tác. Kèm `samples/BigSample/mapping.csv`
(17 dòng, **trộn 4 cột và 5 cột** — các dòng `Steps.cs` dùng cột `csharpFile`).
Mở `samples/BigSample/BigSample.csproj` trong VS, trỏ app vào 2 file đó để thử mapping mode /
Batch / Kiểm tra mapping.csv. (Test `BigSampleFixtureTests` đảm bảo mapping.csv luôn khớp code.)

`samples/BigSample/cmd-input.txt` — khối `cmdLabel <Tab> cmdVar` dựng sẵn (có chú thích `#`,
vài dòng lỗi cố ý): mở, copy, dán thẳng vào ô **Batch**.

## Chụp bằng chứng (gộp QuickShot vào app)

Chế độ chạy chính khi làm evidence Unit Test. App **chạy nền ở khay hệ thống**; bấm `X` chỉ thu
về tray (hotkey vẫn sống), thoát hẳn qua chuột phải icon tray → **Thoát**.

### Hotkey (sửa trong `settings.json`, cạnh `.exe`)

| Phím tắt | Chức năng |
|---|---|
| `Ctrl+Shift+R` | Kéo chuột khoanh **vùng chụp**, nhớ lại để dùng cho cả đợt |
| `Ctrl+Shift+G` | Tới test case đang chọn: xóa breakpoint cũ trong file → đặt đúng 1 cái → copy biểu thức Watch |
| `Ctrl+Shift+S` | **Chụp bằng chứng** (đang trong đợt) hoặc chụp vùng đã lưu (ngoài đợt) |
| `Ctrl+Shift+F` | Chụp toàn màn hình |
| `Ctrl+Shift+W` | Chụp cửa sổ đang active |

### Chuẩn bị 1 lần cho cả đợt

1. Trỏ `mapping.csv` + `target .cs` (app nhớ từ lần trước).
2. Bấm **Chụp bằng chứng…** → dán danh sách, mỗi dòng:
   `TC-id ⇥ cmdLabel ⇥ cmdVar ⇥ kỳ vọng` (⇥ = Tab hoặc ≥2 dấu cách).
   - Bỏ trống `cmdVar` = chỉ đặt breakpoint ở label, không so giá trị.
   - Bỏ trống `kỳ vọng` = không so giá trị, chỉ kiểm dừng đúng dòng.
   - Dòng chỉ có 1 cột = `cmdLabel`, TC-id tự đánh số. Dòng trống / `#` bị bỏ qua.
3. **Bắt đầu đợt chụp** → cửa sổ thu về tray, còn lại **thanh mỏng** luôn nổi trên cùng (kéo được).
4. Sắp VS thấy **cả dòng code lẫn cửa sổ Watch** → `Ctrl+Shift+R` khoanh vùng. Chỉ làm một lần.

### Vòng lặp mỗi test case

```
thanh mỏng:  ▶ TC-017 · CHECK_INPUT/%RC% · Program.cs:19 · F5 → dừng → Ctrl+V vào Watch

Ctrl+Shift+G   xóa BP cũ, đặt BP, goto, copy "rc" vào clipboard
F5  +  Ctrl+V  trong VS: chạy, dán biểu thức vào cửa sổ Watch
     ↓ chương trình dừng → app TỰ chấm (bám DebuggerEvents.OnEnterBreakMode) → ding/buzz
thanh mỏng:  ✅ TC-017 · Program.cs:19 · rc = 0 (kỳ vọng 0) · chụp được
Ctrl+Shift+S   chụp vùng → vào Clipboard → đánh dấu ✓ → tự sang TC-018
Ctrl+V         dán vào tài liệu bằng chứng
```

Ảnh bằng chứng **chỉ vào Clipboard, không lưu file** (dán thẳng vào tài liệu). Các hotkey chụp
thường (`F`/`W`/ngoài đợt) vẫn lưu PNG vào `SaveFolder` như QuickShot cũ.

### Gác cổng — cái chặn ảnh sai

`Ctrl+Shift+S` chấm trạng thái VS **trước khi** chụp. Đỏ thì buzz và **không tạo ảnh nào**:

| Tình huống | Xử lý |
|---|---|
| Chưa ở break mode | 🚫 chặn |
| Dừng nhưng không do breakpoint (Break All / exception) | 🚫 chặn |
| Dừng sai dòng / sai file so với test case | 🚫 chặn |
| Biểu thức không evaluate được (sai tên, chưa vào scope) | 🚫 chặn |
| Giá trị ≠ kỳ vọng | ⚠️ hỏi lại (có ca cố tình chụp lỗi) |
| Giá trị y hệt lần chụp trước, **cùng phiên debug** | ⚠️ hỏi lại — nghi chưa reset biến |
| Dòng breakpoint không nhắc tới biến | ⚠️ ghi cảnh báo vào log |

Đối chiếu dòng dùng `Debugger.BreakpointLastHit` (breakpoint nào thật sự làm dừng), không phải vị
trí con trỏ — nên người dùng click lung tung trong editor cũng không đánh lừa được.

Worklist + vị trí đang làm dở được lưu vào `settings.json`: đóng app mở lại, dán đúng danh sách cũ
là chạy tiếp từ chỗ dừng.

### Dán ảnh vào Excel đúng cỡ sẵn

Điền `ClipboardWidthInches` / `ClipboardHeightInches` (inch, khớp đơn vị Format Picture của Excel)
trong `settings.json` → mọi ảnh vào clipboard đều đúng cỡ đó, khỏi kéo tay từng cái. `0` = tắt.

## Kiểm thử thủ công

| # | Thao tác | Kỳ vọng |
|---|---|---|
| 1 | Mở `samples/SampleTarget` trong VS; chạy app; xem dropdown | Có 1 dòng `VS 17.x — SampleTarget.sln (pid ...)` |
| 2 | File = `...\SampleTarget\Program.cs`, Line = `5`, bấm **Go To Line** | Con trỏ VS nhảy tới dòng 5 (`int counter = i * i;`); log `đã tới Program.cs:5` |
| 3 | Bấm **Toggle Breakpoint** (Line vẫn `5`) | Chấm đỏ hiện ở dòng 5; log `đã đặt breakpoint tại Program.cs:5` |
| 4 | Bấm **Toggle Breakpoint** lần nữa | Chấm đỏ biến mất; log `đã xóa breakpoint tại Program.cs:5` |
| 5 | Đặt lại breakpoint ở dòng 5, **F5** debug `SampleTarget`, đợi chương trình **dừng** ở dòng 5; Watch = `counter`, bấm **Add Watch**, rồi **Ctrl+V** + Enter vào ô Watch | Log `đã copy "counter" + mở cửa sổ Watch…`; cửa sổ Watch nổi lên; sau Ctrl+V dòng `counter` xuất hiện kèm giá trị. **File `.cs` KHÔNG bị sửa** |
| 6 | Watch = `label`, bấm **Add Watch** khi vẫn đang dừng, Ctrl+V | Dòng `label` xuất hiện trong Watch kèm giá trị chuỗi |
| 7 | Bấm **Add Watch** khi KHÔNG ở break mode | Log `Chưa ở break mode — F5 chạy chương trình...`; không clipboard, không thao tác VS |
| 8 | Đóng VS, bấm một nút bất kỳ | Log `... instance đã đóng — bấm Refresh`; app không crash |
| 9 | Mapping mode: trỏ `mapping.csv` + `samples\SampleTarget\Program.cs`; đóng/mở lại app | 2 đường dẫn còn nguyên; form nổi trên cùng |
| 10 | Paste `CHECK_INPUT` + `%RC%`, bấm **Tra & Chạy** (hoặc Ctrl+Enter) | `File`/`Line`/`Watch` tự điền (`Watch=rc`); VS nhảy tới dòng `rc = inputExists ? 0 : 1;`; chấm đỏ hiện; log `mapping: CHECK_INPUT/%RC% → Program.cs:<n>, watch "rc" …` |
| 11 | Để trống `cmdVar`, cmdLabel = `VALIDATE_DATE`, **Tra & Chạy** | goto + breakpoint ở dòng `inDate = "2026-09-08";`; `Watch` không đổi; log `… breakpoint sẵn sàng` |
| 12 | cmdLabel = `CHECK_INPUT`, cmdVar = `%SAI%`, **Tra & Chạy** | Hiện danh sách `%INPUT_FILE%`, `%RC%`, `if "%RC%" NEQ "0"` để chọn |
| 13 | cmdLabel = `KHONGCO`, cmdVar = `%RC%`, **Tra & Chạy** → ở form "Thêm mapping mới" gõ `csharpLabel = NOSUCH`, `csharpVar = rc` → OK | Form báo đỏ `không thấy "NOSUCH:"`; `mapping.csv` không đổi |
| 14 | Sửa `csharpLabel = END_PROC` (giữ `csharpVar = rc`) → OK | `mapping.csv` có thêm dòng `KHONGCO,%RC%,END_PROC,rc`; log `ĐÃ THÊM mapping…`; VS goto + breakpoint ở `END_PROC`; `Watch = rc` |
| 15 | Sau bước 10: **F5** debug `SampleTarget`, đợi dừng ở breakpoint, bấm **Add Watch**, Ctrl+V | Dòng `rc` xuất hiện trong cửa sổ Watch kèm giá trị `0` |
| 16 | Bấm **Batch…**, dán 3 dòng: `CHECK_INPUT<Tab>%RC%` / `VALIDATE_DATE` / `KHONGCO<Tab>%RC%`, bấm **Chạy** | Bảng: 2 `[OK]` (Program.cs:19, :24) + 1 `[LỖI] … không thấy label`; 2 chấm đỏ trong VS; tổng kết `2 OK, 1 lỗi / 3 dòng` |
| 17 | Bật **Chỉ tra**, lặp lại bước 16 | Cùng bảng nhưng không đặt breakpoint; tổng kết có `(Chỉ tra …)` |

### Chụp bằng chứng

| # | Thao tác | Kỳ vọng |
|---|---|---|
| 18 | Chạy app; xem khay hệ thống; bấm `X` trên cửa sổ | Có icon tray; cửa sổ biến mất nhưng app còn sống (balloon "Vẫn đang chạy"); double-click tray mở lại |
| 19 | Bấm **Chụp bằng chứng…**, dán `TC-01⇥CHECK_INPUT⇥%RC%⇥0` và `TC-02⇥VALIDATE_DATE`, bấm **Bắt đầu đợt chụp** | Cửa sổ thu về tray; thanh mỏng hiện `▶ TC-01 · CHECK_INPUT/%RC% · Program.cs:19`; hộp thoại nhắc khoanh vùng |
| 20 | Sắp VS thấy code + Watch, bấm `Ctrl+Shift+R`, kéo chọn vùng | Log `Đã nhớ vùng chụp …`; thanh mỏng không đổi |
| 21 | Bấm `Ctrl+Shift+S` khi **chưa** F5 | **Buzz**, KHÔNG có ảnh vào clipboard; thanh mỏng đỏ `❌ Chưa dừng ở breakpoint…` |
| 22 | Bấm `Ctrl+Shift+G`, rồi F5 trong VS, đợi dừng | Chỉ còn 1 chấm đỏ trong file; khi dừng app **tự** kêu ding, thanh mỏng xanh `✅ TC-01 · Program.cs:19 · rc = 0 (kỳ vọng 0) · chụp được` |
| 23 | Bấm `Ctrl+Shift+S` | Ding; ảnh bay về góc; `Ctrl+V` vào Word/Excel ra ảnh; thanh mỏng `✓ TC-01 đã chụp … ▶ tiếp: TC-02`; đếm `1/2`. **Không** có file PNG mới trong `SaveFolder` |
| 24 | Sửa worklist thành kỳ vọng sai (`TC-01⇥CHECK_INPUT⇥%RC%⇥9`), lặp bước 22–23 | Thanh mỏng vàng `⚠ … LỆCH`; bấm chụp → hộp thoại hỏi Yes/No, mặc định No; chọn No thì không có ảnh |
| 25 | Đóng app, mở lại, dán đúng worklist cũ, **Bắt đầu** | Đếm giữ nguyên số đã chụp, con trỏ về đúng test case đang làm dở |

## Ghi chú

- App build x64 để khớp tiến trình 64-bit của VS.
- Nếu VS đang bận (đang build/gỡ lỗi), lời gọi tự động retry tối đa ~10 giây.
- Add Watch không tự gõ nữa (tránh sửa nhầm file `.cs`) — nó copy + mở cửa sổ Watch, bạn Ctrl+V.
