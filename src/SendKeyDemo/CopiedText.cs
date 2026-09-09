using System.Text;
using System.Text.RegularExpressions;

namespace SendKeyDemo;

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
}
