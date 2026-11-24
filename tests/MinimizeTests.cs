using System;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Gamepad
{
    internal static partial class Tests
    {
        [StructLayout(LayoutKind.Sequential)]
        struct WindowRect
        {
            public int Left, Top, Right, Bottom;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct TitleBarInfo
        {
            public int Size;
            public WindowRect Bounds;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public uint[] State;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public WindowRect[] Parts;
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        static extern IntPtr SendTitleBarMessage(IntPtr window, int message, IntPtr param, ref TitleBarInfo info);
        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hwnd);

        static Point MinimizeButton(IntPtr hwnd)
        {
            var title = new TitleBarInfo { Size = Marshal.SizeOf(typeof(TitleBarInfo)), State = new uint[6],
                                           Parts = new WindowRect[6] };
            SendTitleBarMessage(hwnd, 0x033f, IntPtr.Zero, ref title);
            var button = title.Parts[2];
            if (button.Right <= button.Left || button.Bottom <= button.Top)
                throw new InvalidOperationException("Cannot locate title-bar minimize button");
            return new Point((button.Left + button.Right) / 2, (button.Top + button.Bottom) / 2);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct QueueMessage
        {
            public IntPtr Window;
            public uint Message;
            public UIntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public Point Position;
            public uint Reserved;
        }

        [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
        static extern bool PeekMouseMessage(out QueueMessage message, IntPtr window, uint first, uint last,
                                            uint remove);

        static void TitleLoopChecks()
        {
            Application.EnableVisualStyles();
            var pad = new SimDevice { Id = "title-loop", PhysicalKey = "title-loop", State = Pad() };
            var provider = new SimProvider { Devices = new[] { pad } };
            var output = new Capture();
            using (var host = new Host(new Settings { AutoKeyboard = false, GameApps = new string[0] }, provider,
                                       output, false))
            {
                try
                {
                    Await(() => host.ActiveId == pad.Id, "filtered-loop controller ready", 1200);
                    Pump(130);
                    Volatile.Write(ref pad.State, Pad("B"));
                    Await(() => output.Events.Contains("M1True"), "mouse-down produced before the modal loop",
                          1200);
                    var release = new Thread(() =>
                                             {
                                                 Thread.Sleep(70);
                                                 Volatile.Write(ref pad.State, Pad());
                                             }) { IsBackground = true };
                    release.Start();
                    var duration = Stopwatch.StartNew();
                    while (!output.Events.Contains("M1False") && duration.ElapsedMilliseconds < 850)
                    {
                        // This is the same kind of queue filter used while native caption buttons track the
                        // pointer. Timer messages are intentionally not dispatched here.
                        QueueMessage ignored;
                        PeekMouseMessage(out ignored, IntPtr.Zero, 0x0200, 0x020e, 0);
                        Thread.Sleep(6);
                    }
                    release.Join();
                    Check(output.Events.Contains("M1False"),
                          "mouse-up arrives during a mouse-only native message loop");
                }
                finally
                {
                    host.ExitThread();
                }
            }
        }
        static void MinimizeChecks()
        {
            Application.EnableVisualStyles();
            var previousCursor = Cursor.Position;
            var previousForeground = Win.GetForegroundWindow();
            var input = new NativeInput();
            var pad = new SimDevice { Id = "minimize", PhysicalKey = "minimize", State = Pad() };
            var provider = new SimProvider { Devices = new[] { pad } };
            var cfg = new Settings { AutoKeyboard = false, GameApps = new string[0] };
            var host = new Host(cfg, provider, input, true);
            var released = new ManualResetEventSlim();
            try
            {
                Pump(220);
                SettingsWindow window = null;
                foreach (Form form in Application.OpenForms)
                {
                    window = form as SettingsWindow;
                    if (window != null)
                        break;
                }
                Check(window != null, "settings window available");
                var hwnd = window.Handle;
                window.WindowState = FormWindowState.Normal;
                var area = Screen.FromPoint(previousCursor).WorkingArea;
                window.Location = new Point(area.Left + Math.Max(20, (area.Width - window.Width) / 2),
                                            area.Top + Math.Max(20, (area.Height - window.Height) / 2));
                window.TopMost = true;
                var activation = Stopwatch.StartNew();
                do
                {
                    window.BringToFront();
                    window.Activate();
                    SetForegroundWindow(hwnd);
                    Pump(100);
                    if (Win.GetForegroundWindow() == hwnd)
                        break;
                    var point = window.PointToScreen(new Point(400, 101));
                    Cursor.Position = point;
                    if (Win.GetAncestor(WindowFromPoint(Cursor.Position), 2) == hwnd)
                    {
                        input.Mouse(1, true);
                        input.Mouse(1, false);
                    }
                    Pump(130);
                } while (Win.GetForegroundWindow() != hwnd && activation.ElapsedMilliseconds < 2000);
                var pos = MinimizeButton(hwnd);
                Cursor.Position = pos;
                Pump(160);
                Check(Win.GetForegroundWindow() == hwnd &&
                          Win.GetAncestor(WindowFromPoint(Cursor.Position), 2) == hwnd && Cursor.Position == pos,
                      "minimize hit target");
                bool neededRecovery = false;
                var release = new Thread(() =>
                                         {
                                             Thread.Sleep(200);
                                             Volatile.Write(ref pad.State, Pad());
                                             if (!released.Wait(1400) && (GetAsyncKeyState(1) & 0x8000) != 0)
                                             {
                                                 neededRecovery = true;
                                                 input.Mouse(1, false);
                                             }
                                         }) { IsBackground = true };
                Volatile.Write(ref pad.State, Pad("B"));
                release.Start();
                Pump(480);
                released.Set();
                release.Join();
                Check(!neededRecovery, "controller releases native title button without watchdog intervention");
                Check(IsIconic(hwnd), "B click minimizes settings");
                Pump(150);
                var start = Cursor.Position;
                var moving = Pad();
                moving.LX = -.85;
                Volatile.Write(ref pad.State, moving);
                Pump(320);
                Volatile.Write(ref pad.State, Pad());
                Check(Cursor.Position.X < start.X - 35, "desktop pointer still works with settings minimized");
            }
            finally
            {
                released.Set();
                host.ExitThread();
                released.Dispose();
                Cursor.Position = previousCursor;
                if (previousForeground != IntPtr.Zero)
                    Win.SetForegroundWindow(previousForeground);
            }
        }
    }
}