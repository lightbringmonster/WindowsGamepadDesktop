using System.Collections.Generic;

namespace Gamepad
{
    internal static partial class Tests
    {
        static void ModeGestureChecks()
        {
            const string binding = "LB+RB";
            var gesture = new ModeGesture();
            var released = new HashSet<string>();
            var both = new HashSet<string> { "RB", "LB" };
            var one = new HashSet<string> { "RB" };
            gesture.Sample(binding, released, 0);
            gesture.Sample(binding, both, 10);
            Check(gesture.Sample(binding, released, 2009) == ModeGestureAction.None && gesture.SuppressSingles,
                  "release at 1999 ms cancels chord and suppresses both individual actions");
            gesture.Sample(binding, both, 3000);
            Check(gesture.Sample(binding, both, 4999) == ModeGestureAction.None &&
                  gesture.Sample(binding, both, 5000) == ModeGestureAction.ReadyToSwitch &&
                  gesture.Sample(binding, both, 5001) == ModeGestureAction.None,
                  "hold emits one ready prompt at two seconds without switching");
            Check(gesture.Sample(binding, one, 5000) == ModeGestureAction.SwitchMode,
                  "release of either shoulder at 2000 ms switches once");
            gesture.Sample(binding, both, 5050);
            Check(gesture.Sample(binding, both, 8170) == ModeGestureAction.None,
                  "repressing one shoulder cannot start a new chord until both release");
            gesture.Sample(binding, released, 8180);
            gesture.Sample(binding, both, 9000);
            Check(gesture.Sample(binding, both, 11999) == ModeGestureAction.ReadyToSwitch &&
                      gesture.Sample(binding, both, 12000) == ModeGestureAction.ToggleLock,
                  "three-second hold toggles the lock without requesting a switch");
            Check(gesture.Sample(binding, both, 18000) == ModeGestureAction.None &&
                      gesture.Sample(binding, released, 18100) == ModeGestureAction.None,
                  "three-second gesture cannot repeat or also switch on release");
            gesture.Sample(binding, both, 19000);
            Check(gesture.Sample(binding, released, 22000) == ModeGestureAction.ToggleLock,
                  "release sampled at the three-second boundary still takes the lock path");
            gesture.Reset();
            Check(gesture.Sample(binding, both, 22500) == ModeGestureAction.None &&
                      gesture.Sample(binding, both, 25900) == ModeGestureAction.None,
                  "held shoulders are ignored on reconnect until first release");
            gesture.Sample(binding, released, 26000);
            gesture.Sample("Back+Start", released, 27000);
            gesture.Sample("Back+Start", new HashSet<string> { "Back", "Start" }, 27100);
            Check(gesture.Sample("Back+Start", released, 29300) == ModeGestureAction.SwitchMode,
                  "per-device custom chord uses the same timing");

            var engine = new MappingEngine(new Capture());
            var profile = new Profile();
            int switches = 0;
            engine.ToggleMode = () => switches++;
            engine.SetContext(false, false, true);
            engine.Tick(Pad(), profile, 0);
            engine.Tick(Pad("LB", "RB"), profile, 20);
            engine.SetContext(true, false, false);
            engine.Tick(Pad("LB", "RB"), profile, 1500);
            engine.Tick(Pad(), profile, 2220);
            Check(switches == 1, "gesture survives overlay close and foreground mode change during hold");
        }
    }
}
