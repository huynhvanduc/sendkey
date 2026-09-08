using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace SendKeyDemo;

public record MapRow(string CmdLabel, string CmdVar, string CsharpLabel, string CsharpVar, int SourceLine);

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
            if (f.Length != 4)
                throw new MappingFormatException(lineNo, $"cần 4 cột, thấy {f.Length}");
            result.Add(new MapRow(f[0].Trim(), f[1].Trim(), f[2].Trim(), f[3].Trim(), lineNo));
        }
        return result;
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
