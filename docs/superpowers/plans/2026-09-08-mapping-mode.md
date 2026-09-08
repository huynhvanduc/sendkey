# SendKeyDemo Mapping Mode — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Thêm "mapping mode" vào app `SendKeyDemo`: paste `cmdLabel` + (tuỳ chọn) `cmdVar`/mệnh đề `if` từ file UT → tra `mapping.csv` → tự Go To Line tới label C# trong file `.cs` đích → đặt breakpoint ở dòng thực thi đầu label → điền sẵn biểu thức Watch. Form luôn nổi trên cùng, nhớ đường dẫn lần cuối. Chế độ thủ công cũ giữ nguyên.

**Architecture:** Logic thuần (đọc CSV RFC 4180, chuẩn hoá input, tra cứu, quét label trong file `.cs`) gom vào `Mapping.cs` — không đụng COM, unit-test được bằng xUnit. Cấu hình (2 đường dẫn + TopMost) trong `AppSettings.cs` đọc/ghi `settings.json` cạnh exe. `MainForm.cs` thêm controls + handler `TraVaChay()` nối các mảnh đó với `VsAutomation` (thêm `EnsureBreakpoint`). Không tự F5 / không điều khiển debug / không chụp màn hình.

**Tech Stack:** .NET 8, WinForms (`net8.0-windows`), NuGet `envdte`, xUnit, `System.Text.Json`.

## Global Constraints

- 3 project: `src/SendKeyDemo` (`net8.0-windows`, `OutputType=WinExe`, `UseWindowsForms=true`, `Nullable=enable`, `ImplicitUsings=enable`, `PlatformTarget=x64`, `NoWarn=NU1701`), `samples/SampleTarget` (`net8.0`, console), `tests/SendKeyDemo.Tests` (`net8.0-windows`, xUnit, `PlatformTarget=x64`, `NoWarn=NU1701`).
- Ưu tiên ít file / ít dòng. Không thêm interface, DI, abstraction ngoài những gì plan này liệt kê. Không tạo file `.Designer.cs`.
- Chuỗi hiển thị cho người dùng (log, nút, nhãn) bằng tiếng Việt.
- `mapping.csv`: đúng 4 cột `cmdLabel,cmdVar,csharpLabel,csharpVar`, có 1 dòng header. Parser theo RFC 4180 — **không** `string.Split(',')`.
- Chuẩn hoá khi so khớp: trim, gộp `\s+` thành 1 space, bỏ `:` đầu label, bỏ `if`/`goto` đầu ô var, so sánh **`ToLowerInvariant`** (không phân biệt hoa thường).
- `csharpLabel` là **label C#** (`nhãn:` + `goto`), **không phải** tên method.
- Commit sau mỗi task bằng đúng lệnh `git` ghi trong task. Mọi commit message kết thúc bằng 2 dòng trailer:
  ```
  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01QzrWo5zB9YtQaSrYEkmBn5
  ```
- Không tự F5, không `Debugger` step, không ghi Excel, không chụp màn hình, không "Run All". 1 `mapping.csv` ⇄ 1 file `.cs`.

---

## File Structure

| File | Trách nhiệm |
|---|---|
| `src/SendKeyDemo/Mapping.cs` | **Mới.** `MapRow`, `MappingFormatException`, `LookupKind`/`LookupResult`, `LabelLineKind`/`LabelLineResult`; `Mapping.ParseCsv`, `Load`, `NormalizeLabel`, `NormalizeVar`, `Resolve`, `FindLabelLineInText`, `FindLabelLine`. Thuần, không COM. |
| `src/SendKeyDemo/AppSettings.cs` | **Mới.** `record AppSettings` + `Load()/Load(path)`, `Save()/Save(path)` qua `System.Text.Json`. |
| `src/SendKeyDemo/VsAutomation.cs` | **Sửa.** Thêm `EnsureBreakpoint(DTE, file, line)` — chỉ thêm breakpoint nếu chưa có, không xoá. |
| `src/SendKeyDemo/MainForm.cs` | **Sửa.** Thêm controls mapping mode + `settings` load/save + `TopMost` + `TraVaChay()` + `GetMapRows()` + `PickFromList()`. Nút/handler cũ không đổi. |
| `src/SendKeyDemo/Program.cs` | Không đổi. |
| `samples/SampleTarget/Program.cs` | **Sửa.** Chuyển sang cấu trúc `nhãn:` + `goto` + biến cục bộ khớp `mapping.csv`. |
| `mapping.csv` | **Sửa.** Căn lại các dòng cho khớp `SampleTarget` sau khi đổi. |
| `tests/SendKeyDemo.Tests/SendKeyDemo.Tests.csproj` | **Mới.** xUnit, tham chiếu `SendKeyDemo`. |
| `tests/SendKeyDemo.Tests/MappingTests.cs` | **Mới.** Test cho `Mapping.*`. |
| `tests/SendKeyDemo.Tests/AppSettingsTests.cs` | **Mới.** Test cho `AppSettings`. |
| `SendKeyDemo.sln` | **Sửa** (qua `dotnet sln add`). |
| `README.md` | **Sửa.** Mục "Mapping mode" + checklist thủ công. |

---

## Task 1: Thêm project test xUnit

**Files:**
- Create: `tests/SendKeyDemo.Tests/SendKeyDemo.Tests.csproj`
- Modify: `SendKeyDemo.sln` (qua `dotnet sln add`)

**Interfaces:**
- Consumes: —
- Produces: project test build được, `dotnet test` chạy xanh.

- [ ] **Step 1: Sinh project test**

Chạy từ `D:\LearnCode\sendkey`:

```bash
dotnet new xunit -o tests/SendKeyDemo.Tests -n SendKeyDemo.Tests
dotnet sln SendKeyDemo.sln add tests/SendKeyDemo.Tests/SendKeyDemo.Tests.csproj
```

- [ ] **Step 2: Ghi đè `tests/SendKeyDemo.Tests/SendKeyDemo.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWindowsForms>true</UseWindowsForms>
    <PlatformTarget>x64</PlatformTarget>
    <IsPackable>false</IsPackable>
    <NoWarn>NU1701</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\SendKeyDemo\SendKeyDemo.csproj" />
  </ItemGroup>

</Project>
```

Giữ nguyên file `tests/SendKeyDemo.Tests/UnitTest1.cs` do template sinh (1 test rỗng, pass) — Task 2 sẽ xoá.

- [ ] **Step 3: Chạy test**

Run: `dotnet test SendKeyDemo.sln`
Expected: `Passed! - Failed: 0, Passed: 1` (test rỗng `UnitTest1.Test1` của template). Build cả solution `succeeded`.

- [ ] **Step 4: Commit**

```bash
git add tests/SendKeyDemo.Tests SendKeyDemo.sln
git commit -m "$(cat <<'EOF'
test: add xUnit project for mapping-mode logic

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01QzrWo5zB9YtQaSrYEkmBn5
EOF
)"
```

---

## Task 2: `Mapping.ParseCsv` — parser RFC 4180

**Files:**
- Create: `src/SendKeyDemo/Mapping.cs`
- Create: `tests/SendKeyDemo.Tests/MappingTests.cs`
- Delete: `tests/SendKeyDemo.Tests/UnitTest1.cs`

**Interfaces:**
- Consumes: —
- Produces: `public static List<string[]> SendKeyDemo.Mapping.ParseCsv(string text)` — tách CSV RFC 4180 (field bọc `"…"`, `""` → `"`, ngăn bằng `,`, dòng bằng `\n`/`\r\n`) thành list các dòng, mỗi dòng là mảng field theo đúng thứ tự. Dòng trống trả về `["".]` (1 field rỗng).

- [ ] **Step 1: Xoá test template, viết test thất bại**

Xoá `tests/SendKeyDemo.Tests/UnitTest1.cs`.

Tạo `tests/SendKeyDemo.Tests/MappingTests.cs`:

```csharp
using SendKeyDemo;
using Xunit;

namespace SendKeyDemo.Tests;

public class MappingTests
{
    [Fact]
    public void ParseCsv_plain_rows()
    {
        var r = Mapping.ParseCsv("a,b,c\n1,2,3\n");
        Assert.Equal(2, r.Count);
        Assert.Equal(new[] { "a", "b", "c" }, r[0]);
        Assert.Equal(new[] { "1", "2", "3" }, r[1]);
    }

    [Fact]
    public void ParseCsv_quoted_field_with_comma_and_escaped_quote()
    {
        var r = Mapping.ParseCsv("x,\"if \"\"%RC%\"\"==\"\"0\"\",y\n");
        Assert.Single(r);
        Assert.Equal(new[] { "x", "if \"%RC%\"==\"0\"", "y" }, r[0]);
    }

    [Fact]
    public void ParseCsv_handles_crlf_and_missing_final_newline()
    {
        var r = Mapping.ParseCsv("a,b\r\n1,2");
        Assert.Equal(2, r.Count);
        Assert.Equal(new[] { "1", "2" }, r[1]);
    }

    [Fact]
    public void ParseCsv_blank_line_yields_single_empty_field()
    {
        var r = Mapping.ParseCsv("a\n\nb\n");
        Assert.Equal(3, r.Count);
        Assert.Equal(new[] { "" }, r[1]);
    }
}
```

- [ ] **Step 2: Chạy test để xác nhận fail**

Run: `dotnet test SendKeyDemo.sln`
Expected: FAIL — build lỗi `'Mapping' does not exist` (chưa có `Mapping.cs`).

- [ ] **Step 3: Tạo `src/SendKeyDemo/Mapping.cs`**

```csharp
using System.Text;

namespace SendKeyDemo;

public static class Mapping
{
    /// <summary>Tách CSV theo RFC 4180. Không dùng Split(',').</summary>
    public static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                field.Append(c); i++; continue;
            }

            if (c == '"') { inQuotes = true; i++; continue; }
            if (c == ',') { row.Add(field.ToString()); field.Clear(); i++; continue; }
            if (c == '\r') { i++; continue; }
            if (c == '\n')
            {
                row.Add(field.ToString()); field.Clear();
                rows.Add(row.ToArray()); row = new List<string>();
                i++; continue;
            }
            field.Append(c); i++;
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }
        return rows;
    }
}
```

- [ ] **Step 4: Chạy test để xác nhận pass**

Run: `dotnet test SendKeyDemo.sln`
Expected: `Passed: 4` (4 test `ParseCsv_*`).

- [ ] **Step 5: Commit**

```bash
git add src/SendKeyDemo/Mapping.cs tests/SendKeyDemo.Tests
git commit -m "$(cat <<'EOF'
feat: RFC 4180 CSV parser for mapping file

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01QzrWo5zB9YtQaSrYEkmBn5
EOF
)"
```

---

## Task 3: `Mapping.Load` — `MapRow` + `MappingFormatException`

**Files:**
- Modify: `src/SendKeyDemo/Mapping.cs`
- Modify: `tests/SendKeyDemo.Tests/MappingTests.cs`

**Interfaces:**
- Consumes: `Mapping.ParseCsv` (Task 2).
- Produces:
  - `public record MapRow(string CmdLabel, string CmdVar, string CsharpLabel, string CsharpVar, int SourceLine)` — `SourceLine` là số dòng 1-based trong file CSV (header = dòng 1).
  - `public class MappingFormatException : Exception` với `public int LineNumber { get; }`.
  - `public static List<MapRow> Mapping.Load(string csvPath)` — đọc file, bỏ dòng header (dòng đầu) và dòng trống; mỗi dòng dữ liệu phải đúng 4 field (đã `Trim()`), sai thì ném `MappingFormatException(lineNo, …)`.

- [ ] **Step 1: Thêm test thất bại vào `MappingTests.cs`**

Thêm `using System.IO;` ở đầu file nếu chưa có, rồi thêm vào class `MappingTests`:

```csharp
    [Fact]
    public void Load_skips_header_and_blank_lines_and_sets_source_line()
    {
        var p = Path.GetTempFileName();
        File.WriteAllText(p,
            "cmdLabel,cmdVar,csharpLabel,csharpVar\n" +
            "CHECK_INPUT,%RC%,CHECK_INPUT,rc\n" +
            "\n" +
            "VALIDATE_DATE,%IN_DATE%,VALIDATE_DATE,inDate\n");
        var rows = Mapping.Load(p);
        File.Delete(p);

        Assert.Equal(2, rows.Count);
        Assert.Equal("CHECK_INPUT", rows[0].CmdLabel);
        Assert.Equal("%RC%", rows[0].CmdVar);
        Assert.Equal(2, rows[0].SourceLine);
        Assert.Equal("VALIDATE_DATE", rows[1].CmdLabel);
        Assert.Equal(4, rows[1].SourceLine);
    }

    [Fact]
    public void Load_throws_with_line_number_on_wrong_column_count()
    {
        var p = Path.GetTempFileName();
        File.WriteAllText(p,
            "cmdLabel,cmdVar,csharpLabel,csharpVar\n" +
            "CHECK_INPUT,%RC%,CHECK_INPUT\n");
        var ex = Assert.Throws<MappingFormatException>(() => Mapping.Load(p));
        File.Delete(p);
        Assert.Equal(2, ex.LineNumber);
    }

    [Fact]
    public void Load_trims_each_field()
    {
        var p = Path.GetTempFileName();
        File.WriteAllText(p,
            "cmdLabel,cmdVar,csharpLabel,csharpVar\n" +
            " CHECK_INPUT , %RC% , CHECK_INPUT , rc \n");
        var rows = Mapping.Load(p);
        File.Delete(p);
        Assert.Equal("CHECK_INPUT", rows[0].CmdLabel);
        Assert.Equal("rc", rows[0].CsharpVar);
    }
```

- [ ] **Step 2: Chạy test để xác nhận fail**

Run: `dotnet test SendKeyDemo.sln`
Expected: FAIL — build lỗi `MapRow` / `MappingFormatException` / `Mapping.Load` chưa tồn tại.

- [ ] **Step 3: Thêm code vào `src/SendKeyDemo/Mapping.cs`**

Thêm `using System.IO;` ở đầu file. Thêm ở cấp namespace (ngoài `class Mapping`):

```csharp
public record MapRow(string CmdLabel, string CmdVar, string CsharpLabel, string CsharpVar, int SourceLine);

public class MappingFormatException : Exception
{
    public int LineNumber { get; }
    public MappingFormatException(int lineNumber, string reason)
        : base($"mapping.csv dòng {lineNumber}: {reason}") => LineNumber = lineNumber;
}
```

Thêm vào trong `public static class Mapping`:

```csharp
    public static List<MapRow> Load(string csvPath)
    {
        var raw = ParseCsv(File.ReadAllText(csvPath));
        var result = new List<MapRow>();
        for (int i = 0; i < raw.Count; i++)
        {
            int lineNo = i + 1;                       // 1-based, header = 1
            if (i == 0) continue;                     // bỏ header
            var f = raw[i];
            if (f.Length == 1 && f[0].Trim().Length == 0) continue;   // dòng trống
            if (f.Length != 4)
                throw new MappingFormatException(lineNo, $"cần 4 cột, thấy {f.Length}");
            result.Add(new MapRow(f[0].Trim(), f[1].Trim(), f[2].Trim(), f[3].Trim(), lineNo));
        }
        return result;
    }
```

- [ ] **Step 4: Chạy test để xác nhận pass**

Run: `dotnet test SendKeyDemo.sln`
Expected: `Passed: 7` (4 cũ + 3 mới).

- [ ] **Step 5: Commit**

```bash
git add src/SendKeyDemo/Mapping.cs tests/SendKeyDemo.Tests/MappingTests.cs
git commit -m "$(cat <<'EOF'
feat: load mapping.csv into MapRow with source line numbers

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01QzrWo5zB9YtQaSrYEkmBn5
EOF
)"
```

---

## Task 4: `Mapping.NormalizeLabel` / `NormalizeVar`

**Files:**
- Modify: `src/SendKeyDemo/Mapping.cs`
- Modify: `tests/SendKeyDemo.Tests/MappingTests.cs`

**Interfaces:**
- Consumes: —
- Produces:
  - `public static string Mapping.NormalizeLabel(string raw)` — trim, gộp `\s+`→` `, bỏ `:` ở đầu, `ToLowerInvariant`.
  - `public static string Mapping.NormalizeVar(string raw)` — trim, gộp `\s+`→` `, bỏ tiền tố `if `/`goto ` (không phân biệt hoa thường), `ToLowerInvariant`.

- [ ] **Step 1: Thêm test thất bại vào `MappingTests.cs`**

```csharp
    [Theory]
    [InlineData(" :CHECK_INPUT ", "check_input")]
    [InlineData("check_input", "check_input")]
    [InlineData("CHECK   INPUT", "check input")]
    [InlineData(": CHECK_INPUT", "check_input")]
    public void NormalizeLabel_cases(string raw, string expected)
        => Assert.Equal(expected, Mapping.NormalizeLabel(raw));

    [Theory]
    [InlineData("%RC%", "%rc%")]
    [InlineData("  if  %RC%  ", "%rc%")]
    [InlineData("goto END_PROC", "end_proc")]
    [InlineData("IF \"%RC%\"==\"0\"", "\"%rc%\"==\"0\"")]
    public void NormalizeVar_cases(string raw, string expected)
        => Assert.Equal(expected, Mapping.NormalizeVar(raw));
```

- [ ] **Step 2: Chạy test để xác nhận fail**

Run: `dotnet test SendKeyDemo.sln`
Expected: FAIL — `Mapping.NormalizeLabel` / `NormalizeVar` chưa tồn tại.

- [ ] **Step 3: Thêm code vào `src/SendKeyDemo/Mapping.cs`**

Thêm `using System.Text.RegularExpressions;` ở đầu file. Thêm vào trong `public static class Mapping`:

```csharp
    static readonly Regex _ws = new(@"\s+");

    public static string NormalizeLabel(string raw)
        => _ws.Replace((raw ?? "").Trim(), " ").TrimStart(':').Trim().ToLowerInvariant();

    public static string NormalizeVar(string raw)
    {
        var s = _ws.Replace((raw ?? "").Trim(), " ");
        s = Regex.Replace(s, @"^(if|goto)\s+", "", RegexOptions.IgnoreCase);
        return s.Trim().ToLowerInvariant();
    }
```

- [ ] **Step 4: Chạy test để xác nhận pass**

Run: `dotnet test SendKeyDemo.sln`
Expected: `Passed: 15` (7 + 8 case mới từ 2 `[Theory]`).

- [ ] **Step 5: Commit**

```bash
git add src/SendKeyDemo/Mapping.cs tests/SendKeyDemo.Tests/MappingTests.cs
git commit -m "$(cat <<'EOF'
feat: normalize pasted label / var before lookup

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01QzrWo5zB9YtQaSrYEkmBn5
EOF
)"
```

---

## Task 5: `Mapping.Resolve`

**Files:**
- Modify: `src/SendKeyDemo/Mapping.cs`
- Modify: `tests/SendKeyDemo.Tests/MappingTests.cs`

**Interfaces:**
- Consumes: `MapRow` (Task 3), `NormalizeLabel`/`NormalizeVar` (Task 4).
- Produces:
  - `public enum LookupKind { NotFoundLabel, NeedPickVar, Duplicate, Ok }`
  - `public record LookupResult(LookupKind Kind, MapRow? Row = null, IReadOnlyList<string>? VarChoices = null, IReadOnlyList<int>? DuplicateLines = null, string? Warning = null)`
  - `public static LookupResult Mapping.Resolve(IReadOnlyList<MapRow> rows, string cmdLabelRaw, string? cmdVarRaw)`:
    - Không có dòng nào cùng label (đã chuẩn hoá) → `NotFoundLabel`.
    - `cmdVarRaw` null/trắng → chế độ label-only: `Ok` với `Row` = dòng đầu của label; nếu các dòng cùng label có `CsharpLabel` khác nhau thì kèm `Warning`.
    - `cmdVarRaw` có giá trị: lọc theo label rồi khớp var (đã chuẩn hoá) → 0 khớp: `NeedPickVar` với `VarChoices` = các `CmdVar` (nguyên văn, distinct) của label đó; >1 khớp: `Duplicate` với `DuplicateLines` = `SourceLine` các dòng khớp; đúng 1: `Ok` với `Row`.

- [ ] **Step 1: Thêm test thất bại vào `MappingTests.cs`**

```csharp
    static IReadOnlyList<MapRow> SampleRows() => new List<MapRow>
    {
        new("CHECK_INPUT",  "%INPUT_FILE%", "CHECK_INPUT",   "inputFile", 2),
        new("CHECK_INPUT",  "%RC%",         "CHECK_INPUT",   "rc",        3),
        new("VALIDATE_DATE","%IN_DATE%",    "VALIDATE_DATE", "inDate",    4),
        new("VALIDATE_DATE","%RC%",         "VALIDATE_DATE", "rc",        5),
    };

    [Fact]
    public void Resolve_exact_match_returns_row()
    {
        var r = Mapping.Resolve(SampleRows(), " check_input ", "%rc%");
        Assert.Equal(LookupKind.Ok, r.Kind);
        Assert.Equal("rc", r.Row!.CsharpVar);
        Assert.Equal("CHECK_INPUT", r.Row!.CsharpLabel);
    }

    [Fact]
    public void Resolve_unknown_label()
        => Assert.Equal(LookupKind.NotFoundLabel,
            Mapping.Resolve(SampleRows(), "NOPE", "%rc%").Kind);

    [Fact]
    public void Resolve_unknown_var_returns_pick_list_for_that_label()
    {
        var r = Mapping.Resolve(SampleRows(), "CHECK_INPUT", "%WAT%");
        Assert.Equal(LookupKind.NeedPickVar, r.Kind);
        Assert.Equal(new[] { "%INPUT_FILE%", "%RC%" }, r.VarChoices);
    }

    [Fact]
    public void Resolve_empty_var_is_label_only_mode()
    {
        var r = Mapping.Resolve(SampleRows(), "VALIDATE_DATE", null);
        Assert.Equal(LookupKind.Ok, r.Kind);
        Assert.Equal("VALIDATE_DATE", r.Row!.CsharpLabel);
        Assert.Null(r.Warning);
    }

    [Fact]
    public void Resolve_duplicate_pair_reports_source_lines()
    {
        var rows = new List<MapRow>
        {
            new("L", "%X%", "L", "x",  2),
            new("L", "%X%", "L", "x2", 7),
        };
        var r = Mapping.Resolve(rows, "L", "%x%");
        Assert.Equal(LookupKind.Duplicate, r.Kind);
        Assert.Equal(new[] { 2, 7 }, r.DuplicateLines);
    }

    [Fact]
    public void Resolve_label_only_warns_when_csharp_label_differs()
    {
        var rows = new List<MapRow>
        {
            new("L", "%A%", "LabelA", "a", 2),
            new("L", "%B%", "LabelB", "b", 3),
        };
        var r = Mapping.Resolve(rows, "L", null);
        Assert.Equal(LookupKind.Ok, r.Kind);
        Assert.NotNull(r.Warning);
    }
```

- [ ] **Step 2: Chạy test để xác nhận fail**

Run: `dotnet test SendKeyDemo.sln`
Expected: FAIL — `LookupKind` / `LookupResult` / `Mapping.Resolve` chưa tồn tại.

- [ ] **Step 3: Thêm code vào `src/SendKeyDemo/Mapping.cs`**

Thêm `using System.Linq;` ở đầu file nếu chưa có (ImplicitUsings thường đã có `System.Linq` — nếu build báo thiếu thì thêm). Thêm ở cấp namespace:

```csharp
public enum LookupKind { NotFoundLabel, NeedPickVar, Duplicate, Ok }

public record LookupResult(
    LookupKind Kind,
    MapRow? Row = null,
    IReadOnlyList<string>? VarChoices = null,
    IReadOnlyList<int>? DuplicateLines = null,
    string? Warning = null);
```

Thêm vào trong `public static class Mapping`:

```csharp
    public static LookupResult Resolve(IReadOnlyList<MapRow> rows, string cmdLabelRaw, string? cmdVarRaw)
    {
        var label = NormalizeLabel(cmdLabelRaw);
        var inLabel = rows.Where(r => NormalizeLabel(r.CmdLabel) == label).ToList();
        if (inLabel.Count == 0)
            return new LookupResult(LookupKind.NotFoundLabel);

        if (string.IsNullOrWhiteSpace(cmdVarRaw))
        {
            var csl = inLabel[0].CsharpLabel;
            var mixed = inLabel.Any(r => r.CsharpLabel != csl);
            return new LookupResult(LookupKind.Ok, inLabel[0],
                Warning: mixed
                    ? $"label \"{cmdLabelRaw.Trim()}\" map tới nhiều csharpLabel; dùng \"{csl}\""
                    : null);
        }

        var v = NormalizeVar(cmdVarRaw);
        var hits = inLabel.Where(r => NormalizeVar(r.CmdVar) == v).ToList();
        if (hits.Count == 0)
            return new LookupResult(LookupKind.NeedPickVar,
                VarChoices: inLabel.Select(r => r.CmdVar).Distinct().ToList());
        if (hits.Count > 1)
            return new LookupResult(LookupKind.Duplicate,
                DuplicateLines: hits.Select(r => r.SourceLine).ToList());
        return new LookupResult(LookupKind.Ok, hits[0]);
    }
```

- [ ] **Step 4: Chạy test để xác nhận pass**

Run: `dotnet test SendKeyDemo.sln`
Expected: `Passed: 21` (15 + 6).

- [ ] **Step 5: Commit**

```bash
git add src/SendKeyDemo/Mapping.cs tests/SendKeyDemo.Tests/MappingTests.cs
git commit -m "$(cat <<'EOF'
feat: resolve (cmdLabel, cmdVar) pair against mapping rows

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01QzrWo5zB9YtQaSrYEkmBn5
EOF
)"
```

---

## Task 6: `Mapping.FindLabelLineInText` / `FindLabelLine`

**Files:**
- Modify: `src/SendKeyDemo/Mapping.cs`
- Modify: `tests/SendKeyDemo.Tests/MappingTests.cs`

**Interfaces:**
- Consumes: —
- Produces:
  - `public enum LabelLineKind { NotFound, Multiple, NoExecutableLine, Ok }`
  - `public record LabelLineResult(LabelLineKind Kind, int Line = 0, IReadOnlyList<int>? MatchLines = null)` — `Line` 1-based.
  - `public static LabelLineResult Mapping.FindLabelLineInText(IReadOnlyList<string> lines, string csharpLabel)`:
    - Khớp dòng theo regex `^\s*<escape(csharpLabel)>\s*:` → 0: `NotFound`; >1: `Multiple` với `MatchLines` (1-based).
    - Đúng 1: nếu chính dòng nhãn có lệnh sau dấu `:` (không phải comment) → `Ok` tại dòng nhãn; nếu không, quét xuống bỏ dòng trống, dòng `//`, dòng `#…` (preprocessor), khối `/* … */`, dòng chỉ có `{` → dòng đầu tiên còn lại là `Ok`; chạm hết file → `NoExecutableLine`.
  - `public static LabelLineResult Mapping.FindLabelLine(string csPath, string csharpLabel)` — đọc `File.ReadAllLines` rồi gọi bản `InText`.

- [ ] **Step 1: Thêm test thất bại vào `MappingTests.cs`**

```csharp
    [Fact]
    public void FindLabelLine_first_executable_line_after_label()
    {
        var src = new[]
        {
            "static void Main() {",
            "    CHECK_INPUT:",
            "",
            "        // set rc",
            "        rc = 0;",
            "        goto END;",
        };
        var r = Mapping.FindLabelLineInText(src, "CHECK_INPUT");
        Assert.Equal(LabelLineKind.Ok, r.Kind);
        Assert.Equal(5, r.Line);
    }

    [Fact]
    public void FindLabelLine_skips_lone_brace_and_block_comment()
    {
        var src = new[] { "  L:", "  {", "  /* a", "     b */", "  DoThing();" };
        var r = Mapping.FindLabelLineInText(src, "L");
        Assert.Equal(LabelLineKind.Ok, r.Kind);
        Assert.Equal(5, r.Line);
    }

    [Fact]
    public void FindLabelLine_skips_preprocessor_line()
    {
        var src = new[] { "  L:", "#pragma warning restore CS0164", "  rc = 0;" };
        var r = Mapping.FindLabelLineInText(src, "L");
        Assert.Equal(LabelLineKind.Ok, r.Kind);
        Assert.Equal(3, r.Line);
    }

    [Fact]
    public void FindLabelLine_statement_on_same_line_as_label()
    {
        var r = Mapping.FindLabelLineInText(new[] { "  L: rc = 0;", "  goto END;" }, "L");
        Assert.Equal(LabelLineKind.Ok, r.Kind);
        Assert.Equal(1, r.Line);
    }

    [Fact]
    public void FindLabelLine_not_found()
        => Assert.Equal(LabelLineKind.NotFound,
            Mapping.FindLabelLineInText(new[] { "x", "y" }, "L").Kind);

    [Fact]
    public void FindLabelLine_multiple_matches()
    {
        var r = Mapping.FindLabelLineInText(new[] { "L:", "  a();", "L:", "  b();" }, "L");
        Assert.Equal(LabelLineKind.Multiple, r.Kind);
        Assert.Equal(new[] { 1, 3 }, r.MatchLines);
    }

    [Fact]
    public void FindLabelLine_no_executable_line_after_label()
        => Assert.Equal(LabelLineKind.NoExecutableLine,
            Mapping.FindLabelLineInText(new[] { "  a();", "  L:", "" }, "L").Kind);

    [Fact]
    public void FindLabelLine_ignores_goto_and_string_occurrences()
    {
        var src = new[]
        {
            "  goto CHECK_INPUT;",
            "  Console.WriteLine(\"CHECK_INPUT: hi\");",
            "  CHECK_INPUT:",
            "  rc = 1;",
        };
        var r = Mapping.FindLabelLineInText(src, "CHECK_INPUT");
        Assert.Equal(LabelLineKind.Ok, r.Kind);
        Assert.Equal(4, r.Line);
    }
```

- [ ] **Step 2: Chạy test để xác nhận fail**

Run: `dotnet test SendKeyDemo.sln`
Expected: FAIL — `LabelLineKind` / `LabelLineResult` / `Mapping.FindLabelLineInText` chưa tồn tại.

- [ ] **Step 3: Thêm code vào `src/SendKeyDemo/Mapping.cs`**

Thêm ở cấp namespace:

```csharp
public enum LabelLineKind { NotFound, Multiple, NoExecutableLine, Ok }

public record LabelLineResult(LabelLineKind Kind, int Line = 0, IReadOnlyList<int>? MatchLines = null);
```

Thêm vào trong `public static class Mapping`:

```csharp
    public static LabelLineResult FindLabelLine(string csPath, string csharpLabel)
        => FindLabelLineInText(File.ReadAllLines(csPath), csharpLabel);

    public static LabelLineResult FindLabelLineInText(IReadOnlyList<string> lines, string csharpLabel)
    {
        var rx = new Regex($@"^\s*{Regex.Escape(csharpLabel)}\s*:");
        var matches = new List<int>();
        for (int i = 0; i < lines.Count; i++)
            if (rx.IsMatch(lines[i])) matches.Add(i + 1);

        if (matches.Count == 0) return new LabelLineResult(LabelLineKind.NotFound);
        if (matches.Count > 1) return new LabelLineResult(LabelLineKind.Multiple, MatchLines: matches);

        int start = matches[0];                       // 1-based dòng nhãn
        var labelLine = lines[start - 1];
        int colon = labelLine.IndexOf(':');
        var tail = colon >= 0 ? labelLine[(colon + 1)..].Trim() : "";
        if (tail.Length > 0 && !tail.StartsWith("//"))
            return new LabelLineResult(LabelLineKind.Ok, start);

        bool inBlock = false;
        for (int ln = start + 1; ln <= lines.Count; ln++)
        {
            var t = lines[ln - 1].Trim();
            if (inBlock) { if (t.Contains("*/")) inBlock = false; continue; }
            if (t.Length == 0) continue;
            if (t.StartsWith("//")) continue;
            if (t.StartsWith("#")) continue;                 // #pragma / #region / #if …
            if (t.StartsWith("/*")) { if (!t.Contains("*/")) inBlock = true; continue; }
            if (t == "{") continue;
            return new LabelLineResult(LabelLineKind.Ok, ln);
        }
        return new LabelLineResult(LabelLineKind.NoExecutableLine);
    }
```

- [ ] **Step 4: Chạy test để xác nhận pass**

Run: `dotnet test SendKeyDemo.sln`
Expected: `Passed: 29` (21 + 8).

- [ ] **Step 5: Commit**

```bash
git add src/SendKeyDemo/Mapping.cs tests/SendKeyDemo.Tests/MappingTests.cs
git commit -m "$(cat <<'EOF'
feat: locate a C# label and its first executable line

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01QzrWo5zB9YtQaSrYEkmBn5
EOF
)"
```

---

## Task 7: `AppSettings`

**Files:**
- Create: `src/SendKeyDemo/AppSettings.cs`
- Create: `tests/SendKeyDemo.Tests/AppSettingsTests.cs`

**Interfaces:**
- Consumes: —
- Produces:
  - `public record AppSettings` với `string? MappingPath { get; init; }`, `string? TargetCsPath { get; init; }`, `bool TopMost { get; init; } = true`.
  - `public static AppSettings AppSettings.Load(string path)` / `public static AppSettings Load()` (dùng `settings.json` cạnh exe). File thiếu / JSON hỏng → `new AppSettings()` (mọi giá trị default, `TopMost = true`).
  - `public void AppSettings.Save(string path)` / `public void Save()`. Lỗi ghi → nuốt (không ném).

- [ ] **Step 1: Viết test thất bại**

Tạo `tests/SendKeyDemo.Tests/AppSettingsTests.cs`:

```csharp
using System;
using System.IO;
using SendKeyDemo;
using Xunit;

namespace SendKeyDemo.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Save_then_Load_round_trips()
    {
        var p = Path.GetTempFileName();
        new AppSettings { MappingPath = @"C:\m.csv", TargetCsPath = @"C:\a.cs", TopMost = false }.Save(p);
        var s = AppSettings.Load(p);
        File.Delete(p);
        Assert.Equal(@"C:\m.csv", s.MappingPath);
        Assert.Equal(@"C:\a.cs", s.TargetCsPath);
        Assert.False(s.TopMost);
    }

    [Fact]
    public void Load_missing_file_returns_defaults_with_topmost_true()
    {
        var s = AppSettings.Load(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid() + ".json"));
        Assert.Null(s.MappingPath);
        Assert.True(s.TopMost);
    }

    [Fact]
    public void Load_corrupt_json_returns_defaults()
    {
        var p = Path.GetTempFileName();
        File.WriteAllText(p, "{ not json");
        var s = AppSettings.Load(p);
        File.Delete(p);
        Assert.True(s.TopMost);
        Assert.Null(s.MappingPath);
    }

    [Fact]
    public void Load_json_without_topmost_key_defaults_to_true()
    {
        var p = Path.GetTempFileName();
        File.WriteAllText(p, "{\"MappingPath\":\"x\"}");
        var s = AppSettings.Load(p);
        File.Delete(p);
        Assert.Equal("x", s.MappingPath);
        Assert.True(s.TopMost);
    }
}
```

- [ ] **Step 2: Chạy test để xác nhận fail**

Run: `dotnet test SendKeyDemo.sln`
Expected: FAIL — `AppSettings` chưa tồn tại.

- [ ] **Step 3: Tạo `src/SendKeyDemo/AppSettings.cs`**

Dùng thuộc tính `{ get; init; }` với initializer `= true` cho `TopMost` (KHÔNG dùng positional record — `System.Text.Json` không áp default của tham số constructor cho key thiếu, sẽ ra `false`).

```csharp
using System.Text.Json;

namespace SendKeyDemo;

public record AppSettings
{
    public string? MappingPath { get; init; }
    public string? TargetCsPath { get; init; }
    public bool TopMost { get; init; } = true;

    static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load() => Load(DefaultPath);

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save() => Save(DefaultPath);

    public void Save(string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // cấu hình không lưu được thì bỏ qua, không làm hỏng app
        }
    }
}
```

- [ ] **Step 4: Chạy test để xác nhận pass**

Run: `dotnet test SendKeyDemo.sln`
Expected: `Passed: 33` (29 + 4).

- [ ] **Step 5: Commit**

```bash
git add src/SendKeyDemo/AppSettings.cs tests/SendKeyDemo.Tests/AppSettingsTests.cs
git commit -m "$(cat <<'EOF'
feat: AppSettings persisted to settings.json next to the exe

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01QzrWo5zB9YtQaSrYEkmBn5
EOF
)"
```

---

## Task 8: `VsAutomation.EnsureBreakpoint`

**Files:**
- Modify: `src/SendKeyDemo/VsAutomation.cs`

**Interfaces:**
- Consumes: `EnvDTE.DTE`.
- Produces: `public static string VsAutomation.EnsureBreakpoint(DTE dte, string file, int line)` — nếu đã có breakpoint đúng `file`+`line` thì trả `"breakpoint đã có tại …"`; nếu chưa thì `Breakpoints.Add("", file, line)`; `COMException` → chuỗi lỗi tiếng Việt. Không bao giờ xoá breakpoint.

Không unit-test được (cần DTE sống). Kiểm bằng build + test thủ công ở Task 12.

- [ ] **Step 1: Thêm method vào `src/SendKeyDemo/VsAutomation.cs`**

Thêm ngay sau method `ToggleBreakpoint` trong `public static class VsAutomation`:

```csharp
    public static string EnsureBreakpoint(DTE dte, string file, int line)
    {
        if (!File.Exists(file)) return $"file không tồn tại: {file}";
        foreach (Breakpoint bp in dte.Debugger.Breakpoints)
        {
            if (string.Equals(bp.File, file, StringComparison.OrdinalIgnoreCase) && bp.FileLine == line)
                return $"breakpoint đã có tại {Path.GetFileName(file)}:{line}";
        }
        try
        {
            dte.Debugger.Breakpoints.Add("", file, line);
        }
        catch (COMException)
        {
            return $"không đặt được breakpoint tại {Path.GetFileName(file)}:{line} — dòng phải là lệnh " +
                   "thực thi (không phải dòng trống / comment / khai báo), và file phải thuộc solution đang mở.";
        }
        return $"đã đặt breakpoint tại {Path.GetFileName(file)}:{line}";
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build SendKeyDemo.sln`
Expected: `Build succeeded`, 0 error, 0 warning (ngoài NU1701 đã chặn).

- [ ] **Step 3: Commit**

```bash
git add src/SendKeyDemo/VsAutomation.cs
git commit -m "$(cat <<'EOF'
feat: EnsureBreakpoint — add breakpoint only if missing, never delete

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01QzrWo5zB9YtQaSrYEkmBn5
EOF
)"
```

---

## Task 9: `MainForm` — controls mapping mode + settings + TopMost

**Files:**
- Modify: `src/SendKeyDemo/MainForm.cs`

**Interfaces:**
- Consumes: `AppSettings` (Task 7).
- Produces: `MainForm` với các control mới (`_mappingPath`, `_targetCs`, `_cmdLabel`, `_cmdVar`, `_run`, `_topMostBox`), settings load/save, `TopMost` theo settings, `PickFile`, `SaveSettings`, `MappingKeyDown`, và stub `void TraVaChay()` (Task 10 cài đặt thật).

- [ ] **Step 1: Ghi đè toàn bộ `src/SendKeyDemo/MainForm.cs`**

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

    // --- mapping mode ---
    readonly TextBox _mappingPath = new() { Dock = DockStyle.Fill };
    readonly Button _mappingBrowse = new() { Text = "Browse...", AutoSize = true };
    readonly TextBox _targetCs = new() { Dock = DockStyle.Fill };
    readonly Button _targetBrowse = new() { Text = "Browse...", AutoSize = true };
    readonly TextBox _cmdLabel = new() { Dock = DockStyle.Fill };
    readonly TextBox _cmdVar = new() { Dock = DockStyle.Fill };
    readonly Button _run = new() { Text = "Tra & Chạy", AutoSize = true };
    readonly CheckBox _topMostBox = new() { Text = "Luôn nổi trên cùng", AutoSize = true, Checked = true };
    bool _loading;

    public MainForm()
    {
        Text = "VS SendKey Automation Demo";
        Width = 780;
        Height = 620;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 0) };
        top.Controls.Add(_instances);
        top.Controls.Add(_refresh);

        var mapGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 3, RowCount = 5, AutoSize = true, Padding = new Padding(8, 4, 8, 4)
        };
        mapGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        mapGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mapGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        mapGrid.Controls.Add(new Label { Text = "mapping.csv", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        mapGrid.Controls.Add(_mappingPath, 1, 0);
        mapGrid.Controls.Add(_mappingBrowse, 2, 0);
        mapGrid.Controls.Add(new Label { Text = "target .cs", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        mapGrid.Controls.Add(_targetCs, 1, 1);
        mapGrid.Controls.Add(_targetBrowse, 2, 1);
        mapGrid.Controls.Add(new Label { Text = "cmdLabel", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        mapGrid.Controls.Add(_cmdLabel, 1, 2);
        mapGrid.Controls.Add(new Label { Text = "cmdVar", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        mapGrid.Controls.Add(_cmdVar, 1, 3);
        mapGrid.Controls.Add(_run, 1, 4);

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

        var bottomPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 0, 8, 4) };
        bottomPanel.Controls.Add(_topMostBox);

        var logHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        logHost.Controls.Add(_log);

        Controls.Add(logHost);
        Controls.Add(bottomPanel);
        Controls.Add(buttons);
        Controls.Add(grid);
        Controls.Add(mapGrid);
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

        _mappingBrowse.Click += (_, _) => PickFile(_mappingPath, "CSV (*.csv)|*.csv|Tất cả (*.*)|*.*");
        _targetBrowse.Click += (_, _) => PickFile(_targetCs, "C# (*.cs)|*.cs|Tất cả (*.*)|*.*");
        _mappingPath.TextChanged += (_, _) => SaveSettings();
        _targetCs.TextChanged += (_, _) => SaveSettings();
        _topMostBox.CheckedChanged += (_, _) => { TopMost = _topMostBox.Checked; SaveSettings(); };
        _run.Click += (_, _) => TraVaChay();
        _cmdLabel.KeyDown += MappingKeyDown;
        _cmdVar.KeyDown += MappingKeyDown;

        Load += (_, _) =>
        {
            _loading = true;
            var s = AppSettings.Load();
            _mappingPath.Text = s.MappingPath ?? "";
            _targetCs.Text = s.TargetCsPath ?? "";
            _topMostBox.Checked = s.TopMost;
            TopMost = s.TopMost;
            _loading = false;

            VsAutomation.OleMessageFilter.Register();
            LoadInstances();
        };
    }

    void MappingKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            TraVaChay();
        }
    }

    void SaveSettings()
    {
        if (_loading) return;
        new AppSettings
        {
            MappingPath = _mappingPath.Text,
            TargetCsPath = _targetCs.Text,
            TopMost = _topMostBox.Checked
        }.Save();
    }

    void PickFile(TextBox target, string filter)
    {
        using var d = new OpenFileDialog { Filter = filter };
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(target.Text));
            if (Directory.Exists(dir)) d.InitialDirectory = dir;
        }
        catch { /* path rỗng / không hợp lệ — bỏ qua */ }
        if (d.ShowDialog(this) == DialogResult.OK) target.Text = d.FileName;
    }

    // Task 10 cài đặt thật.
    void TraVaChay() => Log("Tra & Chạy: (được cài đặt ở Task 10)");

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
        {
            _instances.Items.Add("(không có instance VS đang chạy)");
            _instances.SelectedIndex = 0;
        }

        _goto.Enabled = _bp.Enabled = _addWatch.Enabled = _run.Enabled = any;
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
        catch (Exception ex) when (ex is InvalidComObjectException ||
            (ex is COMException ce && (uint)ce.HResult is 0x800706BA or 0x80010108 or 0x800401FD))
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
Expected: `Build succeeded`, 0 error/warning (ngoài NU1701).

- [ ] **Step 3: Chạy app, kiểm control mới + nhớ đường dẫn**

Run: `dotnet run --project src/SendKeyDemo`
Expected:
- Form mở, cửa sổ nổi trên các cửa sổ khác (thử mở Notepad/trình duyệt đè lên — form vẫn ở trên).
- Có ô `mapping.csv`, `target .cs` (kèm Browse), `cmdLabel`, `cmdVar`, nút `Tra & Chạy`, checkbox `Luôn nổi trên cùng` (đã tích) ở dưới cùng.
- Gõ đường dẫn bất kỳ vào `mapping.csv` và `target .cs`, bỏ tích `Luôn nổi trên cùng`, đóng app, chạy lại → 2 đường dẫn còn nguyên, checkbox vẫn bỏ tích, form không còn nổi trên cùng.
- Bấm `Tra & Chạy` → log `Tra & Chạy: (được cài đặt ở Task 10)` (nếu có instance VS) hoặc `chưa chọn instance` (nếu không).
- Tích lại `Luôn nổi trên cùng` trước khi đóng để trạng thái sạch cho Task 12.

- [ ] **Step 4: Commit**

```bash
git add src/SendKeyDemo/MainForm.cs
git commit -m "$(cat <<'EOF'
feat: mapping-mode inputs, settings persistence, always-on-top toggle

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01QzrWo5zB9YtQaSrYEkmBn5
EOF
)"
```

---

## Task 10: `MainForm.TraVaChay` — luồng tra cứu + chạy

**Files:**
- Modify: `src/SendKeyDemo/MainForm.cs`

**Interfaces:**
- Consumes: `Mapping.Load` / `Resolve` / `FindLabelLine` (Task 3/5/6), `MapRow`, `LookupKind`, `LabelLineKind`, `MappingFormatException` (Task 3), `VsAutomation.GoToLine` (đã có), `VsAutomation.EnsureBreakpoint` (Task 8).
- Produces: `TraVaChay()` thật + `GetMapRows()` + `PickFromList()`; cache mapping theo `LastWriteTimeUtc`.

- [ ] **Step 1: Thêm field cache mapping**

Trong `src/SendKeyDemo/MainForm.cs`, ngay sau dòng `bool _loading;` thêm:

```csharp
    List<MapRow>? _mapRows;
    string _mapRowsPath = "";
    DateTime _mapRowsMtime;
```

- [ ] **Step 2: Thay method stub `TraVaChay` bằng bản thật + thêm 2 helper**

Xoá dòng:

```csharp
    // Task 10 cài đặt thật.
    void TraVaChay() => Log("Tra & Chạy: (được cài đặt ở Task 10)");
```

và thay bằng:

```csharp
    List<MapRow>? GetMapRows()
    {
        var path = _mappingPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Log($"Tra & Chạy: không thấy mapping.csv: {path}");
            return null;
        }
        var mtime = File.GetLastWriteTimeUtc(path);
        if (_mapRows != null && _mapRowsPath == path && _mapRowsMtime == mtime)
            return _mapRows;
        try
        {
            _mapRows = Mapping.Load(path);
            _mapRowsPath = path;
            _mapRowsMtime = mtime;
            Log($"Đã nạp mapping.csv: {_mapRows.Count} dòng.");
            return _mapRows;
        }
        catch (MappingFormatException ex)
        {
            Log("Tra & Chạy: " + ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            Log("Tra & Chạy: lỗi đọc mapping.csv — " + ex.Message);
            return null;
        }
    }

    static string? PickFromList(string title, IReadOnlyList<string> items)
    {
        using var dlg = new Form
        {
            Text = title, Width = 440, Height = 320,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, TopMost = true
        };
        var list = new ListBox { Dock = DockStyle.Fill };
        foreach (var it in items) list.Items.Add(it);
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 32 };
        list.DoubleClick += (_, _) => { if (list.SelectedItem != null) ok.PerformClick(); };
        dlg.Controls.Add(list);
        dlg.Controls.Add(ok);
        dlg.AcceptButton = ok;
        return dlg.ShowDialog() == DialogResult.OK ? list.SelectedItem as string : null;
    }

    void TraVaChay()
    {
        if (_instances.SelectedItem is not VsInstance)
        {
            Log("Tra & Chạy: chưa chọn instance VS.");
            return;
        }

        var rows = GetMapRows();
        if (rows == null) return;

        var csPath = _targetCs.Text.Trim();
        if (string.IsNullOrWhiteSpace(csPath) || !File.Exists(csPath))
        {
            Log($"Tra & Chạy: không thấy file .cs đích: {csPath}");
            return;
        }

        var labelRaw = _cmdLabel.Text.Trim();
        if (labelRaw.Length == 0)
        {
            Log("Tra & Chạy: chưa nhập cmdLabel.");
            return;
        }
        var varRaw = _cmdVar.Text.Trim();
        bool labelOnly = varRaw.Length == 0;

        var res = Mapping.Resolve(rows, labelRaw, labelOnly ? null : varRaw);

        if (res.Kind == LookupKind.NotFoundLabel)
        {
            Log($"Tra & Chạy: không thấy label \"{labelRaw}\" trong mapping.csv.");
            return;
        }
        if (res.Kind == LookupKind.Duplicate)
        {
            Log($"Tra & Chạy: mapping trùng dòng {string.Join(", ", res.DuplicateLines!)}.");
            return;
        }
        if (res.Kind == LookupKind.NeedPickVar)
        {
            var pick = PickFromList($"Chọn biến của label {labelRaw}", res.VarChoices!);
            if (pick == null)
            {
                Log("Tra & Chạy: đã hủy chọn biến.");
                return;
            }
            _cmdVar.Text = pick;
            varRaw = pick;
            labelOnly = false;
            res = Mapping.Resolve(rows, labelRaw, pick);
            if (res.Kind != LookupKind.Ok)
            {
                Log("Tra & Chạy: vẫn không khớp sau khi chọn biến.");
                return;
            }
        }

        var row = res.Row!;
        if (res.Warning != null) Log("Tra & Chạy: " + res.Warning);

        var lineRes = Mapping.FindLabelLine(csPath, row.CsharpLabel);
        if (lineRes.Kind == LabelLineKind.NotFound)
        {
            Log($"Tra & Chạy: không thấy label \"{row.CsharpLabel}:\" trong {Path.GetFileName(csPath)}.");
            return;
        }
        if (lineRes.Kind == LabelLineKind.Multiple)
        {
            Log($"Tra & Chạy: label \"{row.CsharpLabel}:\" xuất hiện ở dòng {string.Join(", ", lineRes.MatchLines!)}.");
            return;
        }
        if (lineRes.Kind == LabelLineKind.NoExecutableLine)
        {
            Log($"Tra & Chạy: sau label \"{row.CsharpLabel}\" không còn dòng thực thi.");
            return;
        }
        int line = lineRes.Line;

        _file.Text = csPath;
        _line.Value = Math.Min(line, (int)_line.Maximum);
        if (!labelOnly) _watch.Text = row.CsharpVar;

        Run("Go To Line", dte => VsAutomation.GoToLine(dte, csPath, line));
        Run("Breakpoint", dte => VsAutomation.EnsureBreakpoint(dte, csPath, line));

        Log(labelOnly
            ? $"mapping: {labelRaw} → {Path.GetFileName(csPath)}:{line} — breakpoint sẵn sàng (F5 để dừng lại)."
            : $"mapping: {labelRaw}/{_cmdVar.Text} → {Path.GetFileName(csPath)}:{line}, watch \"{row.CsharpVar}\" — F5 dừng ở breakpoint rồi bấm Add Watch.");
    }
```

- [ ] **Step 3: Build**

Run: `dotnet build SendKeyDemo.sln`
Expected: `Build succeeded`, 0 error/warning (ngoài NU1701).

- [ ] **Step 4: Chạy toàn bộ test (không hồi quy)**

Run: `dotnet test SendKeyDemo.sln`
Expected: `Passed: 33`.

- [ ] **Step 5: Commit**

```bash
git add src/SendKeyDemo/MainForm.cs
git commit -m "$(cat <<'EOF'
feat: Tra & Chạy — lookup mapping, goto label, set breakpoint, prefill watch

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01QzrWo5zB9YtQaSrYEkmBn5
EOF
)"
```

---

## Task 11: `SampleTarget` cấu trúc label + `mapping.csv` căn lại

**Files:**
- Modify: `samples/SampleTarget/Program.cs`
- Modify: `mapping.csv`

**Interfaces:**
- Consumes: —
- Produces: sample có `nhãn:` + `goto` + biến cục bộ khớp `mapping.csv`, để test thủ công mapping mode.

- [ ] **Step 1: Ghi đè `samples/SampleTarget/Program.cs`**

Batch migrate thường tắt CS0164 (nhãn không tham chiếu) toàn cục; ở đây tắt cho cả
method để không có dòng `#pragma` nào chen giữa nhãn và lệnh đầu tiên.

```csharp
using System;
using System.Threading;

namespace SampleTarget;

static class Program
{
#pragma warning disable CS0164 // nhãn không được tham chiếu (mô phỏng cấu trúc batch)
    static void Main()
    {
        int rc = 0;
        string inputFile = "data.txt";
        bool inputExists = true;      // giả lập input hợp lệ để chạy hết các label
        string inDate = "";

        Console.WriteLine("SampleTarget khởi động — F5 trong VS để debug.");

    CHECK_INPUT:
        rc = inputExists ? 0 : 1;
        Console.WriteLine($"CHECK_INPUT: inputFile={inputFile}, rc={rc}");
        if (rc != 0) goto END_PROC;

    VALIDATE_DATE:
        inDate = "2026-09-08";
        rc = inDate.Length == 10 ? 0 : 1;
        Console.WriteLine($"VALIDATE_DATE: inDate={inDate}, rc={rc}");
        goto END_PROC;

    END_PROC:
        Console.WriteLine($"END_PROC: rc={rc}");
        Thread.Sleep(60_000);        // giữ tiến trình sống đủ lâu để thao tác watch / chụp
    }
#pragma warning restore CS0164
}
```

- [ ] **Step 2: Ghi đè `mapping.csv`**

```
cmdLabel,cmdVar,csharpLabel,csharpVar
CHECK_INPUT,%INPUT_FILE%,CHECK_INPUT,inputFile
CHECK_INPUT,%RC%,CHECK_INPUT,rc
CHECK_INPUT,"if ""%RC%"" NEQ ""0""",CHECK_INPUT,rc != 0
VALIDATE_DATE,%IN_DATE%,VALIDATE_DATE,inDate
VALIDATE_DATE,%RC%,VALIDATE_DATE,rc
END_PROC,%RC%,END_PROC,rc
```

- [ ] **Step 3: Build + chạy sample**

Run: `dotnet build SendKeyDemo.sln`
Expected: `Build succeeded`, 0 error, 0 warning (CS0164 đã bị `#pragma` chặn tại chỗ).

Run: `dotnet run --project samples/SampleTarget`
Expected: in `SampleTarget khởi động…`, `CHECK_INPUT: inputFile=data.txt, rc=0`, `VALIDATE_DATE: inDate=2026-09-08, rc=0`, `END_PROC: rc=0`, rồi treo 60s. Ctrl+C để dừng.

- [ ] **Step 4: Kiểm nhanh resolve khớp file thật**

Run: `dotnet test SendKeyDemo.sln`
Expected: `Passed: 33` (không đổi — sanity).

- [ ] **Step 5: Commit**

```bash
git add samples/SampleTarget/Program.cs mapping.csv
git commit -m "$(cat <<'EOF'
test: SampleTarget uses label + goto structure aligned to mapping.csv

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01QzrWo5zB9YtQaSrYEkmBn5
EOF
)"
```

---

## Task 12: README + kiểm thử thủ công E2E với Visual Studio

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: toàn bộ app.
- Produces: —

- [ ] **Step 1: Thêm mục "Mapping mode" vào `README.md`**

Chèn ngay trước mục `## Kiểm thử thủ công`:

```markdown
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

Không khớp `cmdVar` → tool hiện danh sách biến của label đó để chọn (không tự đoán).
Checkbox **Luôn nổi trên cùng** giữ form không bị trình duyệt / Excel che.
```

- [ ] **Step 2: Thêm dòng vào bảng "Kiểm thử thủ công" của `README.md`**

Thêm vào cuối bảng:

```markdown
| 9 | Mapping mode: trỏ `mapping.csv` + `samples\SampleTarget\Program.cs`; đóng/mở lại app | 2 đường dẫn còn nguyên; form nổi trên cùng |
| 10 | Paste `CHECK_INPUT` + `%RC%`, bấm **Tra & Chạy** (hoặc Ctrl+Enter) | `File`/`Line`/`Watch` tự điền (`Watch=rc`); VS nhảy tới dòng `rc = inputExists ? 0 : 1;`; chấm đỏ hiện; log `mapping: CHECK_INPUT/%RC% → Program.cs:<n>, watch "rc" …` |
| 11 | Để trống `cmdVar`, cmdLabel = `VALIDATE_DATE`, **Tra & Chạy** | goto + breakpoint ở dòng `inDate = "2026-09-08";`; `Watch` không đổi; log `… breakpoint sẵn sàng` |
| 12 | cmdLabel = `CHECK_INPUT`, cmdVar = `%SAI%`, **Tra & Chạy** | Hiện danh sách `%INPUT_FILE%`, `%RC%`, `if "%RC%" NEQ "0"` để chọn |
| 13 | cmdLabel = `KHONGCO`, **Tra & Chạy** | log `không thấy label "KHONGCO" trong mapping.csv`; không thao tác VS |
| 14 | Sau bước 10: **F5** debug `SampleTarget`, đợi dừng ở breakpoint, bấm **Add Watch** | Dòng `rc` xuất hiện trong cửa sổ Watch kèm giá trị `0` |
```

- [ ] **Step 3: Build Release + chạy toàn bộ test**

Run: `dotnet build SendKeyDemo.sln -c Release`
Expected: `Build succeeded`, 0 error, 0 warning (ngoài NU1701).

Run: `dotnet test SendKeyDemo.sln`
Expected: `Passed: 33`, `Failed: 0`.

- [ ] **Step 4: Kiểm thử thủ công với Visual Studio thật**

Mở `SendKeyDemo.sln` (hoặc riêng `samples/SampleTarget`) bằng Visual Studio 2022/2026. Chạy `dotnet run --project src/SendKeyDemo`. Làm lần lượt dòng 1–14 trong bảng "Kiểm thử thủ công" của `README.md` — tất cả phải đúng kỳ vọng. Ghi lại phiên bản VS đã test trong mô tả commit / PR.

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "$(cat <<'EOF'
docs: document mapping mode and its manual test checklist

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01QzrWo5zB9YtQaSrYEkmBn5
EOF
)"
```

---

## Self-Review Notes

**Spec coverage:**
- mapping.csv 4 cột + RFC 4180 → Task 2 (`ParseCsv`), Task 3 (`Load`), Task 11 (file mẫu).
- `csharpVar` chứa sẵn biểu thức đã dịch (mệnh đề `if`) → không có logic dịch trong tool; `Resolve` trả nguyên `CsharpVar`, `MainForm` gán thẳng vào `_watch` (Task 5, Task 10); dòng ví dụ trong `mapping.csv` (Task 11).
- Hai chế độ (watch / điểm dừng label; ô cmdVar rỗng = label-only) → `Resolve` nhánh `cmdVarRaw` null (Task 5), `TraVaChay` biến `labelOnly` (Task 10).
- Chuẩn hoá (trim, gộp space, bỏ `:` đầu, bỏ `if`/`goto`, case-insensitive) + fallback list khi lệch → `NormalizeLabel`/`NormalizeVar` (Task 4), `NeedPickVar` + `PickFromList` (Task 5, Task 10).
- Kích hoạt bằng nút + `Ctrl+Enter` → `_run.Click` + `MappingKeyDown` (Task 9).
- Điền kết quả vào ô thủ công cũ (`File`/`Line`/`Watch`) → Task 10 Step 2.
- `mapping.csv` + `.cs` đích: 2 ô path + Browse, nhớ lần cuối trong `settings.json` cạnh exe → `AppSettings` (Task 7), wiring (Task 9).
- Resolve dòng breakpoint bằng cách quét `<csharpLabel>:` (không cột line/offset) → `FindLabelLineInText` (Task 6).
- `EnsureBreakpoint` chỉ thêm nếu chưa có, không xoá → Task 8.
- Luôn nổi trên cùng + checkbox tắt/bật, lưu vào settings → `_topMostBox` + `AppSettings.TopMost` (Task 7, Task 9).
- Bảng xử lý lỗi trong spec (mapping.csv sai cột, .cs không tồn tại, label không có, var không khớp, cặp trùng, `<csharpLabel>:` không thấy / thấy >1, sau label không còn lệnh, chưa chọn instance) → nhánh trong `Load` (Task 3), `Resolve` (Task 5), `FindLabelLineInText` (Task 6), `GetMapRows` + `TraVaChay` (Task 10).
- Giữ chế độ thủ công cũ → Task 9 giữ nguyên `_goto`/`_bp`/`_addWatch` + handler + `Run`.
- Test: xUnit cho `Mapping.*` + `AppSettings` (Task 2–7); checklist thủ công (Task 12).
- Ngoài phạm vi (Run-All, chụp màn hình, Excel, tự F5, nhiều `.cs`) → không có task nào chạm tới; ghi rõ trong Global Constraints.

**Placeholder scan:** `TraVaChay` ở Task 9 là stub CÓ CHỦ ĐÍCH (một dòng `Log`), được thay toàn bộ ở Task 10 Step 2 — không phải placeholder treo. Không còn "TBD"/"xử lý lỗi phù hợp"/"viết test cho phần trên" ở đâu.

**Type consistency:**
- `MapRow(CmdLabel, CmdVar, CsharpLabel, CsharpVar, SourceLine)` — dùng nhất quán ở Task 3 (tạo), Task 5 (`Resolve` đọc `CsharpLabel`/`CsharpVar`/`SourceLine`), Task 10 (`row.CsharpLabel`/`row.CsharpVar`).
- `LookupResult` field `DuplicateLines` (không phải `DuplicateRows`), `VarChoices`, `Warning`, `Row` — Task 5 định nghĩa, Task 10 dùng đúng tên.
- `LabelLineResult` field `Line`, `MatchLines` — Task 6 định nghĩa, Task 10 dùng đúng tên.
- Enum: `LookupKind.{NotFoundLabel,NeedPickVar,Duplicate,Ok}`, `LabelLineKind.{NotFound,Multiple,NoExecutableLine,Ok}` — Task 5/6 định nghĩa, Task 10 match đúng.
- `Mapping.FindLabelLine(string, string)` vs `FindLabelLineInText(IReadOnlyList<string>, string)` — Task 6 định nghĩa cả hai; Task 10 gọi `FindLabelLine` (bản đọc file).
- `AppSettings` khởi tạo bằng object initializer `{ MappingPath =, TargetCsPath =, TopMost = }` — Task 7 (thuộc tính `init`), Task 9 (`SaveSettings`) khớp.
- `VsAutomation.EnsureBreakpoint(DTE, string, int)` trả `string` — Task 8 định nghĩa, Task 10 gọi qua `Run("Breakpoint", dte => …)` khớp chữ ký `Func<DTE,string>`.
- `MainForm.Run(string, Func<DTE,string>)` — giữ nguyên từ bản hiện tại; Task 9 chép đúng nguyên văn (gồm cả nhánh `catch when` HResult VS đã đóng).

**Refinement ngoài spec (có chủ đích):** `FindLabelLineInText` xử lý thêm trường hợp nhãn có lệnh cùng dòng (`L: stmt;`) → dừng ngay tại dòng nhãn, thay vì luôn nhảy xuống dòng kế tiếp. Đúng hơn với ý "dòng thực thi đầu label" của spec, chi phí 3 dòng code + 1 test; README ghi rõ quy ước "đặt nhãn trên dòng riêng".
