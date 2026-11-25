using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
namespace Gamepad
{
    // Run explicitly on an unlocked interactive desktop. Inject only while our fixture is foreground.
    internal sealed class Fixture : Form
    {
        readonly TextBox edit =
            new TextBox { Location = new Point(30, 70), Size = new Size(360, 32), Name = "FixtureEdit" };
        readonly TextBox password = new TextBox { Location = new Point(30, 135), Width = 260,
                                                  UseSystemPasswordChar = true, Name = "FixturePassword" };
        readonly TextBox readOnly =
            new TextBox { Location = new Point(30, 188), Width = 260, ReadOnly = true, Name = "FixtureReadOnly" };
        readonly Button other =
            new Button { Text = "Focus target", Location = new Point(30, 15), Size = new Size(155, 38) };
        public Fixture()
        {
            Text = "Gamepad Desktop · 验收窗口";
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 245);
            Controls.AddRange(new Control[] { edit, password, readOnly, other });
            other.Click += (sender, args) => edit.Focus();
            Shown += (s, e) =>
            {
                Activate();
                other.Focus();
            };
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x8010)
            {
                Control c = m.WParam.ToInt32() == 0   ? (Control)edit
                            : m.WParam.ToInt32() == 1 ? password
                            : m.WParam.ToInt32() == 2 ? (Control)readOnly
                                                      : other;
                c.Focus();
                m.Result = c.Handle;
                return;
            }
            if (m.Msg == 0x8011)
            {
                m.Result = (IntPtr)(edit.Text == "q你好世界" ? 2 : edit.Text == "q" ? 1 : 0);
                return;
            }
            if (m.Msg == 0x8012)
            {
                FormBorderStyle = FormBorderStyle.None;
                Bounds = Screen.FromControl(this).Bounds;
                m.Result = (IntPtr)(Bounds == Screen.FromControl(this).Bounds ? 1 : 0);
                return;
            }
            base.WndProc(ref m);
        }
    }
    internal static partial class Tests
    {
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr w, int m, IntPtr p, IntPtr l);
        [DllImport("user32.dll")]
        static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")]
        static extern bool ClientToScreen(IntPtr w, ref Point point);
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr w);
        [DllImport("user32.dll")]
        static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")]
        static extern short GetAsyncKeyState(int vk);
        static void Pump(int ms)
        {
            var t = Stopwatch.StartNew();
            do
            {
                Application.DoEvents();
                Thread.Sleep(4);
            } while (t.ElapsedMilliseconds < ms);
        }
        static void Await(Func<bool> condition, string name, int timeout)
        {
            var t = Stopwatch.StartNew();
            while (!condition() && t.ElapsedMilliseconds < timeout)
                Pump(15);
            Check(condition(), name);
        }
        static IntPtr OverlayHandle()
        {
            return FindWindow(null, "手柄键盘");
        }
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr w);
        static bool Shown()
        {
            return IsWindowVisible(OverlayHandle());
        }
        static void Hit(SimDevice pad, string key)
        {
            pad.State = Pad(key);
            Pump(65);
            pad.State = Pad();
            Pump(95);
        }
        sealed class GuardedSink : IInputSink
        {
            readonly IntPtr fixture;
            readonly NativeInput native = new NativeInput();
            internal GuardedSink(IntPtr w)
            {
                fixture = w;
            }
            void Guard()
            {
                if (Win.GetForegroundWindow() != fixture)
                    throw new InvalidOperationException("Fixture lost foreground; input stopped.");
            }
            public bool Key(int k, bool down)
            {
                Guard();
                return native.Key(k, down);
            }
            public bool Mouse(int b, bool down)
            {
                Guard();
                return native.Mouse(b, down);
            }
            public void Text(string s)
            {
                Guard();
                native.Text(s);
            }
            public void Move(int x, int y)
            {
                Guard();
                native.Move(x, y);
            }
            public void Wheel(int x, int y)
            {
                Guard();
                native.Wheel(x, y);
            }
        }
        static void HoldModeChord(SimDevice pad, int duration)
        {
            pad.State = Pad("LB", "RB");
            Pump(duration);
            pad.State = Pad();
            Pump(110);
        }
        static void WindowsChecks()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            var original = Cursor.Position;
            var prior = Win.GetForegroundWindow();
            using (var fixture = Process.Start(
                       new ProcessStartInfo(Application.ExecutablePath, "--fixture") { UseShellExecute = false }))
            {
                Host host = null;
                try
                {
                    var w = IntPtr.Zero;
                    var start = Stopwatch.StartNew();
                    int pid = 0;
                    while (start.ElapsedMilliseconds < 6000 && !fixture.HasExited)
                    {
                        w = FindWindow(null, "Gamepad Desktop · 验收窗口");
                        Win.GetWindowThreadProcessId(w, out pid);
                        if (w != IntPtr.Zero && pid == fixture.Id)
                            break;
                        Pump(30);
                    }
                    if (w == IntPtr.Zero || pid != fixture.Id)
                        throw new Exception("Missing fixture");
                    SetForegroundWindow(w);
                    Pump(200);
                    if (Win.GetForegroundWindow() != w)
                    {
                        var point = new Point(40, 30);
                        ClientToScreen(w, ref point);
                        Cursor.Position = point;
                        Pump(90);
                        if (Win.GetAncestor(WindowFromPoint(Cursor.Position), 2) != w)
                            throw new Exception("Fixture is covered; activation cancelled");
                        var native = new NativeInput();
                        native.Mouse(1, true);
                        native.Mouse(1, false);
                        Pump(180);
                    }
                    Check(Win.GetForegroundWindow() == w, "fixture foreground");
                    var pad = new SimDevice { Id = "sim:0", PhysicalKey = "fixture", State = Pad() };
                    var cfg = new Settings();
                    cfg.For(pad.Id).Desktop["RT"] = "MouseLeft";
                    var provider = new SimProvider { Devices = new[] { pad } };
                    host = new Host(cfg, provider, new GuardedSink(w), false);
                    Pump(280);
                    Check(host.ActiveId == pad.Id, "production host discovers simulated primary");
                    SendMessage(w, 0x8010, (IntPtr)0, IntPtr.Zero);
                    Pump(350);
                    Check(!Shown(), "physical focus alone does not open overlay");
                    Await(() => FocusDetection.Inspect().Editable, "editable provider ready", 2500);
                    var info = FocusDetection.Inspect();
                    var focused = System.Windows.Automation.AutomationElement.FocusedElement;
                    Console.WriteLine(
                        "Fixture UIA id=" + info.Id + " type=" + focused.Current.ControlType.ProgrammaticName +
                        " focus=" + focused.Current.HasKeyboardFocus + " bounds=" + info.Bounds + " patterns=" +
                        String.Join(",", focused.GetSupportedPatterns().Select(p => p.ProgrammaticName)));
                    Check(info.Editable && !info.Password && !info.Bounds.IsEmpty,
                          "cross-process editable UIA + geometry");
                    var nonEdit = new Point(74, 30);
                    ClientToScreen(w, ref nonEdit);
                    Cursor.Position = nonEdit;
                    Pump(100);
                    Hit(pad, "B");
                    Pump(650);
                    Check(FocusDetection.Inspect().Editable && !Shown(),
                          "click outside the edit does not open the keyboard when it gains focus indirectly");
                    Cursor.Position = new Point(info.Bounds.Left + 10, info.Bounds.Top + 8);
                    Pump(120);
                    Hit(pad, "B");
                    Await(Shown, "pad mouse click opens keyboard via UIA", 3000);
                    Pump(80);
                    Check(Win.GetForegroundWindow() == w, "overlay preserves target focus");
                    Hit(pad, "B");
                    Check(SendMessage(w, 0x8011, IntPtr.Zero, IntPtr.Zero).ToInt32() == 1,
                          "SendInput reaches original edit");
                    Hit(pad, "RB");
                    // Starting at Q: N is index 5 in row 2. Use actual controller navigation.
                    Hit(pad, "Down");
                    Hit(pad, "Down");
                    for (int i = 0; i < 5; i++)
                        Hit(pad, "Right");
                    Hit(pad, "B");
                    // Normalized vertical selection maps N to J (column 7 from 7 to 9 -> 7); use a model mirror.
                    var mirror =
                        new KeyboardModel(() => new PinyinDictionary(Path.Combine(
                                              AppDomain.CurrentDomain.BaseDirectory, "data"))) { Insert = s =>
                                                                                                 {},
                                                                                                 Key = s =>
                                                                                                 {},
                                                                                                 Close = () =>
                                                                                                 {} };
                    mirror.Open("拼音");
                    mirror.Command("Down");
                    mirror.Command("Down");
                    for (int i = 0; i < 5; i++)
                        mirror.Command("Right");
                    mirror.Command("Select");
                    Check(mirror.Composition == "n", "navigation sequence begins N");
                    foreach (char ch in "ihaoshijie")
                    {
                        int row = mirror.Rows.FindIndex(a => a.Contains(ch.ToString()));
                        while (mirror.Row != row)
                        {
                            Hit(pad, "Down");
                            mirror.Command("Down");
                        }
                        int col = Array.IndexOf(mirror.Rows[row], ch.ToString());
                        while (mirror.Col != col)
                        {
                            Hit(pad, "Right");
                            mirror.Command("Right");
                        }
                        Hit(pad, "B");
                        mirror.Command("Select");
                    }
                    Hit(pad, "Y");
                    Check(SendMessage(w, 0x8011, IntPtr.Zero, IntPtr.Zero).ToInt32() == 2,
                          "continuous Chinese sentence reaches original edit in one commit");
                    Hit(pad, "A");
                    Await(() => !Shown(), "return closes visible keyboard", 1000);
                    Hit(pad, "A");
                    Pump(1000);
                    Check(!Shown(), "desktop Escape cannot reopen the keyboard at the same field");
                    Hit(pad, "B");
                    Await(Shown, "click can reopen the keyboard after Escape", 1800);
                    SendMessage(w, 0x8010, (IntPtr)3, IntPtr.Zero);
                    Await(() => !Shown(), "same-window focus loss closes keyboard", 2500);
                    SendMessage(w, 0x8010, (IntPtr)1, IntPtr.Zero);
                    Pump(80);
                    Check(FocusDetection.Inspect().Password && FocusDetection.Inspect().Editable,
                          "password detection through metadata");
                    SendMessage(w, 0x8010, (IntPtr)2, IntPtr.Zero);
                    Pump(80);
                    Check(!FocusDetection.Inspect().Editable, "read-only edit excluded");
                    Hit(pad, "RB");
                    Await(Shown, "manual fallback on unknown/read-only target", 1200);
                    pad.State = new ControllerState();
                    Await(() => !Shown(), "device loss hides keyboard", 1600);
                    pad.State = Pad();
                    Await(() => host.ActiveId == pad.Id, "reconnect primary", 2200);
                    Pump(80);
                    var held = Pad();
                    held.RT = 1;
                    pad.State = held;
                    Pump(80);
                    Check((GetAsyncKeyState(1) & 0x8000) != 0, "native mouse held by trigger");
                    pad.State = new ControllerState();
                    Pump(120);
                    Check((GetAsyncKeyState(1) & 0x8000) == 0, "native mouse released on disconnect");
                    host.ExitThread();
                    pad.State = Pad();
                    cfg.GameApps = new[] { fixture.ProcessName + ".exe" };
                    cfg.For(pad.Id).Desktop["RS"] = "Mode";
                    host = new Host(cfg, provider, new GuardedSink(w), false);
                    Await(() => host.ModeName == "原生手柄", "foreground rule enters native mode on Windows",
                          1500);
                    Hit(pad, "B");
                    Check((GetAsyncKeyState(1) & 0x8000) == 0, "game profile sends no native mouse press");
                    pad.State = Pad("LB", "RB");
                    Await(() => IsWindowVisible(FindWindow(null, "松开切换 · 桌面键鼠")),
                          "holding for two seconds displays the release prompt", 2850);
                    Check(host.ModeName == "原生手柄" && Win.GetForegroundWindow() == w,
                          "release prompt keeps the previous mode and foreground window");
                    pad.State = Pad();
                    Await(() => host.ModeName == "桌面键鼠" &&
                          IsWindowVisible(FindWindow(null, "已切换 · 桌面键鼠")),
                          "release switches mode and confirms it on screen", 1000);
                    Check(host.HotkeyAvailable, "emergency hotkey registered");
                    new InputLedger(new GuardedSink(w)).Tap(KeyBinding.Parse("Key:Ctrl+Alt+F12"));
                    Pump(160);
                    Check(host.ModeName == "已暂停", "global emergency hotkey pauses mapping");
                    host.Pause(false);
                    pad.State = Pad();
                    Pump(100);
                    pad.State = Pad("LB", "RB");
                    Pump(2150);
                    pad.State = Pad();
                    Pump(90);
                    Check(host.ModeName == "原生手柄", "long chord enters native mode");
                    Check(SendMessage(w, 0x8012, IntPtr.Zero, IntPtr.Zero).ToInt32() == 1 &&
                          Win.GetForegroundWindow() == w, "foreground fixture covers the full display");
                    HoldModeChord(pad, 2150);
                    Check(host.ModeName == "桌面键鼠", "full-screen foreground can switch to desktop");
                    HoldModeChord(pad, 3150);
                    Check(host.ModeName == "桌面键鼠 · 已锁定", "full-screen long hold locks current mode");
                    Hit(pad, "RS");
                    HoldModeChord(pad, 2150);
                    Check(host.ModeName == "桌面键鼠 · 已锁定", "lock rejects short chord and custom mode binding");
                    HoldModeChord(pad, 3150);
                    Check(host.ModeName == "桌面键鼠", "long hold unlocks without changing mode");
                    HoldModeChord(pad, 2150);
                    Check(host.ModeName == "原生手柄", "unlocked full-screen foreground switches back to native");
                    fixture.CloseMainWindow();
                    fixture.WaitForExit(1800);
                    Pump(300);
                    Await(() => host.ModeName == "桌面键鼠", "closing profiled process restores desktop", 2000);
                }
                finally
                {
                    if (host != null)
                        host.ExitThread();
                    if (!fixture.HasExited)
                    {
                        fixture.CloseMainWindow();
                        if (!fixture.WaitForExit(1500))
                            fixture.Kill();
                    }
                    Cursor.Position = original;
                    if (prior != IntPtr.Zero)
                        SetForegroundWindow(prior);
                }
            }
        }
    }
}
