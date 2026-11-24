using System;
using System.Collections.Generic;
using System.Linq;

namespace Gamepad
{
    internal static class ConfirmBackBindings
    {
        internal static string FindConfirm(Profile profile, IEnumerable<string> buttons)
        {
            return Find(profile, buttons, "MouseLeft", "Select");
        }

        internal static string FindBack(Profile profile, IEnumerable<string> buttons)
        {
            return Find(profile, buttons, "Key:Escape", "Close");
        }

        internal static void Apply(Profile profile, string confirm, string back, IEnumerable<string> buttons)
        {
            if (confirm == null || back == null || confirm == back || !buttons.Contains(confirm) ||
                !buttons.Contains(back))
                throw new FormatException("请选择两个不同的手柄按键");
            Move(profile, confirm, FindConfirm(profile, buttons), "MouseLeft", "Select");
            Move(profile, back, FindBack(profile, buttons), "Key:Escape", "Close");
        }

        static string Find(Profile profile, IEnumerable<string> buttons, string desktop, string keyboard)
        {
            return buttons.FirstOrDefault(button => Read(profile.Desktop, button) == desktop &&
                                                    Read(profile.Keyboard, button) == keyboard);
        }

        static string Read(Dictionary<string, string> bindings, string button)
        {
            string action;
            return bindings.TryGetValue(button, out action) ? action : "None";
        }

        static void Move(Profile profile, string target, string previous, string desktop, string keyboard)
        {
            if (previous != null && previous != target)
            {
                profile.Desktop[previous] = Read(profile.Desktop, target);
                profile.Keyboard[previous] = Read(profile.Keyboard, target);
            }
            profile.Desktop[target] = desktop;
            profile.Keyboard[target] = keyboard;
        }
    }
}
