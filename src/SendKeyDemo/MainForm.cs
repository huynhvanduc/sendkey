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

    // Task 10 cài đặt thật.
    void TraVaChay() => Log("Tra & Chạy: (được cài đặt ở Task 10)");

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
