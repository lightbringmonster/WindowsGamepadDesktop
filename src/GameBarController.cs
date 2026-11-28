using Microsoft.Win32;

namespace Gamepad
{
    internal static class GameBarController
    {
        const string Path = @"Software\Microsoft\GameBar";
        const string Value = "UseNexusForGameBarEnabled";

        internal static bool IsBlocked(RegistryKey user)
        {
            using (var key = user.OpenSubKey(Path))
                return key != null && object.Equals(key.GetValue(Value), 0);
        }

        internal static void SetBlocked(RegistryKey user, bool blocked)
        {
            using (var key = user.CreateSubKey(Path))
                key.SetValue(Value, blocked ? 0 : 1, RegistryValueKind.DWord);
        }
    }
}
