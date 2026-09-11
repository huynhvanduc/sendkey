using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SendKeyDemo;

public record MapRow(string CmdLabel, string CmdVar, string CsharpLabel, string CsharpVar, int SourceLine,
    string CsharpFile = "");

public enum LookupKind { NotFoundLabel, NeedPickVar, Duplicate, Ok }

public enum LabelLineKind { NotFound, Multiple, NoExecutableLine, AnchorNotFound, Ok }

public record LabelLineResult(LabelLineKind Kind, int Line = 0, IReadOnlyList<int>? MatchLines = null, int LabelLine = 0);

/// <summary>
/// Một điểm dừng khi chụp: 1 dòng code = 1 ảnh (nhánh if nằm cùng dòng thì tách theo cột), Watch chỉ gồm các biến
/// rơi vào đây. Mệnh đề if: Condition = biểu thức C# của mệnh đề; SetStatement = lệnh chạy ngay sau khi chụp ở đây
/// để ép mệnh đề ĐÚNG (rỗng mà có Condition = dev tự set).
/// </summary>
public record StopPoint(string File, int Line, int LabelLine, IReadOnlyList<string> Watch, IReadOnlyList<string> Items,
    int Column = 0, string SetStatement = "", string Condition = "");

/// <summary>Một 「」 đã tra ra chỗ dừng — đầu vào của <see cref="Mapping.GroupStops"/>.</summary>
public record StopTarget(string File, int Line, int LabelLine, string Watch, string Item,
    int Column = 0, string SetStatement = "", string Condition = "");

public record LookupResult(
    LookupKind Kind,
    MapRow? Row = null,
    IReadOnlyList<string>? VarChoices = null,
    IReadOnlyList<int>? DuplicateLines = null,
    string? Warning = null);

public class MappingFormatException : Exception
{
    public int LineNumber { get; }
    public MappingFormatException(int lineNumber, string reason)
        : base($"mapping.csv dòng {lineNumber}: {reason}") => LineNumber = lineNumber;
}

public static class Mapping
{
    static readonly Regex _ws = new(@"\s+");

    public static string NormalizeLabel(string raw)
        => _ws.Replace((raw ?? "").Trim(), " ").TrimStart(':').Trim().ToLowerInvariant();

    /// <summary>Dọn label để GHI vào file — như NormalizeLabel nhưng GIỮ NGUYÊN hoa/thường.</summary>
    public static string CleanLabel(string raw)
        => _ws.Replace((raw ?? "").Trim(), " ").Trim(':').Trim();

    public static string NormalizeVar(string raw)
    {
        var s = _ws.Replace((raw ?? "").Trim(), " ");
        s = Regex.Replace(s, @"^(if|goto)\s+", "", RegexOptions.IgnoreCase);
        return s.Trim().ToLowerInvariant();
    }

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
            if (f.Length is not (4 or 5))
                throw new MappingFormatException(lineNo, $"cần 4 hoặc 5 cột, thấy {f.Length}");
            result.Add(new MapRow(f[0].Trim(), f[1].Trim(), f[2].Trim(), f[3].Trim(), lineNo,
                f.Length == 5 ? f[4].Trim() : ""));
        }
        return result;
    }

    static string ToCsvField(string s)
        => s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;

    /// <summary>Ghi thêm 1 dòng vào cuối mapping.csv (RFC 4180). Ném IOException nếu file đang bị khoá.</summary>
    public static void AppendRow(string csvPath, MapRow row)
    {
        var existing = File.ReadAllText(csvPath);
        var prefix = existing.Length > 0 && !existing.EndsWith("\n") ? "\n" : "";
        var fields = row.CsharpFile.Length > 0
            ? new[] { row.CmdLabel, row.CmdVar, row.CsharpLabel, row.CsharpVar, row.CsharpFile }
            : new[] { row.CmdLabel, row.CmdVar, row.CsharpLabel, row.CsharpVar };
        var line = string.Join(",", fields.Select(ToCsvField));
        File.AppendAllText(csvPath, prefix + line + "\n", new UTF8Encoding(false));
    }

    static readonly Regex _plainIdent = new(@"^[A-Za-z_][\w.]*$");
    static readonly Regex _anyLabel = new(@"^\s*[A-Za-z_]\w*\s*:");

    /// <summary>true nếu s là biểu thức (có toán tử / khoảng trắng…), không phải định danh C# thuần.</summary>
    public static bool IsExpression(string? s)
        => !string.IsNullOrWhiteSpace(s) && !_plainIdent.IsMatch(s!.Trim());

    public static LabelLineResult FindLabelLine(string csPath, string csharpLabel, string? anchorExpr = null)
        => FindLabelLineInText(File.ReadAllLines(csPath), csharpLabel, anchorExpr);

    /// <summary>
    /// Tra dòng breakpoint cho <paramref name="csharpLabel"/>. Nếu <paramref name="anchorExpr"/> là
    /// biểu thức (vd "rc != 0") thì quét từ dòng đầu label xuống, dừng ở dòng đầu tiên chứa biểu thức đó
    /// (khớp cả khi cách nhau khoảng trắng khác nhau); không thấy → AnchorNotFound.
    /// </summary>
    public static LabelLineResult FindLabelLineInText(IReadOnlyList<string> lines, string csharpLabel, string? anchorExpr = null)
    {
        var rx = new Regex($@"^\s*{Regex.Escape(csharpLabel)}\s*:");
        var matches = new List<int>();
        for (int i = 0; i < lines.Count; i++)
            if (rx.IsMatch(lines[i])) matches.Add(i + 1);

        if (matches.Count == 0) return new LabelLineResult(LabelLineKind.NotFound);
        if (matches.Count > 1) return new LabelLineResult(LabelLineKind.Multiple, MatchLines: matches);

        int firstExec = FirstExecLine(lines, matches[0]);
        if (firstExec == 0) return new LabelLineResult(LabelLineKind.NoExecutableLine);

        if (IsExpression(anchorExpr))
        {
            var aOne = _ws.Replace(anchorExpr!.Trim(), " ");
            var aNo = _ws.Replace(anchorExpr!, "");
            bool inBlock = false;
            for (int ln = firstExec; ln <= lines.Count; ln++)
            {
                var raw = lines[ln - 1];
                var t = raw.Trim();
                if (inBlock) { if (t.Contains("*/")) inBlock = false; continue; }
                if (ln != firstExec && _anyLabel.IsMatch(raw) && t != "default:" && !t.StartsWith("case "))
                    break;                                   // đã sang nhãn / case khác
                if (t.StartsWith("//")) continue;
                if (t.StartsWith("/*")) { if (!t.Contains("*/")) inBlock = true; continue; }
                if (_ws.Replace(t, " ").Contains(aOne) || _ws.Replace(t, "").Contains(aNo))
                    return new LabelLineResult(LabelLineKind.Ok, ln, LabelLine: matches[0]);
            }
            return new LabelLineResult(LabelLineKind.AnchorNotFound);
        }

        return new LabelLineResult(LabelLineKind.Ok, firstExec, LabelLine: matches[0]);
    }

    /// <summary>Dòng thực thi đầu tiên tại/sau dòng nhãn (1-based); 0 nếu không còn dòng nào.</summary>
    static int FirstExecLine(IReadOnlyList<string> lines, int labelLineNo)
    {
        var labelLine = lines[labelLineNo - 1];
        int colon = labelLine.IndexOf(':');
        var tail = colon >= 0 ? labelLine[(colon + 1)..].Trim() : "";
        if (tail.Length > 0 && !tail.StartsWith("//")) return labelLineNo;

        bool inBlock = false;
        for (int ln = labelLineNo + 1; ln <= lines.Count; ln++)
        {
            var t = lines[ln - 1].Trim();
            if (inBlock) { if (t.Contains("*/")) inBlock = false; continue; }
            if (t.Length == 0) continue;
            if (t.StartsWith("//")) continue;
            if (t.StartsWith("#")) continue;                 // #pragma / #region / #if …
            if (t.StartsWith("/*")) { if (!t.Contains("*/")) inBlock = true; continue; }
            if (t == "{") continue;
            return ln;
        }
        return 0;
    }

    /// <summary>Soát mapping.csv: cặp trùng, và label không tra được trong file .cs (linesFor trả null = bỏ qua dòng đó).</summary>
    public static List<string> Validate(IReadOnlyList<MapRow> rows, Func<MapRow, IReadOnlyList<string>?> linesFor)
    {
        var problems = new List<string>();

        var dups = rows
            .GroupBy(r => (NormalizeLabel(r.CmdLabel), NormalizeVar(r.CmdVar)))
            .Where(g => g.Count() > 1);
        foreach (var g in dups)
            problems.Add($"trùng cặp cmdLabel+cmdVar ở dòng {string.Join(", ", g.Select(r => r.SourceLine))}");

        foreach (var r in rows)
        {
            var lines = linesFor(r);
            if (lines == null) continue;
            var ll = FindLabelLineInText(lines, r.CsharpLabel, r.CsharpVar);
            switch (ll.Kind)
            {
                case LabelLineKind.NotFound:
                    problems.Add($"dòng {r.SourceLine}: không thấy \"{r.CsharpLabel}:\" trong file .cs");
                    break;
                case LabelLineKind.Multiple:
                    problems.Add($"dòng {r.SourceLine}: \"{r.CsharpLabel}:\" xuất hiện {ll.MatchLines!.Count} lần trong file .cs");
                    break;
                case LabelLineKind.NoExecutableLine:
                    problems.Add($"dòng {r.SourceLine}: sau \"{r.CsharpLabel}:\" không còn dòng thực thi");
                    break;
                case LabelLineKind.AnchorNotFound:
                    problems.Add($"dòng {r.SourceLine}: không thấy biểu thức \"{r.CsharpVar}\" sau \"{r.CsharpLabel}:\"");
                    break;
            }
        }
        return problems;
    }

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

    /// <summary>
    /// Gom các đích đã tra thành điểm dừng: cùng file + dòng + cột thì chung 1 ảnh. Thứ tự: file theo lần
    /// xuất hiện đầu tiên, trong 1 file theo dòng rồi cột (thứ tự chương trình chạy qua).
    /// </summary>
    public static List<StopPoint> GroupStops(IReadOnlyList<StopTarget> targets)
    {
        var fileOrder = targets.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return targets
            .GroupBy(t => (File: t.File.ToLowerInvariant(), t.Line, t.Column))
            .Select(g => new StopPoint(g.First().File, g.Key.Line, g.First().LabelLine,
                g.Select(t => t.Watch).Where(w => w.Length > 0).Distinct().ToList(),
                g.Select(t => t.Item).ToList(),
                g.Key.Column,
                g.Select(t => t.SetStatement).FirstOrDefault(s => s.Length > 0) ?? "",
                g.Select(t => t.Condition).FirstOrDefault(s => s.Length > 0) ?? ""))
            .OrderBy(s => fileOrder.FindIndex(f => string.Equals(f, s.File, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(s => s.Line)
            .ThenBy(s => s.Column)
            .ToList();
    }

    static readonly Regex _ifHead = new(@"\bif\s*\(");

    /// <summary>
    /// Lệnh đầu tiên của nhánh if ở dòng <paramref name="ifLine"/> (1-based). Cùng dòng, vd `if (rc != 0) goto X;`
    /// → (ifLine, cột 1-based của `goto`); nhánh xuống dòng → (dòng thực thi kế tiếp, 0), bỏ qua `{`.
    /// Không phải dòng `if (` hoặc điều kiện kéo sang dòng khác → null.
    /// </summary>
    public static (int Line, int Column)? BranchStart(IReadOnlyList<string> lines, int ifLine)
    {
        if (ifLine < 1 || ifLine > lines.Count) return null;
        var text = lines[ifLine - 1];
        var m = _ifHead.Match(text);
        if (!m.Success) return null;

        int depth = 0, close = -1;
        for (int i = m.Index + m.Length - 1; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) { close = i; break; }
        }
        if (close < 0) return null;

        int col = close + 1;
        while (col < text.Length && char.IsWhiteSpace(text[col])) col++;
        if (col < text.Length && text[col] == '{')
        {
            col++;
            while (col < text.Length && char.IsWhiteSpace(text[col])) col++;
        }
        var tail = text[col..];
        if (tail.Length > 0 && !tail.StartsWith("//")) return (ifLine, col + 1);

        for (int ln = ifLine + 1; ln <= lines.Count; ln++)
        {
            var t = lines[ln - 1].Trim();
            if (t.Length == 0 || t == "{" || t.StartsWith("//") || t.StartsWith("#")) continue;
            return (ln, 0);
        }
        return null;
    }
}

// ==================== CopiedText ====================

/// <summary>
/// Bóc nội dung vừa copy từ file test case (Excel tiếng Nhật).
/// Quy tắc duy nhất cần biết: biến / mệnh đề nằm trong 「 」, label thì không.
/// Logic thuần — không đụng clipboard, để test được.
/// </summary>
public static class CopiedText
{
    /// <summary>Một mẩu vừa copy: hoặc là biến/mệnh đề (trong ngoặc), hoặc là label.</summary>
    public record Piece(bool IsVar, string Value);

    /// <summary>Đoạn dài hơn ngần này mà không có 「 」 thì coi là câu mô tả, không phải label.</summary>
    public const int MaxLabelLength = 64;

    static readonly Regex _bracket = new(@"[「『]([^」』]*)[」』]");
    static readonly Regex _spaces = new(@"\s+");

    /// <summary>
    /// Đưa về dạng so khớp được: full-width → ASCII (％→%, Ｒ→R, ：→:), U+3000 → khoảng trắng
    /// thường, xuống dòng/tab → khoảng trắng, gộp khoảng trắng liên tiếp.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch >= '！' && ch <= '～') sb.Append((char)(ch - 0xFEE0));  // ASCII full-width
            else if (ch == '　') sb.Append(' ');                               // khoảng trắng Nhật
            else if (ch is '\r' or '\n' or '\t') sb.Append(' ');
            else sb.Append(ch);
        }
        return _spaces.Replace(sb.ToString(), " ").Trim();
    }

    /// <summary>Excel bọc ô có xuống dòng / tab / nháy trong dấu " và nhân đôi nháy bên trong.</summary>
    public static string UnquoteExcel(string? raw)
    {
        var s = (raw ?? "").Trim();
        return s.Length >= 2 && s[0] == '"' && s[^1] == '"'
            ? s[1..^1].Replace("\"\"", "\"")
            : s;
    }

    /// <summary>Nội dung cặp 「 」 (hoặc 『 』) ĐẦU TIÊN; null nếu không có ngoặc nào.</summary>
    public static string? ExtractBracket(string? raw)
    {
        var m = _bracket.Match(raw ?? "");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    /// <summary>
    /// Phân loại mẩu vừa copy. Có 「 」 → biến/mệnh đề (lấy phần bên trong).
    /// Không có → ứng viên label, trừ khi quá dài (câu mô tả) thì trả null để tool bỏ qua.
    /// </summary>
    public static Piece? Classify(string? raw)
    {
        var s = Normalize(UnquoteExcel(raw));
        if (s.Length == 0) return null;

        if (ExtractBracket(s) is { } inner)
        {
            inner = inner.Trim();
            return inner.Length == 0 ? null : new Piece(true, inner);
        }

        var label = s.Trim(':', '：', ' ').Trim();
        if (label.Length == 0 || label.Length > MaxLabelLength) return null;
        return new Piece(false, label);
    }

    static readonly Regex _goto = new(@"^goto\s*:?\s*([^\s:]+)\s*$", RegexOptions.IgnoreCase);
    static readonly Regex _batchVar = new(@"%[^%\s]+%|![^!\s]+!|^(if|goto)\s", RegexOptions.IgnoreCase);

    /// <summary>「goto :END_PROC」 → "END_PROC"; không phải lệnh goto → null.</summary>
    public static string? GotoTarget(string? inner)
    {
        var m = _goto.Match((inner ?? "").Trim());
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// Mọi mẩu trong 1 lần copy. Có 「 」 → mỗi cặp là 1 biến/mệnh đề; nhiều cặp thì chỉ giữ cặp trông như
    /// biến batch (%X%, !X!, if…, goto…) để 「0」 trong "「%RC%」が「0」であること" không bị coi là biến.
    /// Không có ngoặc → 1 label như <see cref="Classify"/>.
    /// </summary>
    public static List<Piece> ClassifyAll(string? raw)
    {
        var s = Normalize(UnquoteExcel(raw));
        var inners = _bracket.Matches(s).Select(m => m.Groups[1].Value.Trim()).Where(v => v.Length > 0).ToList();
        if (inners.Count > 1)
        {
            var vars = inners.Where(v => _batchVar.IsMatch(v)).ToList();
            inners = vars.Count > 0 ? vars : inners.Take(1).ToList();
        }
        if (inners.Count > 0) return inners.Select(v => new Piece(true, v)).ToList();
        return Classify(raw) is { } p ? new List<Piece> { p } : new List<Piece>();
    }
}

// ==================== IfClause ====================

/// <summary>Cách ép mệnh đề if batch ĐÚNG (vào nhánh): tên biến batch (bỏ %) + giá trị cần set.</summary>
public record IfBypass(string Var, string Value);

/// <summary>
/// Mệnh đề if batch copy trong 「」. Chỉ nhận dạng so sánh biến — `"%RC%" NEQ "0"`, `%N% GTR 3`, kèm /i, not —
/// đủ để gợi ý giá trị; user sửa được giá trị trong Watch nên không cần hoàn hảo.
/// </summary>
public static class IfClause
{
    static readonly Regex _if = new(@"^if\s", RegexOptions.IgnoreCase);
    static readonly Regex _cmp = new(
        @"^if\s+(?:/i\s+)?(?<not>not\s+)?(?:""%(?<v1>[^%""\s]+)%""|%(?<v2>[^%""\s]+)%)\s*" +
        @"(?<op>==|equ|neq|lss|leq|gtr|geq)\s*(?:""(?<x1>[^""]*)""|(?<x2>[^\s""]+))",
        RegexOptions.IgnoreCase);

    public static bool IsIf(string? item) => _if.IsMatch((item ?? "").Trim());

    /// <summary>Giá trị làm mệnh đề ĐÚNG; if exist / defined / errorlevel / không nhận ra → null (dev tự set).</summary>
    public static IfBypass? Suggest(string? item)
    {
        var m = _cmp.Match((item ?? "").Trim());
        if (!m.Success) return null;

        var name = m.Groups["v1"].Success ? m.Groups["v1"].Value : m.Groups["v2"].Value;
        var x = m.Groups["x1"].Success ? m.Groups["x1"].Value : m.Groups["x2"].Value;
        var op = m.Groups["op"].Value.ToLowerInvariant();
        if (m.Groups["not"].Success)
            op = op switch
            {
                "==" or "equ" => "neq", "neq" => "equ",
                "gtr" => "leq", "leq" => "gtr", "lss" => "geq", "geq" => "lss",
                _ => op,
            };

        string? value = op switch
        {
            "==" or "equ" => x,
            "neq" => x == "0" ? "1" : "0",
            _ when !int.TryParse(x, out _) => null,
            "gtr" => (int.Parse(x) + 1).ToString(),
            "lss" => (int.Parse(x) - 1).ToString(),
            _ => x.Trim(),   // geq / leq
        };
        return value == null ? null : new IfBypass(name, value);
    }

    /// <summary>Lệnh set từ mẫu trong settings, vd <c>SET("{var}", "{value}")</c>.</summary>
    public static string Statement(string template, IfBypass bypass)
        => template.Replace("{var}", bypass.Var).Replace("{value}", bypass.Value);
}
