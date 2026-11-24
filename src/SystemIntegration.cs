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
        readonly Action emergency, physical, showSettings, exit;
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
        public MessageSink(Action stop, Action activity, Action<bool> session, Action settings, Action quit)
        {
            emergency = stop;
            physical = activity;
            locked = session;
            showSettings = settings;
            exit = quit;
            CreateHandle(new CreateParams { Caption = "WindowsGamepadDesktop.System", Parent = (IntPtr)(-3) });
            HasHotkey = Win.RegisterHotKey(Handle, 71, 0x4003, 0x7B); // MOD_NOREPEAT | CTRL | ALT
            HasRaw = Register(0x100, Handle);
            WTSRegisterSessionNotification(Handle, 0);
        }
        static bool Register(uint flags, IntPtr target)
        {
            var dev = new[] { new RawDevice { Page = 1, Usage = 2, Flags = flags, Window = target },
                              new RawDevice { Page = 1, Usage = 6, Flags = flags, Window = target } };
            return RegisterRawInputDevices(dev, 2, (uint)Marshal.SizeOf(typeof(RawDevice)));
        }
        protected override void WndProc(ref Message m)
        {
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
            messages = new MessageSink(() => Pause(true), PhysicalActivity,
                                       locked =>
                                       {
                                           systemBlocked = locked;
                                           CloseKeyboard();
                                       },
                                       () => ShowSettings(), () => ExitThread());
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
            settings.Activate();
        }
    }
}