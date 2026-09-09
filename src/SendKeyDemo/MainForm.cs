using System.Runtime.InteropServices;
using EnvDTE;

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
    readonly Button _run = new() { Text = "Tra & Chạy", AutoSize = true };
    readonly Button _checkMapping = new() { Text = "Kiểm tra mapping.csv", AutoSize = true };
    readonly Button _clearBpFile = new() { Text = "Xóa BP file này", AutoSize = true };
    readonly Button _copyWatch = new() { Text = "Copy Watch", AutoSize = true };
    readonly CheckBox _topMostBox = new() { Text = "Luôn nổi trên cùng", AutoSize = true, Checked = true };
    readonly CheckBox _lookupOnlyBox = new() { Text = "Chỉ tra (không cần VS)", AutoSize = true };
    bool _loading;
    List<MapRow>? _mapRows;
    string _mapRowsPath = "";
    DateTime _mapRowsMtime;

    public MainForm()
    {
        Text = "VS SendKey Automation Demo";
        Width = 780;
        Height = 620;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 0) };
        top.Controls.Add(_instances);
        top.Controls.Add(_refresh);

        var mapGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 3, RowCount = 5, AutoSize = true, Padding = new Padding(8, 4, 8, 4)
        };
        mapGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        mapGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mapGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        mapGrid.Controls.Add(new Label { Text = "mapping.csv", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        mapGrid.Controls.Add(_mappingPath, 1, 0);
        mapGrid.Controls.Add(_mappingBrowse, 2, 0);
        mapGrid.Controls.Add(new Label { Text = "target .cs", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        mapGrid.Controls.Add(_targetCs, 1, 1);
        mapGrid.Controls.Add(_targetBrowse, 2, 1);
        mapGrid.Controls.Add(new Label { Text = "cmdLabel", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        mapGrid.Controls.Add(_cmdLabel, 1, 2);
        mapGrid.Controls.Add(new Label { Text = "cmdVar", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        mapGrid.Controls.Add(_cmdVar, 1, 3);
        var mapBtns = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
        mapBtns.Controls.Add(_run);
        mapBtns.Controls.Add(_checkMapping);
        mapGrid.Controls.Add(mapBtns, 1, 4);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true, Padding = new Padding(8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label { Text = "File", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        grid.Controls.Add(_file, 1, 0);
        grid.Controls.Add(_browse, 2, 0);
        grid.Controls.Add(new Label { Text = "Line", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        grid.Controls.Add(_line, 1, 1);
        grid.Controls.Add(new Label { Text = "Watch", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        grid.Controls.Add(_watch, 1, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 0, 8, 8) };
        buttons.Controls.Add(_goto);
        buttons.Controls.Add(_bp);
        buttons.Controls.Add(_clearBpFile);
        buttons.Controls.Add(_addWatch);
        buttons.Controls.Add(_copyWatch);

        var bottomPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 0, 8, 4) };
        bottomPanel.Controls.Add(_topMostBox);
        bottomPanel.Controls.Add(_lookupOnlyBox);

        var logHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        logHost.Controls.Add(_log);

        Controls.Add(logHost);
        Controls.Add(bottomPanel);
        Controls.Add(buttons);
        Controls.Add(grid);
        Controls.Add(mapGrid);
        Controls.Add(top);

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
        _lookupOnlyBox.CheckedChanged += (_, _) =>
            _run.Enabled = _lookupOnlyBox.Checked || _instances.SelectedItem is VsInstance;

        _mappingBrowse.Click += (_, _) => PickFile(_mappingPath, "CSV (*.csv)|*.csv|Tất cả (*.*)|*.*");
        _targetBrowse.Click += (_, _) => PickFile(_targetCs, "C# (*.cs)|*.cs|Tất cả (*.*)|*.*");
        _mappingPath.TextChanged += (_, _) => SaveSettings();
        _targetCs.TextChanged += (_, _) => SaveSettings();
        _topMostBox.CheckedChanged += (_, _) => { TopMost = _topMostBox.Checked; SaveSettings(); };
        _run.Click += (_, _) => TraVaChay();
        _cmdLabel.KeyDown += MappingKeyDown;
        _cmdVar.KeyDown += MappingKeyDown;

        Load += (_, _) =>
        {
            _loading = true;
            var s = AppSettings.Load();
            _mappingPath.Text = s.MappingPath ?? "";
            _targetCs.Text = s.TargetCsPath ?? "";
            _topMostBox.Checked = s.TopMost;
            TopMost = s.TopMost;
            _loading = false;

            VsAutomation.OleMessageFilter.Register();
            LoadInstances();
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
        new AppSettings
        {
            MappingPath = _mappingPath.Text,
            TargetCsPath = _targetCs.Text,
            TopMost = _topMostBox.Checked
        }.Save();
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

    /// <summary>Form nhập 1 dòng mapping mới; validate csharpLabel với file .cs đích trước khi trả về.</summary>
    static MapRow? AddMappingDialog(string csPath, string cmdLabelPrefill, string cmdVarPrefill,
        IReadOnlyList<MapRow> rows)
    {
        var tbCmdLabel = new TextBox { Text = cmdLabelPrefill, Dock = DockStyle.Fill };
        var tbCmdVar = new TextBox { Text = cmdVarPrefill, Dock = DockStyle.Fill };
        var tbCsLabel = new TextBox { Dock = DockStyle.Fill };
        var tbCsVar = new TextBox { Dock = DockStyle.Fill };
        var err = new Label
        {
            ForeColor = Color.Firebrick, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
        };

        var grid = new TableLayoutPanel { ColumnCount = 2, RowCount = 5, Dock = DockStyle.Fill, Padding = new Padding(8) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
        grid.Controls.Add(err, 1, 4);

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
            Text = "Thêm mapping mới", Width = 470, Height = 280,
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

            var ll = Mapping.FindLabelLine(csPath, csL);
            switch (ll.Kind)
            {
                case LabelLineKind.NotFound:
                    err.Text = $"csharpLabel: không thấy \"{csL}:\" trong {Path.GetFileName(csPath)}.";
                    tbCsLabel.Focus(); return;
                case LabelLineKind.Multiple:
                    err.Text = $"csharpLabel: \"{csL}:\" xuất hiện ở dòng {string.Join(", ", ll.MatchLines!)}.";
                    tbCsLabel.Focus(); return;
                case LabelLineKind.NoExecutableLine:
                    err.Text = $"csharpLabel: sau \"{csL}:\" không còn dòng thực thi.";
                    tbCsLabel.Focus(); return;
            }

            result = new MapRow(cmdL, cmdV, csL, csV, 0);
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
            Log($"ĐÃ THÊM mapping: {r.CmdLabel} / {r.CmdVar} → {r.CsharpLabel} / {r.CsharpVar} — kiểm tra lại bản dịch csharpVar.");
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

        var csPath = _targetCs.Text.Trim();
        if (string.IsNullOrWhiteSpace(csPath) || !File.Exists(csPath))
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

        var res = Mapping.Resolve(rows, labelRaw, labelOnly ? null : varRaw);

        if (res.Kind == LookupKind.Duplicate)
        {
            Log($"Tra & Chạy: mapping trùng dòng {string.Join(", ", res.DuplicateLines!)}.");
            return;
        }

        MapRow row;
        if (res.Kind == LookupKind.NotFoundLabel)
        {
            var added = AddMappingDialog(csPath, labelRaw, labelOnly ? "" : varRaw, rows);
            if (added == null) { Log("Tra & Chạy: đã hủy thêm mapping."); return; }
            row = SaveAddedRow(added);
            labelOnly = false;
        }
        else if (res.Kind == LookupKind.NeedPickVar)
        {
            var pick = PickFromList($"Chọn biến của label {labelRaw}", res.VarChoices!, out var addNew);
            if (addNew)
            {
                var added = AddMappingDialog(csPath, labelRaw, varRaw, rows);
                if (added == null) { Log("Tra & Chạy: đã hủy thêm mapping."); return; }
                row = SaveAddedRow(added);
            }
            else
            {
                if (pick == null) { Log("Tra & Chạy: đã hủy chọn biến."); return; }
                _cmdVar.Text = pick;
                var re = Mapping.Resolve(rows, labelRaw, pick);
                if (re.Kind != LookupKind.Ok) { Log("Tra & Chạy: vẫn không khớp sau khi chọn biến."); return; }
                row = re.Row!;
            }
            labelOnly = false;
        }
        else
        {
            row = res.Row!;
        }

        if (res.Warning != null) Log("Tra & Chạy: " + res.Warning);

        var lineRes = Mapping.FindLabelLine(csPath, row.CsharpLabel);
        if (lineRes.Kind == LabelLineKind.NotFound)
        {
            Log($"Tra & Chạy: không thấy label \"{row.CsharpLabel}:\" trong {Path.GetFileName(csPath)}.");
            return;
        }
        if (lineRes.Kind == LabelLineKind.Multiple)
        {
            Log($"Tra & Chạy: label \"{row.CsharpLabel}:\" xuất hiện ở dòng {string.Join(", ", lineRes.MatchLines!)}.");
            return;
        }
        if (lineRes.Kind == LabelLineKind.NoExecutableLine)
        {
            Log($"Tra & Chạy: sau label \"{row.CsharpLabel}\" không còn dòng thực thi.");
            return;
        }
        int line = lineRes.Line;

        _file.Text = csPath;
        _line.Value = Math.Min(line, (int)_line.Maximum);
        if (!labelOnly) _watch.Text = row.CsharpVar;

        if (lookupOnly)
        {
            Log($"Chỉ tra: {labelRaw} → {Path.GetFileName(csPath)}:{line}"
                + (labelOnly ? "" : $", watch \"{row.CsharpVar}\"")
                + " — đã điền File/Line/Watch (không thao tác VS).");
            return;
        }

        Run("Go To Line", dte => VsAutomation.GoToLine(dte, csPath, line));
        Run("Breakpoint", dte => VsAutomation.EnsureBreakpoint(dte, csPath, line));

        Log(labelOnly
            ? $"mapping: {labelRaw} → {Path.GetFileName(csPath)}:{line} — breakpoint sẵn sàng (F5 để dừng lại)."
            : $"mapping: {labelRaw}/{_cmdVar.Text} → {Path.GetFileName(csPath)}:{line}, watch \"{row.CsharpVar}\" — F5 dừng ở breakpoint rồi bấm Add Watch.");
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

        var csPath = _targetCs.Text.Trim();
        IReadOnlyList<string>? csLines = File.Exists(csPath) ? File.ReadAllLines(csPath) : null;
        if (csLines == null)
            Log("Kiểm tra mapping.csv: chưa trỏ file .cs hợp lệ — chỉ kiểm trùng cặp cmdLabel+cmdVar.");

        var problems = Mapping.Validate(rows, csLines);
        if (problems.Count == 0)
        {
            Log($"Kiểm tra mapping.csv: OK — {rows.Count} dòng, không thấy vấn đề.");
            return;
        }
        Log($"Kiểm tra mapping.csv: {problems.Count} vấn đề / {rows.Count} dòng:");
        foreach (var p in problems) Log("  - " + p);
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
        _run.Enabled = any || _lookupOnlyBox.Checked;
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

    void Log(string msg) =>
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {msg}{Environment.NewLine}");
}
