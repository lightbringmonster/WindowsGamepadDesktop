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
        readonly PointerWorker pointer;
        readonly IInputSink sink;
        readonly string settingsPath;
        internal Settings Config;
        internal readonly Dictionary<string, ControllerState> States = new Dictionary<string, ControllerState>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly KeyboardModel model;
        readonly KeyboardWindow keyboard;
        readonly RadialMenuWindow radialMenu = new RadialMenuWindow();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        IControllerDevice activeDevice;
        string selected;
        long nextScan;
        bool paused, game, modeLocked, closing;
        IntPtr target, radialTarget, observed, lastApp;
        readonly ApplicationProfiles policy = new ApplicationProfiles();
        bool firstRule = true;
        public Host(Settings cfg, IControllerProvider provider, IInputSink output, bool openSettings,
                    string settingsPath = null)
        {
            Config = cfg;
            this.settingsPath = settingsPath ?? SettingsStore.Location;
            game = cfg.StartsNative;
            sink = output;
            manager = new DeviceManager(provider);
            engine = new MappingEngine(sink, false);
            engine.SetContext(game, false, false);
            engine.ToggleMode = ToggleGame;
            engine.ModeSwitchReady = ShowSwitchPrompt;
            engine.PrecisionChanged = ShowPrecision;
            engine.RadialPreview = ShowRadial;
            engine.RadialEnded = EndRadial;
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
            pointer = new PointerWorker(sink, Win.GetForegroundWindow);
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
            pointer.Suspend();
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
            pointer.Suspend();
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
        void ShowPrecision(PointerGear gear)
        {
            Notify("指针精细度 · " +
                   (gear == PointerGear.Standard ? "标准" : gear == PointerGear.Fine ? "精细" : "极精（松开恢复）"));
        }
        void ShowRadial(int selection)
        {
            var window = Win.GetForegroundWindow();
            if (window == IntPtr.Zero || window != observed)
                return;
            if (!radialMenu.Visible)
            {
                radialTarget = window;
                radialMenu.Present(window, selection);
            }
            else
                radialMenu.SelectAction(selection);
        }
        void EndRadial(int selectedAction)
        {
            var window = radialTarget;
            radialTarget = IntPtr.Zero;
            if (radialMenu.Visible)
                radialMenu.Hide();
            if (selectedAction >= 0 && window != IntPtr.Zero && window == Win.GetForegroundWindow() &&
                Win.IsWindow(window))
                engine.Ledger.Tap(RadialAction.Items[selectedAction].Keys);
        }
        void ToggleGame()
        {
            SetGame(!game);
        }
        void ToggleModeLock()
        {
            modeLocked = !modeLocked;
            Notify(modeLocked ? ModeName : ModeName + " · 已解锁");
        }
        void SetGame(bool value, bool applicationRule = false)
        {
            if (modeLocked)
                return;
            if (game == value)
            {
                if (applicationRule && game && !paused)
                    Notify("原生手柄 · 应用规则");
                return;
            }
            game = value;
            CloseKeyboard();
            Notify(applicationRule && paused ? ModeName : "已切换 · " + (game ? "原生手柄" : "桌面键鼠"));
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
                SetGame(transition.Value, applicationRule: true);
        }
        void Tick()
        {
            if (closing)
                return;
            long now = clock.ElapsedMilliseconds;
            IntPtr fg = Win.GetForegroundWindow();
            if (fg != observed && engine.RadialEngaged)
                engine.CancelRadial();
            if (keyboard.Visible && (fg != target || !Win.IsWindow(target)))
                CloseKeyboard();
            if (fg != IntPtr.Zero)
                CheckForeground(fg);
            bool scan = now >= nextScan;
            if (scan)
                nextScan = now + 850;
            foreach (var d in manager.Devices)
                if (selected == d.Id)
                    States[d.Id] = pointer.Latest();
                else if (scan ||
                    (SettingsVisible && States.ContainsKey(d.Id) && States[d.Id].Connected))
                    States[d.Id] = d.Read();
            // Keep using the selected controller until discovery or a fresh sample requires reselection.
            var active = selected != null && manager.Primary == selected && States[selected].Connected &&
                         (String.IsNullOrEmpty(Config.Primary) || Config.Primary == selected)
                             ? activeDevice
                             : manager.Select(Config.Primary, States.Where(p => p.Value.Connected).Select(p => p.Key));
            string id = active == null ? null : active.Id;
            if (id != selected)
            {
                pointer.Bind(active, id == null ? null : States[id]);
                selected = id;
                activeDevice = active;
                engine.Reset();
                CloseKeyboard();
            }
            if (id != null && !SessionBlocked)
            {
                var profile = Config.For(id);
                engine.Tick(States[id], profile, now);
                if (engine.CanMove)
                    pointer.Allow(profile, fg, engine.Gear);
                else
                    pointer.Suspend();
            }
            else
                engine.Reset();
            CheckFocus(now);
            messages.SetPollInterval(id == null ? (keyboard.Visible ? 100 : 250) : 15);
        }
        internal void Apply(Settings next)
        {
            if (next.StartupMode == "LastUsed")
                next.LastNativeMode = game;
            SettingsStore.Save(settingsPath, next);
            if (Config.Primary != next.Primary)
                manager.Primary = next.Primary;
            if (!Config.GameApps.SequenceEqual(next.GameApps, StringComparer.OrdinalIgnoreCase))
            {
                policy.Reload();
                firstRule = true;
            }
            Config = next;
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
        void RememberMode()
        {
            if (Config.StartupMode != "LastUsed" || Config.LastNativeMode == game)
                return;
            Config.LastNativeMode = game;
            try
            {
                SettingsStore.Save(settingsPath, Config);
            }
            catch (IOException)
            {
                Trace.TraceWarning("The last controller mode could not be saved.");
            }
            catch (UnauthorizedAccessException)
            {
                Trace.TraceWarning("The last controller mode could not be saved.");
            }
        }

        protected override void ExitThreadCore()
        {
            if (closing)
                return;
            closing = true;
            CloseKeyboard();
            engine.Reset();
            pointer.Dispose();
            RememberMode();
            StopSystem();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            keyboard.Dispose();
            radialMenu.Dispose();
            base.ExitThreadCore();
        }
    }
}
