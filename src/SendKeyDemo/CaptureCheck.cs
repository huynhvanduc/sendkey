namespace SendKeyDemo;

/// <summary>Mức gác cổng trước khi chụp bằng chứng.</summary>
public enum CheckLevel
{
    /// <summary>Mọi thứ khớp — chụp thẳng.</summary>
    Ok,
    /// <summary>Nghi chưa reset biến — hỏi lại rồi mới chụp.</summary>
    Confirm,
    /// <summary>Sai thao tác cơ học — không chụp.</summary>
    Block,
}

/// <summary>Ảnh chụp trạng thái debugger của VS tại một thời điểm. Đọc bởi <see cref="VsAutomation.ReadDebugState"/>.</summary>
public record DebugSnapshot(
    bool Available,
    bool InBreakMode,
    string HitFile,
    int HitLine,
    int BreakpointsInFile,
    bool ExprValid,
    string ExprValue,
    int ProcessId,
    string? Error = null)
{
    public static DebugSnapshot Unavailable(string error) =>
        new(false, false, "", 0, 0, false, "", 0, error);
}

/// <summary>Lần chụp gần nhất — để phát hiện "giá trị y hệt lần trước, chưa reset".</summary>
public record LastCapture(int ProcessId, string TcId, string Expr, string Value);

public record CheckResult(CheckLevel Level, string Message);

/// <summary>
/// Chấm điểm "có được phép chụp bằng chứng không". Logic thuần, không đụng COM —
/// nhận <see cref="DebugSnapshot"/> đã đọc sẵn nên test được.
/// </summary>
public static class CaptureCheck
{
    /// <summary>So hai giá trị debugger đọc được, bỏ qua nháy bao ngoài và hoa/thường.</summary>
    public static bool ValuesMatch(string? actual, string? expected)
        => string.Equals(Unquote(actual), Unquote(expected), StringComparison.OrdinalIgnoreCase);

    static string Unquote(string? s)
    {
        var t = (s ?? "").Trim();
        if (t.Length >= 2 && ((t[0] == '"' && t[^1] == '"') || (t[0] == '\'' && t[^1] == '\'')))
            t = t[1..^1];
        return t.Trim();
    }

    static bool SameFile(string a, string b)
        => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
           string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Chặn cứng khi sai thao tác cơ học (chưa break / dừng nhầm dòng / không đọc được biểu thức);
    /// chỉ hỏi lại khi nghi chưa reset biến. <paramref name="tcId"/> chỉ để ghi vào thông báo.
    /// </summary>
    public static CheckResult Evaluate(
        DebugSnapshot s,
        string tcId,
        string expectedFile,
        int expectedLine,
        string watchExpr,
        LastCapture? previous)
    {
        if (!s.Available)
            return new CheckResult(CheckLevel.Block, s.Error ?? "Không đọc được trạng thái VS — bấm Refresh chọn lại instance.");

        if (!s.InBreakMode)
            return new CheckResult(CheckLevel.Block,
                "Chưa dừng ở breakpoint — F5 và để chương trình DỪNG lại rồi mới chụp.");

        if (string.IsNullOrEmpty(s.HitFile) || s.HitLine <= 0)
            return new CheckResult(CheckLevel.Block,
                "Dừng nhưng không phải do breakpoint (Break All / exception?) — không chụp.");

        if (!SameFile(s.HitFile, expectedFile) || s.HitLine != expectedLine)
            return new CheckResult(CheckLevel.Block,
                $"Dừng ở {Path.GetFileName(s.HitFile)}:{s.HitLine} — không phải dòng của {tcId} " +
                $"({Path.GetFileName(expectedFile)}:{expectedLine}).");

        bool hasExpr = !string.IsNullOrWhiteSpace(watchExpr);

        if (hasExpr && !s.ExprValid)
            return new CheckResult(CheckLevel.Block,
                $"Không đọc được giá trị \"{watchExpr}\" ở dòng này — sai biểu thức hoặc biến chưa vào scope.");

        var bpNote = s.BreakpointsInFile > 1 ? $" (còn {s.BreakpointsInFile} breakpoint trong file)" : "";

        if (hasExpr && previous is { } prev &&
            prev.ProcessId == s.ProcessId && prev.ProcessId != 0 &&
            !string.Equals(prev.TcId, tcId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(prev.Expr, watchExpr, StringComparison.OrdinalIgnoreCase) &&
            ValuesMatch(prev.Value, s.ExprValue))
        {
            return new CheckResult(CheckLevel.Confirm,
                $"{watchExpr} = {s.ExprValue} y hệt lần chụp {prev.TcId}, cùng phiên debug — " +
                "biến đã được gán lại chưa? Vẫn chụp?");
        }

        var valuePart = hasExpr ? $" · {watchExpr} = {s.ExprValue}" : "";

        return new CheckResult(CheckLevel.Ok,
            $"{tcId} · {Path.GetFileName(expectedFile)}:{expectedLine}{valuePart} · chụp được{bpNote}");
    }
}
