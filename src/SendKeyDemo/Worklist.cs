namespace SendKeyDemo;

/// <summary>Một dòng việc cần chụp bằng chứng. <c>CmdVar</c>/<c>Expected</c> có thể rỗng.</summary>
public record WorkItem(string TcId, string CmdLabel, string CmdVar, string Expected)
{
    public string Describe() => CmdVar.Length == 0 ? CmdLabel : $"{CmdLabel}/{CmdVar}";
    public string ToLine() => string.Join("\t", TcId, CmdLabel, CmdVar, Expected);
}

/// <summary>
/// Danh sách test case của một đợt chụp: con trỏ hiện tại + đánh dấu đã chụp.
/// Logic thuần, không đụng UI/VS — để test được.
/// </summary>
public sealed class Worklist
{
    readonly List<WorkItem> _items;
    readonly HashSet<string> _done = new(StringComparer.OrdinalIgnoreCase);

    public Worklist(IEnumerable<WorkItem> items) => _items = items.ToList();

    public IReadOnlyList<WorkItem> Items => _items;
    public int Index { get; private set; }
    public int Total => _items.Count;
    public int DoneCount => _done.Count;
    public bool IsEmpty => _items.Count == 0;

    public WorkItem? Current => Index >= 0 && Index < _items.Count ? _items[Index] : null;

    public bool IsDone(string tcId) => _done.Contains(tcId);

    public void MarkDone(string tcId)
    {
        if (!string.IsNullOrWhiteSpace(tcId)) _done.Add(tcId);
    }

    public void MoveTo(int index)
    {
        if (_items.Count == 0) { Index = 0; return; }
        Index = Math.Clamp(index, 0, _items.Count - 1);
    }

    /// <summary>Nhảy tới dòng CHƯA chụp kế tiếp. false = phía sau không còn dòng nào chưa chụp.</summary>
    public bool MoveNextPending()
    {
        for (int i = Index + 1; i < _items.Count; i++)
        {
            if (!IsDone(_items[i].TcId)) { Index = i; return true; }
        }
        return false;
    }

    public string[] DoneIds() => _done.ToArray();

    public void RestoreDone(IEnumerable<string> ids)
    {
        foreach (var id in ids) MarkDone(id);
    }

    public string[] ToLines() => _items.Select(i => i.ToLine()).ToArray();

    /// <summary>
    /// Tách một dòng thành tối đa <paramref name="max"/> cột, ngăn bằng Tab hoặc ≥2 dấu cách.
    /// Cột cuối giữ nguyên phần còn lại (kể cả có khoảng trắng bên trong).
    /// </summary>
    public static string[] SplitColumns(string raw, int max)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return Array.Empty<string>();

        bool useTab = s.Contains('\t');
        var parts = new List<string>();
        int start = 0;

        while (parts.Count < max - 1)
        {
            int sep = -1, sepLen = 0;
            if (useTab)
            {
                sep = s.IndexOf('\t', start);
                if (sep >= 0)
                {
                    int j = sep;
                    while (j < s.Length && s[j] == '\t') j++;
                    sepLen = j - sep;
                }
            }
            else
            {
                for (int i = start; i + 1 < s.Length; i++)
                {
                    if (s[i] == ' ' && s[i + 1] == ' ')
                    {
                        sep = i;
                        int j = i;
                        while (j < s.Length && s[j] == ' ') j++;
                        sepLen = j - i;
                        break;
                    }
                }
            }

            if (sep < 0) break;
            parts.Add(s[start..sep].Trim());
            start = sep + sepLen;
        }

        parts.Add(s[start..].Trim());
        return parts.ToArray();
    }

    /// <summary>
    /// Đọc worklist dán từ Excel: mỗi dòng <c>tcId ⇥ cmdLabel ⇥ cmdVar ⇥ kỳ vọng</c>.
    /// Dòng chỉ có 1 cột = cmdLabel, tcId tự đánh số. Dòng trống / bắt đầu bằng # bị bỏ qua.
    /// </summary>
    public static List<WorkItem> Parse(string? text)
    {
        var items = new List<WorkItem>();
        int auto = 0;

        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var c = SplitColumns(line, 4);
            if (c.Length == 0) continue;
            auto++;

            items.Add(c.Length == 1
                ? new WorkItem($"{auto:00}", c[0], "", "")
                : new WorkItem(
                    c[0].Length > 0 ? c[0] : $"{auto:00}",
                    c[1],
                    c.Length > 2 ? c[2] : "",
                    c.Length > 3 ? c[3] : ""));
        }
        return items;
    }
}
