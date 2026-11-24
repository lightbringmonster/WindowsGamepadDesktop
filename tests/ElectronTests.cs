using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
namespace Gamepad
{
    internal static partial class Tests
    {
        // Use a fresh Code profile and a generated text file so the user's workspace is untouched.
        static void ElectronChecks(string executable, string scratch)
        {
            Application.EnableVisualStyles();
            var before = Win.GetForegroundWindow();
            var cursor = Cursor.Position;
            string root = Path.GetFullPath(scratch);
            var marker = "GamepadQA" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string profile = Path.Combine(root, "profile"), file = Path.Combine(root, marker + ".txt");
            Directory.CreateDirectory(Path.Combine(profile, "User"));
            File.WriteAllText(
                Path.Combine(profile, "User", "settings.json"),
                "{\"workbench.startupEditor\":\"none\",\"editor.accessibilitySupport\":\"on\",\"telemetry.telemetryLevel\":\"off\",\"update.mode\":\"none\"}");
            File.WriteAllText(file, "Gamepad Desktop test fixture.");
            var args =
                "--new-window --skip-welcome --disable-extensions --disable-workspace-trust --force-renderer-accessibility --user-data-dir=\"" +
                profile + "\" \"" + file + "\"";
            using (Process.Start(new ProcessStartInfo(executable, args) { UseShellExecute = false }))
            {
            }
            IntPtr w = IntPtr.Zero;
            Host host = null;
            try
            {
                Await(
                    () =>
                    {
                        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
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
                    "isolated Electron editor window", 25000);
                var wait = Stopwatch.StartNew();
                while (Win.GetForegroundWindow() != w && wait.ElapsedMilliseconds < 5000)
                {
                    FocusOwnedWindow(w);
                    if (Win.GetForegroundWindow() != w)
                    {
                        var pos = new Point(220, 175);
                        ClientToScreen(w, ref pos);
                        if (Win.GetAncestor(WindowFromPoint(pos), 2) == w)
                        {
                            Cursor.Position = pos;
                            Pump(60);
                            if (Win.GetAncestor(WindowFromPoint(Cursor.Position), 2) == w)
                            {
                                var input = new NativeInput();
                                input.Mouse(1, true);
                                input.Mouse(1, false);
                            }
                        }
                        Pump(300);
                    }
                }
                Check(Win.GetForegroundWindow() == w, "Electron fixture foreground");
                var sink = new GuardedSink(w);
                var keys = new InputLedger(sink);
                Pump(900);
                keys.Tap(KeyBinding.Parse("Key:Ctrl+P"));
                Pump(500);
                var pad = new SimDevice { Id = "fixture", PhysicalKey = "fixture", State = Pad() };
                host = new Host(new Settings(), new SimProvider { Devices = new[] { pad } }, sink, false);
                Pump(300);
                Console.WriteLine("Quick-open focused type=" +
                                  System.Windows.Automation.AutomationElement.FocusedElement.Current.ControlType
                                      .ProgrammaticName);
                Hit(pad, "RB");
                Await(Shown, "manual fallback on Electron palette", 1500);
                Hit(pad, "A");
                keys.Tap(KeyBinding.Parse("Key:Escape"));
                Pump(200);
                keys.Tap(KeyBinding.Parse("Key:Ctrl+F"));
                Pump(380);
                Await(() => FocusDetection.Inspect().Editable, "Electron Find field editable metadata", 2200);
                var rect = FocusDetection.Inspect().Bounds;
                Cursor.Position = new Point(rect.X + 15, rect.Y + rect.Height / 2);
                Pump(100);
                Hit(pad, "B");
                Await(Shown, "Electron Find field auto-keyboard", 2200);
                Hit(pad, "A");
                keys.Tap(KeyBinding.Parse("Key:Escape"));
                Pump(180);
                bool editable = FocusDetection.Inspect().Editable;
                Console.WriteLine("Monaco focused editor metadata editable=" + editable);
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
}