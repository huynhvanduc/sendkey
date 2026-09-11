using EnvDTE;
using System.Runtime.InteropServices;

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
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 9f)
    };

    // --- mapping mode ---
    readonly TextBox _mappingPath = new() { Dock = DockStyle.Fill };
    readonly Button _mappingBrowse = new() { Text = "Browse...", AutoSize = true };
    readonly TextBox _targetCs = new() { Dock = DockStyle.Fill };
    readonly Button _targetBrowse = new() { Text = "Browse...", AutoSize = true };
    readonly Button _mappingOpen = new() { Text = "Mở", AutoSize = true };
    readonly Button _targetOpen = new() { Text = "Mở", AutoSize = true };
    readonly Button _checkMapping = new() { Text = "Kiểm tra", AutoSize = true };
    readonly Button _clearBpFile = new() { Text = "Xóa BP file này", AutoSize = true };
    readonly Button _copyWatch = new() { Text = "Copy Watch", AutoSize = true };
    readonly CheckBox _topMostBox = new() { Text = "Luôn nổi trên cùng", AutoSize = true, Checked = true };
    bool _loading;
    List<MapRow>? _mapRows;
    string _mapRowsPath = "";
    DateTime _mapRowsMtime;

    // --- chụp bằng chứng (gộp từ QuickShot) ---
    readonly Button _startEvidence = new() { Text = "▶  Khởi động" };
    readonly Button _advancedToggle = new() { Text = "▸  Công cụ khác" };
    Panel _advanced = new();
    Label _hotkeyHint = new();
    readonly NotifyIcon _tray = new() { Visible = true, Text = "SendKey Evidence" };
    HotkeyWindow _hotkeys = new();
    EvidenceSession? _evidence;
    AppSettings _settings = new();
    Rectangle? _savedRegion;
    bool _reallyExit;

    static FlowLayoutPanel ButtonCell(params Control[] buttons)
    {
        var cell = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
            WrapContents = false,
        };
        cell.Controls.AddRange(buttons);
        return cell;
    }

    static Label RowLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 0, 6, 0),
    };

    public MainForm()
    {
        Text = "SendKey Evidence";
        AutoScaleMode = AutoScaleMode.Dpi;

        // ---------- 1. Khối chuẩn bị: 3 thứ duy nhất cần trước khi chụp ----------
        _instances.Dock = DockStyle.Fill;

        var setup = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 3,
            AutoSize = true,
            Padding = new Padding(12, 12, 12, 4),
        };
        setup.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        setup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        setup.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var refreshCell = ButtonCell(_refresh);
        var mappingBtnCell = ButtonCell(_mappingBrowse, _mappingOpen, _checkMapping);
        var targetBtnCell = ButtonCell(_targetBrowse, _targetOpen);

        setup.Controls.Add(RowLabel("Visual Studio"), 0, 0);
        setup.Controls.Add(_instances, 1, 0);
        setup.Controls.Add(refreshCell, 2, 0);
        setup.Controls.Add(RowLabel("mapping.csv"), 0, 1);
        setup.Controls.Add(_mappingPath, 1, 1);
        setup.Controls.Add(mappingBtnCell, 2, 1);
        setup.Controls.Add(RowLabel("target .cs"), 0, 2);
        setup.Controls.Add(_targetCs, 1, 2);
        setup.Controls.Add(targetBtnCell, 2, 2);

        // ---------- 2. Hành động chính: một nút ----------
        _startEvidence.Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold);
        _startEvidence.Dock = DockStyle.Top;
        _startEvidence.AutoSize = true;
        _startEvidence.Padding = new Padding(0, 9, 0, 9);

        var hotkeyHint = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(2, 6, 2, 0),
        };
        _hotkeyHint = hotkeyHint;

        var primary = new Panel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 4, 12, 8) };
        primary.Controls.Add(hotkeyHint);
        primary.Controls.Add(_startEvidence);

        // ---------- 3. Công cụ khác: thu gọn, mặc định ẩn ----------
        _advancedToggle.Dock = DockStyle.Top;
        _advancedToggle.FlatStyle = FlatStyle.Flat;
        _advancedToggle.FlatAppearance.BorderSize = 0;
        _advancedToggle.TextAlign = ContentAlignment.MiddleLeft;
        _advancedToggle.AutoSize = true;
        _advancedToggle.TabStop = false;
        _advancedToggle.ForeColor = SystemColors.GrayText;
        _advancedToggle.Click += (_, _) => ToggleAdvanced();

        var adv = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            AutoSize = true,
            Padding = new Padding(12, 0, 12, 8),
        };
        adv.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        adv.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        adv.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

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

        _advanced = new Panel { Dock = DockStyle.Top, AutoSize = true, Visible = false };
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
        _browse.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Filter = "C# files (*.cs)|*.cs|All files (*.*)|*.*" };
            if (d.ShowDialog(this) == DialogResult.OK) _file.Text = d.FileName;
        };
        _goto.Click += (_, _) => Run("Go To Line", dte => VsAutomation.GoToLine(dte, _file.Text, (int)_line.Value));
        _bp.Click += (_, _) => Run("Toggle Breakpoint", dte => VsAutomation.ToggleBreakpoint(dte, _file.Text, (int)_line.Value));
        _clearBpFile.Click += (_, _) => Run("Xóa breakpoint", dte => VsAutomation.ClearBreakpointsInFile(dte, _file.Text));
        _addWatch.Click += (_, _) => Run("Add Watch", dte => VsAutomation.AddWatch(dte, _watch.Text));
        _copyWatch.Click += (_, _) => CopyWatch();
        _checkMapping.Click += (_, _) => KiemTraMapping();

        _mappingBrowse.Click += (_, _) => PickFile(_mappingPath, "CSV (*.csv)|*.csv|Tất cả (*.*)|*.*");
        _targetBrowse.Click += (_, _) => PickFile(_targetCs, "C# (*.cs)|*.cs|Tất cả (*.*)|*.*");
        _mappingOpen.Click += (_, _) => OpenInEditor(_mappingPath.Text);
        _targetOpen.Click += (_, _) => OpenInEditor(_targetCs.Text);
        _mappingPath.TextChanged += (_, _) => SaveSettings();
        _targetCs.TextChanged += (_, _) => SaveSettings();
        _topMostBox.CheckedChanged += (_, _) => { TopMost = _topMostBox.Checked; SaveSettings(); };
        _startEvidence.Click += (_, _) => StartClipboardMode();
        _instances.SelectedIndexChanged += (_, _) => _evidence?.AttachWatcher();

        Load += (_, _) =>
        {
            // Form được tạo ở DPI hệ thống (màn chính 125%) rồi mới hiện lên màn đang dùng; sang màn
            // 100% thì WinForms nhân mọi thứ ×0.8. Nên không đặt px cứng — quy từ đơn vị 96-dpi.
            MinimumSize = LogicalToDeviceUnits(new Size(680, 470));
            Size = LogicalToDeviceUnits(new Size(780, 580));
            CenterToScreen();

            _loading = true;
            _settings = AppSettings.Load();
            _mappingPath.Text = _settings.MappingPath ?? "";
            _targetCs.Text = _settings.TargetCsPath ?? "";
            _topMostBox.Checked = _settings.TopMost;
            TopMost = _settings.TopMost;
            _loading = false;

            _evidence = new EvidenceSession(this, _settings);
            _evidence.Bar.OpenConfigRequested += ShowConfigWindow;

            BuildTray();
            var hotkeyProblem = RegisterHotkeys();
            if (hotkeyProblem != null) Log("Hotkey: " + hotkeyProblem);
            RefreshHotkeyHint();

            VsAutomation.OleMessageFilter.Register();
            LoadInstances();
            _evidence.AttachWatcher();
        };
    }

    // ---- EvidenceSession (thanh chụp) gọi thẳng vào các hàm internal của cửa sổ này ----

    internal DTE? CurrentDte() => (_instances.SelectedItem as VsInstance)?.Dte;
    internal Rectangle? SavedRegion => _savedRegion;

    internal string? AddMapping(string cmdLabel, string cmdVar, string csLabel, string csVar)
    {
        var mappingCsv = _mappingPath.Text.Trim();
        if (mappingCsv.Length == 0 || !File.Exists(mappingCsv)) return "Chưa trỏ mapping.csv ở cửa sổ cấu hình.";

        var cmdL = Mapping.CleanLabel(cmdLabel);
        var cmdV = cmdVar.Trim();
        var csL = Mapping.CleanLabel(csLabel);
        var csV = csVar.Trim();
        if (cmdL.Length == 0) return "cmdLabel đang trống.";
        if (csL.Length == 0 || (cmdV.Length > 0 && csV.Length == 0)) return "csharpLabel / csharpVar không được để trống.";   // ca goto: chỉ cần label

        var csPath = _targetCs.Text.Trim();
        if (csPath.Length == 0 || !File.Exists(csPath)) return $"Không thấy file .cs đích: {csPath}";

        var ll = Mapping.FindLabelLine(csPath, csL, csV);
        switch (ll.Kind)
        {
            case LabelLineKind.NotFound:
                return $"Không thấy \"{csL}:\" trong {Path.GetFileName(csPath)}.";
            case LabelLineKind.Multiple:
                return $"\"{csL}:\" xuất hiện ở dòng {string.Join(", ", ll.MatchLines!)} — sửa code hoặc dùng csharpFile.";
            case LabelLineKind.NoExecutableLine:
                return $"Sau \"{csL}:\" không còn dòng thực thi.";
            case LabelLineKind.AnchorNotFound:
                return $"Không thấy biểu thức \"{csV}\" sau \"{csL}:\".";
        }

        try
        {
            Mapping.AppendRow(mappingCsv, new MapRow(cmdL, cmdV, csL, csV, 0));
            _mapRows = null;   // buộc GetMapRows nạp lại
            Log($"ĐÃ THÊM mapping: {cmdL} / {cmdV} → {csL} / {csV} — kiểm tra lại bản dịch csharpVar.");
            return null;
        }
        catch (IOException)
        {
            return "Không ghi được mapping.csv (đang mở trong Excel?) — đóng Excel rồi Enter lại.";
        }
        catch (Exception ex)
        {
            return "Lỗi ghi mapping.csv: " + ex.Message;
        }
    }

    void SaveSettings()
    {
        if (_loading) return;
        _settings.MappingPath = _mappingPath.Text;
        _settings.TargetCsPath = _targetCs.Text;
        _settings.TopMost = _topMostBox.Checked;
        _settings.Save();
    }

    void OpenInEditor(string path)
    {
        path = path.Trim();
        if (path.Length == 0 || !File.Exists(path)) { Log($"Mở file: không thấy {path}"); return; }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log("Mở file: lỗi — " + ex.Message);
        }
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

    internal List<MapRow>? GetMapRows()
    {
        var path = _mappingPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
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
        catch (MappingFormatException ex)
        {
            Log(ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            Log("Lỗi đọc mapping.csv — " + ex.Message);
            return null;
        }
    }

    void CopyWatch()
    {
        var t = _watch.Text.Trim();
        if (t.Length == 0) { Log("Copy Watch: ô Watch đang trống."); return; }
        try
        {
            Clipboard.SetText(t);
            Log($"Copy Watch: đã copy \"{t}\" — dán (Ctrl+V) vào cửa sổ Watch của VS.");
        }
        catch (Exception ex)
        {
            Log("Copy Watch: lỗi clipboard — " + ex.Message);
        }
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

        var problems = Mapping.Validate(rows, LinesFor);
        if (problems.Count == 0)
        {
            Log($"Kiểm tra mapping.csv: OK — {rows.Count} dòng, không thấy vấn đề.");
            return;
        }
        Log($"Kiểm tra mapping.csv: {problems.Count} vấn đề / {rows.Count} dòng:");
        foreach (var p in problems) Log("  - " + p);
    }

    internal string EffectiveCsPath(MapRow row)
    {
        var f = row.CsharpFile.Trim();
        if (f.Length == 0) return _targetCs.Text.Trim();
        if (Path.IsPathRooted(f)) return f;
        var baseDir = Path.GetDirectoryName(_mappingPath.Text.Trim()) ?? "";
        try { return Path.GetFullPath(Path.Combine(baseDir, f)); }
        catch { return f; }
    }

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

        _goto.Enabled = _bp.Enabled = _clearBpFile.Enabled = _addWatch.Enabled = any;
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

    // ==================== chụp bằng chứng: tray + hotkey ====================

    // Dùng luôn icon đã nhúng trong exe (ApplicationIcon = app.ico) — khỏi giữ thêm file icon thứ hai.
    static Icon LoadAppIcon() => Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

    void ToggleAdvanced()
    {
        _advanced.Visible = !_advanced.Visible;
        _advancedToggle.Text = _advanced.Visible ? "▾  Công cụ khác" : "▸  Công cụ khác";
        // Nút Flat đang giữ focus sẽ vẽ viền đen cả hàng — chuyển focus đi (mở ra thì vào ô File).
        ActiveControl = _advanced.Visible ? _file : null;
    }

    void RefreshHotkeyHint()
        => _hotkeyHint.Text =
            $"Trong đợt:   {_settings.DefineRegionHotkey} khoanh vùng (1 lần)   ·   " +
            $"{_settings.GotoCurrentHotkey} đặt breakpoint   ·   {_settings.CaptureRegionHotkey} chụp";

    /// <summary>Nút chính — vào thẳng chế độ copy từ Excel, không qua hộp thoại nào.</summary>
    void StartClipboardMode()
    {
        if (_evidence == null) return;

        if (_mappingPath.Text.Trim() is var m && (m.Length == 0 || !File.Exists(m)))
        {
            MessageBox.Show(this, "Chưa trỏ mapping.csv ở trên.", "Thiếu đường dẫn",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_targetCs.Text.Trim() is var t && (t.Length == 0 || !File.Exists(t)))
        {
            MessageBox.Show(this, "Chưa trỏ file .cs đích ở trên.", "Thiếu đường dẫn",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _evidence.Start();
        AfterEvidenceStart();
    }

    void AfterEvidenceStart()
    {
        if (_savedRegion == null)
            MessageBox.Show(this,
                $"Sắp cửa sổ VS sao cho thấy tab tên file, dòng code và cửa sổ Watch, rồi bấm {_settings.DefineRegionHotkey} " +
                "để khoanh vùng chụp. Chỉ cần làm một lần cho cả đợt.",
                "Còn một bước", MessageBoxButtons.OK, MessageBoxIcon.Information);

        Hide();   // thu về tray, trên màn hình chỉ còn thanh nổi
    }

    void BuildTray()
    {
        _tray.Icon = LoadAppIcon();
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
        menu.Items.Add("Thoát", null, (_, _) => { _reallyExit = true; Close(); });

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowConfigWindow();
    }

    string? RegisterHotkeys()
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
        RegisterOne(_settings.GotoCurrentHotkey, defaults.GotoCurrentHotkey, "Chạy cặp đang chọn",
            () => _evidence?.Run(), failed);

        return failed.Count > 0 ? "không đăng ký được: " + string.Join("; ", failed) : null;
    }

    void RegisterOne(string spec, string fallback, string label, Action action, List<string> failed)
    {
        if (HotkeyParser.TryParse(spec, out var mod, out var key) && _hotkeys.Register(mod, key, action)) return;

        if (spec != fallback &&
            HotkeyParser.TryParse(fallback, out var fmod, out var fkey) && _hotkeys.Register(fmod, fkey, action))
            failed.Add($"{label} → dùng mặc định {fallback} vì \"{spec}\" hỏng/bị chiếm");
        else
            failed.Add($"{label} ({spec})");
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

    /// <summary>Phím chụp: đang trong đợt bằng chứng thì chụp CÓ GÁC CỔNG, ngoài đợt thì chụp thường.</summary>
    void CaptureHotkey()
    {
        if (_evidence is { Active: true } session) { session.CaptureCurrent(); return; }

        if (_savedRegion is not { } r)
        {
            Log($"Chụp: chưa khoanh vùng — bấm {_settings.DefineRegionHotkey} một lần.");
            return;
        }
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

    // Bấm X = thu về tray (app còn sống để hotkey vẫn chạy). Thoát hẳn chỉ qua menu tray.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            _tray.BalloonTipTitle = "Vẫn đang chạy";
            _tray.BalloonTipText = "App thu về khay hệ thống, hotkey vẫn hoạt động. Thoát hẳn: chuột phải icon → Thoát.";
            _tray.ShowBalloonTip(2000);
            return;
        }

        base.OnFormClosing(e);
        _evidence?.Dispose();
        _hotkeys.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
    }

    internal void Log(string msg) =>
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}{Environment.NewLine}");
}
