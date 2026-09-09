using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SendKeyDemo;

public record MapRow(string CmdLabel, string CmdVar, string CsharpLabel, string CsharpVar, int SourceLine,
    string CsharpFile = "");

public enum LookupKind { NotFoundLabel, NeedPickVar, Duplicate, Ok }

public enum LabelLineKind { NotFound, Multiple, NoExecutableLine, Ok }

public record LabelLineResult(LabelLineKind Kind, int Line = 0, IReadOnlyList<int>? MatchLines = null);

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
            var ll = FindLabelLineInText(lines, r.CsharpLabel);
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
            }
        }
        return problems;
    }

    /// <summary>Tách 1 dòng batch thành (cmdLabel, cmdVar): ngăn bằng Tab hoặc ≥2 dấu cách; không có = cả dòng là label.</summary>
    public static (string Label, string Var) SplitBatchLine(string raw)
    {
        var s = (raw ?? "").Trim();
        int tab = s.IndexOf('\t');
        if (tab >= 0) return (s[..tab].Trim(), s[(tab + 1)..].Trim());
        for (int i = 0; i + 1 < s.Length; i++)
        {
            if (s[i] == ' ' && s[i + 1] == ' ')
            {
                int j = i;
                while (j < s.Length && s[j] == ' ') j++;
                return (s[..i].Trim(), s[j..].Trim());
            }
        }
        return (s, "");
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
}
