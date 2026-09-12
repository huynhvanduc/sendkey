using System.Text;
using System.Text.RegularExpressions;

namespace SendKeyDemo;

public record MapRow(string CmdLabel, string CmdVar, string CsharpLabel, string CsharpVar, int SourceLine,
    string CsharpFile = "");

public enum LookupKind { NotFoundLabel, NeedPickVar, Duplicate, Ok }

public enum LabelLineKind { NotFound, Multiple, NoExecutableLine, AnchorNotFound, Ok }

public record LabelLineResult(LabelLineKind Kind, int Line = 0, IReadOnlyList<int>? MatchLines = null, int LabelLine = 0);

public record StopPoint(string File, int Line, int LabelLine, IReadOnlyList<string> Watch, IReadOnlyList<string> Items,
    int Column = 0, string Condition = "");

public record StopTarget(string File, int Line, int LabelLine, string Watch, string Item, int Column = 0, string Condition = "");

/// <summary>LabelLine = dòng khai báo nhãn (để cuộn/bôi đen); ExecLine = dòng thực thi đầu để đặt breakpoint, 0 nếu không có.</summary>
public record LabelHit(int LabelLine, int ExecLine, string Name);

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

    public static string CleanLabel(string raw)
        => _ws.Replace((raw ?? "").Trim(), " ").Trim(':').Trim();

    public static string NormalizeVar(string raw)
    {
        var s = _ws.Replace((raw ?? "").Trim(), " ");
        s = Regex.Replace(s, @"^(if|goto)\s+", "", RegexOptions.IgnoreCase);
        return s.Trim().ToLowerInvariant();
    }

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

    public static bool IsExpression(string? s)
        => !string.IsNullOrWhiteSpace(s) && !_plainIdent.IsMatch(s!.Trim());

    public static LabelLineResult FindLabelLine(string csPath, string csharpLabel, string? anchorExpr = null)
        => FindLabelLineInText(File.ReadAllLines(csPath), csharpLabel, anchorExpr);

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
            return FindInLabel(lines, matches[0], anchorExpr!) is { Count: > 0 } found
                ? new LabelLineResult(LabelLineKind.Ok, found[0], LabelLine: matches[0])
                : new LabelLineResult(LabelLineKind.AnchorNotFound);

        return new LabelLineResult(LabelLineKind.Ok, firstExec, LabelLine: matches[0]);
    }

    /// <summary>Mọi label bắt đầu bằng prefix — khớp cả tên y hệt lẫn tên có hậu tố (_aa → _aa, _aa1), theo thứ tự dòng.</summary>
    public static List<LabelHit> FindLabelsByPrefix(IReadOnlyList<string> lines, string prefix)
    {
        var hits = new List<LabelHit>();
        if (string.IsNullOrWhiteSpace(prefix)) return hits;

        var rx = new Regex($@"^\s*({Regex.Escape(prefix.Trim())}\w*)\s*:");
        for (int i = 0; i < lines.Count; i++)
            if (rx.Match(lines[i]) is { Success: true } m)
                hits.Add(new LabelHit(i + 1, FirstExecLine(lines, i + 1), m.Groups[1].Value));
        return hits;
    }

    /// <summary>Mọi dòng trong thân label khớp biểu thức: từ dòng thực thi đầu tới trước label/case kế tiếp, bỏ comment.</summary>
    public static List<int> FindInLabel(IReadOnlyList<string> lines, int labelLine, string expr)
    {
        var hits = new List<int>();
        if (string.IsNullOrWhiteSpace(expr) || labelLine < 1 || labelLine > lines.Count) return hits;

        int firstExec = FirstExecLine(lines, labelLine);
        if (firstExec == 0) return hits;

        var aOne = _ws.Replace(expr.Trim(), " ");
        var aNo = _ws.Replace(expr, "");
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
                hits.Add(ln);
        }
        return hits;
    }

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
    /// Cùng một biến, ở cùng một dòng: bấm G lại là làm mới Watch của chính nó. Khoá có cả Item nên
    /// biến KHÁC ở cùng dòng thì cộng dồn — GroupStops gom lại thành 1 điểm dừng thấy đủ cả hai.
    /// </summary>
    public static bool SamePick(StopTarget p, string file, int line, string item)
        => p.Line == line
           && string.Equals(p.File, file, StringComparison.OrdinalIgnoreCase)
           && string.Equals(p.Item, item, StringComparison.OrdinalIgnoreCase);

    public static List<StopPoint> GroupStops(IReadOnlyList<StopTarget> targets)
    {
        var fileOrder = targets.Select(t => t.File).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return targets
            .GroupBy(t => (File: t.File.ToLowerInvariant(), t.Line, t.Column))
            .Select(g => new StopPoint(g.First().File, g.Key.Line, g.First().LabelLine,
                g.Select(t => t.Watch).Where(w => w.Length > 0).Distinct().ToList(),
                g.Select(t => t.Item).ToList(),
                g.Key.Column,
                g.Select(t => t.Condition).FirstOrDefault(s => s.Length > 0) ?? ""))
            .OrderBy(s => fileOrder.FindIndex(f => string.Equals(f, s.File, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(s => s.Line)
            .ThenBy(s => s.Column)
            .ToList();
    }

    static readonly Regex _ifHead = new(@"\bif\s*\(");

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

    /// <summary>
    /// Watch nên điền cho một dòng, theo hình dạng dòng đó: dòng if → mệnh đề; dòng gán → biến + vế phải.
    /// Dòng gán cần vế phải vì breakpoint dừng TRƯỚC khi dòng chạy, lúc đó biến còn mang giá trị cũ.
    /// Mọi chuỗi trả về là lát nguyên văn của dòng: SetWatch bôi đen đúng text đó trong file .cs.
    /// </summary>
    public static List<string> WatchFor(string line, string varName)
    {
        var v = (varName ?? "").Trim();
        if (v.Length == 0) return new List<string>();

        var text = (line ?? "").Trim();
        var one = new List<string> { v };
        var mask = LiteralMask(text);

        // Vòng lặp chỉ điều hướng + chụp: không lôi điều kiện lặp vào Watch.
        if (!IfClause.IsLoop(text) && IfCondition(text, mask) is { } cond && Mentions(cond, v))
            return new List<string> { cond };

        int eq = AssignIndex(text, mask);
        if (eq <= 0) return one;

        // Vế trái có khai báo kiểu thì bỏ kiểu: "int rc" → "rc".
        var target = text[..eq].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        if (target != v) return one;

        // Cắt comment đuôi rồi ";" kết câu — cả hai đều bỏ qua cái nằm trong nháy.
        int end = text.Length;
        for (int i = eq + 1; i + 1 < text.Length; i++)
            if (!mask[i] && text[i] == '/' && text[i + 1] == '/') { end = i; break; }
        while (end > eq + 1 && char.IsWhiteSpace(text[end - 1])) end--;

        // Không kết thúc bằng ";" → câu lệnh còn viết tiếp ở dòng sau, vế phải ở đây chỉ là mảnh cụt.
        if (end <= eq + 1 || mask[end - 1] || text[end - 1] != ';') return one;

        // Vế phải LUÔN vào Watch, bất kể là số, chuỗi hay biểu thức — user cần thấy đúng giá trị sắp gán.
        var rhs = text[(eq + 1)..(end - 1)].Trim();
        return rhs.Length == 0 ? one : new List<string> { v, rhs };
    }

    static bool Mentions(string haystack, string needle)
        => _ws.Replace(haystack, " ").Contains(_ws.Replace(needle.Trim(), " "))
           || _ws.Replace(haystack, "").Contains(_ws.Replace(needle, ""));

    /// <summary>Đánh dấu vị trí nằm trong chuỗi / ký tự, để tìm "=", "//", ";" không dính phần trong nháy.</summary>
    static bool[] LiteralMask(string text)
    {
        var mask = new bool[text.Length];
        int i = 0;
        while (i < text.Length)
        {
            char quote = text[i];
            if (quote != '"' && quote != '\'') { i++; continue; }

            // Tiền tố @ / $ / @$ / $@ ngay trước dấu nháy: có @ là chuỗi verbatim, "" bên trong là nháy escape.
            bool verbatim = false;
            if (quote == '"')
                for (int p = i - 1; p >= 0 && (text[p] == '@' || text[p] == '$'); p--)
                {
                    if (text[p] == '@') verbatim = true;
                    mask[p] = true;
                }

            mask[i] = true;
            int j = i + 1;
            while (j < text.Length)
            {
                mask[j] = true;
                if (verbatim)
                {
                    if (text[j] == '"')
                    {
                        if (j + 1 < text.Length && text[j + 1] == '"') { mask[j + 1] = true; j += 2; continue; }
                        j++; break;
                    }
                }
                else
                {
                    if (text[j] == '\\' && j + 1 < text.Length) { mask[j + 1] = true; j += 2; continue; }
                    if (text[j] == quote) { j++; break; }
                }
                j++;
            }
            i = j;
        }
        return mask;
    }

    /// <summary>Nội dung cặp ngoặc ngoài cùng ngay sau "if". Có goto cùng dòng vẫn đúng vì chỉ lấy phần trong ngoặc.</summary>
    static string? IfCondition(string text, bool[] mask)
    {
        var m = _ifHead.Match(text);
        while (m.Success && mask[m.Index]) m = m.NextMatch();   // "if (" nằm trong nháy thì không tính
        if (!m.Success) return null;

        int open = m.Index + m.Length - 1;
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (mask[i]) continue;
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return text[(open + 1)..i].Trim();
        }
        return null;
    }

    // Vị trí dấu "=" của phép gán. Bỏ qua so sánh, lambda và mọi dạng gán kép. -1 nếu dòng không phải phép gán.
    static int AssignIndex(string text, bool[] mask)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (mask[i] || text[i] != '=') continue;
            if (i + 1 < text.Length && (text[i + 1] == '=' || text[i + 1] == '>')) { i++; continue; }
            if (i > 0 && "=!<>+-*/%&|^".Contains(text[i - 1])) continue;
            return i;
        }
        return -1;
    }
}

// ==================== CopiedText ====================

public static class CopiedText
{
    public record Piece(bool IsVar, string Value);

    public const int MaxLabelLength = 64;

    static readonly Regex _bracket = new(@"[「『]([^」』]*)[」』]");
    static readonly Regex _spaces = new(@"\s+");

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

    public static string UnquoteExcel(string? raw)
    {
        var s = (raw ?? "").Trim();
        return s.Length >= 2 && s[0] == '"' && s[^1] == '"'
            ? s[1..^1].Replace("\"\"", "\"")
            : s;
    }

    public static string? ExtractBracket(string? raw)
    {
        var m = _bracket.Match(raw ?? "");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

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

    public static string? GotoTarget(string? inner)
    {
        var m = _goto.Match((inner ?? "").Trim());
        return m.Success ? m.Groups[1].Value : null;
    }

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
public record IfBypass(string Var, string Value);

public static class IfClause
{
    static readonly Regex _if = new(@"^if\s", RegexOptions.IgnoreCase);
    static readonly Regex _cmp = new(
        @"^if\s+(?:/i\s+)?(?<not>not\s+)?(?:""%(?<v1>[^%""\s]+)%""|%(?<v2>[^%""\s]+)%)\s*" +
        @"(?<op>==|equ|neq|lss|leq|gtr|geq)\s*(?:""(?<x1>[^""]*)""|(?<x2>[^\s""]+))",
        RegexOptions.IgnoreCase);

    static readonly Regex _loop = new(@"^(for|while|do)\b", RegexOptions.IgnoreCase);

    public static bool IsIf(string? item) => _if.IsMatch((item ?? "").Trim());

    /// <summary>Mệnh đề vòng lặp: chỉ điều hướng + chụp, không bao giờ sinh lệnh SET để bypass.</summary>
    public static bool IsLoop(string? item) => _loop.IsMatch((item ?? "").Trim());

    public static IfBypass? Suggest(string? item)
    {
        if (IsLoop(item)) return null;

        var m = _cmp.Match((item ?? "").Trim());
        if (!m.Success) return null;

        var name = m.Groups["v1"].Success ? m.Groups["v1"].Value : m.Groups["v2"].Value;
        var x = m.Groups["x1"].Success ? m.Groups["x1"].Value : m.Groups["x2"].Value;
        var op = m.Groups["op"].Value.ToLowerInvariant();
        if (m.Groups["not"].Success)
            op = op switch
            {
                "==" or "equ" => "neq",
                "neq" => "equ",
                "gtr" => "leq",
                "leq" => "gtr",
                "lss" => "geq",
                "geq" => "lss",
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

    public static string Statement(string template, IfBypass bypass)
        => template.Replace("{var}", bypass.Var).Replace("{value}", bypass.Value);
}
