using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
namespace Gamepad
{
    internal sealed class MessageSink : NativeWindow, IDisposable
    {
        const int PollMessage = 0x800B;
        readonly Action emergency, physical, showSettings, exit, poll;
        readonly System.Threading.Timer pollTimer;
        readonly object pollLifetime = new object();
        int pollPending, pollInterval = 15;
        bool pollClosed;
        readonly Action<bool> locked;
        internal readonly bool HasHotkey, HasRaw;
        [StructLayout(LayoutKind.Sequential)]
        struct RawDevice
        {
            public ushort Page, Usage;
            public uint Flags;
            public IntPtr Window;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct RawHeader
        {
            public uint Type, Size;
            public IntPtr Device, Param;
        }
        [DllImport("user32.dll")]
        static extern bool RegisterRawInputDevices(RawDevice[] devices, uint count, uint size);
        [DllImport("user32.dll")]
        static extern uint GetRawInputData(IntPtr raw, uint command, out RawHeader header, ref uint size,
                                           uint headerSize);
        [DllImport("wtsapi32.dll")]
        static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);
        [DllImport("wtsapi32.dll")]
        static extern bool WTSUnRegisterSessionNotification(IntPtr window);
        [DllImport("user32.dll", EntryPoint = "SendNotifyMessageW")]
        static extern bool SendNotifyMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        public MessageSink(Action stop, Action activity, Action<bool> session, Action settings, Action quit,
                           Action pollInput)
        {
            emergency = stop;
            physical = activity;
            locked = session;
            showSettings = settings;
            exit = quit;
            poll = pollInput;
            CreateHandle(new CreateParams { Caption = "WindowsGamepadDesktop.System", Parent = (IntPtr)(-3) });
            HasHotkey = Win.RegisterHotKey(Handle, 71, 0x4003, 0x7B); // MOD_NOREPEAT | CTRL | ALT
            HasRaw = Register(0x100, Handle);
            WTSRegisterSessionNotification(Handle, 0);
            var window = Handle;
            pollTimer = new System.Threading.Timer(_ => RequestPoll(window), null, pollInterval, pollInterval);
        }
        // The native title-bar tracking loop can filter WM_TIMER while waiting for mouse-up.
        // A cross-thread sent message still reaches the UI thread during that loop.
        void RequestPoll(IntPtr window)
        {
            lock (pollLifetime)
            {
                if (pollClosed || System.Threading.Interlocked.CompareExchange(ref pollPending, 1, 0) != 0)
                    return;
                if (!SendNotifyMessage(window, PollMessage, IntPtr.Zero, IntPtr.Zero))
                    System.Threading.Interlocked.Exchange(ref pollPending, 0);
            }
        }
        public void SetPollInterval(int interval)
        {
            if (pollClosed || interval == pollInterval)
                return;
            pollInterval = interval;
            pollTimer.Change(interval, interval);
        }
        static bool Register(uint flags, IntPtr target)
        {
            var dev = new[] { new RawDevice { Page = 1, Usage = 2, Flags = flags, Window = target },
                              new RawDevice { Page = 1, Usage = 6, Flags = flags, Window = target } };
            return RegisterRawInputDevices(dev, 2, (uint)Marshal.SizeOf(typeof(RawDevice)));
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == PollMessage)
            {
                try
                {
                    if (!pollClosed)
                        poll();
                }
                finally
                {
                    System.Threading.Interlocked.Exchange(ref pollPending, 0);
                }
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == 0x8009)
                showSettings();
            if (m.Msg == 0x800a)
                exit();
            if (m.Msg == 0x312 && m.WParam.ToInt32() == 71)
                emergency();
            if (m.Msg == 0x2b1)
            {
                int why = m.WParam.ToInt32();
                if (why == 7 || why == 4 || why == 2)
                    locked(true);
                else if (why == 8 || why == 1 || why == 3)
                    locked(false);
            }
            if (m.Msg == 0xff)
            {
                uint bytes = (uint)Marshal.SizeOf(typeof(RawHeader));
                RawHeader h;
                if (GetRawInputData(m.LParam, 0x10000005, out h, ref bytes, bytes) != UInt32.MaxValue &&
                    h.Device != IntPtr.Zero)
                    physical();
            }
            base.WndProc(ref m);
        }
        public void Dispose()
        {
            lock (pollLifetime)
            {
                pollClosed = true;
                pollTimer.Dispose();
            }
            if (HasRaw)
                Register(1, IntPtr.Zero);
            if (HasHotkey)
                Win.UnregisterHotKey(Handle, 71);
            WTSUnRegisterSessionNotification(Handle);
            DestroyHandle();
        }
    }
    internal static class Startup
    {
        const string Path = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "WindowsGamepadDesktop";
        static string Command
        {
            get {
                return "\"" + Application.ExecutablePath + "\" --tray";
            }
        }
        public static bool Enabled
        {
            get {
                using (var k = Registry.CurrentUser.OpenSubKey(Path)) return k != null
                    && String.Equals(k.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase);
            }
        }
        public static void Set(bool value)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Path))
            {
                if (value)
                    k.SetValue(Name, Command);
                else
                    k.DeleteValue(Name, false);
            }
        }
    }
    internal sealed class ModeToast : Form
    {
        readonly Timer timeout = new Timer { Interval = 1250 };
        string label = "";
        public ModeToast()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(23, 40, 44);
            ForeColor = Color.White;
            DoubleBuffered = true;
            timeout.Tick += (s, e) =>
            {
                timeout.Stop();
                Hide();
            };
        }
        protected override bool ShowWithoutActivation
        {
            get {
                return true;
            }
        }
        protected override CreateParams CreateParams
        {
            get {
                var p = base.CreateParams;
                p.ExStyle |= 0x080000a0;
                return p;
            }
        }
        public void Present(string text)
        {
            label = text;
            Text = text;
            var r = Screen.FromHandle(Win.GetForegroundWindow()).WorkingArea;
            int w = Math.Max(220, r.Width / 5), h = Math.Max(50, r.Height / 14);
            Bounds = new Rectangle(r.Left + (r.Width - w) / 2, r.Top + h / 3, w, h);
            Show();
            Invalidate();
            timeout.Stop();
            timeout.Start();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (var f = new Font("Microsoft YaHei UI", Height * .33f, FontStyle.Bold, GraphicsUnit.Pixel))
                TextRenderer.DrawText(e.Graphics, label, f, ClientRectangle, Color.FromArgb(158, 242, 213),
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                timeout.Dispose();
            base.Dispose(disposing);
        }
    }
    internal sealed partial class Host
    {
        MessageSink messages;
        ModeToast toast;
        SettingsWindow settings;
        internal bool HotkeyAvailable
        {
            get {
                return messages.HasHotkey;
            }
        }
        internal bool SettingsVisible
        {
            get {
                return settings != null && settings.Visible;
            }
        }
        internal bool RawAvailable
        {
            get {
                return messages.HasRaw;
            }
        }
        partial void InitializeSystem()
        {
            toast = new ModeToast();
            messages = new MessageSink(() => Pause(true), CancelFocusRequest,
                                       locked =>
                                       {
                                           systemBlocked = locked;
                                           CloseKeyboard();
                                       },
                                       () => ShowSettings(), () => ExitThread(), Tick);
        }
        partial void StopSystem()
        {
            if (focus != null)
                focus.Dispose();
            if (settings != null)
                settings.Close();
            if (messages != null)
                messages.Dispose();
            if (toast != null)
                toast.Dispose();
        }
        partial void Notify(string message)
        {
            if (toast != null && !systemBlocked)
                toast.Present(message);
        }
        partial void BuildExtraMenu(ContextMenuStrip m)
        {
            var item = new ToolStripMenuItem("登录时自启动") { Checked = Startup.Enabled };
            item.Click += (s, e) =>
            {
                try
                {
                    Startup.Set(!Startup.Enabled);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "自启动");
                }
            };
            m.Items.Add(item);
        }
        partial void ShowSettings()
        {
            if (settings == null || settings.IsDisposed)
            {
                settings = new SettingsWindow(this);
                settings.FormClosed += (s, e) => settings = null;
            }
            settings.Show();
            if (settings.WindowState == FormWindowState.Minimized)
                Win.ShowWindow(settings.Handle, 9); // SW_RESTORE
            settings.Activate();
        }
    }
}