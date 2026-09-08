# SendKeyDemo — Mapping Mode — Design

**Ngày:** 2026-09-08
**Trạng thái:** Đã chốt, chờ implement
**Liên quan:** nối tiếp `2026-09-08-vs-sendkey-automation-design.md`

## Bối cảnh

Đội dev đang migrate batch từ `cmd` sang C# (.NET 8). Cấu trúc `cmd` gồm các
`label` và đoạn xử lý trong label; code C# migrate **giữ nguyên cấu trúc đó**
bằng `label:` + `goto` trong C#.

Khi chụp Unit Test cần: mở file test case UT, copy tên biến `cmd` (hoặc mệnh đề
trong `if`), sang tool để đặt breakpoint đúng chỗ trong code C# rồi Add Watch giá
trị. Việc scroll tìm dòng, đặt breakpoint tay, gõ lại biểu thức watch đang tốn
thời gian.

`SendKeyDemo` hiện có (xem spec trước) điều khiển Visual Studio đang chạy qua
EnvDTE: **Go To Line**, **Toggle Breakpoint** theo file+line, **Add Watch** một
biểu thức — tất cả nhập tay.

## Mục tiêu

Thêm **"mapping mode"** vào `SendKeyDemo`: người dùng paste `cmdLabel` + (tuỳ chọn)
`cmdVar`/mệnh đề từ file UT → tool tra `mapping.csv` → tự **Go To Line** tới label
C# trong file `.cs` đích → đặt **breakpoint** đúng dòng thực thi đầu label → điền
sẵn biểu thức để bấm **Add Watch** khi đã dừng ở break.

Chế độ thủ công cũ (nhập tay File / Line / Watch) **giữ nguyên**, chạy song song.

## Nguyên tắc

- **Tối ưu copy–paste.** Hai ô paste, một phím tắt `Ctrl+Enter`, không cần rời bàn phím.
- **Luôn nổi trên cùng.** Form không bị trình duyệt / Excel che khi copy qua lại.
- **Phòng ngừa chọn sai.** Tra cứu là exact-match sau chuẩn hoá nhẹ; lệch thì hiện
  danh sách để người dùng click, **không tự đoán**.
- **Không cần maintain.** Ít file, ít abstraction — bám theo spec cũ.
- **Đúng thì tin cậy.** Goto + breakpoint qua EnvDTE (chính xác theo file+line);
  chỉ Add Watch cần SendKeys.
- **Không dịch cú pháp trong tool.** Mọi bản dịch `cmd` → C# nằm sẵn trong `mapping.csv`.

## mapping.csv

- Vị trí file mẫu: `mapping.csv` ở gốc repo.
- 4 cột, có **1 dòng header**: `cmdLabel,cmdVar,csharpLabel,csharpVar`
- Ý nghĩa cột:
  - `cmdLabel` — tên label trong `cmd` (không kèm dấu `:` đầu).
  - `cmdVar` — token biến `cmd` (`%RC%`) **hoặc** nguyên mệnh đề `if` (`if "%RC%"=="0"`).
  - `csharpLabel` — tên **label C#** tương ứng (code migrate dùng `label:` + `goto`),
    **không phải** tên method.
  - `csharpVar` — định danh C# (`rc`) **hoặc** biểu thức C# đã dịch sẵn (`rc == 0`)
    — đây là chuỗi sẽ gõ vào cửa sổ Watch.
- Một `cmdLabel` có nhiều biến → nhiều dòng.
- `cmdVar` **không** unique toàn cục (`%RC%` xuất hiện ở nhiều label); chỉ unique
  trong phạm vi một `cmdLabel`. Khoá tra cứu là cặp (`cmdLabel`, `cmdVar`).
- Parser theo **RFC 4180**: field chứa `,` / `"` / xuống dòng phải bọc `"…"`, dấu
  `"` bên trong viết `""`. **Không** dùng `string.Split(',')`.
- Người soạn `mapping.csv` tự chịu trách nhiệm nội dung/định dạng; tool chỉ đọc.

Ví dụ (`mapping.csv` hiện có trong repo):

```
cmdLabel,cmdVar,csharpLabel,csharpVar
CHECK_INPUT,%INPUT_FILE%,CheckInput,inputFile
CHECK_INPUT,%RC%,CheckInput,rc
VALIDATE_DATE,%IN_DATE%,ValidateDate,inputDate
VALIDATE_DATE,%RC%,ValidateDate,rc
```

Dòng mệnh đề `if` (người dùng tự thêm khi cần), bọc quote đúng chuẩn:

```
CHECK_INPUT,"if ""%RC%""==""0""",CheckInput,rc == 0
```

## Hai chế độ thao tác

| Chế độ | Input | Hành động tool |
|---|---|---|
| **Watch biến / mệnh đề** | ô `cmdLabel` + ô `cmdVar` (có giá trị) | tra CSV → GoToLine `csharpLabel` → breakpoint dòng thực thi đầu label → điền `Watch = csharpVar`. Khi đã F5 dừng ở break, người dùng bấm **Add Watch**. |
| **Điểm dừng label** | chỉ ô `cmdLabel`, ô `cmdVar` **để trống** | tra CSV → GoToLine `csharpLabel` → breakpoint dòng thực thi đầu label. Không đụng tới Watch. |

Phân biệt: ô `cmdVar` rỗng ⇒ chế độ "điểm dừng label".

## UI

Thêm vào form hiện có (không dùng Designer, dựng bằng code như file cũ):

```
[ ComboBox instance ......................... ] [ Refresh ]
mapping.csv [ path .............................. ] [ Browse ]
target .cs  [ path .............................. ] [ Browse ]
── Mapping ───────────────────────────────────────────────
cmdLabel  [ paste ............................... ]
cmdVar    [ paste ............................... ]   (rỗng = điểm dừng label)
          [ Tra & Chạy ]   ← phím tắt Ctrl+Enter
── Thủ công ──────────────────────────────────────────────
File  [ ........................................ ] [ Browse ]
Line  [ ____ ]      Watch [ ........................ ]
[ Go To Line ]  [ Toggle Breakpoint ]  [ Add Watch ]
── Log ──────────────────────────────────────────────────
[ TextBox multiline readonly, dock fill ......... ]
[x] Luôn nổi trên cùng
```

- **Luôn nổi trên cùng:** `Form.TopMost = true` mặc định (khi khởi động), kèm
  checkbox để tắt/bật. Trạng thái checkbox lưu vào `settings.json` cùng 2 đường dẫn.
- Hai ô đường dẫn `mapping.csv` và `target .cs` **nhớ lần dùng cuối** (settings.json
  cạnh exe). Mở app lần sau tự điền lại.
- `Ctrl+Enter` ở bất kỳ ô nào trong nhóm Mapping ⇒ kích hoạt **Tra & Chạy**.
- Kết quả tra cứu ghi thẳng vào **ô thủ công cũ** (`File`, `Line`, `Watch`) — người
  dùng nhìn thấy trước khi debug, sửa tay được. Đây cũng là lớp "phòng ngừa chọn sai".
- Nút **Add Watch** vẫn là nút cũ; sau khi F5 dừng ở break, `Watch` đã có sẵn biểu
  thức → chỉ việc bấm.

## Luồng "Tra & Chạy"

1. **Đọc mapping.** Load `mapping.csv` (cache theo `LastWriteTimeUtc`; đổi thì đọc lại).
2. **Chuẩn hoá input:**
   - `cmdLabel`: trim 2 đầu, gộp nhiều khoảng trắng thành 1, bỏ dấu `:` ở đầu.
   - `cmdVar`: trim, gộp khoảng trắng, bỏ từ khoá `if` / `goto` ở đầu nếu có.
   - So khớp **không phân biệt hoa thường**.
3. **Tra cặp (`cmdLabel`, `cmdVar`):**
   - Không có dòng nào khớp `cmdLabel` → log `không thấy label "<X>" trong mapping.csv`, **dừng**.
   - Có `cmdLabel`, ô `cmdVar` **rỗng** → chế độ điểm dừng label: lấy `csharpLabel`
     từ dòng đầu tiên của label đó (mọi dòng cùng `cmdLabel` có cùng `csharpLabel`;
     nếu không, log cảnh báo và dùng dòng đầu).
   - Có `cmdLabel`, `cmdVar` có giá trị nhưng không khớp dòng nào → hiện **danh sách
     `cmdVar` thuộc label đó** (ListBox/dialog nhỏ) để người dùng click chọn; chọn
     xong chạy tiếp. Không tự đoán.
   - Khớp đúng > 1 dòng (mapping trùng) → log `mapping trùng: dòng N, M`, **dừng**.
4. **Resolve dòng breakpoint:**
   - Quét `target .cs` theo regex `^\s*<csharpLabel>\s*:` (escape `csharpLabel`).
   - Không thấy → log `không thấy label "<csharpLabel>:" trong <file>`, **dừng**.
   - Thấy > 1 → log cảnh báo + số các dòng, **dừng**.
   - Thấy đúng 1: từ dòng ngay sau đó, bỏ qua dòng trống và dòng comment (`//`, `/* */`),
     lấy dòng đầu tiên còn lại làm **dòng breakpoint**. Nếu chạm cuối file → log lỗi, dừng.
5. **Điền ô thủ công:** `File` = `target .cs`; `Line` = dòng resolve; `Watch` =
   `csharpVar` (chỉ khi ở chế độ watch).
6. **Thực thi qua EnvDTE:**
   - `GoToLine(dte, file, line)` — như hiện tại.
   - `EnsureBreakpoint(dte, file, line)` — **method mới**: nếu đã có breakpoint ở
     đúng `file`+`line` thì giữ nguyên; nếu chưa thì `Breakpoints.Add("", file, line)`.
     Không bao giờ tự xoá (khác `ToggleBreakpoint`).
7. **Log kết quả:**
   `mapping: CHECK_INPUT/%RC% → Job001.cs:145, watch "rc" — breakpoint đã sẵn sàng`
   (chế độ điểm dừng label: bỏ phần `watch ...`).
8. Người dùng **F5** debug, để chương trình **dừng ở breakpoint**, rồi bấm **Add Watch**
   (không đổi so với hiện tại).

## Bố cục code

Giữ tối giản, bám cấu trúc hiện có (`src/SendKeyDemo/`).

### `Mapping.cs` — mới, nhỏ

- `record MapRow(string CmdLabel, string CmdVar, string CsharpLabel, string CsharpVar)`
- `static List<MapRow> Load(string csvPath)` — parser CSV RFC 4180 tự viết (nhỏ:
  xử lý quote `"`, `""`, `,`, CRLF), bỏ dòng header, bỏ dòng rỗng. Dòng sai số cột
  → ném/`throw` với số dòng để caller log.
- `static MapLookup Resolve(IReadOnlyList<MapRow> rows, string cmdLabel, string? cmdVar)`
  — trả về kiểu union đơn giản (enum `Kind` + payload):
  `NotFoundLabel` / `NeedPickVar(list)` / `Duplicate(lines)` / `Ok(MapRow)` /
  `Ok(labelOnly: csharpLabel)`.
- `static LineLookup FindLabelLine(string csPath, string csharpLabel)`
  — trả về `NotFound` / `Multiple(int[] lines)` / `Ok(int line)`.

### `VsAutomation.cs` — thêm

- `static string EnsureBreakpoint(DTE dte, string file, int line)` — như §6 bước 6.
  Dùng lại vòng lặp duyệt `dte.Debugger.Breakpoints` của `ToggleBreakpoint`, chỉ bỏ
  nhánh `.Delete()`.

### `AppSettings.cs` — mới, rất nhỏ

- `record AppSettings(string? MappingPath, string? TargetCsPath, bool TopMost = true)`
- `static AppSettings Load()` / `static void Save(AppSettings)` — đọc/ghi
  `settings.json` cạnh exe qua `System.Text.Json`. Lỗi đọc → trả default rỗng.

### `MainForm.cs` — sửa

- Thêm controls: 2 ô path + Browse, 2 ô paste `cmdLabel` / `cmdVar`, nút `Tra & Chạy`,
  checkbox `Luôn nổi trên cùng`.
- `Load`: đọc `AppSettings`, điền 2 ô path, đặt `TopMost` + checkbox theo settings.
- 2 ô path `TextChanged` (hoặc sau Browse) và checkbox `CheckedChanged` → `AppSettings.Save`;
  checkbox cũng cập nhật `this.TopMost` ngay.
- `KeyDown` trên nhóm Mapping: `Ctrl+Enter` → `TraVaChay_Click`.
- `TraVaChay_Click`: thực hiện luồng §"Luồng Tra & Chạy", mọi nhánh lỗi → `Log(...)`
  và return; không throw ra ngoài.
- Nút cũ (`Go To Line` / `Toggle Breakpoint` / `Add Watch`) và handler cũ **không đổi**.

## Xử lý lỗi

| Tình huống | Xử lý |
|---|---|
| `mapping.csv` không mở được / sai số cột | log `mapping.csv lỗi tại dòng N: <lý do>`, không crash |
| `target .cs` không tồn tại | log, dừng |
| `cmdLabel` không có trong csv | log `không thấy label "<X>"`, dừng |
| `cmdVar` không khớp trong label | hiện danh sách `cmdVar` cùng label để click chọn |
| Cặp (`cmdLabel`,`cmdVar`) khớp > 1 dòng | log `mapping trùng: dòng N, M`, dừng |
| `<csharpLabel>:` không thấy trong `.cs` | log `không thấy label "<csharpLabel>:" trong <file>`, dừng |
| `<csharpLabel>:` thấy > 1 | log cảnh báo + các số dòng, dừng |
| Sau label không còn dòng thực thi | log `label "<csharpLabel>" không có dòng thực thi phía sau`, dừng |
| Chưa chọn instance VS / VS đã đóng | như hiện tại (`Run(...)` bắt COM lỗi, nhắc Refresh) |
| VS bận | `OleMessageFilter` retry ~10s (như hiện tại) |
| Add Watch khi chưa break mode | như hiện tại (log nhắc F5) |

## Test

- **xUnit** (thêm vào — hoặc file test thuần nếu chưa có project test):
  - `Mapping.Load`: parse dòng thường; dòng có field bọc quote chứa `,` và `""`;
    bỏ header; dòng rỗng; dòng thiếu cột → ném có số dòng.
  - `Mapping.Resolve`: khớp đúng; label không có; var không có → `NeedPickVar` kèm
    đúng danh sách; trùng dòng → `Duplicate`; `cmdVar` null → nhánh label-only.
  - `Mapping.FindLabelLine`: label duy nhất → đúng dòng; bỏ comment/dòng trống phía
    sau; không có → `NotFound`; 2 label trùng tên → `Multiple`.
  - Chuẩn hoá input: `" :CHECK_INPUT "` → `check_input`; `"if %RC%"` → `%rc%`
    (case-insensitive).
- **Thủ công** (bổ sung vào README checklist):
  1. Trỏ `mapping.csv` + `samples/SampleTarget/Program.cs` (thêm vài `label:` +
     biến vào sample để test), đóng/mở lại app → 2 ô path còn nguyên.
  2. Paste `CHECK_INPUT` + `%RC%`, `Ctrl+Enter` → `File/Line/Watch` tự điền, editor
     VS nhảy đúng dòng, breakpoint xuất hiện, log đúng định dạng.
  3. Paste `CHECK_INPUT`, để trống `cmdVar`, `Ctrl+Enter` → goto + breakpoint, `Watch`
     không đổi.
  4. Paste `CHECK_INPUT` + `%SAI%` → hiện danh sách var của `CHECK_INPUT` để chọn.
  5. Paste label không có trong csv → log lỗi, không có thao tác VS nào.
  6. F5 sample, dừng ở breakpoint, bấm **Add Watch** → dòng watch xuất hiện kèm giá trị.
  7. Mở trình duyệt / Excel đè lên → form vẫn nổi trên cùng. Bỏ tích `Luôn nổi trên
     cùng` → form chìm bình thường; đóng/mở lại app → trạng thái checkbox giữ nguyên.

## Ngoài phạm vi (v1 — YAGNI)

- Không "Run All" / chạy cả test case; mỗi lần một biến do người dùng paste.
- Không tích hợp chụp màn hình (đã có tool riêng, hợp nhất sau).
- Không ghi giá trị watch ra Excel.
- Không tự F5 / điều khiển tiến trình debug.
- Không hỗ trợ nhiều file `.cs` cho một `mapping.csv` (batch = 1 script → 1 file).
- Không dịch cú pháp batch → C# trong tool (nằm sẵn trong csv).
- Không cột line/offset/anchor trong csv; resolve dòng bằng cách quét `<csharpLabel>:`.
