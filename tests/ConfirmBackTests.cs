using System;
using System.Collections.Generic;
using System.Linq;

namespace Gamepad
{
    internal static partial class Tests
    {
        static void CheckProviderRefresh()
        {
            var first = new SimDevice { Id = "first:1", PhysicalKey = "physical:1", State = Pad() };
            var duplicate = new SimDevice { Id = "other:1", PhysicalKey = first.PhysicalKey, State = Pad() };
            var independentA = new SimDevice { Id = "other:2", State = Pad() };
            var independentB = new SimDevice { Id = "other:3", State = Pad() };
            var main = new SimProvider { Devices = new[] { first } };
            var extra = new SimProvider { Devices = new IControllerDevice[0] };
            var manager = new DeviceManager(main, extra);
            Check(manager.Select("", new[] { first.Id }) == first, "first controller selected before hot plug");
            extra.Devices = new[] { duplicate, independentA, independentB };
            manager.Refresh();
            Check(manager.Devices.Contains(first) && !manager.Devices.Contains(duplicate) &&
                      manager.Devices.Contains(independentA) && manager.Devices.Contains(independentB),
                  "dynamic enumeration deduplicates known hardware and preserves unknown identities");
            main.Devices = new IControllerDevice[0];
            manager.Refresh();
            Check(!manager.Devices.Contains(first) &&
                      manager.Select("", manager.Devices.Select(d => d.Id)) == null,
                  "discovery removal releases the primary slot without switching to a different device");
            var replacement = new SimDevice { Id = first.Id, PhysicalKey = first.PhysicalKey, State = Pad() };
            main.Devices = new[] { replacement };
            manager.Refresh();
            Check(manager.Select("", new[] { replacement.Id }) == replacement && !manager.Devices.Contains(first),
                  "reconnected provider can replace the object while keeping the profile ID");
        }

        static void ConfirmBackChecks()
        {
            var profile = new Profile();
            var buttons = XInputDevice.Standard.Where(id => !Profile.IsAxis(id)).ToArray();
            Check(ConfirmBackBindings.FindConfirm(profile, buttons) == "B" &&
                      ConfirmBackBindings.FindBack(profile, buttons) == "A",
                  "default confirm and back pair");
            ConfirmBackBindings.Apply(profile, "A", "B", buttons);
            Check(profile.Desktop["A"] == "MouseLeft" && profile.Keyboard["A"] == "Select" &&
                      profile.Desktop["B"] == "Key:Escape" && profile.Keyboard["B"] == "Close",
                  "swapped A and B on desktop and keyboard");
            ConfirmBackBindings.Apply(profile, "Down", "Right", buttons);
            Check(profile.Desktop["Down"] == "MouseLeft" && profile.Keyboard["Down"] == "Select" &&
                      profile.Desktop["Right"] == "Key:Escape" && profile.Keyboard["Right"] == "Close" &&
                      profile.Keyboard["A"] == "Down" && profile.Keyboard["B"] == "Right" &&
                      profile.Desktop["RT"] == "MouseLeft" && profile.ModeChord == "LB+RB",
                  "D-pad assignments preserve displaced actions, trigger drag and mode chord");

            var more = buttons.Concat(Enumerable.Range(17, 40).Select(i => "Button." + i))
                           .Concat(new[] { "Paddle4" })
                           .ToArray();
            ConfirmBackBindings.Apply(profile, "Button.48", "Paddle4", more);
            var saved = new Settings();
            saved.Profiles["test:extended"] = profile;
            var restored = SettingsStore.Decode(SettingsStore.Encode(saved)).For("test:extended");
            Check(ConfirmBackBindings.FindConfirm(restored, more) == "Button.48" &&
                      ConfirmBackBindings.FindBack(restored, more) == "Paddle4",
                  "independent extended buttons persist in an exported profile");

            var capture = new Capture();
            var engine = new MappingEngine(capture);
            var commands = new List<string>();
            engine.KeyboardCommand = commands.Add;
            engine.Tick(Pad(), restored, 0);
            engine.Tick(Pad("Button.48"), restored, 15);
            engine.Tick(Pad(), restored, 30);
            engine.Tick(Pad("Paddle4"), restored, 45);
            engine.Tick(Pad(), restored, 60);
            Check(capture.Events.SequenceEqual(new[] { "M1True", "M1False", "K27True", "K27False" }),
                  "extended controls emit desktop click and Escape");
            engine.SetContext(false, false, true);
            engine.Tick(Pad(), restored, 100);
            engine.Tick(Pad("Button.48"), restored, 115);
            engine.Tick(Pad(), restored, 130);
            engine.Tick(Pad("Paddle4"), restored, 145);
            Check(commands.SequenceEqual(new[] { "Select", "Close" }) &&
                      KeyboardWindow.MakeLegend(restored).Contains("Paddle4  返回"),
                  "extended controls select and close keyboard with matching help text");
        }
    }
}
