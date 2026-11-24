using System;
using System.Linq;
namespace Gamepad
{
    // A manual choice lasts until the matching foreground rule changes.
    internal sealed class ApplicationProfiles
    {
        string current;
        public bool? Enter(string processName, string[] gameApps)
        {
            string next = Matches(processName, gameApps) ? processName : "";
            if (String.Equals(current, next, StringComparison.OrdinalIgnoreCase))
                return null;
            current = next;
            return next.Length != 0;
        }
        internal static bool Matches(string processName, string[] entries)
        {
            return entries.Contains(processName, StringComparer.OrdinalIgnoreCase);
        }

        public void Reload()
        {
            current = null;
        }
    }
}