using System;
using System.Linq;
namespace Gamepad
{
    internal static partial class Tests
    {
        static partial void MoreFocus()
        {
            Check(new[] { 0xCB, 0xCC, 0xCD, 0xCE, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA }
                      .All(ShellGamepadKeyFilter.IsNavigationKey) &&
                  new[] { 0x41, 0xC3, 0xC4, 0xCA, 0xCF, 0xDB }.All(key => !ShellGamepadKeyFilter.IsNavigationKey(key)),
                  "only native D-pad and stick virtual keys are classified as navigation");
            Check(ShellGamepadKeyFilter.StickForNavigationKey(0xD3) == "LeftStick" &&
                  ShellGamepadKeyFilter.StickForNavigationKey(0xD6) == "LeftStick" &&
                  ShellGamepadKeyFilter.StickForNavigationKey(0xD7) == "RightStick" &&
                  ShellGamepadKeyFilter.StickForNavigationKey(0xDA) == "RightStick" &&
                  ShellGamepadKeyFilter.StickForNavigationKey(0xCE) == null,
                  "desktop can reserve just the stick that is mapped to the pointer or scrolling");
            var cap = new Capture();
            var engine = new MappingEngine(cap);
            var p = new Profile();
            int nav = 0;
            engine.KeyboardCommand = a =>
            {
                if (a == "Right")
                    nav++;
            };
            engine.SetContext(false, false, true);
            engine.Tick(Pad(), p, 0);
            var s = Pad();
            s.LX = .9;
            engine.Tick(s, p, 30);
            engine.Tick(s, p, 460);
            Check(nav == 2 && cap.Events.Count == 0, "keyboard owns stick incl. repeat");
            engine.SetContext(false, false, false);
            engine.Tick(Pad(), p, 500);
            engine.Tick(Pad("LB", "RB"), p, 520);
            engine.Tick(Pad("RB"), p, 590);
            engine.Tick(Pad(), p, 610);
            Check(cap.Events.Count == 0, "aborted chord suppresses both singles");
            Check(Host.IsStartSearchHandoff("StartMenuExperienceHost", "SearchHost", "SearchTextBox") &&
                  Host.IsStartSearchHandoff("StartMenuExperienceHost", "SearchApp", "SearchTextBox"),
                  "start menu can hand an intentional click to Windows Search");

            Check(!Host.IsStartSearchHandoff("chrome", "SearchHost", "SearchTextBox") &&
                  !Host.IsStartSearchHandoff("StartMenuExperienceHost", "msedge", "SearchTextBox") &&
                  !Host.IsStartSearchHandoff("StartMenuExperienceHost", "SearchHost", "UnrelatedInput"),
                  "other foreground handoffs remain outside the search exception");

            string invalid = "{\"Version\":99}";
            bool rejected = false;
            try
            {
                SettingsStore.Decode(invalid);
            }
            catch (FormatException)
            {
                rejected = true;
            }
            Check(rejected, "reject future configuration");
            var q = new Profile();
            q.Desktop["Paddle1"] = "Key:Ctrl+Shift+S";
            q.Keyboard["Paddle1"] = "Select";
            q.Validate();
            Check(KeyBinding.Parse(q.Desktop["Paddle1"]).SequenceEqual(new[] { 17, 16, 83 }),
                  "capability-based extra controls");
        }
    }
}