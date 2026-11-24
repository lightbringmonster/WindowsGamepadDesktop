using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;
using System.Collections.Generic;
namespace Gamepad
{
    internal static partial class Tests
    {
        delegate bool WindowVisitor(IntPtr window, IntPtr state);
        [DllImport("user32.dll")]
        static extern bool EnumWindows(WindowVisitor visitor, IntPtr state);
        static HashSet<IntPtr> ShellWindows()
        {
            var result = new HashSet<IntPtr>();
            EnumWindows(
                (w, p) =>
                {
                    var name = new System.Text.StringBuilder(64);
                    ReadClass(w, name, name.Capacity);
                    if (name.ToString() == "CabinetWClass")
                        result.Add(w);
                    return true;
                },
                IntPtr.Zero);
            return result;
        }
        [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
        static extern int ReadClass(IntPtr w, System.Text.StringBuilder name, int size);
        static string ProcessOf(IntPtr hwnd)
        {
            int id;
            Win.GetWindowThreadProcessId(hwnd, out id);
            try
            {
                using (var p = Process.GetProcessById(id)) return p.ProcessName;
            }
            catch (ArgumentException)
            {
                return "";
            }
        }
        static void FocusOwnedWindow(IntPtr w)
        {
            var root = AutomationElement.FromHandle(w);
            object pattern;
            if (root.TryGetCurrentPattern(WindowPattern.Pattern, out pattern))
                ((WindowPattern)pattern).SetWindowVisualState(WindowVisualState.Normal);
            try
            {
                root.SetFocus();
            }
            catch (InvalidOperationException)
            {
            }
            SetForegroundWindow(w);
            Pump(220);
        }
        static IntPtr FindExplorerFolder(string folder)
        {
            Type t = Type.GetTypeFromProgID("Shell.Application");
            dynamic app = Activator.CreateInstance(t);
            try
            {
                foreach (dynamic win in app.Windows())
                {
                    try
                    {
                        string url = win.LocationURL;
                        if (!String.IsNullOrEmpty(url) && new Uri(url).IsFile &&
                            String.Equals(new Uri(url).LocalPath.TrimEnd((char)92), folder,
                                          StringComparison.OrdinalIgnoreCase))
                            return (IntPtr)(int)win.HWND;
                    }
                    catch (COMException)
                    {
                    }
                    finally
                    {
                        if (Marshal.IsComObject(win))
                            Marshal.ReleaseComObject(win);
                    }
                }
                return IntPtr.Zero;
            }
            finally
            {
                Marshal.ReleaseComObject(app);
            }
        }
        static void ExplorerChecks(string scratch)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            var before = Win.GetForegroundWindow();
            var cursor = Cursor.Position;
            var existing = ShellWindows();
            string dir = Path.Combine(Path.GetFullPath(scratch),
                                      "GamepadQA-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            IntPtr window = IntPtr.Zero;
            Host host = null;
            try
            {
                using (Process.Start("explorer.exe", "/n,\"" + dir + "\"")) {}
                Await(
                    () =>
                    {
                        window = FindExplorerFolder(dir);
                        return window != IntPtr.Zero;
                    },
                    "dedicated File Explorer window", 9000);
                FocusOwnedWindow(window);
                Check(Win.GetForegroundWindow() == window, "File Explorer foreground");
                var sink = new GuardedSink(window);
                var keys = new InputLedger(sink);
                keys.Tap(KeyBinding.Parse("Key:Ctrl+L"));
                Pump(220);
                Await(() => FocusDetection.Inspect().Editable, "Explorer address bar editable metadata", 2100);
                var pad = new SimDevice { Id = "sim", PhysicalKey = "sim", State = Pad() };
                host = new Host(new Settings(), new SimProvider { Devices = new[] { pad } }, sink, false);
                Pump(300);
                var box = FocusDetection.Inspect().Bounds;
                Cursor.Position = new Point(box.X + 16, box.Y + box.Height / 2);
                Pump(90);
                Hit(pad, "B");
                Await(Shown, "Explorer address bar auto-opens keyboard", 2300);
                Hit(pad, "A");
                Await(() => !Shown(), "Explorer dismiss without changing address", 1200);
            }
            finally
            {
                if (host != null)
                    host.ExitThread();
                if (window != IntPtr.Zero && !existing.Contains(window) && FindExplorerFolder(dir) == window)
                    PostClose(window, 0x10, IntPtr.Zero, IntPtr.Zero);
                Cursor.Position = cursor;
                if (before != IntPtr.Zero)
                    SetForegroundWindow(before);
            }
        }
        static void SearchChecks()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            var before = Win.GetForegroundWindow();
            var cursor = Cursor.Position;
            IntPtr shell = IntPtr.Zero;
            using (var anchor =
                       new Form { Text = "手柄桌面 · 搜索检测", TopMost = true,
                                  StartPosition = FormStartPosition.CenterScreen, Size = new Size(420, 200) })
            {
                Host host = null;
                try
                {
                    anchor.Show();
                    FocusOwnedWindow(anchor.Handle);
                    if (Win.GetForegroundWindow() != anchor.Handle)
                    {
                        var p = new Point(50, 70);
                        ClientToScreen(anchor.Handle, ref p);
                        Cursor.Position = p;
                        Pump(80);
                        if (Win.GetAncestor(WindowFromPoint(p), 2) != anchor.Handle)
                            throw new Exception("Search test window covered");
                        var input = new NativeInput();
                        input.Mouse(1, true);
                        input.Mouse(1, false);
                        Pump(150);
                    }
                    new InputLedger(new GuardedSink(anchor.Handle)).Tap(KeyBinding.Parse("Key:Win+S"));
                    anchor.TopMost = false;
                    Await(
                        () =>
                        {
                            shell = Win.GetForegroundWindow();
                            var n = ProcessOf(shell);
                            return n == "SearchHost" || n == "SearchApp";
                        },
                        "Windows Search foreground", 4500);
                    Await(() => FocusDetection.Inspect().Editable, "Windows Search editable metadata", 2600);
                    var pad = new SimDevice { Id = "sim", PhysicalKey = "sim", State = Pad() };
                    host = new Host(new Settings(), new SimProvider { Devices = new[] { pad } },
                                    new GuardedSink(shell), false);
                    Pump(300);
                    var edit = FocusDetection.Inspect().Bounds;
                    Cursor.Position = new Point(edit.X + 20, edit.Y + edit.Height / 2);
                    Pump(75);
                    Hit(pad, "B");
                    Await(Shown, "Windows Search auto-opens keyboard", 2400);
                    Hit(pad, "A");
                }
                finally
                {
                    if (host != null)
                        host.ExitThread();
                    if (shell != IntPtr.Zero && Win.GetForegroundWindow() == shell &&
                        ProcessOf(shell).StartsWith("Search", StringComparison.OrdinalIgnoreCase))
                        new InputLedger(new GuardedSink(shell)).Tap(KeyBinding.Parse("Key:Escape"));
                    Cursor.Position = cursor;
                    if (before != IntPtr.Zero)
                        SetForegroundWindow(before);
                }
            }
        }
    }
}