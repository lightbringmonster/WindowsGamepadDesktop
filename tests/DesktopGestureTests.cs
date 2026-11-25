using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Gamepad
{
    internal static partial class Tests
    {
        static ControllerState AimRadial(int sector, bool holdTrigger)
        {
            var state = Pad();
            state.RT = holdTrigger ? 1 : 0;
            double radians = sector * RadialAction.SectorAngle * Math.PI / 180;
            state.LX = Math.Sin(radians);
            state.LY = Math.Cos(radians);
            return state;
        }

        static void DesktopGestureChecks()
        {
            var profile = new Profile();
            var legacy = new Settings();
            legacy.For("test").Desktop["RT"] = "MouseLeft";
            Check(profile.Desktop["RT"] == "RadialMenu" &&
                      SettingsStore.Decode(SettingsStore.Encode(legacy)).For("test").Desktop["RT"] == "MouseLeft",
                  "new profiles open the radial menu and saved custom bindings round-trip");

            var capture = new Capture();
            var engine = new MappingEngine(capture);
            var gears = new List<PointerGear>();
            var previews = new List<int>();
            var selections = new List<int>();
            engine.PrecisionChanged = gears.Add;
            engine.RadialPreview = previews.Add;
            engine.RadialEnded = selections.Add;
            engine.Tick(Pad(), profile, 0);
            var precision = Pad();
            precision.LT = 1;
            engine.Tick(precision, profile, 10);
            engine.Tick(Pad(), profile, 159);
            Check(engine.Gear == PointerGear.Fine, "short precision press latches fine");
            engine.Tick(precision, profile, 220);
            engine.Tick(precision, profile, 370);
            Check(engine.Gear == PointerGear.Ultra, "held precision binding uses ultra");
            engine.Tick(Pad(), profile, 500);
            Check(gears.SequenceEqual(new[] { PointerGear.Fine, PointerGear.Ultra, PointerGear.Fine }),
                  "releasing ultra returns to the previous precision gear");
            engine.Tick(precision, profile, 520);
            engine.Tick(Pad(), profile, 570);
            Check(engine.Gear == PointerGear.Standard, "short press returns to standard gear");
            var distances = new List<int>();
            foreach (var gear in new[] { PointerGear.Standard, PointerGear.Fine, PointerGear.Ultra })
            {
                var meter = new MotionCounter();
                var stick = Pad();
                stick.LX = .30;
                new PointerMotion(meter).Apply(stick, profile, 1, gear);
                distances.Add(meter.PixelsX);
            }
            Check(distances[0] > distances[1] && distances[1] > distances[2] && distances[2] >= 9,
                  "each precision curve retains useful movement at low stick deflection");

            var aim = AimRadial(9, true);
            engine.Tick(aim, profile, 600);
            engine.Tick(aim, profile, 749);
            Check(previews.Count == 0 && !engine.CanMove && capture.Events.Count == 0,
                  "pending radial hold suppresses desktop motion without opening early");
            engine.Tick(aim, profile, 750);
            engine.Tick(AimRadial(8, false), profile, 780);
            Check(previews.SequenceEqual(new[] { 9 }) && selections.SequenceEqual(new[] { 9 }) && !engine.CanMove,
                  "release confirms only the highlighted sector and requires recentering");
            engine.Tick(Pad(), profile, 800);
            engine.Tick(aim, profile, 810);
            engine.Tick(aim, profile, 960);
            engine.Tick(Pad(), profile, 980);
            Check(selections.Last() == -1, "recentering cancels the radial command");
            engine.Tick(Pad(), profile, 1000);
            engine.Tick(aim, profile, 1010);
            engine.Tick(aim, profile, 1160);
            engine.SetContext(true, false, false);
            engine.Tick(AimRadial(9, false), profile, 1180);
            Check(selections.Count == 3 && selections.Last() == -1,
                  "native transition cancels the radial menu without dispatching a command");
            Check(RadialAction.Items.All(item => item.Keys != null), "all radial menu shortcuts resolve to keys");
        }

        static void RadialWindowChecks(string directory)
        {
            Application.EnableVisualStyles();
            Directory.CreateDirectory(directory);
            var pad = new SimDevice { Id = "radial", PhysicalKey = "radial", State = Pad() };
            var provider = new SimProvider { Devices = new[] { pad } };
            var capture = new Capture();
            using (var anchor = new Form { Text = "Radial menu test", Size = new Size(420, 270) })
            using (var host = new Host(new Settings { GameApps = new string[0] }, provider, capture, false))
            {
                try
                {
                    anchor.Show();
                    FocusOwnedWindow(anchor.Handle);
                    Await(() => host.ActiveId == pad.Id, "radial test controller selected", 1200);
                    Pump(70);
                    pad.State = AimRadial(9, true);
                    Await(() => VisibleRadialWindow() != IntPtr.Zero, "hold opens radial window", 1200);
                    Check(Win.GetForegroundWindow() == anchor.Handle, "radial window preserves focus");
                    Check(capture.Events.Count == 0, "radial hold suppresses desktop motion");
                    SnapPopupWindow(VisibleRadialWindow(), Path.Combine(directory, "radial-copy.png"));
                    pad.State = AimRadial(9, false);
                    Await(() => VisibleRadialWindow() == IntPtr.Zero, "release hides radial window", 1200);
                    Check(capture.Events.SequenceEqual(new[] { "K17True", "K67True", "K67False", "K17False" }),
                          "the radial window dispatches one selected shortcut");
                    pad.State = Pad();
                    Pump(130);
                    capture.Events.Clear();
                    pad.State = AimRadial(9, true);
                    Await(() => VisibleRadialWindow() != IntPtr.Zero, "radial window can reopen", 1200);
                    using (var other = new Form { Text = "Second target", Size = new Size(300, 180) })
                    {
                        other.Show();
                        FocusOwnedWindow(other.Handle);
                        pad.State = AimRadial(9, false);
                        Await(() => VisibleRadialWindow() == IntPtr.Zero,
                              "foreground change closes the radial window", 1200);
                        Check(capture.Events.Count == 0, "foreground change cancels radial output");
                        pad.State = Pad();
                    }
                }
                finally
                {
                    host.ExitThread();
                }
            }
        }

        static IntPtr VisibleRadialWindow()
        {
            var window = FindWindow(null, "手柄快捷菜单");
            return IsWindowVisible(window) ? window : IntPtr.Zero;
        }
    }
}
