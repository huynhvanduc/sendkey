using EnvDTE;
using System.Runtime.InteropServices;

namespace SendKeyDemo;

public class MainForm : Form
{
    readonly ComboBox _instances = new() { Width = 560, Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly Button _refresh = new() { Text = "Refresh", AutoSize = true };
    readonly TextBox _file = new() { Dock = DockStyle.Fill };
    readonly Button _browse = new() { Text = "Browse...", AutoSize = true };
    readonly NumericUpDown _line = new() { Minimum = 1, Maximum = 1_000_000, Value = 1, Width = 100 };
    readonly TextBox _watch = new() { Dock = DockStyle.Fill };
    readonly Button _goto = new() { Text = "Go To Line", AutoSize = true };
    readonly Button _bp = new() { Text = "Toggle Breakpoint", AutoSize = true };
    readonly Button _addWatch = new() { Text = "Add Watch", AutoSize = true };
    readonly TextBox _log = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9f) };

    // --- mapping mode ---
    readonly TextBox _mappingPath = new() { Dock = DockStyle.Fill };
    readonly Button _mappingBrowse = new() { Text = "Browse...", AutoSize = true };
    readonly Button _mappingOpen = new() { Text = "Mở", AutoSize = true };
    readonly Button _checkMapping = new() { Text = "Kiểm tra", AutoSize = true };
    readonly Button _clearBpFile = new() { Text = "Xóa BP file này", AutoSize = true };
    readonly Button _copyWatch = new() { Text = "Copy Watch", AutoSize = true };
    readonly CheckBox _topMostBox = new() { Text = "Luôn nổi trên cùng", AutoSize = true, Checked = true };
    bool _loading;
    List<MapRow>? _mapRows;
    string _mapRowsPath = "";
    DateTime _mapRowsMtime;

    // --- chụp bằng chứng (gộp từ QuickShot) ---
    readonly Button _startEvidence = new() { Text = "▶  Khởi động", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 9, 0, 9) };
    readonly Button _advancedToggle = new()
    {
        Text = "▸  Công cụ khác", Dock = DockStyle.Top, FlatStyle = FlatStyle.Flat, FlatAppearance = { BorderSize = 0 },
        TextAlign = ContentAlignment.MiddleLeft, AutoSize = true, TabStop = false, ForeColor = SystemColors.GrayText,
    };
    readonly Panel _advanced = new() { Dock = DockStyle.Top, AutoSize = true, Visible = false };
    readonly Label _hotkeyHint = new() { Dock = DockStyle.Top, AutoSize = true, ForeColor = SystemColors.GrayText, Padding = new Padding(2, 6, 2, 0) };
    readonly NotifyIcon _tray = new() { Visible = true, Text = "SendKey Evidence" };
    HotkeyWindow _hotkeys = new();
    EvidenceSession? _evidence;
    AppSettings _settings = new();
    Rectangle? _savedRegion;

    static FlowLayoutPanel ButtonCell(params Control[] buttons)
    {
        var cell = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), WrapContents = false };
        cell.Controls.AddRange(buttons);
        return cell;
    }

    static Label RowLabel(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 0, 6, 0) };

    static TableLayoutPanel Grid(Padding padding)
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true, Padding = padding };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return grid;
    }

    public MainForm()
    {
        Text = "SendKey Evidence";
        AutoScaleMode = AutoScaleMode.Dpi;

        // ---------- 1. Khối chuẩn bị: 2 thứ duy nhất cần trước khi chụp ----------
        var setup = Grid(new Padding(12, 12, 12, 4));
        setup.RowCount = 2;
        setup.Controls.Add(RowLabel("Visual Studio"), 0, 0);
        setup.Controls.Add(_instances, 1, 0);
        setup.Controls.Add(ButtonCell(_refresh), 2, 0);
        setup.Controls.Add(RowLabel("mapping.csv"), 0, 1);
        setup.Controls.Add(_mappingPath, 1, 1);
        setup.Controls.Add(ButtonCell(_mappingBrowse, _mappingOpen, _checkMapping), 2, 1);

        // ---------- 2. Hành động chính: một nút ----------
        _startEvidence.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);
        var primary = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 4, 12, 8) };
        primary.Controls.Add(_hotkeyHint);
        primary.Controls.Add(_startEvidence);

        // ---------- 3. Công cụ khác: thu gọn, mặc định ẩn ----------
        _advancedToggle.Click += (_, _) => ToggleAdvanced();
        var adv = Grid(new Padding(12, 0, 12, 8));
        var vsBtns = ButtonCell(_goto, _bp, _clearBpFile, _addWatch, _copyWatch);

        adv.Controls.Add(RowLabel("File"), 0, 0);
        adv.Controls.Add(_file, 1, 0);
        adv.Controls.Add(_browse, 2, 0);
        adv.Controls.Add(RowLabel("Line"), 0, 1);
        // Bọc trong ô AutoSize: khối này layout lúc còn ẩn (cột rộng 0) nên ô Line rộng cố định bị ép còn 1 vạch.
        adv.Controls.Add(ButtonCell(_line), 1, 1);
        adv.Controls.Add(RowLabel("Watch"), 0, 2);
        adv.Controls.Add(_watch, 1, 2);
        adv.Controls.Add(vsBtns, 1, 3);
        adv.SetColumnSpan(vsBtns, 2);   // hàng nút dài: cho lấn sang cột Browse, không bị cắt

        _advanced.Controls.Add(adv);

        // ---------- 4. Log + chân cửa sổ ----------
        var bottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Padding = new Padding(12, 0, 12, 6),
        };
        bottomPanel.Controls.Add(_topMostBox);

        var logHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 12, 4) };
        logHost.Controls.Add(_log);

        Controls.Add(logHost);
        Controls.Add(bottomPanel);
        Controls.Add(_advanced);
        Controls.Add(_advancedToggle);
        Controls.Add(primary);
        Controls.Add(setup);

        _refresh.Click += (_, _) => LoadInstances();
        _browse.Click += (_, _) => PickFile(_file, "C# files (*.cs)|*.cs|All files (*.*)|*.*");
        _goto.Click += (_, _) => Run("Go To Line", dte => VsAutomation.GoToLine(dte, _file.Text, (int)_line.Value));
        _bp.Click += (_, _) => Run("Toggle Breakpoint", dte => VsAutomation.ToggleBreakpoint(dte, _file.Text, (int)_line.Value));
        _clearBpFile.Click += (_, _) => Run("Xóa breakpoint", dte => VsAutomation.ClearBreakpointsInFile(dte, _file.Text));
        _addWatch.Click += (_, _) => Run("Add Watch", dte => VsAutomation.AddWatch(dte, _watch.Text));
        _copyWatch.Click += (_, _) => CopyWatch();
        _checkMapping.Click += (_, _) => KiemTraMapping();

        _mappingBrowse.Click += (_, _) => PickFile(_mappingPath, "CSV (*.csv)|*.csv|Tất cả (*.*)|*.*");
        _mappingOpen.Click += (_, _) => OpenInEditor(_mappingPath.Text);
        _mappingPath.TextChanged += (_, _) => SaveSettings();
        _topMostBox.CheckedChanged += (_, _) => { TopMost = _topMostBox.Checked; SaveSettings(); };
        _startEvidence.Click += (_, _) => StartClipboardMode();
        _instances.SelectedIndexChanged += (_, _) => _evidence?.AttachWatcher();

        Load += (_, _) =>
        {
            // Form tạo ở DPI màn chính rồi hiện ở màn khác, nên đừng đặt px cứng — quy từ đơn vị 96-dpi.
            MinimumSize = LogicalToDeviceUnits(new Size(680, 470));
            Size = LogicalToDeviceUnits(new Size(780, 580));
            CenterToScreen();

            _loading = true;
            _settings = AppSettings.Load();
            _mappingPath.Text = _settings.MappingPath ?? "";
            _topMostBox.Checked = _settings.TopMost;
            TopMost = _settings.TopMost;
            _loading = false;

            _evidence = new EvidenceSession(this, _settings);
            _evidence.Bar.OpenConfigRequested += ShowConfigWindow;

            BuildTray();
            RegisterHotkeys();
            _hotkeyHint.Text = $"Trong đợt:   {_settings.DefineRegionHotkey} khoanh vùng (1 lần)   ·   " +
                $"{_settings.GotoCurrentHotkey} đặt breakpoint   ·   {_settings.MoveArrowHotkey} dời mũi tên vàng   ·   " +
                $"{_settings.CaptureRegionHotkey} chụp   ·   {_settings.ClearBreakpointsHotkey} xóa breakpoint";

            VsAutomation.OleMessageFilter.Register();
            LoadInstances();
            _evidence.AttachWatcher();
        };
    }

    // ---- EvidenceSession (thanh chụp) gọi thẳng vào các hàm internal của cửa sổ này ----

    internal DTE? CurrentDte() => (_instances.SelectedItem as VsInstance)?.Dte;
    string CurrentClassPath() => CurrentDte() is { } dte ? VsAutomation.CurrentClassFile(dte) ?? "" : "";
    internal Rectangle? SavedRegion => _savedRegion;

    internal string? AddMapping(string cmdLabel, string cmdVar, string csLabel, string csVar)
    {
        var mappingCsv = _mappingPath.Text.Trim();
        if (!File.Exists(mappingCsv)) return "Chưa trỏ mapping.csv ở cửa sổ cấu hình.";

        var cmdL = Mapping.CleanLabel(cmdLabel);
        var cmdV = cmdVar.Trim();
        var csL = Mapping.CleanLabel(csLabel);
        var csV = csVar.Trim();
        if (cmdL.Length == 0) return "cmdLabel đang trống.";
        if (csL.Length == 0 || (cmdV.Length > 0 && csV.Length == 0)) return "csharpLabel / csharpVar không được để trống.";   // ca goto: chỉ cần label

        var csPath = CurrentClassPath();
        if (!File.Exists(csPath))
            return "Chưa mở file .cs nào trong VS — mở file chứa nhãn rồi thử lại.";

        var ll = Mapping.FindLabelLine(csPath, csL, csV);
        if (ll.Kind != LabelLineKind.Ok)
            return ll.Kind switch
            {
                LabelLineKind.NotFound => $"Không thấy \"{csL}:\" trong {Path.GetFileName(csPath)}.",
                LabelLineKind.Multiple => $"\"{csL}:\" xuất hiện ở dòng {string.Join(", ", ll.MatchLines!)} — sửa code hoặc dùng csharpFile.",
                LabelLineKind.NoExecutableLine => $"Sau \"{csL}:\" không còn dòng thực thi.",
                _ => $"Không thấy biểu thức \"{csV}\" sau \"{csL}:\".",
            };

        try
        {
            Mapping.AppendRow(mappingCsv, new MapRow(cmdL, cmdV, csL, csV, 0));
            _mapRows = null;   // buộc GetMapRows nạp lại
            Log($"ĐÃ THÊM mapping: {cmdL} / {cmdV} → {csL} / {csV} — kiểm tra lại bản dịch csharpVar.");
            return null;
        }
        catch (IOException) { return "Không ghi được mapping.csv (đang mở trong Excel?) — đóng Excel rồi Enter lại."; }
        catch (Exception ex) { return "Lỗi ghi mapping.csv: " + ex.Message; }
    }

    void SaveSettings()
    {
        if (_loading) return;
        _settings.MappingPath = _mappingPath.Text;
        _settings.TopMost = _topMostBox.Checked;
        _settings.Save();
    }

    void OpenInEditor(string path)
    {
        path = path.Trim();
        if (!File.Exists(path)) { Log($"Mở file: không thấy {path}"); return; }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Log("Mở file: lỗi — " + ex.Message); }
    }

    void PickFile(TextBox target, string filter)
    {
        using var d = new OpenFileDialog { Filter = filter };
        var dir = Safe.Try<string?>(() => Path.GetDirectoryName(Path.GetFullPath(target.Text)), null);   // path rỗng / không hợp lệ thì bỏ qua
        if (Directory.Exists(dir)) d.InitialDirectory = dir;
        if (d.ShowDialog(this) == DialogResult.OK) target.Text = d.FileName;
    }

    internal List<MapRow>? GetMapRows()
    {
        var path = _mappingPath.Text.Trim();
        if (!File.Exists(path))
        {
            Log($"Không thấy mapping.csv: {path}");
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
        catch (MappingFormatException ex) { Log(ex.Message); return null; }
        catch (Exception ex) { Log("Lỗi đọc mapping.csv — " + ex.Message); return null; }
    }

    void CopyWatch()
    {
        var t = _watch.Text.Trim();
        if (t.Length == 0) { Log("Copy Watch: ô Watch đang trống."); return; }
        try { Clipboard.SetText(t); Log($"Copy Watch: đã copy \"{t}\" — dán (Ctrl+V) vào cửa sổ Watch của VS."); }
        catch (Exception ex) { Log("Copy Watch: lỗi clipboard — " + ex.Message); }
    }

    void KiemTraMapping()
    {
        var rows = GetMapRows();
        if (rows == null) return;

        var cache = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string>? LinesFor(MapRow r)
        {
            var p = EffectiveCsPath(r);
            if (string.IsNullOrWhiteSpace(p)) return null;
            if (!cache.TryGetValue(p, out var lines))
                cache[p] = lines = File.Exists(p) ? File.ReadAllLines(p) : null;
            return lines;
        }

        var res = Mapping.Validate(rows, LinesFor);

        // Tách rõ 3 số: gộp "chưa kiểm được" vào OK là để user tưởng đã soát hết cả file.
        var tail = res.Unchecked > 0
            ? $", {res.Unchecked} chưa kiểm được (nhãn không có trong file đang mở trong VS)"
            : "";
        Log($"Kiểm tra mapping.csv: {rows.Count} dòng — {res.Ok} OK, {res.Problems.Count} lỗi{tail}{(res.Problems.Count == 0 ? "." : ":")}");
        foreach (var p in res.Problems) Log("  - " + p);
    }

    internal string EffectiveCsPath(MapRow row)
    {
        var f = row.CsharpFile.Trim();
        if (f.Length == 0) return CurrentClassPath();
        if (Path.IsPathRooted(f)) return f;
        var baseDir = Path.GetDirectoryName(_mappingPath.Text.Trim()) ?? "";
        return Safe.Try(() => Path.GetFullPath(Path.Combine(baseDir, f)), f);
    }

    void LoadInstances()
    {
        _instances.Items.Clear();
        try { foreach (var vs in VsAutomation.FindVisualStudios()) _instances.Items.Add(vs); }
        catch (Exception ex) { Log("LỖI khi quét VS: " + ex.Message); }

        var any = _instances.Items.Count > 0;
        if (!any) _instances.Items.Add("(không có instance VS đang chạy)");
        _instances.SelectedIndex = 0;

        _goto.Enabled = _bp.Enabled = _clearBpFile.Enabled = _addWatch.Enabled = any;
        Log(any ? $"Tìm thấy {_instances.Items.Count} instance VS." : "Không tìm thấy VS nào đang chạy.");
    }

    void Run(string label, Func<DTE, string> action)
    {
        if (_instances.SelectedItem is not VsInstance vs) { Log(label + ": chưa chọn instance."); return; }
        try { Log($"{label}: {action(vs.Dte)}"); }
        catch (Exception ex) when (ex is InvalidComObjectException ||
            (ex is COMException ce && (uint)ce.HResult is 0x800706BA or 0x80010108 or 0x800401FD))
        {
            Log($"{label}: instance đã đóng — bấm Refresh.");
        }
        catch (Exception ex) { Log($"{label} LỖI: {ex.Message}"); }
    }

    // ==================== chụp bằng chứng: tray + hotkey ====================

    void ToggleAdvanced()
    {
        _advanced.Visible = !_advanced.Visible;
        _advancedToggle.Text = _advanced.Visible ? "▾  Công cụ khác" : "▸  Công cụ khác";
        // Nút Flat đang giữ focus sẽ vẽ viền đen cả hàng — chuyển focus đi (mở ra thì vào ô File).
        ActiveControl = _advanced.Visible ? _file : null;
    }

    void StartClipboardMode()
    {
        if (_evidence == null) return;

        if (!File.Exists(_mappingPath.Text.Trim()))
        {
            MessageBox.Show(this, "Chưa trỏ mapping.csv ở trên.", "Thiếu đường dẫn",
                MessageBoxButtons.OK, _settings.MsgIcon(MessageBoxIcon.Warning));
            return;
        }
        _evidence.Start();

        if (_savedRegion == null)
            MessageBox.Show(this,
                $"Sắp cửa sổ VS sao cho thấy tab tên file, dòng code và cửa sổ Watch, rồi bấm {_settings.DefineRegionHotkey} " +
                "để khoanh vùng chụp. Chỉ cần làm một lần cho cả đợt.",
                "Còn một bước", MessageBoxButtons.OK, _settings.MsgIcon(MessageBoxIcon.Information));

        Hide();   // thu về tray, trên màn hình chỉ còn thanh nổi
    }

    void BuildTray()
    {
        // Dùng luôn icon đã nhúng trong exe (ApplicationIcon = app.ico) — khỏi giữ thêm file icon thứ hai.
        _tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        Icon = _tray.Icon;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Cửa sổ cấu hình", null, (_, _) => ShowConfigWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Bắt đầu chụp bằng chứng", null, (_, _) => StartClipboardMode());
        menu.Items.Add("Dừng đợt chụp", null, (_, _) => StopEvidence());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add($"Khoanh vùng chụp ({_settings.DefineRegionHotkey})", null, (_, _) => DefineRegion());
        menu.Items.Add($"Chụp toàn màn hình ({_settings.FullScreenHotkey})", null,
            (_, _) => PlainCapture(ScreenCapture.CursorScreenBounds()));
        menu.Items.Add($"Chụp cửa sổ hiện tại ({_settings.ActiveWindowHotkey})", null, (_, _) => CaptureActiveWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Thoát", null, (_, _) => Close());

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowConfigWindow();

        // Dùng chung menu với khay: đang chụp chỉ còn thanh nổi, mà icon khay hay bị Windows giấu.
        _evidence?.Bar.AttachMenu(menu);
    }

    void RegisterHotkeys()
    {
        _hotkeys.Dispose();
        _hotkeys = new HotkeyWindow();
        var defaults = new AppSettings();
        var failed = new List<string>();

        RegisterOne(_settings.DefineRegionHotkey, defaults.DefineRegionHotkey, "Khoanh vùng", DefineRegion, failed);
        RegisterOne(_settings.CaptureRegionHotkey, defaults.CaptureRegionHotkey, "Chụp", CaptureHotkey, failed);
        RegisterOne(_settings.FullScreenHotkey, defaults.FullScreenHotkey, "Chụp toàn màn hình",
            () => PlainCapture(ScreenCapture.CursorScreenBounds()), failed);
        RegisterOne(_settings.ActiveWindowHotkey, defaults.ActiveWindowHotkey, "Chụp cửa sổ", CaptureActiveWindow, failed);
        RegisterOne(_settings.ClearBreakpointsHotkey, defaults.ClearBreakpointsHotkey, "Xóa breakpoint",
            ClearBreakpointsHotkey, failed);
        RegisterOne(_settings.GotoCurrentHotkey, defaults.GotoCurrentHotkey, "Chạy cặp đang chọn",
            () => _evidence?.Run(), failed);
        RegisterOne(_settings.MoveArrowHotkey, defaults.MoveArrowHotkey, "Dời mũi tên vàng",
            () => _evidence?.MoveArrow(), failed);

        if (failed.Count > 0) Log("Hotkey: không đăng ký được: " + string.Join("; ", failed));
    }

    void ClearBreakpointsHotkey()
    {
        if (_evidence is { Active: true } session) { session.ClearBreakpoints(); return; }
        Run("Xóa breakpoint", dte => VsAutomation.CurrentClassFile(dte) is { } f && !string.IsNullOrWhiteSpace(f)
            ? VsAutomation.ClearBreakpointsInFile(dte, f)
            : "chưa mở file .cs nào trong VS.");
    }

    void RegisterOne(string spec, string fallback, string label, Action action, List<string> failed)
    {
        if (_hotkeys.Register(spec, action)) return;
        failed.Add(spec != fallback && _hotkeys.Register(fallback, action)
            ? $"{label} → dùng mặc định {fallback} vì \"{spec}\" hỏng/bị chiếm"
            : $"{label} ({spec})");
    }

    void DefineRegion()
    {
        // Có ClipboardWidth/HeightInches thì khung cỡ cố định (click để chốt), không thì kéo tự do.
        using var sel = new RegionSelector(_settings.ClipboardPixelSize);
        sel.ShowDialog();
        if (sel.Result is not { } r) return;
        _savedRegion = r;
        Log($"Đã nhớ vùng chụp {r.Width}x{r.Height} — từ giờ {_settings.CaptureRegionHotkey} chụp đúng vùng này.");
        if (_settings.ClipboardPixelSize == null)
            Log("Mẹo: điền ClipboardWidthInches / ClipboardHeightInches trong settings.json để khung chụp luôn cùng một cỡ.");
    }

    void CaptureHotkey()
    {
        if (_evidence is { Active: true } session) { session.CaptureCurrent(); return; }

        if (_savedRegion is not { } r) { Log($"Chụp: chưa khoanh vùng — bấm {_settings.DefineRegionHotkey} một lần."); return; }
        PlainCapture(r);
    }

    void CaptureActiveWindow()
    {
        if (ScreenCapture.ActiveWindowBounds() is not { } b) { Log("Chụp: không có cửa sổ hợp lệ."); return; }
        PlainCapture(b);
    }

    // Chụp thường (ngoài đợt bằng chứng) vẫn lưu PNG như QuickShot cũ.
    void PlainCapture(Rectangle region) => Log("Chụp: " + ScreenCapture.Grab(region, _settings, saveFile: true).Message);

    void ShowConfigWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    void StopEvidence()
    {
        if (_evidence is not { Active: true }) { Log("Chụp bằng chứng: không có đợt nào đang chạy."); return; }
        _evidence.Stop();
        Log("Chụp bằng chứng: đã dừng đợt.");
        ShowConfigWindow();
    }

    // Bấm X là thoát hẳn; muốn chạy nền thì bấm "▶ Khởi động", nút đó tự Hide() và giữ hotkey.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        _evidence?.Dispose();
        _hotkeys.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
    }

    // Hotkey copy là global nên không copy được chữ ra khỏi app — mọi dòng log phải có đường ra bằng file.
    internal void Log(string msg)
    {
        string line = $"{DateTime.Now:HH:mm:ss}  {msg}{Environment.NewLine}";
        _log.AppendText(line);
        Safe.Try(() => File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "sendkeydemo.log"), line));
    }
}
