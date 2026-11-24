using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using DllImportAlias = System.Runtime.InteropServices.DllImportAttribute;
using System.Windows.Forms;
namespace Gamepad
{
    internal static partial class Tests
    {
        [DllImportAlias("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetWindowText(IntPtr h, System.Text.StringBuilder buffer, int max);
        static string Title(IntPtr hwnd)
        {
            var b = new System.Text.StringBuilder(512);
            GetWindowText(hwnd, b, b.Capacity);
            return b.ToString();
        }
        [DllImportAlias("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr order, int x, int y, int width, int height,
                                        uint flags);
        static void BrowserChecks(string profile)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            string marker = "GamepadQA" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string root = Path.GetFullPath(profile);
            Directory.CreateDirectory(root);
            string file = Path.Combine(root, "field.html");
            File.WriteAllText(
                file, "<!doctype html><meta charset=utf-8><title>" + marker +
                          "</title><input autofocus aria-label=\"" + marker +
                          "\" style='font-size:32px;margin:80px' oninput=\"if(this.value==='q')document.title='" +
                          marker + " OK'\">");
            string browser = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                                          @"Microsoft\Edge\Application\msedge.exe");
            var before = Win.GetForegroundWindow();
            var cursor = Cursor.Position;
            var options =
                "--user-data-dir=\"" + Path.Combine(root, "profile") +
                ("\" --no-first-run --no-default-browser-check --disable-features=msEdgeFirstRunExperience " +
                 "--disable-background-networking --force-renderer-accessibility --new-window \"") +
                new Uri(file).AbsoluteUri + "\"";
            using (var b = Process.Start(new ProcessStartInfo(browser, options) { UseShellExecute = false }))
            {
                IntPtr w = IntPtr.Zero;
                Host host = null;
                try
                {
                    Await(
                        () =>
                        {
                            foreach (var p in Process.GetProcessesByName("msedge"))
                                using (p)
                                {
                                    if (p.MainWindowTitle.Contains(marker))
                                    {
                                        w = p.MainWindowHandle;
                                        return true;
                                    }
                                }
                            return false;
                        },
                        "isolated Edge test page", 18000);
                    var element = System.Windows.Automation.AutomationElement.FromHandle(w);
                    object pattern;
                    if (element.TryGetCurrentPattern(System.Windows.Automation.WindowPattern.Pattern, out pattern))
                        ((System.Windows.Automation.WindowPattern)pattern)
                            .SetWindowVisualState(System.Windows.Automation.WindowVisualState.Normal);
                    try
                    {
                        element.SetFocus();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    Win.ShowWindow(w, 9);
                    var area = System.Linq.Enumerable.Last(System.Windows.Forms.Screen.AllScreens).WorkingArea;
                    SetWindowPos(w, (IntPtr)(-1), area.Left + 30, area.Top + 30, 990, 740, 0x40);
                    SetForegroundWindow(w);
                    Pump(850);
                    if (Win.GetForegroundWindow() != w)
                    {
                        Point point = Point.Empty;
                        Await(
                            () =>
                            {
                                for (int y = 95; y < 650; y += 80)
                                    for (int x = 90; x < 900; x += 110)
                                    {
                                        var candidate = new Point(x, y);
                                        ClientToScreen(w, ref candidate);
                                        if (Win.GetAncestor(WindowFromPoint(candidate), 2) == w)
                                        {
                                            point = candidate;
                                            return true;
                                        }
                                    }
                                return false;
                            },
                            "test browser has a visible click target", 3500);
                        Cursor.Position = point;
                        Pump(70);
                        if (Win.GetAncestor(WindowFromPoint(Cursor.Position), 2) != w)
                            throw new Exception("Test browser covered");
                        var input = new NativeInput();
                        input.Mouse(1, true);
                        input.Mouse(1, false);
                        Pump(150);
                    }
                    Check(Win.GetForegroundWindow() == w, "isolated Edge foreground");
                    var native = new GuardedSink(w);
                    var keys = new InputLedger(native);
                    keys.Tap(KeyBinding.Parse("Key:Ctrl+L"));
                    Pump(240);
                    Await(() => FocusDetection.Inspect().Editable, "Edge address bar reports editable", 2500);
                    var pad = new SimDevice { Id = "fixture", PhysicalKey = "fixture", State = Pad() };
                    host = new Host(new Settings(), new SimProvider { Devices = new[] { pad } }, native, false);
                    Pump(280);
                    var bounds = FocusDetection.Inspect().Bounds;
                    Cursor.Position = new Point(bounds.Left + 12, bounds.Top + bounds.Height / 2);
                    Pump(80);
                    Hit(pad, "B");
                    Await(Shown, "Edge address bar opens keyboard", 2200);
                    Hit(pad, "A");
                    Await(() => !Shown(), "A dismisses Edge keyboard", 1000);
                    // Reload only the generated local document and let its autofocus select the HTML input.
                    keys.Tap(KeyBinding.Parse("Key:Escape"));
                    keys.Tap(KeyBinding.Parse("Key:Ctrl+R"));
                    Pump(700);
                    var document = System.Windows.Automation.AutomationElement.FromHandle(w);
                    var edits =
                        document.FindAll(System.Windows.Automation.TreeScope.Descendants,
                                         new System.Windows.Automation.PropertyCondition(
                                             System.Windows.Automation.AutomationElement.ControlTypeProperty,
                                             System.Windows.Automation.ControlType.Edit));
                    System.Windows.Automation.AutomationElement web = null;
                    foreach (System.Windows.Automation.AutomationElement e in edits)
                    {
                        var r = e.Current.BoundingRectangle;
                        if (!r.IsEmpty && r.Height >= 35 && e.Current.Name == marker)
                        {
                            web = e;
                        }
                    }
                    if (web == null)
                        throw new Exception("Local HTML input unavailable");
                    var box = web.Current.BoundingRectangle;
                    Cursor.Position = new Point((int)(box.X + 15), (int)(box.Y + box.Height / 2));
                    Pump(80);
                    Hit(pad, "B");
                    Await(Shown, "Edge HTML field opens keyboard", 2200);
                    Hit(pad, "B");
                    Await(() => Title(w).Contains(marker + " OK"), "real Edge page receives character", 2000);
                }
                finally
                {
                    if (host != null)
                        host.ExitThread();
                    if (w != IntPtr.Zero && Title(w).Contains(marker))
                        PostClose(w, 0x10, IntPtr.Zero, IntPtr.Zero);
                    Cursor.Position = cursor;
                    if (before != IntPtr.Zero)
                        SetForegroundWindow(before);
                }
            }
        }
        [DllImportAlias("user32.dll", EntryPoint = "PostMessageW")]
        static extern bool PostClose(IntPtr w, int message, IntPtr p, IntPtr l);
    }
}