using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

internal static class GuiBootstrap
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private readonly TextBox _project = new TextBox();
    private readonly TextBox _elf = new TextBox();
    private readonly TextBox _module = new TextBox();
    private readonly TextBox _measurements = new TextBox();
    private readonly TextBox _calibrations = new TextBox();
    private readonly TextBox _log = new TextBox();
    private readonly ListView _results = new ListView();
    private readonly Button _run = new Button();
    private readonly Button _refreshStatus = new Button();
    private readonly Label _canapeStatus = new Label();
    private readonly Label _asap3Status = new Label();
    private readonly Label _measurementStatus = new Label();
    private readonly CheckBox _darkMode = new CheckBox();
    private readonly ProgressBar _progress = new ProgressBar();
    private readonly Label _status = new Label();
    private readonly UserSettings _settings;

    public MainForm()
    {
        Text = "QT 自动添加工具 - CANape 观测量/标定量";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 780);
        Size = new Size(1200, 860);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Font = new Font("Microsoft YaHei UI", 10F);
        AutoScroll = true;
        AutoScrollMinSize = new Size(0, 1120);
        _settings = UserSettings.Load();
        FormClosing += delegate { SaveSettings(); };

        TableLayoutPanel root = new TableLayoutPanel();
        root.Dock = DockStyle.None;
        root.Location = new Point(0, 0);
        root.Height = 1120;
        root.Width = ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
        root.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        root.Padding = new Padding(12);
        root.ColumnCount = 1;
        root.RowCount = 6;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 37F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 37F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 26F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(CreatePathPanel(), 0, 0);
        root.Controls.Add(CreateHint(), 0, 1);
        root.Controls.Add(CreateListGroup("观测量（每行一个变量名）", _measurements), 0, 2);
        root.Controls.Add(CreateListGroup("标定量（每行一个变量名）", _calibrations), 0, 3);
        root.Controls.Add(CreateLogGroup(), 0, 4);
        root.Controls.Add(CreateActionPanel(), 0, 5);
        // Rebuild the central work area as user-resizable split panes.
        root.Controls.Clear();
        root.RowCount = 4;
        root.RowStyles.Clear();
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(CreatePathPanel(), 0, 0);
        root.Controls.Add(CreateHint(), 0, 1);
        root.Controls.Add(CreateWorkspace(), 0, 2);
        root.Controls.Add(CreateActionPanel(), 0, 3);
        Controls.Add(root);
        LoadSettings();
        ApplyTheme();
        UpdateCanapeProcessStatus();
    }

    private Control CreateWorkspace()
    {
        SplitContainer lists = new SplitContainer();
        lists.Dock = DockStyle.Fill;
        lists.Orientation = Orientation.Horizontal;
        lists.SplitterWidth = 8;
        lists.IsSplitterFixed = false;
        lists.SizeChanged += delegate
        {
            if (lists.Tag == null && lists.Height >= 260)
            {
                lists.Panel1MinSize = 120;
                lists.Panel2MinSize = 120;
                lists.SplitterDistance = Math.Max(lists.Panel1MinSize,
                    (lists.Height - lists.SplitterWidth) / 2);
                lists.Tag = true;
            }
        };
        lists.Panel1.Controls.Add(CreateListGroup("观测量（每行一个变量名）", _measurements));
        lists.Panel2.Controls.Add(CreateListGroup("标定量（每行一个变量名）", _calibrations));

        SplitContainer workspace = new SplitContainer();
        workspace.Dock = DockStyle.Fill;
        workspace.Orientation = Orientation.Horizontal;
        workspace.SplitterWidth = 8;
        workspace.IsSplitterFixed = false;
        workspace.SizeChanged += delegate
        {
            if (workspace.Tag == null && workspace.Height >= 420)
            {
                workspace.Panel1MinSize = 260;
                workspace.Panel2MinSize = 160;
                workspace.SplitterDistance = Math.Max(workspace.Panel1MinSize,
                    workspace.Height * 2 / 3);
                workspace.Tag = true;
            }
        };
        workspace.Panel1.Controls.Add(lists);
        workspace.Panel2.Controls.Add(CreateLogGroup());
        return workspace;
    }

    private Control CreatePathPanel()
    {
        TableLayoutPanel panel = new TableLayoutPanel();
        panel.Dock = DockStyle.Top;
        panel.AutoSize = true;
        panel.ColumnCount = 3;
        panel.RowCount = 3;
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95F));
        AddPathRow(panel, 0, "CANape 工程", _project, "选择...", BrowseProject);
        AddPathRow(panel, 1, "ELF 文件", _elf, "选择...", BrowseElf);

        Label moduleLabel = new Label();
        moduleLabel.Text = "模块名称";
        moduleLabel.Anchor = AnchorStyles.Left;
        moduleLabel.AutoSize = true;
        _module.Text = "XCPsim";
        _module.Dock = DockStyle.Fill;
        _module.Margin = new Padding(3, 4, 8, 4);
        Label note = new Label();
        note.Text = "通常为 XCPsim";
        note.Anchor = AnchorStyles.Left;
        note.AutoSize = true;
        panel.Controls.Add(moduleLabel, 0, 2);
        panel.Controls.Add(_module, 1, 2);
        panel.Controls.Add(note, 2, 2);
        return panel;
    }

    private static void AddPathRow(TableLayoutPanel panel, int row, string label, TextBox textBox, string buttonText, EventHandler click)
    {
        Label title = new Label();
        title.Text = label;
        title.Anchor = AnchorStyles.Left;
        title.AutoSize = true;
        textBox.Dock = DockStyle.Fill;
        textBox.Margin = new Padding(3, 4, 8, 4);
        Button button = new Button();
        button.Text = buttonText;
        button.Dock = DockStyle.Fill;
        button.Click += click;
        panel.Controls.Add(title, 0, row);
        panel.Controls.Add(textBox, 1, row);
        panel.Controls.Add(button, 2, row);
    }

    private Control CreateHint()
    {
        TableLayoutPanel panel = new TableLayoutPanel();
        panel.Dock = DockStyle.Top;
        panel.AutoSize = true;
        panel.ColumnCount = 5;
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Label hint = new Label();
        hint.AutoSize = true;
        hint.Padding = new Padding(0, 8, 8, 8);
        hint.Text = "使用前请打开 CANape 工程，停止测量，并在 Tools → ASAP3 中保持 ASAP3 Active。";
        panel.Controls.Add(hint, 0, 0);

        ConfigureStatusLabel(_canapeStatus, "CANape：检查中...");
        ConfigureStatusLabel(_asap3Status, "ASAP3：检查中...");
        ConfigureStatusLabel(_measurementStatus, "测量：检查中...");
        panel.Controls.Add(_canapeStatus, 1, 0);
        panel.Controls.Add(_asap3Status, 2, 0);
        panel.Controls.Add(_measurementStatus, 3, 0);

        _refreshStatus.Text = "刷新状态";
        _refreshStatus.AutoSize = true;
        _refreshStatus.Click += RefreshStatus;
        panel.Controls.Add(_refreshStatus, 4, 0);
        return panel;
    }

    private static void ConfigureStatusLabel(Label label, string text)
    {
        label.Text = text;
        label.AutoSize = true;
        label.Padding = new Padding(8, 8, 8, 8);
    }

    private static Control CreateListGroup(string title, TextBox textBox)
    {
        GroupBox group = new GroupBox();
        group.Text = title;
        group.Dock = DockStyle.Fill;
        group.Padding = new Padding(9);
        textBox.Dock = DockStyle.Fill;
        textBox.Multiline = true;
        textBox.ScrollBars = ScrollBars.Both;
        textBox.AcceptsReturn = true;
        textBox.WordWrap = false;
        textBox.Font = new Font("Consolas", 10F);
        group.Controls.Add(textBox);
        return group;
    }

    private Control CreateLogGroup()
    {
        GroupBox group = new GroupBox();
        group.Text = "执行日志 / 结果分类";
        group.Dock = DockStyle.Fill;
        group.Padding = new Padding(9);
        TableLayoutPanel content = new TableLayoutPanel();
        content.Dock = DockStyle.Fill;
        content.RowCount = 2;
        content.ColumnCount = 1;
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 35F));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 65F));

        _results.Dock = DockStyle.Fill;
        _results.View = View.Details;
        _results.FullRowSelect = true;
        _results.GridLines = true;
        _results.Columns.Add("类别", 125);
        _results.Columns.Add("状态", 120);
        _results.Columns.Add("变量名", 560);
        content.Controls.Add(_results, 0, 0);

        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Both;
        _log.WordWrap = false;
        _log.Font = new Font("Consolas", 9F);
        content.Controls.Add(_log, 0, 1);
        group.Controls.Add(content);
        return group;
    }

    private Control CreateActionPanel()
    {
        FlowLayoutPanel panel = new FlowLayoutPanel();
        panel.Dock = DockStyle.Fill;
        panel.AutoSize = true;
        panel.Padding = new Padding(0, 8, 0, 0);
        _run.Text = "连接并添加";
        _run.AutoSize = true;
        _run.Padding = new Padding(18, 5, 18, 5);
        _run.Click += RunUpdate;
        panel.Controls.Add(_run);

        Button importMeasurements = CreateListButton("导入观测量", ImportMeasurements);
        Button importCalibrations = CreateListButton("导入标定量", ImportCalibrations);
        Button exportMeasurements = CreateListButton("导出观测量", ExportMeasurements);
        Button exportCalibrations = CreateListButton("导出标定量", ExportCalibrations);
        panel.Controls.Add(importMeasurements);
        panel.Controls.Add(importCalibrations);
        panel.Controls.Add(exportMeasurements);
        panel.Controls.Add(exportCalibrations);

        Button clear = new Button();
        clear.Text = "清除保存配置";
        clear.AutoSize = true;
        clear.Click += ClearSettings;
        panel.Controls.Add(clear);

        _darkMode.Text = "深色模式";
        _darkMode.AutoSize = true;
        _darkMode.Margin = new Padding(8, 8, 0, 0);
        _darkMode.CheckedChanged += delegate { ApplyTheme(); SaveSettings(); };
        panel.Controls.Add(_darkMode);

        _status.AutoSize = true;
        _status.Margin = new Padding(16, 10, 0, 0);
        _status.Text = "就绪";
        panel.Controls.Add(_status);
        _progress.Width = 120;
        _progress.Height = 18;
        _progress.Style = ProgressBarStyle.Marquee;
        _progress.Visible = false;
        _progress.Margin = new Padding(12, 8, 0, 0);
        panel.Controls.Add(_progress);
        return panel;
    }

    private static Button CreateListButton(string text, EventHandler handler)
    {
        Button button = new Button();
        button.Text = text;
        button.AutoSize = true;
        button.Click += handler;
        return button;
    }

    private void LoadSettings()
    {
        _project.Text = _settings.ProjectPath ?? string.Empty;
        _elf.Text = _settings.ElfPath ?? string.Empty;
        _module.Text = string.IsNullOrWhiteSpace(_settings.ModuleName) ? "XCPsim" : _settings.ModuleName;
        _measurements.Text = _settings.Measurements ?? string.Empty;
        _calibrations.Text = _settings.Calibrations ?? string.Empty;
        _darkMode.Checked = _settings.DarkMode;
    }

    private void SaveSettings()
    {
        _settings.ProjectPath = _project.Text.Trim();
        _settings.ElfPath = _elf.Text.Trim();
        _settings.ModuleName = _module.Text.Trim();
        _settings.Measurements = _measurements.Text;
        _settings.Calibrations = _calibrations.Text;
        _settings.DarkMode = _darkMode.Checked;
        _settings.Save();
    }

    private void ClearSettings(object sender, EventArgs e)
    {
        _project.Clear();
        _elf.Clear();
        _module.Text = "XCPsim";
        _measurements.Clear();
        _calibrations.Clear();
        _darkMode.Checked = false;
        _settings.Clear();
        _status.Text = "已清除保存配置";
    }

    private void BrowseProject(object sender, EventArgs e)
    {
        using (OpenFileDialog dialog = new OpenFileDialog())
        {
            dialog.Filter = "CANape Project (*.cna)|*.cna|All files (*.*)|*.*";
            dialog.Title = "Select CANape Project";
            SetInitialDirectory(dialog, _settings.ProjectDirectory);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _project.Text = dialog.FileName;
                _settings.ProjectDirectory = Path.GetDirectoryName(dialog.FileName);
                SaveSettings();
            }
        }
    }

    private void BrowseElf(object sender, EventArgs e)
    {
        using (OpenFileDialog dialog = new OpenFileDialog())
        {
            dialog.Filter = "ELF files (*.elf)|*.elf|All files (*.*)|*.*";
            dialog.Title = "Select ELF File";
            SetInitialDirectory(dialog, _settings.ElfDirectory);
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _elf.Text = dialog.FileName;
                _settings.ElfDirectory = Path.GetDirectoryNa�M��G����ƭy�y(directoryProperty, Path.GetDirectoryName(dialog.FileName));
            SaveSettings();
        }
    }

    private void ExportList(TextBox source, string title, string defaultName)
    {
        using (SaveFileDialog dialog = new SaveFileDialog())
        {
            dialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
            dialog.Title = title;
            dialog.FileName = defaultName;
            SetInitialDirectory(dialog, _settings.ProjectDirectory);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(dialog.FileName, source.Text.Replace("\r\n", "\n"), new UTF8Encoding(false));
            _status.Text = "列表已导出：" + Path.GetFileName(dialog.FileName);
        }
    }

    private string GetListDirectory(string propertyName)
    {
        if (propertyName == "MeasurementDirectory") return _settings.MeasurementDirectory;
        return _settings.CalibrationDirectory;
    }

    private void SetListDirectory(string propertyName, string directory)
    {
        if (propertyName == "MeasurementDirectory") _settings.MeasurementDirectory = directory;
        else _settings.CalibrationDirectory = directory;
    }

    private void RefreshStatus(object sender, EventArgs e)
    {
        if (!File.Exists(_project.Text.Trim()))
        {
            SetStatusLabels("CANape：未选择工程", "ASAP3：未知", "测量：未知");
            return;
        }

        _refreshStatus.Enabled = false;
        SetStatusLabels("CANape：检查中...", "ASAP3：检查中...", "测量：检查中...");
        string project = Path.GetFullPath(_project.Text.Trim());
        string module = string.IsNullOrWhiteSpace(_module.Text) ? "XCPsim" : _module.Text.Trim();
        string probe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CanapeReadOnlyProbe.exe");
        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            string output = string.Empty;
            int code = 1;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    ProcessStartInfo info = new ProcessStartInfo("\"" + probe + "\"", Quote(project) + " " + Quote(module));
                    info.WorkingDirectory = Path.GetDirectoryName(probe);
                    info.UseShellExecute = false;
                    info.CreateNoWindow = true;
                    info.RedirectStandardOutput = true;
                    info.RedirectStandardError = true;
                    using (Process process = Process.Start(info))
                    {
                        if (process.WaitForExit(10000))
                        {
                            output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                            code = process.ExitCode;
                        }
                        else
                        {
                            try { process.Kill(); } catch { }
                            output = "ASAP3 probe timeout";
                            code = 124;
                        }
                    }
                }
                catch (Exception ex) { output = ex.Message; code = 1; }
                if (code == 0) break;
                if (attempt < 3) System.Threading.Thread.Sleep(1000);
            }

            string result = output;
            int exitCode = code;
            BeginInvoke((Action)delegate
            {
                _refreshStatus.Enabled = true;
                if (exitCode != 0)
                {
                    bool canapeRunning = Process.GetProcessesByName("CANape").Length > 0 ||
                        Process.GetProcessesByName("CANape64").Length > 0;
                    string apiError = ReadValue(result, "Asap3Init5 failed:");
                    if (string.IsNullOrWhiteSpace(apiError)) apiError = "连接失败";
                    SetStatusLabels(
                        canapeRunning ? "CANape：已运行" : "CANape：未运行",
                        "ASAP3：不可用(" + apiError + ")",
                        "测量：未知");
                    return;
                    SetStatusLabels("CANape：未连接", "ASAP3：不可用", "测量：未知");
                    return;
                }
                string state = ReadValue(result, "MeasurementState=");
                SetStatusLabels("CANape：已连接", "ASAP3：Active", state == "0" ? "测量：已停止" : "测量：运行中");
            });
        });
    }

    private void UpdateCanapeProcessStatus()
    {
        bool canapeRunning = Process.GetProcessesByName("CANape").Length > 0 ||
            Process.GetProcessesByName("CANape64").Length > 0;
        SetStatusLabels(
            canapeRunning ? "CANape：已运行" : "CANape：未运行",
            "ASAP3：未检测",
            "测量：未检测");
    }

    private static string ReadValue(string text, string prefix)
    {
        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return line.Substring(prefix.Length).Trim();
        }
        return string.Empty;
    }

    private void SetStatusLabels(string canape, string asap3, string measurement)
    {
        _canapeStatus.Text = canape;
        _asap3Status.Text = asap3;
        _measurementStatus.Text = measurement;
    }

    private void ParseResults(string output)
    {
        _results.Items.Clear();
        foreach (string line in output.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("RESULT|", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = line.Split(new[] { '|' }, 4);
                if (parts.Length == 4)
                {
                    ListViewItem item = new ListViewItem(parts[1].Equals("measurement", StringComparison.OrdinalIgnoreCase) ? "观测量" : "标定量");
                    item.SubItems.Add(ToChineseState(parts[2]));
                    item.SubItems.Add(parts[3]);
                    _results.Items.Add(item);
                }
            }
            else if (line.StartsWith("Object=", StringComparison.OrdinalIgnoreCase))
            {
                string name = line.Substring("Object=".Length).Split(',')[0];
                string type = line.IndexOf("ObjectType=1", StringComparison.OrdinalIgnoreCase) >= 0 ? "标定量已验证" : "观测量已验证";
                ListViewItem item = new ListViewItem("ASAP3");
                item.SubItems.Add(type);
                item.SubItems.Add(name);
                _results.Items.Add(item);
            }
        }
    }

    private static string ToChineseState(string state)
    {
        if (state.Equals("created", StringComparison.OrdinalIgnoreCase)) return "新增";
        if (state.Equals("updated", StringComparison.OrdinalIgnoreCase)) return "已更新";
        if (state.Equals("not-found", StringComparison.OrdinalIgnoreCase)) return "ELF未找到";
        if (state.Equals("failed", StringComparison.OrdinalIgnoreCase)) return "处理失败";
        return state;
    }

    private void ApplyTheme()
    {
        bool dark = _darkMode.Checked;
        BackColor = dark ? Color.FromArgb(32, 33, 36) : SystemColors.Control;
        ForeColor = dark ? Color.White : SystemColors.ControlText;
        ApplyThemeToControl(this, dark);
    }

    private static void ApplyThemeToControl(Control control, bool dark)
    {
        foreach (Control child in control.Controls)
        {
            child.BackColor = dark ? Color.FromArgb(45, 45, 48) : SystemColors.Window;
            child.ForeColor = dark ? Color.White : SystemColors.ControlText;
            if (child is Button || child is CheckBox || child is Label || child is GroupBox || child is ProgressBar)
            {
                child.BackColor = dark ? Color.FromArgb(32, 33, 36) : SystemColors.Control;
            }
            ApplyThemeToControl(child, dark);
        }
    }

    private void RunUpdate(object sender, EventArgs e)
    {
        string error = ValidateInputs();
        if (error != null)
        {
            MessageBox.Show(this, error, "Input validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        SaveSettings();

        string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OnlineUpdate.ps1");
        if (!File.Exists(script))
        {
            MessageBox.Show(this, "OnlineUpdate.ps1 was not found:\r\n" + script, "Missing file", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string measurementFile = Path.Combine(Path.GetTempPath(), "QTAutoMeasurements_" + Guid.NewGuid().ToString("N") + ".txt");
        string calibrationFile = Path.Combine(Path.GetTempPath(), "QTAutoCalibrations_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(measurementFile, _measurements.Text.Replace("\r\n", "\n"), new UTF8Encoding(false));
        File.WriteAllText(calibrationFile, _calibrations.Text.Replace("\r\n", "\n"), new UTF8Encoding(false));

        string arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(script)
            + " -Project " + Quote(Path.GetFullPath(_project.Text.Trim()))
            + " -Elf " + Quote(Path.GetFullPath(_elf.Text.Trim()))
            + " -Measurements " + Quote(measurementFile)
            + " -Calibrations " + Quote(calibrationFile)
            + " -Module " + Quote(_module.Text.Trim());

        _run.Enabled = false;
        _refreshStatus.Enabled = false;
        _progress.Visible = true;
        _log.Clear();
        _results.Items.Clear();
        _status.Text = "正在连接 CANape 并添加，请稍候...";
        UseWaitCursor = true;

        System.Threading.ThreadPool.QueueUserWorkItem(delegate
        {
            string output;
            int exitCode;
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("powershell.exe", arguments);
                info.WorkingDirectory = Path.GetDirectoryName(script);
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.StandardOutputEncoding = Encoding.UTF8;
                info.StandardErrorEncoding = Encoding.UTF8;
                using (Process process = Process.Start(info))
                {
                    output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    exitCode = process.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.ToString();
                exitCode = 1;
            }
            finally
            {
                TryDelete(measurementFile);
                TryDelete(calibrationFile);
            }

            BeginInvoke((Action)delegate
            {
                _run.Enabled = true;
                _refreshStatus.Enabled = true;
                _progress.Visible = false;
                UseWaitCursor = false;
                _log.Text = output;
                ParseResults(output);
                if (exitCode == 0)
                {
                    _status.Text = "添加完成，已通过在线验证";
                    MessageBox.Show(this, "观测量和标定量已添加成功。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    _status.Text = "添加失败，请查看执行日志";
                    MessageBox.Show(this, "操作未完成，详细失败阶段和 CANape/ASAP3 信息已显示在执行日志中。", "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            });
        });
    }

    private string ValidateInputs()
    {
        if (!File.Exists(_project.Text.Trim())) return "Please select a valid CANape project.";
        if (!File.Exists(_elf.Text.Trim())) return "Please select a valid ELF file.";
        if (string.IsNullOrWhiteSpace(_module.Text)) return "Please enter the CANape module name.";
        if (!HasVariable(_measurements.Text) && !HasVariable(_calibrations.Text)) return "Enter at least one measurement or calibration variable.";
        return null;
    }

    private static bool HasVariable(string text)
    {
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        foreach (string line in lines)
        {
            string value = line.Trim();
            if (value.Length > 0 && !value.StartsWith("#")) return true;
        }
        return false;
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

[DataContract]
internal sealed class UserSettings
{
    [DataMember] public string ProjectPath;
    [DataMember] public string ElfPath;
    [DataMember] public string ModuleName;
    [DataMember] public string Measurements;
    [DataMember] public string Calibrations;
    [DataMember] public string ProjectDirectory;
    [DataMember] public string ElfDirectory;
    [DataMember] public string MeasurementDirectory;
    [DataMember] public string CalibrationDirectory;
    [DataMember] public bool DarkMode;

    private static string FilePath
    {
        get
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CanapeA2LUpdater");
            return Path.Combine(directory, "settings.json");
        }
    }

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                using (FileStream stream = File.OpenRead(FilePath))
                {
                    return (UserSettings)new DataContractJsonSerializer(typeof(UserSettings)).ReadObject(stream);
                }
            }
        }
        catch { }
        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            string directory = Path.GetDirectoryName(FilePath);
            Directory.CreateDirectory(directory);
            using (FileStream stream = File.Create(FilePath))
            {
                new DataContractJsonSerializer(typeof(UserSettings)).WriteObject(stream, this);
            }
        }
        catch { }
    }

    public void Clear()
    {
        ProjectPath = null;
        ElfPath = null;
        ModuleName = "XCPsim";
        Measurements = null;
        Calibrations = null;
        ProjectDirectory = null;
        ElfDirectory = null;
        MeasurementDirectory = null;
        CalibrationDirectory = null;
        DarkMode = false;
        Save();
    }
}
