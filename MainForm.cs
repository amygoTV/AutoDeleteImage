using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AutoDeleteImage
{
    public class MainForm : Form
    {
        // ==================== UI 控件 ====================
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _miExit;
        private ToolStripMenuItem _miAutoStart;

        private DataGridView _dgvTasks;
        private Button _btnAdd;
        private Button _btnEdit;
        private Button _btnDelete;
        private Button _btnRunNow;
        private Label _lblStatus;
        private ListBox _lstLog;
        private Timer _timer;

        private AutoCleanService _cleanService;
        private readonly List<TaskItem> _tasks = new List<TaskItem>();
        private readonly List<string> _pendingLogs = new List<string>();
        private string _editingId = null;
        private bool _exiting = false;
        private bool _lastRunFailed = false;

        // 版本号（每次更新请同步修改 开发日志.md）
        private const string APP_VERSION = "1.3.1";
        private const int MAX_TASKS = 20;

        public MainForm()
        {
            InitializeComponent();

            LoadAppIcon();
            InitTrayIcon();

            LoadTasks();
            RefreshGrid();
            ApplyAutoStartUI();

            _cleanService = new AutoCleanService();
            _cleanService.LogMessage += OnServiceLog;
            _cleanService.RunStarted += OnServiceRunStarted;
            _cleanService.TaskCompleted += OnServiceTaskCompleted;
            _cleanService.TasksChanged += OnServiceTasksChanged;

            // 每 60 秒确认一次后台监控是否在运行，不阻塞界面
            _timer = new Timer { Interval = 60 * 1000 };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            // 立即启动后台监控（内部延迟 5 秒执行，不会拖慢启动）
            _cleanService.Start(_tasks);

            AppendLog($"程序已启动（v{APP_VERSION}），后台监控已开启");
        }

        private void InitializeComponent()
        {
            Text = "自动删图工具 v" + APP_VERSION;
            Size = new Size(1100, 720);
            MinimumSize = new Size(900, 620);
            StartPosition = FormStartPosition.CenterScreen;
            Font = SystemFonts.MessageBoxFont;

            // ---------- 顶部工具栏 ----------
            var toolPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56
            };

            _btnAdd = new Button { Text = "新增任务", Width = 84, Height = 30, Left = 12, Top = 13 };
            _btnEdit = new Button { Text = "编辑任务", Width = 84, Height = 30, Left = 104, Top = 13 };
            _btnDelete = new Button { Text = "删除任务", Width = 84, Height = 30, Left = 196, Top = 13 };
            _btnRunNow = new Button { Text = "立即执行", Width = 92, Height = 30, Left = 296, Top = 13 };

            toolPanel.Controls.AddRange(new Control[] { _btnAdd, _btnEdit, _btnDelete, _btnRunNow });

            // ---------- 任务列表 ----------
            _dgvTasks = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                MultiSelect = false,
                RowHeadersVisible = false,
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeight = 30,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                RowTemplate = { Height = 30 }
            };

            // 第一列：复选框（勾选=启用，取消=禁用）
            _dgvTasks.Columns.Add(new DataGridViewCheckBoxColumn
            {
                Name = "colEnabled",
                HeaderText = "启用",
                Width = 60,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            });
            _dgvTasks.Columns.Add("colName", "任务名称");
            _dgvTasks.Columns.Add("colFolder", "文件夹路径");
            _dgvTasks.Columns.Add("colExt", "文件格式");
            _dgvTasks.Columns.Add("colRetention", "保留天数");
            _dgvTasks.Columns.Add("colTrigger", "触发方式");
            _dgvTasks.Columns.Add("colLastRun", "上次运行");
            _dgvTasks.Columns.Add("colDeleted", "已删除数");

            _dgvTasks.Columns["colName"].Width = 140;
            _dgvTasks.Columns["colExt"].Width = 130;
            _dgvTasks.Columns["colRetention"].Width = 80;
            _dgvTasks.Columns["colTrigger"].Width = 120;
            _dgvTasks.Columns["colLastRun"].Width = 150;
            _dgvTasks.Columns["colDeleted"].Width = 80;
            // 只让文件夹路径列自动填满剩余空间
            _dgvTasks.Columns["colFolder"].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            // 禁止表头排序：否则行顺序与任务列表不一致，会编辑/删除错任务
            foreach (DataGridViewColumn col in _dgvTasks.Columns)
            {
                col.SortMode = DataGridViewColumnSortMode.NotSortable;
            }

            _dgvTasks.CellClick += DgvTasks_CellClick;

            // ---------- 底部状态与日志 ----------
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 190,
                Padding = new Padding(10)
            };

            _lstLog = new ListBox
            {
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9F),
                IntegralHeight = false
            };

            _lblStatus = new Label
            {
                Text = "未运行",
                Dock = DockStyle.Top,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                ForeColor = Color.Black
            };

            bottomPanel.Controls.Add(_lstLog);
            bottomPanel.Controls.Add(_lblStatus);

            // 注意添加顺序：先加 Fill，再加 Top/Bottom，避免顶部工具栏遮住表格表头
            Controls.Add(_dgvTasks);
            Controls.Add(toolPanel);
            Controls.Add(bottomPanel);

            _btnAdd.Click += BtnAdd_Click;
            _btnEdit.Click += BtnEdit_Click;
            _btnDelete.Click += BtnDelete_Click;
            _btnRunNow.Click += BtnRunNow_Click;

            Resize += MainForm_Resize;
            FormClosing += MainForm_FormClosing;
            Shown += MainForm_Shown;
        }

        // ==================== 图标与托盘 ====================

        private void LoadAppIcon()
        {
            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Delete_image.ico");
                Icon = File.Exists(icoPath) ? new Icon(icoPath) : CreateAppIcon();
            }
            catch
            {
                Icon = SystemIcons.Application;
            }
        }

        private void InitTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Icon = Icon ?? SystemIcons.Application,
                Visible = true,
                Text = "自动删图工具 v" + APP_VERSION
            };

            _trayMenu = new ContextMenuStrip();
            _miAutoStart = new ToolStripMenuItem("开机自启");
            _miExit = new ToolStripMenuItem("退出");

            _miAutoStart.Click += MiAutoStart_Click;
            _miExit.Click += MiExit_Click;

            _trayMenu.Items.Add(_miAutoStart);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add(_miExit);

            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.DoubleClick += TrayIcon_DoubleClick;
        }

        private void ShowTrayBalloon(string text)
        {
            if (_trayIcon == null) return;
            _trayIcon.BalloonTipTitle = "自动删图工具";
            _trayIcon.BalloonTipText = text;
            _trayIcon.ShowBalloonTip(2000);
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                _trayIcon.Visible = true;
                ShowTrayBalloon("程序已最小化到托盘，双击图标恢复");
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_exiting) return;

            // 点关闭按钮 = 最小化到托盘，程序继续在后台清理
            e.Cancel = true;
            Hide();
            _trayIcon.Visible = true;
            ShowTrayBalloon("程序仍在后台运行，右键托盘图标可退出");
        }

        private void TrayIcon_DoubleClick(object sender, EventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void MiAutoStart_Click(object sender, EventArgs e)
        {
            bool enable = !AutoStartHelper.IsEnabled;
            try
            {
                AutoStartHelper.SetEnabled(enable);
                _miAutoStart.Checked = enable;
                AppendLog(enable ? "已开启开机自启" : "已关闭开机自启");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "设置开机自启失败：" + ex.Message, "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void MiExit_Click(object sender, EventArgs e)
        {
            _exiting = true;

            try { SaveTasks(); } catch { }

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
            }

            try { _cleanService?.Dispose(); } catch { }

            AppendLog("程序已退出");

            // 确保详细日志缓冲全部落盘
            LogHelper.Close();

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            Close();
        }

        // ==================== 状态栏与日志 ====================

        private void SetStatus(string text, Color backColor, Color foreColor)
        {
            if (_lblStatus == null || IsDisposed || !IsHandleCreated) return;

            if (_lblStatus.InvokeRequired)
            {
                try
                {
                    _lblStatus.BeginInvoke(new Action<string, Color, Color>(SetStatus), text, backColor, foreColor);
                }
                catch (InvalidOperationException) { }
                return;
            }

            _lblStatus.Text = text;
            _lblStatus.BackColor = backColor;
            _lblStatus.ForeColor = foreColor;
        }

        private void SetIdleStatus()
        {
            SetStatus("未运行", Color.White, Color.Black);
        }

        private void SetRunningStatus()
        {
            SetStatus("运行中", Color.FromArgb(60, 170, 100), Color.White);
        }

        private void SetFailedStatus()
        {
            SetStatus("运行失败", Color.FromArgb(200, 60, 60), Color.White);
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            FlushPendingLogs();
        }

        private void AppendLog(string msg)
        {
            // 先写文件日志（任何线程都可调用）
            LogHelper.Write(msg);

            if (IsDisposed) return;

            if (_lstLog == null || !IsHandleCreated)
            {
                // 窗体还没创建句柄（启动阶段），先缓存，显示后补到界面
                lock (_pendingLogs)
                {
                    _pendingLogs.Add(msg);
                }
                return;
            }

            if (_lstLog.InvokeRequired)
            {
                try
                {
                    _lstLog.BeginInvoke(new Action<string>(AppendLogToUi), msg);
                }
                catch (InvalidOperationException) { }
                return;
            }

            AppendLogToUi(msg);
        }

        private void AppendLogToUi(string msg)
        {
            if (_lstLog == null || _lstLog.IsDisposed || IsDisposed) return;

            _lstLog.Items.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
            while (_lstLog.Items.Count > 500) _lstLog.Items.RemoveAt(0);
            _lstLog.TopIndex = Math.Max(0, _lstLog.Items.Count - 1);
        }

        private void FlushPendingLogs()
        {
            List<string> pending;
            lock (_pendingLogs)
            {
                if (_pendingLogs.Count == 0) return;
                pending = new List<string>(_pendingLogs);
                _pendingLogs.Clear();
            }

            foreach (var msg in pending)
            {
                AppendLogToUi(msg);
            }
        }

        private void OnServiceLog(string msg)
        {
            AppendLog(msg);

            if (msg.Contains("出错") || msg.Contains("错误") || msg.Contains("异常") || msg.Contains("失败"))
            {
                _lastRunFailed = true;
                SetFailedStatus();
            }
        }

        private void OnServiceRunStarted()
        {
            _lastRunFailed = false;
            SetRunningStatus();
        }

        private void OnServiceTaskCompleted(int count)
        {
            AppendLog($"本轮清理结束，共删除 {count} 个文件");
            if (_lastRunFailed)
            {
                SetFailedStatus();
            }
            else
            {
                SetIdleStatus();
            }
            RefreshGridSafe();
            SaveTasksSafe();
        }

        private void SaveTasksSafe()
        {
            if (IsDisposed || !IsHandleCreated) return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(SaveTasksSafe));
                }
                catch (InvalidOperationException) { }
                return;
            }

            try { SaveTasks(); } catch { }
        }

        private void OnServiceTasksChanged()
        {
            RefreshGridSafe();
        }

        // ==================== 任务表格 ====================

        private void RefreshGridSafe()
        {
            if (IsDisposed || !IsHandleCreated) return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(RefreshGridSafe));
                }
                catch (InvalidOperationException) { }
                return;
            }

            RefreshGrid();
        }

        private void RefreshGrid()
        {
            int keepRow = -1;
            if (_dgvTasks.CurrentCell != null)
            {
                keepRow = _dgvTasks.CurrentCell.RowIndex;
            }

            _dgvTasks.Rows.Clear();

            foreach (var t in _tasks)
            {
                int rowIndex = _dgvTasks.Rows.Add(
                    t.Enabled,
                    t.Name,
                    t.FolderPath,
                    t.FileExtensions,
                    t.RetentionDays,
                    GetTriggerDescription(t),
                    t.LastRunTime == DateTime.MinValue ? "未运行" : t.LastRunTime.ToString("yyyy-MM-dd HH:mm"),
                    t.DeletedCount
                );
                // 用任务 Id 绑定行，避免因排序或刷新导致行号错位
                _dgvTasks.Rows[rowIndex].Tag = t.Id;
            }

            if (keepRow >= 0 && keepRow < _dgvTasks.Rows.Count)
            {
                try
                {
                    _dgvTasks.CurrentCell = _dgvTasks.Rows[keepRow].Cells["colName"];
                    _dgvTasks.Rows[keepRow].Selected = true;
                }
                catch (InvalidOperationException) { }
            }
        }

        private static string GetTriggerDescription(TaskItem t)
        {
            if (t.TriggerMode == 1) return $"空闲 {t.IdleMinutes} 分钟";
            if (t.TriggerMode == 2) return $"磁盘 < {t.DiskFreeThresholdGB}GB";
            if (t.TriggerMode == 3) return $"每日 {t.ScheduleTime}";
            return "手动/定时";
        }

        private TaskItem GetSelectedTask()
        {
            if (_dgvTasks.CurrentRow == null) return null;

            string id = _dgvTasks.CurrentRow.Tag as string;
            if (string.IsNullOrEmpty(id)) return null;

            return _tasks.FirstOrDefault(t => t.Id == id);
        }

        private void DgvTasks_CellClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (e.ColumnIndex != _dgvTasks.Columns["colEnabled"].Index) return;

            var row = _dgvTasks.Rows[e.RowIndex];
            string id = row.Tag as string;
            var task = _tasks.FirstOrDefault(t => t.Id == id);
            if (task == null) return;

            task.Enabled = !task.Enabled;
            row.Cells["colEnabled"].Value = task.Enabled;

            SaveTasks();
            AppendLog($"{(task.Enabled ? "启用" : "禁用")}任务：{task.Name}");
        }

        // ==================== 任务增删改 ====================

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            if (_tasks.Count >= MAX_TASKS)
            {
                MessageBox.Show(this, $"最多只能添加 {MAX_TASKS} 个任务。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _editingId = null;
            ShowTaskDialog(null);
        }

        private void BtnEdit_Click(object sender, EventArgs e)
        {
            var task = GetSelectedTask();
            if (task == null)
            {
                MessageBox.Show(this, "请先在表格中选择要编辑的任务。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _editingId = task.Id;
            ShowTaskDialog(task);
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            var task = GetSelectedTask();
            if (task == null)
            {
                MessageBox.Show(this, "请先在表格中选择要删除的任务。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show(this, $"确定删除任务 [{task.Name}] 吗？", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result != DialogResult.Yes) return;

            _tasks.Remove(task);
            SaveTasks();
            RefreshGrid();
            AppendLog($"已删除任务：{task.Name}");
        }

        private void BtnRunNow_Click(object sender, EventArgs e)
        {
            if (_cleanService == null) return;

            if (_cleanService.IsRunning)
            {
                AppendLog("清理任务正在运行中，请稍候……");
                return;
            }

            var enabledTasks = _tasks.Where(t => t.Enabled).ToList();
            if (enabledTasks.Count == 0)
            {
                AppendLog("没有启用的任务，请先勾选表格第一列");
                MessageBox.Show(this, "没有启用的任务。\n请在表格第一列勾选要执行的任务。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (!_cleanService.RunNow(enabledTasks))
            {
                AppendLog("清理任务正在运行中，请稍候……");
                return;
            }

            AppendLog($"开始手动清理，共 {enabledTasks.Count} 个任务");
        }

        // ==================== 后台监控 ====================

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_cleanService == null) return;
            _cleanService.Start(_tasks);
        }

        // ==================== 配置文件 ====================

        private void LoadTasks()
        {
            _tasks.Clear();

            int count = IniHelper.GetInt("Tasks", "Count", 0);
            if (count > MAX_TASKS) count = MAX_TASKS;

            for (int i = 0; i < count; i++)
            {
                string prefix = "Task" + i;
                var task = new TaskItem
                {
                    Id = IniHelper.GetValue(prefix, "Id", Guid.NewGuid().ToString("N")),
                    Name = IniHelper.GetValue(prefix, "Name", ""),
                    FolderPath = IniHelper.GetValue(prefix, "FolderPath", ""),
                    FileExtensions = IniHelper.GetValue(prefix, "FileExtensions", ".jpg,.png,.bmp"),
                    RetentionDays = IniHelper.GetInt(prefix, "RetentionDays", 7),
                    Enabled = IniHelper.GetBool(prefix, "Enabled", true),
                    TriggerMode = IniHelper.GetInt(prefix, "TriggerMode", 0),
                    IdleMinutes = IniHelper.GetInt(prefix, "IdleMinutes", 30),
                    ScheduleTime = IniHelper.GetValue(prefix, "ScheduleTime", "02:00"),
                    EnableDiskThreshold = IniHelper.GetBool(prefix, "EnableDiskThreshold", false),
                    DiskFreeThresholdGB = IniHelper.GetDouble(prefix, "DiskFreeThresholdGB", 10.0),
                    TargetDriveLetter = IniHelper.GetValue(prefix, "TargetDriveLetter", "C"),
                    DeletedCount = IniHelper.GetInt(prefix, "DeletedCount", 0)
                };

                DateTime lastRun;
                if (DateTime.TryParse(IniHelper.GetValue(prefix, "LastRunTime", ""), out lastRun))
                {
                    task.LastRunTime = lastRun;
                }

                _tasks.Add(task);
            }

            if (_tasks.Count == 0)
            {
                AppendLog("还没有任务，请点击“新增任务”添加");
            }
        }

        private void SaveTasks()
        {
            IniHelper.SetValue("Tasks", "Count", _tasks.Count);

            for (int i = 0; i < _tasks.Count; i++)
            {
                var t = _tasks[i];
                string prefix = "Task" + i;

                IniHelper.SetValue(prefix, "Id", t.Id);
                IniHelper.SetValue(prefix, "Name", t.Name);
                IniHelper.SetValue(prefix, "FolderPath", t.FolderPath);
                IniHelper.SetValue(prefix, "FileExtensions", t.FileExtensions);
                IniHelper.SetValue(prefix, "RetentionDays", t.RetentionDays);
                IniHelper.SetValue(prefix, "Enabled", t.Enabled);
                IniHelper.SetValue(prefix, "TriggerMode", t.TriggerMode);
                IniHelper.SetValue(prefix, "IdleMinutes", t.IdleMinutes);
                IniHelper.SetValue(prefix, "ScheduleTime", t.ScheduleTime);
                IniHelper.SetValue(prefix, "EnableDiskThreshold", t.EnableDiskThreshold);
                IniHelper.SetValue(prefix, "DiskFreeThresholdGB", t.DiskFreeThresholdGB);
                IniHelper.SetValue(prefix, "TargetDriveLetter", t.TargetDriveLetter);
                IniHelper.SetValue(prefix, "DeletedCount", t.DeletedCount);
                IniHelper.SetValue(prefix, "LastRunTime",
                    t.LastRunTime == DateTime.MinValue ? "" : t.LastRunTime.ToString("yyyy-MM-dd HH:mm:ss"));
            }
        }

        private void ApplyAutoStartUI()
        {
            if (_miAutoStart != null)
            {
                _miAutoStart.Checked = AutoStartHelper.IsEnabled;
            }
        }

        // ==================== 任务编辑对话框 ====================

        private void ShowTaskDialog(TaskItem task)
        {
            var dlg = new Form
            {
                Text = task == null ? "新增任务" : "编辑任务",
                Size = new Size(700, 560),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false
            };

            int x = 20;
            int curY = 15;

            void AddRow(string label, Control ctrl, int ctrlWidth, int height)
            {
                var lb = new Label { Text = label, Left = x, Top = curY + 4, Width = 100 };
                ctrl.Left = x + 110;
                ctrl.Top = curY;
                ctrl.Width = ctrlWidth;
                ctrl.Height = height;
                dlg.Controls.Add(lb);
                dlg.Controls.Add(ctrl);
                curY += height + 8;
            }

            // 任务名称
            var txtName = new TextBox();
            AddRow("任务名称：", txtName, 300, 28);

            // 文件夹路径
            var txtFolder = new TextBox
            {
                Left = x + 110,
                Top = curY,
                Width = 420,
                Height = 28
            };
            var btnBrowse = new Button
            {
                Text = "浏览...",
                Left = x + 540,
                Top = curY,
                Width = 80,
                Height = 28
            };
            btnBrowse.Click += (s, ev) =>
            {
                using (var fbd = new FolderBrowserDialog())
                {
                    fbd.Description = "请选择要自动清理的文件夹";
                    fbd.ShowNewFolderButton = false;
                    if (!string.IsNullOrWhiteSpace(txtFolder.Text) && Directory.Exists(txtFolder.Text.Trim()))
                    {
                        fbd.SelectedPath = txtFolder.Text.Trim();
                    }
                    if (fbd.ShowDialog(dlg) == DialogResult.OK)
                    {
                        txtFolder.Text = fbd.SelectedPath;
                    }
                }
            };
            dlg.Controls.Add(new Label { Text = "文件夹路径：", Left = x, Top = curY + 4, Width = 100 });
            dlg.Controls.Add(txtFolder);
            dlg.Controls.Add(btnBrowse);
            curY += 36;

            // 递归清理规则提示
            dlg.Controls.Add(new Label
            {
                Text = "提示：仅当子文件夹全部是图片/CSV/TXT 时才递归清理",
                Left = x + 110,
                Top = curY,
                Width = 440,
                Height = 18,
                ForeColor = SystemColors.GrayText
            });
            curY += 24;

            // 文件格式（复选框）
            dlg.Controls.Add(new Label { Text = "文件格式：", Left = x, Top = curY + 4, Width = 100 });

            var knownExts = new[] { ".jpg", ".png", ".bmp", ".txt", ".csv" };
            var extChecks = new CheckBox[knownExts.Length];
            for (int i = 0; i < knownExts.Length; i++)
            {
                extChecks[i] = new CheckBox
                {
                    Text = knownExts[i],
                    Left = x + 110 + i * 68,
                    Top = curY,
                    Width = 64
                };
                dlg.Controls.Add(extChecks[i]);
            }

            var cbxOther = new CheckBox
            {
                Text = "其他：",
                Left = x + 460,
                Top = curY,
                Width = 60
            };
            var txtOther = new TextBox
            {
                Left = x + 525,
                Top = curY,
                Width = 95
            };
            dlg.Controls.Add(cbxOther);
            dlg.Controls.Add(txtOther);
            curY += 32;

            // 保留天数
            var txtDays = new TextBox { Width = 80 };
            AddRow("保留天数：", txtDays, 80, 28);

            // 启用
            var chkEnabled = new CheckBox
            {
                Text = "启用此任务",
                Left = x + 110,
                Top = curY,
                Width = 120
            };
            dlg.Controls.Add(chkEnabled);
            curY += 32;

            // 触发方式
            dlg.Controls.Add(new Label { Text = "触发方式：", Left = x, Top = curY + 4, Width = 100 });
            var cbxTrigger = new ComboBox
            {
                Left = x + 110,
                Top = curY,
                Width = 220,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cbxTrigger.Items.AddRange(new object[]
            {
                "手动 / 定时检查",
                "电脑空闲时触发",
                "磁盘空余不足时",
                "每日定时执行"
            });
            cbxTrigger.SelectedIndex = 0;
            dlg.Controls.Add(cbxTrigger);
            curY += 36;

            // ---------- 触发参数（按触发方式动态排列） ----------
            int optionsTop = curY;

            var lblIdle = new Label { Text = "空闲分钟数：", Left = x, Top = optionsTop + 4, Width = 100 };
            var txtIdle = new TextBox { Left = x + 110, Top = optionsTop, Width = 100 };

            var lblSched = new Label { Text = "定时时间：", Left = x, Top = optionsTop + 4, Width = 100 };
            var txtSched = new TextBox { Left = x + 110, Top = optionsTop, Width = 100 };

            var lblDisk = new Label { Text = "磁盘空余(GB)：", Left = x, Top = optionsTop + 4, Width = 100 };
            var txtDisk = new TextBox { Left = x + 110, Top = optionsTop, Width = 100 };

            var lblDrive = new Label { Text = "监控盘符：", Left = x, Top = optionsTop + 4, Width = 100 };
            var txtDrive = new TextBox { Left = x + 110, Top = optionsTop, Width = 80 };

            dlg.Controls.AddRange(new Control[]
            {
                lblIdle, txtIdle, lblSched, txtSched, lblDisk, txtDisk, lblDrive, txtDrive
            });

            // ---------- 填充现有值 / 默认值 ----------
            var currentExts = new List<string>();
            if (task != null)
            {
                currentExts = ParseExtensions(task.FileExtensions);

                txtName.Text = task.Name;
                txtFolder.Text = task.FolderPath;

                for (int i = 0; i < knownExts.Length; i++)
                {
                    extChecks[i].Checked = currentExts.Contains(knownExts[i].TrimStart('.'));
                }

                var otherExts = currentExts
                    .Where(e => !knownExts.Contains("." + e))
                    .ToList();
                cbxOther.Checked = otherExts.Count > 0;
                txtOther.Text = string.Join(",", otherExts);

                txtDays.Text = task.RetentionDays.ToString();
                chkEnabled.Checked = task.Enabled;
                cbxTrigger.SelectedIndex = task.TriggerMode >= 0 && task.TriggerMode < cbxTrigger.Items.Count
                    ? task.TriggerMode : 0;
                txtIdle.Text = task.IdleMinutes.ToString();
                txtSched.Text = task.ScheduleTime;
                txtDisk.Text = task.DiskFreeThresholdGB.ToString();
                txtDrive.Text = task.TargetDriveLetter;
            }
            else
            {
                for (int i = 0; i < extChecks.Length; i++)
                {
                    extChecks[i].Checked = knownExts[i] == ".jpg" || knownExts[i] == ".png" || knownExts[i] == ".bmp";
                }
                txtDays.Text = "7";
                chkEnabled.Checked = true;
                cbxTrigger.SelectedIndex = 0;
                txtIdle.Text = "30";
                txtSched.Text = "02:00";
                txtDisk.Text = "10";
                txtDrive.Text = "C";
            }

            // ---------- 底部按钮（位置随触发参数动态变化） ----------
            var btnOK = new Button
            {
                Text = "确定",
                Left = x + 110,
                Top = optionsTop,
                Width = 84,
                Height = 28
            };
            var btnCancel = new Button
            {
                Text = "取消",
                Left = x + 210,
                Top = optionsTop,
                Width = 84,
                Height = 28,
                DialogResult = DialogResult.Cancel
            };

            // 触发方式切换时显示对应参数，并重新排列按钮和对话框高度
            Action updateTriggerUI = () =>
            {
                int mode = cbxTrigger.SelectedIndex;
                int y = optionsTop;

                lblIdle.Visible = txtIdle.Visible = mode == 1;
                if (mode == 1)
                {
                    lblIdle.Top = y + 4;
                    txtIdle.Top = y;
                    y += 36;
                }

                lblSched.Visible = txtSched.Visible = mode == 3;
                if (mode == 3)
                {
                    lblSched.Top = y + 4;
                    txtSched.Top = y;
                    y += 36;
                }

                bool diskMode = mode == 2;
                lblDisk.Visible = txtDisk.Visible = diskMode;
                lblDrive.Visible = txtDrive.Visible = diskMode;
                if (diskMode)
                {
                    lblDisk.Top = y + 4;
                    txtDisk.Top = y;
                    y += 36;
                    lblDrive.Top = y + 4;
                    txtDrive.Top = y;
                    y += 36;
                }

                int btnTop = y + 16;
                btnOK.Top = btnTop;
                btnCancel.Top = btnTop;

                dlg.ClientSize = new Size(dlg.ClientSize.Width, btnTop + btnOK.Height + 14);
            };
            cbxTrigger.SelectedIndexChanged += (s, ev) => updateTriggerUI();
            updateTriggerUI();

            btnOK.Click += (s, ev) =>
            {
                if (string.IsNullOrWhiteSpace(txtName.Text))
                {
                    MessageBox.Show(dlg, "请填写任务名称。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtName.Focus();
                    return;
                }
                if (string.IsNullOrWhiteSpace(txtFolder.Text))
                {
                    MessageBox.Show(dlg, "请选择要清理的文件夹。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtFolder.Focus();
                    return;
                }

                int days;
                if (!int.TryParse(txtDays.Text.Trim(), out days) || days < 0)
                {
                    MessageBox.Show(dlg, "保留天数必须是不小于 0 的整数。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtDays.Focus();
                    return;
                }

                bool hasExt = extChecks.Any(c => c.Checked) ||
                              (cbxOther.Checked && !string.IsNullOrWhiteSpace(txtOther.Text));
                if (!hasExt)
                {
                    MessageBox.Show(dlg, "请至少勾选一种文件格式。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (cbxTrigger.SelectedIndex == 1)
                {
                    int idle;
                    if (!int.TryParse(txtIdle.Text.Trim(), out idle) || idle <= 0)
                    {
                        MessageBox.Show(dlg, "空闲分钟数必须是大于 0 的整数。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        txtIdle.Focus();
                        return;
                    }
                }
                else if (cbxTrigger.SelectedIndex == 2)
                {
                    double disk;
                    if (!double.TryParse(txtDisk.Text.Trim(), out disk) || disk < 0)
                    {
                        MessageBox.Show(dlg, "磁盘空余阈值必须是大于等于 0 的数字。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        txtDisk.Focus();
                        return;
                    }
                }
                else if (cbxTrigger.SelectedIndex == 3)
                {
                    TimeSpan ts;
                    if (!TimeSpan.TryParse(txtSched.Text.Trim(), out ts) ||
                        ts < TimeSpan.Zero || ts >= TimeSpan.FromDays(1))
                    {
                        MessageBox.Show(dlg, "定时时间格式应为 HH:mm，例如 02:00。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        txtSched.Focus();
                        return;
                    }
                }

                if (!Directory.Exists(txtFolder.Text.Trim()))
                {
                    var r = MessageBox.Show(dlg, "该文件夹当前不存在，仍然保存这个任务吗？", "提示",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r != DialogResult.Yes) return;
                }

                dlg.DialogResult = DialogResult.OK;
                dlg.Close();
            };

            dlg.Controls.Add(btnOK);
            dlg.Controls.Add(btnCancel);
            dlg.AcceptButton = btnOK;
            dlg.CancelButton = btnCancel;

            if (dlg.ShowDialog(this) != DialogResult.OK)
            {
                _editingId = null;
                return;
            }

            // ---------- 收集结果 ----------
            var resultExts = new List<string>();
            for (int i = 0; i < knownExts.Length; i++)
            {
                if (extChecks[i].Checked) resultExts.Add(knownExts[i]);
            }
            if (cbxOther.Checked)
            {
                foreach (var e in ParseExtensions(txtOther.Text))
                {
                    string ext = "." + e;
                    if (!resultExts.Contains(ext)) resultExts.Add(ext);
                }
            }

            int retentionDays;
            if (!int.TryParse(txtDays.Text.Trim(), out retentionDays)) retentionDays = 7;

            int idleMinutes;
            if (!int.TryParse(txtIdle.Text.Trim(), out idleMinutes)) idleMinutes = 30;

            double diskThreshold;
            if (!double.TryParse(txtDisk.Text.Trim(), out diskThreshold)) diskThreshold = 10.0;

            string drive = string.IsNullOrWhiteSpace(txtDrive.Text)
                ? "C"
                : txtDrive.Text.Trim().Substring(0, 1).ToUpperInvariant();

            var newTask = new TaskItem
            {
                Id = _editingId ?? Guid.NewGuid().ToString("N"),
                Name = txtName.Text.Trim(),
                FolderPath = txtFolder.Text.Trim(),
                FileExtensions = string.Join(",", resultExts),
                RetentionDays = retentionDays,
                Enabled = chkEnabled.Checked,
                TriggerMode = cbxTrigger.SelectedIndex,
                IdleMinutes = idleMinutes,
                ScheduleTime = txtSched.Text.Trim(),
                EnableDiskThreshold = cbxTrigger.SelectedIndex == 2,
                DiskFreeThresholdGB = diskThreshold,
                TargetDriveLetter = drive
            };

            if (_editingId != null)
            {
                var old = _tasks.FirstOrDefault(t => t.Id == _editingId);
                if (old != null)
                {
                    newTask.LastRunTime = old.LastRunTime;
                    newTask.DeletedCount = old.DeletedCount;
                    newTask.LastError = old.LastError;
                    _tasks[_tasks.IndexOf(old)] = newTask;
                }
                else
                {
                    _tasks.Add(newTask);
                }
            }
            else
            {
                _tasks.Add(newTask);
            }

            _editingId = null;
            SaveTasks();
            RefreshGrid();
            AppendLog("任务已保存：" + newTask.Name);
        }

        private static List<string> ParseExtensions(string raw)
        {
            return (raw ?? string.Empty)
                .Split(new[] { ',', '，', ';', '；', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().TrimStart('.').ToLowerInvariant())
                .Where(s => s.Length > 0)
                .Distinct()
                .ToList();
        }

        // 没有 .ico 文件时的兜底图标
        private Icon CreateAppIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    using (var bgBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                        new Point(0, 0), new Point(0, 32),
                        Color.FromArgb(70, 130, 180), Color.FromArgb(50, 100, 150)))
                    {
                        g.FillEllipse(bgBrush, 2, 2, 28, 28);
                    }

                    using (var pen = new Pen(Color.White, 1.5f))
                    {
                        g.DrawEllipse(pen, 2, 2, 27, 27);
                    }

                    using (var brush = new SolidBrush(Color.White))
                    {
                        g.FillRectangle(brush, 10, 16, 12, 9);
                        g.FillRectangle(brush, 8, 24, 16, 4);
                    }
                }

                return Icon.FromHandle(bitmap.GetHicon());
            }
        }
    }
}
