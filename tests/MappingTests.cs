using System;
using System.Collections.Generic;
using System.Linq;
namespace Gamepad
{
    internal sealed class Capture : IInputSink
    {
        public readonly List<string> Events = new List<string>();
        public bool Key(int k, bool d)
        {
            Events.Add("K" + k + d);
            return true;
        }
        public bool Mouse(int b, bool d)
        {
            Events.Add("M" + b + d);
            return true;
        }
        public void Move(int x, int y)
        {
            Events.Add("move");
        }
        public void Wheel(int x, int y)
        {
            Events.Add("wheel");
        }
        public void Text(string s)
        {
            Events.Add("text");
        }
    }
    internal static partial class Tests
    {
        static ControllerState Pad(params string[] ids)
        {
            var s = new ControllerState { Connected = true };
            s.Buttons.UnionWith(ids);
            return s;
        }
        static partial void More()
        {
            var output = new Capture();
            var engine = new MappingEngine(output);
            var p = new Profile();
            p.Desktop["RT"] = "MouseLeft";
            int modes = 0, keyboards = 0;
            engine.ToggleMode = delegate
            {
                modes++;
                engine.SetContext(!engine.Game, false, false);
            };
            engine.ToggleKeyboard = delegate
            {
                keyboards++;
                engine.SetContext(false, false, true);
            };
            engine.Tick(Pad(), p, 0);
            engine.Tick(Pad("LB"), p, 10);
            engine.Tick(Pad("LB", "RB"), p, 25);
            engine.Tick(Pad("LB", "RB"), p, 2024);
            Check(modes == 0 && keyboards == 0 && output.Events.Count == 0,
                  "chord does not leak before threshold");
            engine.Tick(Pad("LB", "RB"), p, 2025);
            engine.Tick(Pad(), p, 2030);
            engine.Tick(Pad("B"), p, 2038);
            Check(modes == 1 && output.Events.Count == 0, "native mode emits no mouse or keys");
            engine.SetContext(false, false, false);
            engine.Tick(Pad(), p, 0);
            var s = Pad("B");
            s.RT = 1;
            engine.Tick(s, p, 10);
            s.Buttons.Clear();
            engine.Tick(s, p, 20);
            Check(output.Events.SequenceEqual(new[] { "M1True" }), "shared drag stays held");
            engine.Tick(new ControllerState(), p, 30);
            Check(output.Events.Last() == "M1False", "disconnect releases drag");
            output.Events.Clear();
            engine.Tick(Pad("Y"), p, 40);
            engine.Tick(Pad(), p, 50);
            Check(output.Events.Count == 0, "neutral rearm");
            engine.Tick(Pad("LB"), p, 60);
            engine.Tick(Pad(), p, 80);
            Check(output.Events.SequenceEqual(new[] { "K18True", "K9True", "K9False", "K18False" }),
                  "deferred Alt+Tab order");
            engine.Tick(Pad("RB"), p, 90);
            engine.Tick(Pad(), p, 95);
            Check(keyboards == 1, "manual keyboard");
            var cfg = new Settings();
            cfg.For("xinput:0").Speed = 1234;
            Check(SettingsStore.Decode(SettingsStore.Encode(cfg)).For("xinput:0").Speed == 1234, "JSON roundtrip");
            Check(SettingsStore.Decode("{\"Version\":1}").Version == 2, "v1 migration");
            ModeGestureChecks();
            MoreUI();
        }
        static partial void MoreUI();
    }
}
