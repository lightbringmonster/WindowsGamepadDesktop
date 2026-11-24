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
            string next =
                gameApps.FirstOrDefault(x => String.Equals(x, processName, StringComparison.OrdinalIgnoreCase)) ??
                "";
            if (String.Equals(current, next, StringComparison.OrdinalIgnoreCase))
                return null;
            current = next;
            return next.Length != 0;
        }
        public void Reload()
        {
            current = null;
        }
    }
}