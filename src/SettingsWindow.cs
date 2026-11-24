using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace Gamepad
{
    internal sealed class SettingsWindow : Form
    {
        static readonly Color Bg = Color.FromArgb(23, 30, 41), Side = Color.FromArgb(31, 41, 54),
                              Field = Color.FromArgb(43, 56, 72), Ink = Color.FromArgb(235, 242, 247),
                              Muted = Color.FromArgb(173, 190, 202), Mint = Color.FromArgb(152, 235, 205);
        readonly Host host;
        Settings draft;
        string deviceId;
        bool loading, loadingMapping;
        readonly Panel content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(20, 14, 20, 12) };
        readonly Label heading = new Label { Dock = DockStyle.Top, Height = 48 };
        readonly FlowLayoutPanel nav =
            new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                                  WrapContents = false, Padding = new Padding(10, 20, 10, 0) };
        readonly List<Button> choices = new List<Button>();
        int page;
        readonly ComboBox profileName = new ComboBox { Width = 184, DropDownStyle = ComboBoxStyle.DropDown };
        readonly ComboBox device = Combo(), primary = Combo(), display = Combo(), layout = Combo();
        readonly ComboBox confirmButton = ButtonPicker("确认按键"), backButton = ButtonPicker("返回按键");
        readonly FlowLayoutPanel quickBindings = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
        readonly Label inputPreview = Caption("");
        ControllerCapabilities displayedCapabilities;
        IControllerDevice displayedDevice;
        readonly List<IControllerDevice> deviceOptions = new List<IControllerDevice>();
        readonly List<string> primaryOptions = new List<string> { "" };
        readonly NumericUpDown speed = Number(100, 4000, 0, 50), dead = Number(2, 75, 0, 1),
                               curve = Number(.5m, 4, 2, .05m), height = Number(28, 52, 0, 1);
        readonly Label status = Caption(""), capabilities = Caption("");
        readonly StickView live = new StickView();
        readonly DataGridView grid = new DataGridView();
        readonly TextBox combination = new TextBox(), apps = new TextBox { Multiline = true, AcceptsReturn = true,
                                                                           ScrollBars = ScrollBars.Vertical };
        readonly CheckBox auto = new CheckBox { Text = "手柄选中可编辑输入框时自动打开键盘", AutoSize = true };
        readonly Timer refresh = new Timer { Interval = 100 };
        readonly ToolTip mappingTips = new ToolTip();
        public SettingsWindow(Host app)
        {
            host = app;
            draft = SettingsStore.Decode(SettingsStore.Encode(host.Config));
            Text = "Windows Gamepad Desktop · 设置";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 10);
            ForeColor = Ink;
            BackColor = Bg;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            MinimumSize = new Size(760, 520);
            ClientSize = new Size(950, 630);
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                                               Margin = Padding.Empty };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(shell);
            nav.BackColor = Side;
            shell.Controls.Add(nav, 0, 0);
            shell.Controls.Add(content, 1, 0);
            var brand = Caption("手柄桌面");
            brand.Font = new Font(Font.FontFamily, 17, FontStyle.Bold);
            brand.ForeColor = Mint;
            brand.Margin = new Padding(6, 0, 0, 26);
            nav.Controls.Add(brand);
            foreach (string t in new[] { "设备", "按键映射", "虚拟键盘", "应用规则" })
            {
                int index = choices.Count;
                var b = Button(t, () => SelectPage(index));
                b.Width = 130;
                b.Height = 42;
                b.TextAlign = ContentAlignment.MiddleLeft;
                choices.Add(b);
                nav.Controls.Add(b);
            }
            nav.Controls.Add(Button("导入配置…", Import));
            nav.Controls.Add(Button("导出配置…", Export));
            var save = Button("保存设置", Save);
            save.BackColor = Mint;
            save.ForeColor = Bg;
            save.Margin = new Padding(3, 21, 3, 3);
            nav.Controls.Add(save);
            heading.ForeColor = Ink;
            heading.Font = new Font(Font.FontFamily, 19, FontStyle.Bold);
            primary.Items.Add("自动选择首个接入的手柄");
            primary.AccessibleName = "主控手柄";
            SetupQuickBindings();
            layout.Items.AddRange(KeyboardModel.Layouts);
            display.Items.Add("跟随目标输入框");
            display.Items.AddRange(Screen.AllScreens.Select(s => s.DeviceName).ToArray());
            SetupGrid();
            mappingTips.SetToolTip(combination, "按满 2 秒后松开切换模式，按满 3 秒锁定或解锁。");
            device.AccessibleName = "编辑设备";
            device.SelectedIndexChanged += (s, e) => SwitchDevice();
            InitDraft();
            refresh.Tick += (s, e) => RefreshDevice();
            refresh.Start();
            FormClosed += (s, e) =>
            {
                refresh.Dispose();
                mappingTips.Dispose();
            };
            SelectPage(0);
        }
        static Label Caption(string text)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 6, 0, 6) };
        }
        static ComboBox Combo()
        {
            return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 250 };
        }
        static ComboBox ButtonPicker(string label)
        {
            return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 126, AccessibleName = label,
                                  FormattingEnabled = true };
        }
        void SetupQuickBindings()
        {
            foreach (var picker in new[] { confirmButton, backButton })
                picker.Format += (sender, args) =>
                {
                    var id = args.ListItem as string;
                    if (id != null && displayedCapabilities != null)
                        args.Value = displayedCapabilities.DisplayLabel(id);
                };
            quickBindings.Controls.Add(Caption("确认"));
            quickBindings.Controls.Add(confirmButton);
            quickBindings.Controls.Add(Caption("返回"));
            quickBindings.Controls.Add(backButton);
            quickBindings.Controls.Add(Button("应用", () => ApplyQuickBindings(false)));
            quickBindings.Controls.Add(Button("互换", () => ApplyQuickBindings(true)));
        }

        void ApplyQuickBindings(bool swap)
        {
            string confirm = confirmButton.SelectedItem as string, back = backButton.SelectedItem as string;
            if (confirm == null || back == null || confirm == back)
            {
                MessageBox.Show(this, "请为确认和返回选择两个不同的按键。", "按键映射");
                return;
            }
            StoreProfile();
            var d = deviceOptions.First(item => item.Id == deviceId);
            ConfirmBackBindings.Apply(draft.For(deviceId), swap ? back : confirm, swap ? confirm : back,
                                      confirmButton.Items.Cast<string>());
            LoadProfile(d);
        }

        static NumericUpDown Number(decimal min, decimal max, int decimals, decimal step)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = decimals, Increment = step,
                                       Width = 110 };
        }
        static Button Button(string t, Action f)
        {
            var b = new Button { Text = t,
                                 AutoSize = true,
                                 FlatStyle = FlatStyle.Flat,
                                 BackColor = Field,
                                 ForeColor = Ink,
                                 Padding = new Padding(8, 3, 8, 3),
                                 Margin = new Padding(3, 3, 3, 5) };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (s, e) => f();
            return b;
        }
        void RefreshDeviceOptions()
        {
            foreach (var item in host.Devices)
            {
                int index = deviceOptions.FindIndex(option => option.Id == item.Id);
                if (index < 0)
                {
                    deviceOptions.Add(item);
                    device.Items.Add(item.Name);
                }
                else
                    deviceOptions[index] = item;

                int primaryIndex = primaryOptions.IndexOf(item.Id);
                if (primaryIndex < 0)
                {
                    primaryOptions.Add(item.Id);
                    primary.Items.Add(item.Name);
                }
                else if (!Equals(primary.Items[primaryIndex], item.Name))
                    primary.Items[primaryIndex] = item.Name;
            }
        }

        void InitDraft()
        {
            loading = true;
            RefreshDeviceOptions();
            int i = deviceOptions.FindIndex(d => d.Id == (host.ActiveId ?? draft.Primary));
            device.SelectedIndex = deviceOptions.Count == 0 ? -1 : Math.Max(0, i);
            if (!primaryOptions.Contains(draft.Primary))
            {
                primaryOptions.Add(draft.Primary);
                primary.Items.Add("已保存的手柄（未连接）");
            }
            primary.SelectedIndex = primaryOptions.IndexOf(draft.Primary);
            auto.Checked = draft.AutoKeyboard;
            height.Value = draft.KeyboardHeight;
            display.SelectedIndex = Math.Max(0, display.Items.IndexOf(draft.Display));
            layout.SelectedItem = draft.StartLayout;
            apps.Lines = draft.GameApps;
            loading = false;
            deviceId = null;
            SwitchDevice();
        }
        void SetupGrid()
        {
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.BackgroundColor = Bg;
            grid.BorderStyle = BorderStyle.None;
            grid.ColumnHeadersHeight = 34;
            grid.RowTemplate.Height = 29;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Side;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Mint;
            grid.DefaultCellStyle.BackColor = Field;
            grid.DefaultCellStyle.ForeColor = Ink;
            grid.GridColor = Side;
            grid.Columns.Add("control", "手柄输入");
            grid.Columns.Add("desktop", "桌面动作");
            grid.Columns.Add("keyboard", "键盘动作");
            grid.Columns[0].ReadOnly = true;
            grid.Columns[1].ToolTipText = "可直接填写快捷键，如 Key:Ctrl+Shift+S。";
            grid.CellValueChanged += (sender, args) =>
            {
                if (!loadingMapping && deviceId != null && args.RowIndex >= 0 && args.ColumnIndex > 0)
                {
                    var profile = draft.For(deviceId);
                    ReadMappingRows(profile);
                    ReflectQuickBindings(profile);
                }
            };
            grid.CellFormatting += (sender, args) =>
            {
                var id = args.Value as string;
                if (args.ColumnIndex == 0 && id != null && displayedCapabilities != null)
                {
                    args.Value = displayedCapabilities.DisplayLabel(id);
                    args.FormattingApplied = true;
                }
            };
            foreach (DataGridViewColumn c in grid.Columns)
                c.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.EditingControlShowing += (s, e) =>
            {
                var combo = e.Control as ComboBox;
                if (combo != null)
                {
                    combo.ForeColor = Color.Black;
                    combo.BackColor = Color.White;
                }
            };
        }
        void StoreProfile()
        {
            if (deviceId == null)
                return;
            grid.EndEdit();
            var p = draft.For(deviceId);
            p.Speed = (double)speed.Value;
            p.DeadZone = (double)dead.Value / 100;
            p.Curve = (double)curve.Value;
            p.ModeChord = combination.Text.Trim();
            ReadMappingRows(p);
        }

        void ReadMappingRows(Profile p)
        {
            foreach (DataGridViewRow r in grid.Rows)
            {
                var key = (string)r.Cells[0].Value;
                p.Desktop[key] = Convert.ToString(r.Cells[1].Value).Trim();
                p.Keyboard[key] = Convert.ToString(r.Cells[2].Value).Trim();
            }
        }
        void SwitchDevice()
        {
            if (loading || device.SelectedIndex < 0)
                return;
            StoreProfile();
            var d = deviceOptions[device.SelectedIndex];
            deviceId = d.Id;
            ReflectProfileName();
            LoadProfile(d);
        }
        void ReflectProfileName()
        {
            string name;
            profileName.Items.Clear();
            profileName.Items.Add("");
            profileName.Items.AddRange(
                draft.Profiles.Keys.Where(k => k.StartsWith("profile/", StringComparison.Ordinal))
                    .Select(k => k.Substring(8))
                    .ToArray());
            profileName.Text = draft.AssignedProfiles.TryGetValue(deviceId, out name) ? name : "";
        }
        void AssignProfile()
        {
            if (deviceId == null)
                return;
            StoreProfile();
            string name = profileName.Text.Trim();
            if (name.Length > 64)
                throw new FormatException("配置名最长为 64 字");
            if (name.Length == 0)
                draft.AssignedProfiles.Remove(deviceId);
            else
            {
                if (!draft.Profiles.ContainsKey("profile/" + name))
                {
                    var js = new System.Web.Script.Serialization.JavaScriptSerializer();
                    draft.Profiles["profile/" + name] = js.Deserialize<Profile>(js.Serialize(draft.For(deviceId)));
                }
                draft.AssignedProfiles[deviceId] = name;
            }
            ReflectProfileName();
            LoadProfile(deviceOptions.First(d => d.Id == deviceId));
        }
        void LoadProfile(IControllerDevice d)
        {
            var p = draft.For(deviceId);
            speed.Value = (decimal)p.Speed;
            dead.Value = (decimal)p.DeadZone * 100;
            curve.Value = (decimal)p.Curve;
            combination.Text = p.ModeChord;
            combination.Width = 186;
            displayedDevice = d;
            displayedCapabilities = d.Capabilities;
            var buttons = d.Capabilities.Controls.Where(k => !Profile.IsAxis(k)).ToArray();
            foreach (var picker in new[] { confirmButton, backButton })
            {
                picker.Items.Clear();
                picker.Items.AddRange(buttons);
            }
            ReflectQuickBindings(p);
            loadingMapping = true;
            grid.Rows.Clear();
            foreach (string k in d.Capabilities.Controls)
            {
                string v, w;
                if (!p.Desktop.TryGetValue(k, out v))
                    v = "None";
                if (!p.Keyboard.TryGetValue(k, out w))
                    w = "None";
                int i = grid.Rows.Add(k, v, w);
                var cell = new DataGridViewComboBoxCell { FlatStyle = FlatStyle.Flat };
                cell.Items.AddRange(Profile.IsAxis(k) ? new[] { "None", "Navigate" } : Profile.KeyboardActions);
                cell.Value = w;
                grid.Rows[i].Cells[2] = cell;
            }
            loadingMapping = false;
            RefreshReading(d);
        }

        void ReflectQuickBindings(Profile profile)
        {
            var buttons = confirmButton.Items.Cast<string>();
            confirmButton.SelectedItem = ConfirmBackBindings.FindConfirm(profile, buttons);
            backButton.SelectedItem = ConfirmBackBindings.FindBack(profile, buttons);
        }

        void RefreshDevice()
        {
            RefreshDeviceOptions();
            if (device.SelectedIndex < 0 && deviceOptions.Count > 0)
                device.SelectedIndex = 0;
            if (deviceId == null)
                return;
            var d = deviceOptions.First(o => o.Id == deviceId);
            if (!ReferenceEquals(displayedDevice, d))
            {
                StoreProfile();
                LoadProfile(d);
                return;
            }
            RefreshReading(d);
        }

        void RefreshReading(IControllerDevice d)
        {
            ControllerState s;
            if (!host.States.TryGetValue(deviceId, out s))
                s = new ControllerState();
            var active = host.Devices.Where(x => host.States.ContainsKey(x.Id) && host.States[x.Id].Connected)
                             .Select(x => x.Name)
                             .ToArray();
            status.Text = "已连接  " + (active.Length == 0 ? "无" : String.Join("、", active)) + "     ·     " +
                          host.ModeName;
            capabilities.Text = d.Capabilities.Controls.Length + " 项输入    陀螺仪 " +
                                (d.Capabilities.HasGyroscope ? "可用" : "不可用") + "    加速度计 " +
                                (d.Capabilities.HasAccelerometer ? "可用" : "不可用") + "    电池 " +
                                (s.BatteryLevel.HasValue ? s.BatteryLevel + "/3" : "不可用");
            var pressed = new List<string>(s.Buttons);
            if (s.LT >= .52 && !pressed.Contains("LT"))
                pressed.Add("LT");
            if (s.RT >= .52 && !pressed.Contains("RT"))
                pressed.Add("RT");
            inputPreview.Text = !s.Connected ? "连接手柄后可查看按下的键" :
                pressed.Count == 0 ? "按下手柄按键可识别对应输入" :
                "按下 " + String.Join("、", pressed.Select(d.Capabilities.DisplayLabel));
            live.State = s;
            if (page == 0)
                live.Invalidate();
        }
        void SelectPage(int i)
        {
            page = i;
            content.SuspendLayout();
            foreach (Control shared in new Control[] { heading, status, primary, device, capabilities, live, speed,
                                                       dead, curve, profileName, combination, grid, auto, layout,
                                                       display, height, apps, quickBindings, inputPreview })
                if (shared.Parent != null)
                    shared.Parent.Controls.Remove(shared);
            foreach (Control old in content.Controls.Cast<Control>().ToArray())
                old.Dispose();
            content.Controls.Add(heading);
            heading.Text = choices[i].Text;
            for (int j = 0; j < choices.Count; j++)
            {
                choices[j].BackColor = i == j ? Mint : Field;
                choices[j].ForeColor = i == j ? Bg : Ink;
            }
            var f =
                new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
                                      WrapContents = false, AutoScroll = true, Padding = new Padding(0, 3, 0, 0) };
            content.Controls.Add(f);
            f.BringToFront();
            if (i == 0)
            {
                f.Controls.Add(status);
                f.Controls.Add(Line("主控手柄", primary));
                f.Controls.Add(Line("查看设备", device));
                f.Controls.Add(capabilities);
                live.Width = Math.Max(440, content.ClientSize.Width - 58);
                live.Height = 153;
                f.Controls.Add(live);
                f.Controls.Add(Line("指针速度（像素 / 秒）", speed));
                f.Controls.Add(Line("摇杆死区（%）", dead));
                f.Controls.Add(Line("加速曲线指数", curve));
                f.Controls.Add(Caption(host.HotkeyAvailable ? "紧急暂停   Ctrl + Alt + F12"
                                                            : "紧急快捷键已被占用，可从托盘暂停"));
            }
            else if (i == 1)
            {
                f.Controls.Add(Line("编辑设备", device));
                var profiles = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
                profiles.Controls.Add(profileName);
                profiles.Controls.Add(Button("载入 / 新建", () =>
                                                            {
                                                                try
                                                                {
                                                                    AssignProfile();
                                                                }
                                                                catch (Exception ex)
                                                                {
                                                                    MessageBox.Show(this, ex.Message);
                                                                }
                                                            }));
                f.Controls.Add(Line("配置名（可留空）", profiles));
                quickBindings.MaximumSize = new Size(Math.Max(440, content.ClientSize.Width - 45), 0);
                f.Controls.Add(quickBindings);
                f.Controls.Add(Caption("同步设置桌面和虚拟键盘。应用后点左侧保存设置。"));
                inputPreview.MaximumSize = new Size(quickBindings.MaximumSize.Width, 0);
                f.Controls.Add(inputPreview);
                f.Controls.Add(Line("模式组合键", combination));
                var picker = Combo();
                picker.Width = 155;
                picker.Items.AddRange(Profile.DesktopActions.Concat(new[] { "Cursor", "Scroll" }).ToArray());
                picker.SelectedIndex = 0;
                var use = Button("应用到选中行", () =>
                                                 {
                                                     if (grid.CurrentRow != null)
                                                     {
                                                         grid.EndEdit();
                                                         grid.CurrentRow.Cells[1].Value = picker.Text;
                                                     }
                                                 });
                var tools = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
                tools.Controls.Add(picker);
                tools.Controls.Add(use);
                tools.Controls.Add(Caption("也可直接编辑表格"));
                f.Controls.Add(tools);
                grid.Width = Math.Max(445, content.ClientSize.Width - 94);
                grid.Height = Math.Max(190, content.ClientSize.Height - 328);
                f.Controls.Add(grid);
            }
            else if (i == 2)
            {
                f.Controls.Add(auto);
                f.Controls.Add(Line("初始布局", layout));
                f.Controls.Add(Line("显示器", display));
                f.Controls.Add(Line("屏幕高度占比（%）", height));
                f.Controls.Add(Caption("拼音布局使用离线候选。系统 IME 布局使用目标应用的输入法。"));
            }
            else
            {
                f.Controls.Add(
                    Caption("以下进程位于前台时使用原生手柄，离开后恢复桌面。每行填写一个 .exe 文件名。"));
                apps.Width = Math.Max(440, content.ClientSize.Width - 65);
                apps.Height = 205;
                f.Controls.Add(apps);
                f.Controls.Add(Button("选择应用程序…",
                                      () =>
                                      {
                                          using (var dlg =
                                                     new OpenFileDialog {
                                                         Filter = "应用程序|*.exe"
                                                     }) if (dlg.ShowDialog(this) == DialogResult.OK) apps.Lines =
                                              apps.Lines.Where(a => !String.IsNullOrWhiteSpace(a))
                                                  .Concat(new[] { System.IO.Path.GetFileName(dlg.FileName) })
                                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                                  .ToArray();
                                      }));
            }
            content.ResumeLayout(true);
        }
        static Control Line(string name, Control input)
        {
            var p =
                new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 5, 0, 5) };
            var label = Caption(name);
            label.AutoSize = false;
            label.Size = new Size(175, 26);
            p.Controls.Add(label);
            p.Controls.Add(input);
            return p;
        }
        Settings Gather()
        {
            if (deviceId != null)
            {
                string named;
                draft.AssignedProfiles.TryGetValue(deviceId, out named);
                if ((named ?? "") != profileName.Text.Trim())
                    AssignProfile();
                StoreProfile();
            }
            draft.Primary = primary.SelectedIndex < 0 ? "" : primaryOptions[primary.SelectedIndex];
            draft.AutoKeyboard = auto.Checked;
            draft.Display = display.SelectedIndex <= 0 ? "" : display.Text;
            draft.StartLayout = layout.Text;
            draft.KeyboardHeight = (int)height.Value;
            draft.GameApps = apps.Lines.Select(s => s.Trim())
                                 .Where(s => s.Length > 0)
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .ToArray();
            draft.Validate();
            return draft;
        }
        protected override bool ProcessCmdKey(ref Message message, Keys key)
        {
            if (key == Keys.Escape && !grid.IsCurrentCellInEditMode)
            {
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref message, key);
        }
        void Save()
        {
            try
            {
                host.Apply(SettingsStore.Decode(SettingsStore.Encode(Gather())));
                Close();
            }
            catch (Exception e)
            {
                MessageBox.Show(this, e.Message, "无法保存设置");
            }
        }
        void Import()
        {
            using (var dlg = new OpenFileDialog { Filter = "JSON 配置|*.json" }) if (dlg.ShowDialog(this) ==
                                                                                     DialogResult.OK)
            try
            {
                var imported = SettingsStore.Load(dlg.FileName);
                draft = imported;
                InitDraft();
            }
            catch (Exception e)
            {
                MessageBox.Show(this, e.Message, "导入失败");
            }
        }
        void Export()
        {
            using (var dlg = new SaveFileDialog { Filter = "JSON 配置|*.json",
                                                  FileName = "gamepad-profile.json" }) if (dlg.ShowDialog(this) ==
                                                                                           DialogResult.OK)
            try
            {
                SettingsStore.Save(dlg.FileName, Gather());
            }
            catch (Exception e)
            {
                MessageBox.Show(this, e.Message, "导出失败");
            }
        }
    }
    internal sealed class StickView : Control
    {
        internal ControllerState State;
        public StickView()
        {
            DoubleBuffered = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var s = State;
            if (s == null)
                return;
            var g = e.Graphics;
            g.Clear(Color.FromArgb(31, 41, 54));
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Plot(g, 52, 54, s.LX, s.LY);
            Plot(g, 195, 54, s.RX, s.RY);
            using (var brush = new SolidBrush(Color.White))
            {
                g.DrawString(String.Format("L   {0:F2} / {1:F2}", s.LX, s.LY), Font, brush, 5, 95);
                g.DrawString(String.Format("R   {0:F2} / {1:F2}", s.RX, s.RY), Font, brush, 153, 95);
                g.DrawString(String.Format("LT    {0:F2}\nRT    {1:F2}", s.LT, s.RT), Font, brush, 295, 32);
                g.DrawString(s.Connected
                                 ? (s.Buttons.Count == 0 ? "已连接 · 等待按键" : String.Join("   ", s.Buttons))
                                 : "等待设备连接",
                             Font, brush, 12, 124);
            }
        }
        static void Plot(Graphics g, int x, int y, double a, double b)
        {
            using (var pen = new Pen(Color.FromArgb(100, 125, 141), 2))
            {
                g.DrawEllipse(pen, x - 34, y - 34, 68, 68);
                g.DrawLine(pen, x - 6, y, x + 6, y);
                g.DrawLine(pen, x, y - 6, x, y + 6);
            }
            using (var brush = new SolidBrush(Color.FromArgb(152, 235, 205)))
                g.FillEllipse(brush, (float)(x + a * 30) - 5, (float)(y - b * 30) - 5, 10, 10);
        }
    }
}