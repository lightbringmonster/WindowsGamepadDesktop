using System.Collections.Generic;

namespace Gamepad
{
    internal enum ModeGestureAction
    {
        None,
        ReadyToSwitch,
        SwitchMode,
        ToggleLock
    }

    internal sealed class ModeGesture
    {
        const int SwitchMilliseconds = 2000;
        const int LockMilliseconds = 3000;
        string binding;
        string first, second;
        bool armed, suppress, switchPromptShown;
        long started = -1;

        public bool SuppressSingles { get; private set; }

        public void Reset()
        {
            armed = suppress = SuppressSingles = switchPromptShown = false;
            started = -1;
        }

        public bool Contains(string button)
        {
            return button == first || button == second;
        }

        public ModeGestureAction Sample(string chord, HashSet<string> down, long now)
        {
            if (binding != chord)
            {
                binding = chord;
                var buttons = chord.Split('+');
                first = buttons[0];
                second = buttons[1];
                Reset();
            }

            bool a = down.Contains(first), b = down.Contains(second);
            if (!armed)
            {
                armed = !a && !b;
                return ModeGestureAction.None;
            }
            if (a && b && !suppress)
            {
                suppress = true;
                switchPromptShown = false;
                started = now;
            }

            SuppressSingles = suppress;
            ModeGestureAction action = ModeGestureAction.None;
            if (started >= 0)
            {
                long held = now - started;
                if (held >= LockMilliseconds)
                {
                    action = ModeGestureAction.ToggleLock;
                    started = -1;
                }
                else if (!a || !b)
                {
                    if (held >= SwitchMilliseconds)
                        action = ModeGestureAction.SwitchMode;
                    started = -1;
                }
                else if (held >= SwitchMilliseconds && !switchPromptShown)
                {
                    switchPromptShown = true;
                    action = ModeGestureAction.ReadyToSwitch;
                }
            }
            if (!a && !b)
                suppress = false;
            return action;
        }
    }
}
