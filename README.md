# SendKey Evidence

App WinForms .NET 8 chạy nền ở khay hệ thống, dùng để **chụp bằng chứng Unit Test** cho code batch
(`nhãn:` + `goto`) đã migrate sang C#. Copy label / 「biến」 từ file test case (Excel) là app **điều hướng** —
cuộn tới chỗ đó trong Visual Studio và bôi đen dòng; breakpoint chỉ đặt khi bạn bấm `Ctrl+Shift+G`.
Khi chương trình dừng thì tự điền Watch đúng biến. App kiểm tra xong mới cho chụp.

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
- Bấm `X` là **thoát hẳn**. Muốn app chạy nền thì bấm **▶ Khởi động** — nút đó tự thu cửa sổ
  về khay, hotkey vẫn chạy. Đang chạy nền mà muốn thoát: chuột phải icon ở khay → **Thoát**.
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
  `mapping.csv`. Để trống thì dùng **file .cs đang mở trong Visual Studio** — nhờ vậy một `mapping.csv`
  dùng được cho nhiều class, chỉ cần mở đúng file trước khi copy.
- Field có `,` hoặc `"` thì bọc trong `"…"`; dấu `"` bên trong viết thành `""`.

Nút **Kiểm tra** cạnh ô mapping.csv báo 3 số: bao nhiêu dòng **OK**, bao nhiêu **lỗi**, bao nhiêu **chưa kiểm được**.

- Trùng cặp `cmdLabel`+`cmdVar` luôn là **lỗi** — nó không phụ thuộc file nào.
- Dòng có `csharpFile`: không thấy nhãn, nhãn trùng chỗ, hay không đọc được file đều là **lỗi**.
- Dòng để trống `csharpFile` chỉ soát được với file đang mở. Không khớp ở đó là **chưa kiểm được**, không
  phải lỗi: code batch migrate chương trình nào cũng có `INIT:` / `CLEANUP:` / `END_PROC:`, nên thấy nhãn
  cùng tên trong file đang mở không chứng minh được đó đúng là nhãn của dòng mapping đang xét.

## Chụp bằng chứng

1. Trỏ **mapping.csv**, chọn instance VS. Không cần trỏ file `.cs` — app luôn dùng file đang mở trong VS.
2. Bấm **▶ Khởi động**. Cửa sổ thu về khay, trên màn hình chỉ còn thanh nổi.
3. Sắp cửa sổ VS sao cho thấy tab tên file, dòng code và cửa sổ Watch, rồi bấm `Ctrl+Shift+R` khoanh vùng chụp.
   Chỉ làm 1 lần cho cả đợt. Nếu `settings.json` có `ClipboardWidthInches` / `ClipboardHeightInches` thì hiện
   **khung cỡ cố định** (inch × 96 px) chạy theo chuột: đưa tới chỗ rồi click để chốt, Esc để huỷ.
   Như vậy ảnh nào cũng cùng một cỡ, dán vào Excel không bị méo.

Mỗi test case:

```
Excel:  Ctrl+C label (không có ngoặc)
          → app cuộn tới nhãn đó trong VS, bôi đen dòng nhãn; chấm báo tìm được mấy chỗ
          → Ctrl+C lại y hệt label đó = sang nhãn kế tiếp (nhãn trùng prefix: _aa, _aa1, _aa2…)
        Ctrl+C 「%RC%」 (1 ô có nhiều 「」 cũng được)
          → app cuộn tới dòng có biến, TRONG chính nhãn đang đứng
          → Ctrl+C lại y hệt = sang dòng kế tiếp
        Ctrl+Shift+G
          ├─ đã có trong mapping.csv → đặt breakpoint ở DÒNG ĐANG CHỌN, thêm biến đang chọn vào Watch,
          │                            mở đúng tab, cuộn cho dòng label nằm đầu vùng nhìn, đưa VS lên trước
          └─ chưa có → thanh nổi mở ô gõ csharpLabel / csharpVar, Enter
                       (app kiểm tra với code trước, ghi thêm dòng rồi đặt breakpoint luôn)
          (VS đang dừng ở BẤT KỲ đâu thì Watch điền NGAY lúc bấm G — không cần mũi tên
           vàng ở đúng dòng này; chưa dừng thì thanh ghi "chờ F5, Watch tự điền khi dừng")
        F5 (bạn tự bấm trong VS) → chương trình chạy tới breakpoint thì dừng
VS:     app xoá Watch cũ, thêm đúng biến của dòng đó, kiểm tra (ding = được, buzz = lỗi)
        Ctrl+Shift+S → ảnh vào clipboard → cửa sổ Excel tự nổi lại → Ctrl+V
```

- Copy **không bao giờ đụng tới breakpoint**, và không cướp focus khỏi Excel — bạn cứ copy tiếp ô sau.
  Chỉ `Ctrl+Shift+G` mới đặt breakpoint và đưa VS lên trước.
- Tìm biến khớp theo **tên trọn vẹn**, nên `rc` không dính `rc2` / `rcTotal` / `_rc`. Mệnh đề (vd `rc != 0`)
  thì khớp theo văn bản, bỏ qua khác biệt khoảng trắng — `if(rc!=0)` vẫn tìm ra.
- Phạm vi tìm là **class đang debug**: khi chương trình đang dừng, app lấy file có **mũi tên vàng** làm mốc,
  không phải tab đang mở. Nhờ vậy điều hướng tới một dòng pin `csharpFile` của class khác không làm mốc
  trôi theo. Chưa dừng thì mới dùng file đang mở.
- App **không bao giờ tự chạy hay khởi động lại phiên debug**. Mở tool khi chương trình đang debug sẵn; breakpoint đặt
  bằng `Ctrl+Shift+G` sẽ dừng khi chương trình chạy tới.
- **Mỗi điểm dừng chỉ Watch biến của đúng dòng đó.** 3 biến nằm ở 3 dòng = 3 ảnh, mỗi ảnh 1 biến: đi tới dòng nào thì
  bấm `Ctrl+Shift+G` ở dòng đó, bấm bao nhiêu lần thì có bấy nhiêu điểm dừng.
- Watch đổi theo **hình dạng dòng** đang chọn, không phải lúc nào cũng là `csharpVar`:

  | Dòng | Watch |
  |---|---|
  | `if (rc != 0) goto HANDLE_ERROR;` | `rc != 0` — chỉ phần trong ngoặc, có `goto` cùng dòng cũng không sao |
  | `rc = inputFile.EndsWith(".csv") ? 0 : 12;` | `rc` **và** `inputFile.EndsWith(".csv") ? 0 : 12` |
  | `Console.WriteLine($"… rc={rc}");` | `rc` |

  Dòng gán cần thêm vế phải vì breakpoint dừng **trước** khi dòng chạy, lúc đó biến còn mang giá trị cũ — chụp mỗi
  biến là ra ảnh sai giá trị. Vế phải lấy **nguyên văn** như trong file (cả dấu nháy, cả tiền tố `@` / `$`), vì app
  điền Watch bằng cách bôi đen đúng đoạn text đó trong `.cs`. Câu lệnh viết tiếp ở dòng sau (không có `;` cuối dòng)
  thì chỉ Watch mỗi biến. Vòng lặp `for` / `while` không áp luật `if`.
- Copy một label **khác** là sang nhóm mới: app quên hết chỗ đã chọn và xoá breakpoint của nhóm cũ.
- Biến rơi vào nhiều dòng code thì **mỗi dòng 1 ảnh**: thanh hiện `1/2`, chụp xong cho chương trình chạy tiếp tới dòng sau
  rồi chụp tiếp: Ctrl+V ảnh vừa chụp vào Excel TRƯỚC (clipboard chỉ giữ 1 ảnh), rồi bấm F5 trong VS — đang dừng thì F5 chỉ
  chạy tiếp, không khởi động lại.
- `「goto :END_PROC」`: dừng ở dòng thực thi đầu tiên của label `END_PROC`, Watch để trống. Chưa có trong mapping thì
  chỉ phải gõ csharpLabel.
- Mệnh đề if, vd `「if "%RC%" NEQ "0"」` (mapping sang `rc != 0`): **2 ảnh**.
  1. Dừng ở dòng `if` → chụp, ảnh thấy giá trị THẬT của mệnh đề.
  2. Chụp xong app chạy lệnh `IfSetStatement` trong `settings.json` (mặc định `SET("{var}", "{value}")`, `{var}` = tên
     biến batch bỏ `%`) với giá trị gợi ý làm mệnh đề ĐÚNG, rồi kiểm lại mệnh đề. Lỗi thì báo đỏ.
  3. Thanh nổi hiện ô giá trị, vd `RC = [1]`. Muốn giá trị khác thì click vào ô, sửa rồi Enter → app chạy lại lệnh set
     với giá trị mới và kiểm lại mệnh đề. (VS không cho thêm dòng Watch tuỳ ý bằng code, nên ô sửa nằm trên thanh.)
  4. Ctrl+V ảnh 1 vào Excel, F5 trong VS → dừng ở lệnh đầu của nhánh (cùng dòng thì theo cột `goto`) → chụp ảnh 2.

  Giá trị gợi ý: `NEQ "0"` → `1`, `NEQ "x"` → `0`, `EQU`/`==` `"x"` → `x`, `GTR n` → n+1, `LSS n` → n−1, `GEQ`/`LEQ n` → n;
  có `not` thì đảo lại. `if exist` / `if defined` / `if errorlevel` hoặc không nhận ra → báo vàng, tự set rồi F5.
- Mệnh đề **vòng lặp** (`for` / `while` / `do`): app nhận ra là vòng lặp nên **không bypass** — chỉ điều hướng + chụp
  bình thường, 1 ảnh, không sinh lệnh SET.
- Watch được điền bằng cách bôi đen biểu thức trong file `.cs` rồi gọi lệnh Add Watch của VS, không gõ phím. Biểu thức
  không có nguyên văn trong file thì app copy sẵn vào clipboard, bạn Ctrl+V vào Watch. Cần VS giao diện tiếng Anh
  (app tìm cửa sổ tên `Watch 1`).

Quy tắc duy nhất khi copy: **trong 「 」 là biến, ngoài ngoặc là label**. App tự xử lý chữ full-width
(`％`→`%`), ngoặc `『 』` và ô Excel có bọc nháy. Đoạn văn dài không có ngoặc thì bị bỏ qua. Một ô có nhiều 「」 thì
chỉ lấy cặp trông như biến batch (`%X%`, `!X!`, `if …`, `goto …`), nên 「0」 trong `「%RC%」が「0」` bị bỏ qua.

| Hotkey | Việc |
|---|---|
| `Ctrl+Shift+R` | Khoanh vùng chụp |
| `Ctrl+Shift+G` | Đặt breakpoint ở dòng đang chọn + thêm biến đang chọn vào Watch (thiếu mapping thì mở ô gõ C#) |
| `Ctrl+Shift+S` | Trong đợt: chụp bằng chứng vào clipboard. Ngoài đợt: chụp vùng đã nhớ, lưu PNG |
| `Ctrl+Shift+F` | Chụp toàn màn hình, lưu PNG |
| `Ctrl+Shift+W` | Chụp cửa sổ đang active, lưu PNG |
| `Ctrl+Shift+D` | Xóa breakpoint app đã đặt + quên nhóm đang chọn. Ngoài đợt: xóa breakpoint trong class đang debug |

Phím của app là phím toàn cục: lúc app chạy, nó đè phím cùng tổ hợp của VS — `Ctrl+Shift+S` (Save All),
`Ctrl+Shift+F` (Find in Files), `Ctrl+Shift+D` (phím mở đầu chord của menu Debug). Đổi phím trong
`settings.json` nếu cần (`CaptureRegionHotkey`, `FullScreenHotkey`, `ClearBreakpointsHotkey`, …).

Khi bấm `Ctrl+Shift+S`, app điền lại Watch (để VS tính lại giá trị) và kiểm tra trạng thái VS rồi mới chụp. Lúc chụp, app tạm ẩn thanh nổi và dời chuột
ra ngoài vùng chụp (để ảnh không dính tooltip giá trị biến), chụp xong trả chuột về chỗ cũ.

Chấm trên thanh lúc **điều hướng** (ngay sau khi copy):

| Tình huống | Xử lý |
|---|---|
| Tìm thấy đúng 1 dòng | 🟢 bấm `Ctrl+Shift+G` được |
| Tìm thấy nhiều dòng | 🟡 bấm `Ctrl+Shift+G` được (đặt ở dòng đang chọn); copy lại để sang dòng kế tiếp |
| Không tìm thấy | 🔴 chặn, kèm lý do. Riêng thiếu mapping thì `Ctrl+Shift+G` mở ô gõ C# |

Chấm lúc **chụp** (`Ctrl+Shift+S`):

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
| 1 | Mở BigSample trong VS, chạy app | Dropdown có instance VS; bấm `X` là **thoát hẳn** — tiến trình `SendKeyDemo` không còn trong Task Manager |
| 2 | Mở exe lần nữa | Cửa sổ bản đang chạy nổi lên, không có bản thứ 2 |
| 3 | Mở `Program.cs` trong VS rồi bấm **Kiểm tra** | `28 dòng — 28 OK, 0 lỗi.` (25 dòng không pin tra trong `Program.cs`, 3 dòng pin `csharpFile=Steps.cs` tra trong `Steps.cs`) |
| 3b | Mở `Steps.cs` rồi bấm **Kiểm tra** lại | `28 dòng — 3 OK, 0 lỗi, 25 chưa kiểm được…` — chỉ 3 dòng pin sẵn còn soát được. Điều cần kiểm là **0 lỗi ở cả hai lần**, số OK đổi theo file đang mở là bình thường |
| 4 | Bấm **▶ Khởi động** | Cửa sổ thu về khay (double-click icon khay mở lại được); thanh nổi hiện `Copy label…`; hộp thoại nhắc khoanh vùng |
| 5 | `Ctrl+Shift+R`, kéo chọn vùng | Log `Đã nhớ vùng chụp …` |
| 6 | Copy `CHECK_INPUT` | VS cuộn tới dòng 32, bôi đen `CHECK_INPUT:`; thanh `1 dòng · label · Program.cs:32`, chấm xanh lá. **Excel vẫn giữ focus**, VS không nhảy lên đè |
| 7 | Copy `VALIDATE` (không có trong `mapping.csv`) | Tìm trong file `.cs` đang mở ở VS: `2 dòng – đang ở 1 · label · Program.cs:38`, chấm vàng |
| 8 | Copy `VALIDATE` lần nữa | `2 dòng – đang ở 2 · label · Program.cs:44` — duyệt sang nhãn kế tiếp |
| 9 | Copy `VALIDATE` lần thứ 3 | Quay lại `đang ở 1` (chạy vòng) |
| 10 | Copy `CHECK_INPUT` rồi `「%RC%」` | `3 dòng – đang ở 1 · rc · Program.cs:34`, chấm vàng (rc rơi vào dòng 34, 35, 36) |
| 11 | Copy `「%RC%」` lại 2 lần | `đang ở 2` (dòng 35), rồi `đang ở 3` (dòng 36) |
| 12 | Copy `「%RC%」` 2 phát liên tiếp thật nhanh (lỡ tay Ctrl+C) | Thanh **không nhảy** sang dòng kế tiếp |
| 13 | Copy `「％ＲＣ％」` | Xử lý y như `%RC%` |
| 14 | Copy một đoạn văn dài không có 「 」 | Thanh không đổi gì |
| 15 | Bấm `Ctrl+Shift+S` khi chưa `Ctrl+Shift+G` | Buzz, không có ảnh |
| 16 | Đưa về dòng 34, bấm `Ctrl+Shift+G` khi chương trình CHƯA dừng | Trong file chỉ còn 1 breakpoint, ở **dòng 34**; VS nổi lên, tab `Program.cs`, dòng `CHECK_INPUT:` ở đầu vùng nhìn; thanh ghi `chờ F5, Watch tự điền khi dừng` |
| 16b | Khi đang dừng sẵn ở dòng 34, bấm `Ctrl+Shift+G` lần nữa | Watch điền **ngay** (không phải chờ F5), chấm chuyển màu theo kết quả chấm, và **không** kêu buzz |
| 16c | Vẫn đang dừng ở dòng 34, copy `「%HDR_OK%」` để sang `VALIDATE_HEADER`, bấm `Ctrl+Shift+G` | Watch đổi sang `headerOk` **ngay**, dù mũi tên vàng vẫn ở dòng 34. Thanh ghi `Watch đã điền · chờ F5 tới dòng này`, chấm **không** đỏ |
| 16d | Vẫn đang dừng trong `Program.cs`, copy `RECALC` rồi `「%AMT%」` (2 dòng này pin `csharpFile=Steps.cs`), rồi copy lại `CHECK_INPUT` | `CHECK_INPUT` vẫn tra trong **`Program.cs`** — mốc là file có mũi tên vàng, không trôi sang `Steps.cs` sau khi vừa điều hướng qua đó |
| 16e | Bấm `Ctrl+Shift+D` | Breakpoint app đặt biến hết; thanh ghi `đã xoá N điểm dừng`, chấm về xám. Bấm lần nữa → `không có điểm dừng nào để xoá`. Breakpoint bạn TỰ đặt ở file khác **không** bị xóa |
| 17 | Bấm `Ctrl+Shift+S` khi chưa chạy | Buzz, không có ảnh, báo `❌ Chưa dừng ở breakpoint…` |
| 18 | Cho chương trình (đang debug sẵn) chạy tới dòng 34 | Watch 1 chỉ còn `rc` và `inputFile.EndsWith(".csv") ? 0 : 12` (dòng cũ bị xoá) — dòng gán nên có cả vế phải; ding; chấm xanh lá. App không tự chạy / khởi động lại debug |
| 19 | Bấm `Ctrl+Shift+S` | Ding; cửa sổ lúc copy nổi lên; Ctrl+V ra ảnh; thanh `✓ đủ 1 ảnh` |
| 20 | Copy `「%INPUT_FILE%」`, bấm `Ctrl+Shift+G` | Thành 2 điểm dừng, **mỗi điểm 1 biến của test case**: dòng 33 Watch `inputFile` + `"orders.csv"`, dòng 34 Watch `rc` + vế phải. Thanh `1/2` |
| 20b | Đi tới dòng 35 (dòng log) rồi dòng 36 (dòng `if`), mỗi dòng bấm `Ctrl+Shift+G` | Dòng 35 Watch mỗi `rc`; dòng 36 Watch `rc != 0` — Watch đổi theo hình dạng dòng |
| 21 | Copy `KHONGCO` rồi `「%RC%」` | Chấm đỏ `「%RC%」 chưa có trong mapping.csv — bấm Ctrl+Shift+G…`; focus vẫn ở chỗ đang copy |
| 22 | Bấm `Ctrl+Shift+G`, gõ `NOSUCH` / `rc`, Enter | Báo đỏ `Không thấy "NOSUCH:"…`; `mapping.csv` không đổi; ô nhập vẫn mở |
| 23 | Sửa ô trái thành `END_PROC`, Enter | `mapping.csv` có thêm dòng; VS đặt breakpoint |
| 24 | Mở `mapping.csv` bằng Excel, lặp bước 23 với cặp khác | Báo đỏ `Không ghi được mapping.csv (đang mở trong Excel?)…` |
| 25 | **Công cụ khác**: File = `Program.cs` của BigSample, Line = một dòng lệnh, bấm **Toggle Breakpoint** 2 lần | Breakpoint hiện ra rồi mất đi |
| 26 | Khi đang dừng ở breakpoint: Watch = `rc`, bấm **Add Watch** | Cửa sổ Watch nổi lên, Ctrl+V ra `rc`; file `.cs` không bị sửa |
| 27 | Copy `CHECK_INPUT`, rồi `「if "%RC%" NEQ "0"」`, bấm `Ctrl+Shift+G` | 2 breakpoint: dòng 36 và cột `goto` của dòng 36; thanh `rc != 0 · Program.cs:36 · 1/2` |
| 28 | Chạy tới dòng 36 → `Ctrl+Shift+S` | Watch chỉ còn `rc != 0` (= false). Chụp xong báo đỏ `SET("RC", "1") lỗi…` vì BigSample không có hàm `SET` — xem dòng 31 |
| 29 | Copy `CHECK_INPUT`, rồi `「for %%i in (1 2) do echo %%i」`, bấm `Ctrl+Shift+G` | Chỉ **1** điểm dừng, không có ảnh 2, không có ô giá trị trên thanh; log `là vòng lặp — chỉ điều hướng + chụp, không sinh lệnh SET` |
| 30 | Copy `「goto :END_PROC」`, bấm `Ctrl+Shift+G`, cho chương trình chạy tới | Breakpoint dòng 89; khi dừng Watch trống; `Ctrl+Shift+S` chụp được |
| 31 | Thoát app, sửa `settings.json`: `"IfSetStatement": "rc = {value}"` (chỉ cho BigSample), mở lại app, làm lại dòng 27–28 | Chụp xong dòng 36 app chạy `rc = 1`, thanh `Ctrl+V rồi F5 trong VS → rc != 0 · Program.cs:36:22 · 2/2`; F5 → dừng ở `goto`, Watch `rc != 0` = true; `Ctrl+Shift+S` → `✓ đủ 2 ảnh` |
