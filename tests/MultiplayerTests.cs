using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Gamepad
{
    internal static partial class Tests
    {
        static void MultiplayerConfigChecks()
        {
            Check(SettingsStore.Decode("{\"Version\":2}").MultiplayerApps.Length == 0,
                  "older profiles default to an empty multiplayer list");
            var cfg = new Settings { MultiplayerApps = new[] { "LocalGame.exe" } };
            var saved = SettingsStore.Decode(SettingsStore.Encode(cfg));
            Check(ApplicationProfiles.Matches("localgame.EXE", saved.MultiplayerApps) &&
                      !ApplicationProfiles.Matches("OtherLocalGame.exe", saved.MultiplayerApps),
                  "multiplayer rules persist and match full process names case insensitively");
        }

        static void FocusFixture(IntPtr window)
        {
            SetForegroundWindow(window);
            Pump(140);
            if (Win.GetForegroundWindow() == window)
                return;
            var point = new Point(75, 120);
            ClientToScreen(window, ref point);
            Cursor.Position = point;
            Pump(70);
            if (Win.GetAncestor(WindowFromPoint(Cursor.Position), 2) != window)
                throw new InvalidOperationException("Multiplayer test fixture is covered");
            var native = new NativeInput();
            native.Mouse(1, true);
            native.Mouse(1, false);
            Pump(150);
        }

        static void MultiplayerChecks()
        {
            Application.EnableVisualStyles();
            var original = Cursor.Position;
            var prior = Win.GetForegroundWindow();
            string token = Guid.NewGuid().ToString("N").Substring(0, 8);
            string executable =
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GamepadLocalTest-" + token + ".exe");
            string title = "Gamepad Local Multiplayer " + token;
            File.Copy(Application.ExecutablePath, executable);
            Process game = null;
            Host host = null;
            using (var anchor =
                       new Form { Text = "Gamepad single-controller test", TopMost = true,
                                  StartPosition = FormStartPosition.CenterScreen, Size = new Size(450, 250) })
            {
                try
                {
                    anchor.Show();
                    FocusFixture(anchor.Handle);
                    Check(Win.GetForegroundWindow() == anchor.Handle, "single controller fixture is foreground");
                    var primary = new SimDevice { Id = "pad:one", PhysicalKey = "one", State = Pad() };
                    var secondary = new SimDevice { Id = "pad:two", PhysicalKey = "two", State = Pad() };
                    var cfg = new Settings { GameApps = new string[0],
                                             MultiplayerApps = new[] { Path.GetFileName(executable) } };
                    var output = new Capture();
                    host =
                        new Host(cfg, new SimProvider { Devices = new[] { primary, secondary } }, output, false);
                    Await(() => host.ActiveId == primary.Id && host.States[secondary.Id].Connected,
                          "both pads are connected and the first is primary", 1200);
                    Pump(160);
                    primary.State = Pad("B");
                    secondary.State = Pad("X");
                    Pump(110);
                    primary.State = secondary.State = Pad();
                    Pump(110);
                    Check(output.Events.SequenceEqual(new[] { "M1True", "M1False" }),
                          "two simultaneous pads produce only the primary desktop action");
                    output.Events.Clear();

                    primary.State = new ControllerState();
                    Await(() => host.ActiveId == null, "primary disconnect stops the desktop mapping", 1300);
                    Hit(secondary, "B");
                    Check(output.Events.Count == 0, "standby pad does not take over after unplug");
                    primary.State = Pad();
                    Await(() => host.ActiveId == primary.Id, "primary reclaim after reconnection", 1700);
                    Pump(120);

                    HoldModeChord(primary, 3210);
                    Check(host.ModeName == "桌面键鼠 · 已锁定", "desktop mode is locked before local multiplayer");
                    primary.State = Pad("B");
                    Await(() => output.Events.SequenceEqual(new[] { "M1True" }),
                          "primary is holding a desktop mouse button before the game", 1500);
                    anchor.TopMost = false;
                    game = Process.Start(new ProcessStartInfo(executable, "--fixture \"" + title +
                                                                              "\"") { UseShellExecute = false });
                    IntPtr gameWindow = IntPtr.Zero;
                    Await(
                        () =>
                        {
                            gameWindow = FindWindow(null, title);
                            return gameWindow != IntPtr.Zero;
                        },
                        "separate multiplayer fixture opens", 6500);
                    FocusFixture(gameWindow);
                    Check(Win.GetForegroundWindow() == gameWindow, "multiplayer fixture is foreground");
                    Await(() => host.ModeName == "本地多人保护 · 原生手柄",
                          "local multiplayer suspends even locked desktop mapping", 1500);
                    Check(output.Events.SequenceEqual(new[] { "M1True", "M1False" }),
                          "entering multiplayer releases the held mouse button once");

                    output.Events.Clear();
                    primary.State = secondary.State = Pad();
                    Pump(120);
                    primary.State = Pad("B", "Y");
                    secondary.State = Pad("X", "RB");
                    Pump(350);
                    primary.State = secondary.State = Pad();
                    Pump(160);
                    Hit(primary, "RB");
                    Check(output.Events.Count == 0 && !Shown(),
                          "protected foreground receives no synthesized keys or overlay from either pad");
                    Check(host.ActiveId == primary.Id && host.States[secondary.Id].Connected,
                          "both physical slots stay connected during protection");

                    primary.State = Pad("B");
                    game.CloseMainWindow();
                    Check(game.WaitForExit(2000), "multiplayer fixture closes normally");
                    anchor.TopMost = true;
                    FocusFixture(anchor.Handle);
                    Await(() => host.ModeName == "桌面键鼠 · 已锁定",
                          "leaving multiplayer restores the previous locked mode", 1500);
                    Pump(180);
                    Check(output.Events.Count == 0, "returning with a held button waits for neutral");
                    primary.State = Pad();
                    Pump(100);
                    Hit(primary, "B");
                    Check(output.Events.SequenceEqual(new[] { "M1True", "M1False" }),
                          "primary desktop mapping resumes after neutral");
                }
                finally
                {
                    if (host != null)
                        host.ExitThread();
                    if (game != null)
                    {
                        if (!game.HasExited)
                        {
                            game.CloseMainWindow();
                            if (!game.WaitForExit(1500))
                                game.Kill();
                        }
                        game.Dispose();
                    }
                    anchor.Close();
                    Cursor.Position = original;
                    if (prior != IntPtr.Zero)
                        SetForegroundWindow(prior);
                    File.Delete(executable);
                }
            }
        }
    }
}