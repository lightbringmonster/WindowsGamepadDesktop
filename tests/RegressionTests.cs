using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
namespace Gamepad
{
    internal static partial class Tests
    {
        internal static void FinalChecks()
        {
            var returning = new Capture();
            var returnEngine = new MappingEngine(returning);
            var profile = new Profile();
            var pointerChanges = new List<bool>();
            returnEngine.Ledger.LeftButtonChanged = pointerChanges.Add;
            returnEngine.Tick(Pad(), profile, 0);
            returnEngine.Tick(Pad("A"), profile, 32);
            returnEngine.Tick(Pad(), profile, 64);
            Check(returning.Events.SequenceEqual(new[] { "K27True", "K27False" }),
                  "desktop Escape still sends both key transitions");
            returnEngine.Tick(Pad("B"), profile, 96);
            var trigger = Pad();
            trigger.RT = 1;
            returnEngine.Tick(trigger, profile, 112);
            returnEngine.Tick(Pad(), profile, 128);
            Check(pointerChanges.SequenceEqual(new[] { true, false }),
                  "shared left button reports one completed simulated click");
            returnEngine.Tick(Pad("B"), profile, 152);
            returnEngine.Reset();
            Check(pointerChanges.SequenceEqual(new[] { true, false, true }),
                  "forced mouse release does not complete a focus click");
            Check(new[] { "Key:Tab", "Key:Shift+Tab", "Key:Ctrl+L", "Key:Ctrl+F" }
                      .All(action => Host.IsFocusNavigation(KeyBinding.Parse(action))),
                  "explicit focus navigation stays eligible");
            Check(new[] { "Key:Escape", "Key:Ctrl+T", "Key:Alt+Tab", "Key:Enter", "Key:Up" }
                      .All(action => !Host.IsFocusNavigation(KeyBinding.Parse(action))),
                  "tab creation and unrelated desktop shortcuts do not request the keyboard");

            var rules = new ApplicationProfiles();
            var apps = new[] { "room.exe" };
            Check(rules.Enter("desktop.exe", apps) == false && rules.Enter("ROOM.EXE", apps) == true,
                  "foreground process name activates game policy");
            Check(rules.Enter("room.exe", apps) == null, "manual override persists in same app");
            Check(rules.Enter("desktop.exe", apps) == false, "game exit restores desktop");
            rules.Reload();
            Check(rules.Enter("film.exe", apps) == false, "unlisted fullscreen app remains desktop");
            var named = new Settings();
            named.Profiles["profile/北通"] = new Profile { Speed = 1410 };
            named.AssignedProfiles["xinput:2"] = "北通";
            Check(SettingsStore.Decode(SettingsStore.Encode(named)).For("xinput:2").Speed == 1410,
                  "named profile follows reassigned slot");
            var cap = new Capture();
            var e = new MappingEngine(cap);
            var p = new Profile();
            e.Tick(Pad(), p, 0);
            var state = Pad();
            state.LX = .1;
            e.Tick(state, p, 20);
            Check(cap.Events.Count == 0, "radial dead zone suppresses drift");
            state.LX = 1;
            e.Tick(state, p, 40);
            Check(cap.Events.Contains("move"), "analog stick moves mouse");
            cap.Events.Clear();
            state.LX = 0;
            state.RY = 1;
            for (int i = 0; i < 20; i++)
                e.Tick(state, p, 60 + 20 * i);
            Check(cap.Events.Contains("wheel"), "right stick generates wheel ticks");
            e.Reset();
            cap.Events.Clear();
            e.Tick(Pad(), p, 0);
            e.Tick(Pad("Down"), p, 20);
            e.Tick(Pad("Down"), p, 500);
            e.Tick(Pad(), p, 510);
            Check(cap.Events.SequenceEqual(new[] { "K40True", "K40True", "K40False" }),
                  "direction key repeat and final release");
            cap.Events.Clear();
            foreach (int start in new[] { 20, 120 })
            {
                e.Tick(Pad("B"), p, start + 600);
                e.Tick(Pad(), p, start + 620);
            }
            Check(cap.Events.SequenceEqual(new[] { "M1True", "M1False", "M1True", "M1False" }),
                  "two B taps preserve double-click timing");
            var dir = Path.Combine(Path.GetTempPath(), "GamepadTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "settings.json");
            try
            {
                SettingsStore.Save(file, named);
                named.For("xinput:2").Speed = 1500;
                SettingsStore.Save(file, named);
                Check(SettingsStore.Load(file).For("xinput:2").Speed == 1500, "atomic config replacement");
            }
            finally
            {
                if (File.Exists(file))
                    File.Delete(file);
                Directory.Delete(dir);
            }
            foreach (int width in new[] { 1920, 2560, 3840 })
            {
                var r = new Rectangle(-width, 0, width, width * 9 / 16);
                var kb = KeyboardWindow.Place(r, Rectangle.Empty, 41);
                Check(r.Contains(kb) && kb.Bottom > r.Height * .9, "bottom placement " + width);
            }
            e.Reset();
            cap.Events.Clear();
            p.Desktop["Axis.Motion"] = "Cursor";
            p.Validate();
            e.Tick(Pad(), p, 0);
            var motion = Pad();
            motion.ExtraAxes["Axis.Motion"] = new Axis2D { X = .8 };
            e.Tick(motion, p, 20);
            Check(cap.Events.Contains("move"), "provider-defined analog axis mapped to pointer");
            string simple = PinyinDictionary.Simplify("遊戲");
            Check(simple == "游戏" &&
                      Enumerable.Range(0, 100).All(i => PinyinDictionary.Simplify("中國") == "中国"),
                  "native simplified Chinese string termination");
        }
    }
}