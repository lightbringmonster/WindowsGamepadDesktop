using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace Gamepad
{
    internal sealed partial class Host : ApplicationContext
    {
        readonly DeviceManager manager;
        readonly MappingEngine engine;
        readonly IInputSink sink;
        internal Settings Config;
        internal readonly Dictionary<string, ControllerState> States = new Dictionary<string, ControllerState>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly KeyboardModel model;
        readonly KeyboardWindow keyboard;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        string selected;
        long nextScan;
        bool paused, game, modeLocked, closing;
        IntPtr target, observed, lastApp;
        readonly ApplicationProfiles policy = new ApplicationProfiles();
        bool firstRule = true;
        public Host(Settings cfg, IControllerProvider provider, IInputSink output, bool openSettings)
        {
            Config = cfg;
            sink = output;
            manager = new DeviceManager(provider);
            engine = new MappingEngine(sink);
            engine.ToggleMode = ToggleGame;
            engine.ModeSwitchReady = ShowSwitchPrompt;
            engine.ToggleModeLock = ToggleModeLock;
            engine.ToggleKeyboard = ToggleKeyboard;
            engine.MappedAction = OnMappedAction;
            engine.Ledger.LeftButtonChanged = OnPrimaryButton;
            engine.KeyboardCommand = Command;
            var dict = new Lazy<PinyinDictionary>(
                () => new PinyinDictionary(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data")));
            model = new KeyboardModel(() => dict.Value);
            keyboard = new KeyboardWindow(model);
            model.Key = k =>
            {
                if (TargetReady())
                    engine.Ledger.Tap(KeyBinding.Parse("Key:" + k));
            };
            model.Insert = v =>
            {
                if (TargetReady())
                    sink.Text(v);
            };
            model.Close = CloseKeyboard;
            tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            tray.Text = "Windows Gamepad Desktop";
            tray.ContextMenuStrip = menu;
            menu.Opening += (s, e) => Menu();
            tray.DoubleClick += (s, e) => ShowSettings();
            tray.Visible = true;
            InitializeSystem();
            if (openSettings)
                ShowSettings();
        }
        internal IEnumerable<IControllerDevice> Devices
        {
            get {
                return manager.Devices;
            }
        }
        internal string ActiveId
        {
            get {
                return selected;
            }
        }
        internal string ModeName
        {
            get {
                string status = paused ? "已暂停" : game ? "原生手柄" : "桌面键鼠";
                return modeLocked ? status + " · 已锁定" : status;
            }
        }
        void Menu()
        {
            menu.Items.Clear();
            var status = menu.Items.Add("当前模式  ·  " + ModeName);
            status.Enabled = false;
            menu.Items.Add("切换控制模式", null, (s, e) => ToggleGame()).Enabled = !modeLocked;
            menu.Items.Add("呼出虚拟键盘", null, (s, e) => RequestTrayKeyboard()).Enabled = !game && !paused;
            menu.Items.Add("设置", null, (s, e) => ShowSettings());
            var pause = new ToolStripMenuItem("暂停映射", null, (s, e) => Pause(!paused));
            pause.Checked = paused;
            menu.Items.Add(pause);
            BuildExtraMenu(menu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出程序", null, (s, e) => ExitThread());
        }
        void RequestTrayKeyboard()
        {
            var restore = lastApp;
            var delay = new Timer { Interval = 180 };
            delay.Tick += (s, e) =>
            {
                delay.Stop();
                delay.Dispose();
                if (restore != IntPtr.Zero && Win.IsWindow(restore) && Win.IsShellMenu(Win.GetForegroundWindow()))
                    Win.SetForegroundWindow(restore);
                if (Win.IsShellMenu(Win.GetForegroundWindow()))
                {
                    Notify("先选择输入框，再按 RB");
                    return;
                }
                ToggleKeyboard();
            };
            delay.Start();
        }
        bool TargetReady()
        {
            return keyboard.Visible && !game && !paused && target != IntPtr.Zero &&
                   target == Win.GetForegroundWindow() && Win.IsWindow(target);
        }
        void ToggleKeyboard()
        {
            if (keyboard.Visible)
                CloseKeyboard();
            else
                OpenKeyboard(Rectangle.Empty, Config.StartLayout);
        }
        void OpenKeyboard(Rectangle bounds, string layout)
        {
            if (paused || game || closing)
                return;
            var foreground = Win.GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == keyboard.Handle)
                return;
            target = foreground;
            model.Open(layout);
            engine.SetContext(game, paused, true);
            keyboard.Legend =
                selected == null ? KeyboardWindow.DefaultLegend : KeyboardWindow.MakeLegend(Config.For(selected));
            keyboard.Present(target, bounds, Config);
        }
        void Command(string cmd)
        {
            if (!TargetReady())
            {
                CloseKeyboard();
                return;
            }
            model.Command(cmd);
            keyboard.Invalidate();
        }
        void CloseKeyboard()
        {
            if (keyboard.Visible)
                keyboard.Hide();
            target = IntPtr.Zero;
            model.ResetText();
            OnKeyboardClosed();
            engine.SetContext(game, paused, false);
        }
        void ShowSwitchPrompt()
        {
            if (!modeLocked)
                Notify("松开切换 · " + (game ? "桌面键鼠" : "原生手柄"));
        }
        void ToggleGame()
        {
            SetGame(!game, true);
        }
        void ToggleModeLock()
        {
            modeLocked = !modeLocked;
            Notify(modeLocked ? ModeName : ModeName + " · 已解锁");
        }
        void SetGame(bool value, bool manual = false)
        {
            if (modeLocked || game == value)
                return;
            game = value;
            CloseKeyboard();
            Notify(manual ? "已切换 · " + (game ? "原生手柄" : "桌面键鼠") : ModeName);
        }
        internal void Pause(bool value)
        {
            paused = value;
            CloseKeyboard();
            Notify(ModeName);
        }
        partial void Notify(string message);
        void CheckForeground(IntPtr hwnd)
        {
            if (hwnd == observed && !firstRule)
                return;
            observed = hwnd;
            if (!Win.IsShellMenu(hwnd))
                lastApp = hwnd;
            int pid;
            Win.GetWindowThreadProcessId(hwnd, out pid);
            string name = "";
            try
            {
                using (var p = Process.GetProcessById(pid)) name = p.ProcessName + ".exe";
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            firstRule = false;
            bool? transition = policy.Enter(name, Config.GameApps);
            if (transition.HasValue)
                SetGame(transition.Value);
        }
        void Tick()
        {
            if (closing)
                return;
            long now = clock.ElapsedMilliseconds;
            IntPtr fg = Win.GetForegroundWindow();
            if (keyboard.Visible && (fg != target || !Win.IsWindow(target)))
                CloseKeyboard();
            if (fg != IntPtr.Zero)
                CheckForeground(fg);
            bool scan = now >= nextScan;
            if (scan)
                nextScan = now + 850;
            foreach (var d in manager.Devices)
                if (scan || selected == d.Id ||
                    (SettingsVisible && States.ContainsKey(d.Id) && States[d.Id].Connected))
                    States[d.Id] = d.Read();
            var active = manager.Select(Config.Primary, States.Where(p => p.Value.Connected).Select(p => p.Key));
            string id = active == null ? null : active.Id;
            if (id != selected)
            {
                selected = id;
                engine.Reset();
                CloseKeyboard();
            }
            if (id != null && !SessionBlocked)
                engine.Tick(States[id], Config.For(id), now);
            else
                engine.Reset();
            CheckFocus(now);
            messages.SetPollInterval(id == null ? (keyboard.Visible ? 100 : 250) : 15);
        }
        internal void Apply(Settings next)
        {
            SettingsStore.Save(SettingsStore.Location, next);
            if (Config.Primary != next.Primary)
                manager.Primary = next.Primary;
            Config = next;
            policy.Reload();
            firstRule = true;
            nextScan = 0;
            CloseKeyboard();
        }
        bool SessionBlocked
        {
            get {
                return systemBlocked;
            }
        }
        bool systemBlocked = false;
        partial void InitializeSystem();
        partial void StopSystem();
        partial void BuildExtraMenu(ContextMenuStrip m);
        partial void ShowSettings();
        partial void CheckFocus(long now);
        partial void OnKeyboardClosed();
        protected override void ExitThreadCore()
        {
            if (closing)
                return;
            closing = true;
            CloseKeyboard();
            engine.Reset();
            StopSystem();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            keyboard.Dispose();
            base.ExitThreadCore();
        }
    }
}