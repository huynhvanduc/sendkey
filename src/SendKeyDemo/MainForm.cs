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
    readonly CheckBox _topMostBox = new() { Text = "Luôn nổi trên cùng", AutoSize = true, Checked = true };
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
        mapGrid.Controls.Add(_run, 1, 4);

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
        buttons.Controls.Add(_addWatch);

        var bottomPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 0, 8, 4) };
        bottomPanel.Controls.Add(_topMostBox);

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
        _addWatch.Click += (_, _) => Run("Add Watch", dte => VsAutomation.AddWatch(dte, _watch.Text));

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

    static string? PickFromList(string title, IReadOnlyList<string> items)
    {
        using var dlg = new Form
        {
            Text = title, Width = 440, Height = 320,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MinimizeBox = false, MaximizeBox = false, TopMost = true
        };
        var list = new ListBox { Dock = DockStyle.Fill };
        foreach (var it in items) list.Items.Add(it);
        if (list.Items.Count > 0) list.SelectedIndex = 0;
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 32 };
        list.DoubleClick += (_, _) => { if (list.SelectedItem != null) ok.PerformClick(); };
        dlg.Controls.Add(list);
        dlg.Controls.Add(ok);
        dlg.AcceptButton = ok;
        return dlg.ShowDialog() == DialogResult.OK ? list.SelectedItem as string : null;
    }

    void TraVaChay()
    {
        if (_instances.SelectedItem is not VsInstance)
        {
            Log("Tra & Chạy: chưa chọn instance VS.");
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

        if (res.Kind == LookupKind.NotFoundLabel)
        {
            Log($"Tra & Chạy: không thấy label \"{labelRaw}\" trong mapping.csv.");
            return;
        }
        if (res.Kind == LookupKind.Duplicate)
        {
            Log($"Tra & Chạy: mapping trùng dòng {string.Join(", ", res.DuplicateLines!)}.");
            return;
        }
        if (res.Kind == LookupKind.NeedPickVar)
        {
            var pick = PickFromList($"Chọn biến của label {labelRaw}", res.VarChoices!);
            if (pick == null)
            {
                Log("Tra & Chạy: đã hủy chọn biến.");
                return;
            }
            _cmdVar.Text = pick;
            varRaw = pick;
            labelOnly = false;
            res = Mapping.Resolve(rows, labelRaw, pick);
            if (res.Kind != LookupKind.Ok)
            {
                Log("Tra & Chạy: vẫn không khớp sau khi chọn biến.");
                return;
            }
        }

        var row = res.Row!;
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

        Run("Go To Line", dte => VsAutomation.GoToLine(dte, csPath, line));
        Run("Breakpoint", dte => VsAutomation.EnsureBreakpoint(dte, csPath, line));

        Log(labelOnly
            ? $"mapping: {labelRaw} → {Path.GetFileName(csPath)}:{line} — breakpoint sẵn sàng (F5 để dừng lại)."
            : $"mapping: {labelRaw}/{_cmdVar.Text} → {Path.GetFileName(csPath)}:{line}, watch \"{row.CsharpVar}\" — F5 dừng ở breakpoint rồi bấm Add Watch.");
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

        _goto.Enabled = _bp.Enabled = _addWatch.Enabled = _run.Enabled = any;
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
