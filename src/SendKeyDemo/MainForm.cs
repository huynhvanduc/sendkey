using System.Runtime.InteropServices;
using EnvDTE;
using QuickShot;

namespace SendKeyDemo;

public class MainForm : Form, IEvidenceHost
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
    readonly Button _mappingOpen = new() { Text = "Mở", AutoSize = true };
    readonly Button _targetOpen = new() { Text = "Mở", AutoSize = true };
    readonly Button _run = new() { Text = "Tra && Chạy", AutoSize = true };   // && = chữ "&", không phải phím tắt
    readonly Button _batch = new() { Text = "Batch…", AutoSize = true };
    readonly Button _recentBtn = new() { Text = "Gần đây ▾", AutoSize = true };
    readonly ContextMenuStrip _recentMenu = new();
    readonly List<string> _recent = new();
    readonly Button _checkMapping = new() { Text = "Kiểm tra mapping.csv", AutoSize = true };
    readonly Button _clearBpFile = new() { Text = "Xóa BP file này", AutoSize = true };
    readonly Button _copyWatch = new() { Text = "Copy Watch", AutoSize = true };
    readonly CheckBox _topMostBox = new() { Text = "Luôn nổi trên cùng", AutoSize = true, Checked = true };
    readonly CheckBox _lookupOnlyBox = new() { Text = "Chỉ tra (không cần VS)", AutoSize = true };
    bool _loading;
    List<MapRow>? _mapRows;
    string _mapRowsPath = "";
    DateTime _mapRowsMtime;

    // --- chụp bằng chứng (gộp từ QuickShot) ---
    readonly Button _startEvidence = new() { Text = "▶  Bắt đầu chụp bằng chứng" };
    readonly Button _worklistBtn = new() { Text = "Chạy theo danh sách…", AutoSize = true };
    readonly Button _advancedToggle = new() { Text = "▸  Công cụ khác" };
    Panel _advanced = new();
    Label _hotkeyHint = new();
    readonly NotifyIcon _tray = new() { Visible = true, Text = "SendKey Evidence" };
    HotkeyWindow _hotkeys = new();
    EvidenceSession? _evidence;
    AppSettings _settings = new();
    Rectangle? _savedRegion;      // vùng chụp đã khoanh, dùng lại cho mọi lần chụp
    bool _reallyExit;             // false = bấm X thì thu về tray, không thoát

    /// <summary>Ô chứa nút bên phải hàng — GrowAndShrink để cột AutoSize đo đúng, không đẩy tràn khung.</summary>
    static FlowLayoutPanel ButtonCell(params Control[] buttons)
    {
        var cell = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0), WrapContents = false,
        };
        cell.Controls.AddRange(buttons);
        return cell;
    }

    // Nhãn AutoSize trong cột AutoSize: tự đo theo font + DPI. Cột px cứng bị co ×0.8 khi cửa sổ
    // chuyển từ màn 125% sang màn 100% và cắt chữ thành "Visual Stu…".
    static Label RowLabel(string text) => new()
    {
        Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 0, 6, 0),
    };

    public MainForm()
    {
        Text = "SendKey Evidence";
        AutoScaleMode = AutoScaleMode.Dpi;
        // Kích thước cửa sổ đặt ở Load (xem lý do ở đó). Ở đây tránh số px cứng — dùng AutoSize.

        // ---------- 1. Khối chuẩn bị: 3 thứ duy nhất cần trước khi chụp ----------
        _instances.Dock = DockStyle.Fill;

        var setup = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 3, RowCount = 3, AutoSize = true,
            Padding = new Padding(12, 12, 12, 4),
        };
        setup.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        setup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        setup.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var refreshCell = ButtonCell(_refresh);
        var mappingBtnCell = ButtonCell(_mappingBrowse, _mappingOpen);
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
            Dock = DockStyle.Top, AutoSize = true, ForeColor = SystemColors.GrayText,
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
            Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true, Padding = new Padding(12, 0, 12, 8),
        };
        adv.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        adv.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        adv.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var mapBtns = ButtonCell(_run, _recentBtn, _batch, _worklistBtn, _checkMapping);
        var vsBtns = ButtonCell(_goto, _bp, _clearBpFile, _addWatch, _copyWatch);

        adv.Controls.Add(RowLabel("cmdLabel"), 0, 0);
        adv.Controls.Add(_cmdLabel, 1, 0);
        adv.Controls.Add(RowLabel("cmdVar"), 0, 1);
        adv.Controls.Add(_cmdVar, 1, 1);
        adv.Controls.Add(mapBtns, 1, 2);
        adv.SetColumnSpan(mapBtns, 2);   // hàng nút dài: cho lấn sang cột Browse, không bị cắt
        adv.Controls.Add(RowLabel("File"), 0, 3);
        adv.Controls.Add(_file, 1, 3);
        adv.Controls.Add(_browse, 2, 3);
        adv.Controls.Add(RowLabel("Line"), 0, 4);
        // Bọc trong ô AutoSize: khối này layout lúc còn ẩn (cột rộng 0) nên ô Line rộng cố định bị ép còn 1 vạch.
        adv.Controls.Add(ButtonCell(_line), 1, 4);
        adv.Controls.Add(RowLabel("Watch"), 0, 5);
        adv.Controls.Add(_watch, 1, 5);
        adv.Controls.Add(vsBtns, 1, 6);
        adv.SetColumnSpan(vsBtns, 2);
        adv.Controls.Add(_lookupOnlyBox, 1, 7);

        _advanced = new Panel { Dock = DockStyle.Top, AutoSize = true, Visible = false };
        _advanced.Controls.Add(adv);

        // ---------- 4. Log + chân cửa sổ ----------
        var bottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(12, 0, 12, 6),
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
        _batch.Click += (_, _) => BatchDialog();
        _recentBtn.Click += (_, _) => ShowRecentMenu();
        _lookupOnlyBox.CheckedChanged += (_, _) =>
            _run.Enabled = _batch.Enabled = _lookupOnlyBox.Checked || _instances.SelectedItem is VsInstance;

        _mappingBrowse.Click += (_, _) => PickFile(_mappingPath, "CSV (*.csv)|*.csv|Tất cả (*.*)|*.*");
        _targetBrowse.Click += (_, _) => PickFile(_targetCs, "C# (*.cs)|*.cs|Tất cả (*.*)|*.*");
        _mappingOpen.Click += (_, _) => OpenInEditor(_mappingPath.Text);
        _targetOpen.Click += (_, _) => OpenInEditor(_targetCs.Text);
        _mappingPath.TextChanged += (_, _) => SaveSettings();
        _targetCs.TextChanged += (_, _) => SaveSettings();
        _topMostBox.CheckedChanged += (_, _) => { TopMost = _topMostBox.Checked; SaveSettings(); };
        _run.Click += (_, _) => TraVaChay();
        _startEvidence.Click += (_, _) => StartClipboardMode();
        _worklistBtn.Click += (_, _) => EvidenceDialog();
        _cmdLabel.KeyDown += MappingKeyDown;
        _cmdVar.KeyDown += MappingKeyDown;
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
            _recent.AddRange(_settings.RecentLookups);
            _recentBtn.Enabled = _recent.Count > 0;
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

    DTE? CurrentDte() => (_instances.SelectedItem as VsInstance)?.Dte;

    // ---- IEvidenceHost: những gì EvidenceSession cần từ cửa sổ này ----

    DTE? IEvidenceHost.Dte => CurrentDte();
    IReadOnlyList<MapRow>? IEvidenceHost.Rows => GetMapRows();
    string IEvidenceHost.CsPathOf(MapRow row) => EffectiveCsPath(row);
    Rectangle? IEvidenceHost.Region => _savedRegion;
    void IEvidenceHost.Log(string msg) => Log(msg);

    /// <summary>
    /// Ghi thêm 1 dòng mapping từ thanh chụp bằng chứng (không popup): validate csharpLabel/csharpVar
    /// với file .cs đích trước, hỏng thì trả lý do để thanh hiện lên. Dòng mới dùng ô "target .cs"
    /// (muốn cột csharpFile riêng thì thêm qua "Tra & Chạy").
    /// </summary>
    string? IEvidenceHost.AddMapping(string cmdLabel, string cmdVar, string csLabel, string csVar)
    {
        var mappingCsv = _mappingPath.Text.Trim();
        if (mappingCsv.Length == 0 || !File.Exists(mappingCsv)) return "Chưa trỏ mapping.csv ở cửa sổ cấu hình.";

        var cmdL = Mapping.CleanLabel(cmdLabel);
        var cmdV = cmdVar.Trim();
        var csL = Mapping.CleanLabel(csLabel);
        var csV = csVar.Trim();
        if (cmdL.Length == 0) return "cmdLabel đang trống.";
        if (csL.Length == 0 || csV.Length == 0) return "csharpLabel / csharpVar không được để trống.";

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

    /// <summary>
    /// Tra 1 cặp cmdLabel/cmdVar CÓ TƯƠNG TÁC — dùng chung cho "Tra & Chạy" và chế độ chụp bằng chứng:
    /// không thấy label → mở form "Thêm mapping mới" (ghi thêm dòng vào mapping.csv rồi chạy tiếp);
    /// cmdVar không khớp → cho chọn trong danh sách biến của label đó (kèm "+ Thêm biến mới…").
    /// </summary>
    (MapRow? Row, int Line, string? Error) ResolveInteractive(string labelRaw, string? varRaw, out bool varEstablished)
    {
        varEstablished = false;

        var rows = GetMapRows();
        if (rows == null) return (null, 0, "không nạp được mapping.csv");

        var csPath = _targetCs.Text.Trim();
        var res = Mapping.Resolve(rows, labelRaw, varRaw);

        MapRow row;
        switch (res.Kind)
        {
            case LookupKind.Duplicate:
                return (null, 0, $"mapping trùng dòng {string.Join(", ", res.DuplicateLines!)}");

            case LookupKind.NotFoundLabel:
            {
                var added = AddMappingDialog(csPath, _mappingPath.Text.Trim(), labelRaw, varRaw ?? "", rows);
                if (added == null) return (null, 0, "đã hủy thêm mapping");
                row = SaveAddedRow(added);
                varEstablished = true;
                break;
            }

            case LookupKind.NeedPickVar:
            {
                var pick = PickFromList($"Chọn biến của label {labelRaw}", res.VarChoices!, out var addNew);
                if (addNew)
                {
                    var added = AddMappingDialog(csPath, _mappingPath.Text.Trim(), labelRaw, varRaw ?? "", rows);
                    if (added == null) return (null, 0, "đã hủy thêm mapping");
                    row = SaveAddedRow(added);
                }
                else
                {
                    if (pick == null) return (null, 0, "đã hủy chọn biến");
                    _cmdVar.Text = pick;
                    var re = Mapping.Resolve(rows, labelRaw, pick);
                    if (re.Kind != LookupKind.Ok) return (null, 0, "vẫn không khớp sau khi chọn biến");
                    row = re.Row!;
                }
                varEstablished = true;
                break;
            }

            default:
                row = res.Row!;
                if (res.Warning != null) Log("Tra: " + res.Warning);
                break;
        }

        var csp = EffectiveCsPath(row);
        if (string.IsNullOrWhiteSpace(csp) || !File.Exists(csp))
            return (null, 0, $"không thấy file .cs cho dòng này: {csp}");

        var ll = Mapping.FindLabelLine(csp, row.CsharpLabel, row.CsharpVar);
        return ll.Kind switch
        {
            LabelLineKind.NotFound =>
                (null, 0, $"không thấy \"{row.CsharpLabel}:\" trong {Path.GetFileName(csp)}"),
            LabelLineKind.Multiple =>
                (null, 0, $"\"{row.CsharpLabel}:\" xuất hiện ở dòng {string.Join(", ", ll.MatchLines!)}"),
            LabelLineKind.NoExecutableLine =>
                (null, 0, $"sau \"{row.CsharpLabel}:\" không còn dòng thực thi"),
            LabelLineKind.AnchorNotFound =>
                (null, 0, $"không thấy biểu thức \"{row.CsharpVar}\" sau \"{row.CsharpLabel}:\""),
            _ => (row, ll.Line, null),
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
        // Sửa trên đối tượng đã nạp, KHÔNG tạo mới — tạo mới sẽ xóa mất phần cấu hình hotkey/chụp.
        _settings.MappingPath = _mappingPath.Text;
        _settings.TargetCsPath = _targetCs.Text;
        _settings.TopMost = _topMostBox.Checked;
        _settings.RecentLookups = _recent.ToArray();
        _settings.Save();
    }

    void PushRecent(string label, string var)
    {
        label = label.Trim();
        if (label.Length == 0) return;
        var key = label + "\t" + var.Trim();
        _recent.RemoveAll(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
        _recent.Insert(0, key);
        if (_recent.Count > 20) _recent.RemoveRange(20, _recent.Count - 20);
        _recentBtn.Enabled = true;
        SaveSettings();
    }

    void ShowRecentMenu()
    {
        _recentMenu.Items.Clear();
        foreach (var entry in _recent)
        {
            var parts = entry.Split('\t');
            var lbl = parts[0];
            var vr = parts.Length > 1 ? parts[1] : "";
            var text = vr.Length == 0 ? lbl : $"{lbl}   |   {vr}";
            if (text.Length > 80) text = text[..80] + "…";
            _recentMenu.Items.Add(text, null, (_, _) =>
            {
                _cmdLabel.Text = lbl;
                _cmdVar.Text = vr;
                _cmdLabel.Focus();
            });
        }
        if (_recentMenu.Items.Count > 0)
            _recentMenu.Show(_recentBtn, new Point(0, _recentBtn.Height));
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

    static string? PickFromList(string title, IReadOnlyList<string> items, out bool addNew)
    {
        bool wantAdd = false;
        using var dlg = new Form
        {
            Text = title, Width = 440, Height = 340,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, TopMost = true
        };
        var list = new ListBox { Dock = DockStyle.Fill };
        foreach (var it in items) list.Items.Add(it);
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 32 };
        var add = new Button { Text = "+ Thêm biến mới…", Dock = DockStyle.Bottom, Height = 32 };
        add.Click += (_, _) => { wantAdd = true; dlg.Close(); };
        list.DoubleClick += (_, _) => { if (list.SelectedItem != null) ok.PerformClick(); };
        dlg.Controls.Add(list);
        dlg.Controls.Add(add);
        dlg.Controls.Add(ok);
        dlg.AcceptButton = ok;
        var picked = dlg.ShowDialog() == DialogResult.OK ? list.SelectedItem as string : null;
        addNew = wantAdd;
        return wantAdd ? null : picked;
    }

    /// <summary>Form nhập 1 dòng mapping mới; validate csharpLabel với file .cs (theo csharpFile nếu có) trước khi trả về.</summary>
    static MapRow? AddMappingDialog(string csPath, string mappingCsvPath, string cmdLabelPrefill, string cmdVarPrefill,
        IReadOnlyList<MapRow> rows)
    {
        var tbCmdLabel = new TextBox { Text = cmdLabelPrefill, Dock = DockStyle.Fill };
        var tbCmdVar = new TextBox { Text = cmdVarPrefill, Dock = DockStyle.Fill };
        var tbCsLabel = new TextBox { Dock = DockStyle.Fill };
        var tbCsVar = new TextBox { Dock = DockStyle.Fill };
        var tbCsFile = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "trống = dùng ô 'target .cs'" };
        var err = new Label
        {
            ForeColor = Color.Firebrick, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
        };

        var grid = new TableLayoutPanel { ColumnCount = 2, RowCount = 6, Dock = DockStyle.Fill, Padding = new Padding(8) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        void AddRow(int r, string cap, Control c)
        {
            grid.Controls.Add(new Label { Text = cap, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) }, 0, r);
            grid.Controls.Add(c, 1, r);
        }
        AddRow(0, "cmdLabel", tbCmdLabel);
        AddRow(1, "cmdVar", tbCmdVar);
        AddRow(2, "csharpLabel", tbCsLabel);
        AddRow(3, "csharpVar", tbCsVar);
        AddRow(4, "csharpFile", tbCsFile);
        grid.Controls.Add(err, 1, 5);

        var ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(84, 28), Margin = new Padding(6, 0, 0, 0), Enabled = false };
        var cancel = new Button { Text = "Hủy", AutoSize = true, MinimumSize = new Size(84, 28), DialogResult = DialogResult.Cancel };
        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true, Padding = new Padding(8)
        };
        btnRow.Controls.Add(ok);       // phải nhất
        btnRow.Controls.Add(cancel);   // bên trái OK

        using var dlg = new Form
        {
            Text = "Thêm mapping mới", Width = 480, Height = 320,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, TopMost = true
        };
        MapRow? result = null;

        void Revalidate(object? s, EventArgs e)
        {
            err.Text = "";
            ok.Enabled = tbCsLabel.Text.Trim().Length > 0 && tbCsVar.Text.Trim().Length > 0;
        }
        tbCmdLabel.TextChanged += Revalidate;
        tbCmdVar.TextChanged += Revalidate;
        tbCsLabel.TextChanged += Revalidate;
        tbCsVar.TextChanged += Revalidate;

        ok.Click += (_, _) =>
        {
            var cmdL = Mapping.CleanLabel(tbCmdLabel.Text);
            var cmdV = tbCmdVar.Text.Trim();
            var csL = Mapping.CleanLabel(tbCsLabel.Text);
            var csV = tbCsVar.Text.Trim();
            tbCmdLabel.Text = cmdL;
            tbCsLabel.Text = csL;

            if (cmdL.Length == 0) { err.Text = "cmdLabel: không được để trống."; tbCmdLabel.Focus(); return; }

            var chk = Mapping.Resolve(rows, cmdL, cmdV.Length == 0 ? null : cmdV);
            if (chk.Kind is LookupKind.Ok or LookupKind.Duplicate)
            {
                err.Text = "Cặp cmdLabel + cmdVar này đã có trong mapping.csv.";
                tbCmdVar.Focus();
                return;
            }

            var csFile = tbCsFile.Text.Trim();
            string effCs;
            if (csFile.Length == 0) effCs = csPath;
            else if (Path.IsPathRooted(csFile)) effCs = csFile;
            else effCs = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(mappingCsvPath) ?? "", csFile));
            if (string.IsNullOrWhiteSpace(effCs) || !File.Exists(effCs))
            {
                err.Text = $"csharpFile: không thấy file .cs: {effCs}";
                tbCsFile.Focus(); return;
            }

            var ll = Mapping.FindLabelLine(effCs, csL, csV);
            switch (ll.Kind)
            {
                case LabelLineKind.NotFound:
                    err.Text = $"csharpLabel: không thấy \"{csL}:\" trong {Path.GetFileName(effCs)}.";
                    tbCsLabel.Focus(); return;
                case LabelLineKind.Multiple:
                    err.Text = $"csharpLabel: \"{csL}:\" xuất hiện ở dòng {string.Join(", ", ll.MatchLines!)}.";
                    tbCsLabel.Focus(); return;
                case LabelLineKind.NoExecutableLine:
                    err.Text = $"csharpLabel: sau \"{csL}:\" không còn dòng thực thi.";
                    tbCsLabel.Focus(); return;
                case LabelLineKind.AnchorNotFound:
                    err.Text = $"csharpVar: không thấy biểu thức \"{csV}\" sau \"{csL}:\".";
                    tbCsVar.Focus(); return;
            }

            result = new MapRow(cmdL, cmdV, csL, csV, 0, csFile);
            dlg.DialogResult = DialogResult.OK;
            dlg.Close();
        };

        dlg.Controls.Add(grid);
        dlg.Controls.Add(btnRow);
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        dlg.Shown += (_, _) => tbCsLabel.Focus();

        return dlg.ShowDialog() == DialogResult.OK ? result : null;
    }

    MapRow SaveAddedRow(MapRow r)
    {
        _cmdLabel.Text = r.CmdLabel;
        _cmdVar.Text = r.CmdVar;
        try
        {
            Mapping.AppendRow(_mappingPath.Text.Trim(), r);
            _mapRows = null;   // buộc GetMapRows nạp lại
            var fileNote = r.CsharpFile.Length > 0 ? $" [{r.CsharpFile}]" : "";
            Log($"ĐÃ THÊM mapping: {r.CmdLabel} / {r.CmdVar} → {r.CsharpLabel} / {r.CsharpVar}{fileNote} — kiểm tra lại bản dịch csharpVar.");
        }
        catch (IOException)
        {
            Log("Tra & Chạy: KHÔNG ghi được mapping.csv (đang mở trong Excel?). Vẫn chạy tiếp với dòng vừa nhập.");
        }
        catch (Exception ex)
        {
            Log("Tra & Chạy: lỗi ghi mapping.csv — " + ex.Message + ". Vẫn chạy tiếp.");
        }
        return r;
    }

    void TraVaChay()
    {
        bool lookupOnly = _lookupOnlyBox.Checked;
        if (!lookupOnly && _instances.SelectedItem is not VsInstance)
        {
            Log("Tra & Chạy: chưa chọn instance VS (hoặc bật \"Chỉ tra\").");
            return;
        }

        var rows = GetMapRows();
        if (rows == null) return;

        var csPath = _targetCs.Text.Trim();   // mặc định cho dòng mapping không có csharpFile
        if ((string.IsNullOrWhiteSpace(csPath) || !File.Exists(csPath))
            && rows.All(r => r.CsharpFile.Length == 0))
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

        var (row, line, error) = ResolveInteractive(labelRaw, labelOnly ? null : varRaw, out var varEstablished);
        if (error != null || row == null)
        {
            Log("Tra & Chạy: " + error + ".");
            return;
        }
        if (varEstablished) labelOnly = false;

        var csp = EffectiveCsPath(row);            // dòng có csharpFile → dùng file đó, không thì ô "target .cs"

        _file.Text = csp;
        _line.Value = Math.Min(line, (int)_line.Maximum);
        if (!labelOnly) _watch.Text = row.CsharpVar;
        PushRecent(_cmdLabel.Text, _cmdVar.Text);

        if (lookupOnly)
        {
            Log($"Chỉ tra: {labelRaw} → {Path.GetFileName(csp)}:{line}"
                + (labelOnly ? "" : $", watch \"{row.CsharpVar}\"")
                + " — đã điền File/Line/Watch (không thao tác VS).");
            return;
        }

        Run("Go To Line", dte => VsAutomation.GoToLine(dte, csp, line));
        Run("Breakpoint", dte => VsAutomation.EnsureBreakpoint(dte, csp, line));

        Log(labelOnly
            ? $"mapping: {labelRaw} → {Path.GetFileName(csp)}:{line} — breakpoint sẵn sàng (F5 để dừng lại)."
            : $"mapping: {labelRaw}/{_cmdVar.Text} → {Path.GetFileName(csp)}:{line}, watch \"{row.CsharpVar}\" — F5 dừng ở breakpoint rồi bấm Add Watch.");
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

    /// <summary>Đường dẫn .cs hiệu lực cho 1 dòng: CsharpFile (tuyệt đối, hoặc tương đối theo thư mục mapping.csv), rỗng = ô "target .cs".</summary>
    string EffectiveCsPath(MapRow row)
    {
        var f = row.CsharpFile.Trim();
        if (f.Length == 0) return _targetCs.Text.Trim();
        if (Path.IsPathRooted(f)) return f;
        var baseDir = Path.GetDirectoryName(_mappingPath.Text.Trim()) ?? "";
        try { return Path.GetFullPath(Path.Combine(baseDir, f)); }
        catch { return f; }
    }

    /// <summary>Resolve không tương tác: (row, dòng breakpoint) hoặc Error mô tả vì sao hỏng.</summary>
    (MapRow? Row, int Line, string? Error) ResolveRowToLine(IReadOnlyList<MapRow> rows, string labelRaw, string? varRaw)
    {
        var res = Mapping.Resolve(rows, labelRaw, varRaw);
        switch (res.Kind)
        {
            case LookupKind.NotFoundLabel:
                return (null, 0, "không thấy label trong mapping.csv");
            case LookupKind.Duplicate:
                return (null, 0, $"mapping trùng dòng {string.Join(", ", res.DuplicateLines!)}");
            case LookupKind.NeedPickVar:
                return (null, 0, $"cmdVar không khớp (biến hợp lệ: {string.Join(", ", res.VarChoices!)})");
        }
        var row = res.Row!;
        var csp = EffectiveCsPath(row);
        if (string.IsNullOrWhiteSpace(csp) || !File.Exists(csp))
            return (null, 0, $"không thấy file .cs: {csp}");
        var ll = Mapping.FindLabelLine(csp, row.CsharpLabel, row.CsharpVar);
        return ll.Kind switch
        {
            LabelLineKind.NotFound => (null, 0, $"không thấy \"{row.CsharpLabel}:\" trong {Path.GetFileName(csp)}"),
            LabelLineKind.Multiple => (null, 0, $"\"{row.CsharpLabel}:\" xuất hiện nhiều lần trong {Path.GetFileName(csp)}"),
            LabelLineKind.NoExecutableLine => (null, 0, $"sau \"{row.CsharpLabel}:\" không còn dòng thực thi"),
            LabelLineKind.AnchorNotFound => (null, 0, $"không thấy biểu thức \"{row.CsharpVar}\" sau \"{row.CsharpLabel}:\""),
            _ => (row, ll.Line, null),
        };
    }

    void BatchDialog()
    {
        var rows = GetMapRows();
        if (rows == null) return;
        bool lookupOnly = _lookupOnlyBox.Checked;
        if (!lookupOnly && _instances.SelectedItem is not VsInstance)
        {
            Log("Batch: chưa chọn instance VS (hoặc bật \"Chỉ tra\").");
            return;
        }

        var input = new TextBox
        {
            Multiline = true, Dock = DockStyle.Top, Height = 150, AcceptsTab = false,
            ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f)
        };
        var output = new TextBox
        {
            Multiline = true, Dock = DockStyle.Fill, ReadOnly = true,
            ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f)
        };
        var hint = new Label
        {
            Text = "Mỗi dòng: cmdLabel <Tab> cmdVar  (trống cmdVar = breakpoint ở label; dòng trống / bắt đầu bằng # bị bỏ qua)",
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2, 4, 2, 4)
        };
        var runBtn = new Button { Text = "Chạy", AutoSize = true, Margin = new Padding(0, 0, 6, 0) };
        var closeBtn = new Button { Text = "Đóng", AutoSize = true, DialogResult = DialogResult.Cancel };

        runBtn.Click += (_, _) =>
        {
            var lines = input.Lines
                .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("#"))
                .ToArray();
            int ok = 0, fail = 0, firstLine = 0;
            string firstCs = "";
            var sb = new System.Text.StringBuilder();
            foreach (var rawLine in lines)
            {
                var (lbl, v) = Mapping.SplitBatchLine(rawLine);
                var r = ResolveRowToLine(rows, lbl, v.Length == 0 ? null : v);
                if (r.Error != null) { sb.AppendLine($"[LỖI] {rawLine.Trim()} — {r.Error}"); fail++; continue; }

                var csp = EffectiveCsPath(r.Row!);
                sb.AppendLine($"[OK]  {rawLine.Trim()} → {Path.GetFileName(csp)}:{r.Line}");
                ok++;
                if (firstLine == 0) { firstLine = r.Line; firstCs = csp; }
                if (!lookupOnly)
                    Run("Breakpoint", dte => VsAutomation.EnsureBreakpoint(dte, csp, r.Line));
            }
            if (!lookupOnly && firstLine > 0)
                Run("Go To Line", dte => VsAutomation.GoToLine(dte, firstCs, firstLine));

            sb.AppendLine();
            sb.AppendLine($"Tổng: {ok} OK, {fail} lỗi / {lines.Length} dòng."
                + (lookupOnly ? " (Chỉ tra — không đặt breakpoint.)" : ""));
            output.Text = sb.ToString();
            Log($"Batch: {ok} OK, {fail} lỗi / {lines.Length} dòng.");
        };

        var btnRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
        btnRow.Controls.Add(closeBtn);
        btnRow.Controls.Add(runBtn);

        var outHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2) };
        outHost.Controls.Add(output);

        using var dlg = new Form
        {
            Text = "Batch — đặt breakpoint hàng loạt", Width = 720, Height = 520,
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = true,
            ShowInTaskbar = false, TopMost = true, Padding = new Padding(8)
        };
        dlg.Controls.Add(outHost);
        dlg.Controls.Add(input);
        dlg.Controls.Add(hint);
        dlg.Controls.Add(btnRow);
        dlg.CancelButton = closeBtn;
        dlg.ShowDialog();
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
        _run.Enabled = _batch.Enabled = any || _lookupOnlyBox.Checked;
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

    // ==================== chụp bằng chứng: tray + hotkey + worklist ====================

    static Icon LoadAppIcon()
    {
        using var s = typeof(MainForm).Assembly.GetManifestResourceStream("SendKeyDemo.app_runtime.ico");
        return s != null ? new Icon(s) : SystemIcons.Application;
    }

    void ToggleAdvanced()
    {
        _advanced.Visible = !_advanced.Visible;
        _advancedToggle.Text = _advanced.Visible ? "▾  Công cụ khác" : "▸  Công cụ khác";
        // Nút Flat đang giữ focus sẽ vẽ viền đen cả hàng — chuyển focus đi (mở ra thì vào ô cmdLabel).
        ActiveControl = _advanced.Visible ? _cmdLabel : null;
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

        _evidence.Start(null);
        AfterEvidenceStart();
    }

    void AfterEvidenceStart()
    {
        if (_savedRegion == null)
            MessageBox.Show(this,
                $"Sắp cửa sổ VS sao cho thấy CẢ dòng code lẫn cửa sổ Watch, rồi bấm {_settings.DefineRegionHotkey} " +
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
        using var sel = new RegionSelector();
        sel.ShowDialog();
        if (sel.Result is not { } r) return;
        _savedRegion = r;
        Log($"Đã nhớ vùng chụp {r.Width}x{r.Height} — từ giờ {_settings.CaptureRegionHotkey} chụp đúng vùng này.");
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

    void EvidenceDialog()
    {
        if (_evidence == null) return;

        var input = new TextBox
        {
            Multiline = true, Dock = DockStyle.Fill, AcceptsTab = false,
            ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f),
            Text = string.Join(Environment.NewLine, _settings.Worklist),
        };

        var hint = new Label
        {
            Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2, 4, 2, 8),
            Text = "Dùng khi bạn ĐÃ CÓ SẴN danh sách test case. Bình thường thì không cần —\r\n" +
                   "nút \"Bắt đầu chụp bằng chứng\" ở cửa sổ chính đọc thẳng từ clipboard.\r\n\r\n" +
                   "Mỗi dòng:  TC-id ⇥ cmdLabel ⇥ cmdVar ⇥ kỳ vọng   (⇥ = Tab hoặc ≥2 dấu cách)\r\n" +
                   "Dòng trống / bắt đầu bằng # bị bỏ qua. Dòng 1 cột = cmdLabel, TC-id tự đánh số.",
        };

        var startBtn = new Button { Text = "Chạy theo danh sách", AutoSize = true, Margin = new Padding(0, 0, 6, 0) };
        var closeBtn = new Button { Text = "Đóng", AutoSize = true, DialogResult = DialogResult.Cancel };

        using var dlg = new Form
        {
            Text = "Chụp bằng chứng — danh sách test case", Width = 760, Height = 560,
            StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = true,
            ShowInTaskbar = false, TopMost = true, Padding = new Padding(8),
        };

        startBtn.Click += (_, _) =>
        {
            var items = Worklist.Parse(input.Text);
            if (items.Count == 0)
            {
                MessageBox.Show(dlg, "Chưa có dòng nào hợp lệ trong ô danh sách.", "Danh sách trống",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var list = new Worklist(items);
            // Cùng danh sách với lần trước -> giữ lại các TC đã chụp và vị trí đang làm dở.
            if (_settings.Worklist.SequenceEqual(list.ToLines()))
            {
                list.RestoreDone(_settings.WorklistDone);
                list.MoveTo(_settings.WorklistIndex);
            }

            _settings.Worklist = list.ToLines();
            _settings.WorklistIndex = list.Index;
            _settings.WorklistDone = list.DoneIds();
            _settings.Save();

            _evidence.Start(list);
            dlg.DialogResult = DialogResult.OK;
            dlg.Close();
            AfterEvidenceStart();
        };

        var btnRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8),
        };
        btnRow.Controls.Add(closeBtn);
        btnRow.Controls.Add(startBtn);

        var inputHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(2) };
        inputHost.Controls.Add(input);

        dlg.Controls.Add(inputHost);
        dlg.Controls.Add(hint);
        dlg.Controls.Add(btnRow);
        dlg.CancelButton = closeBtn;
        dlg.ShowDialog(this);
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

    void Log(string msg) =>
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}{Environment.NewLine}");
}
