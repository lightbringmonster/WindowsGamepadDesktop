using System;
using Microsoft.Win32;

namespace Gamepad
{
    internal static partial class Tests
    {
        static void GameBarControllerChecks()
        {
            var path = @"Software\WindowsGamepadDesktop.Test-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var testUser = Registry.CurrentUser.CreateSubKey(path))
                {
                    Check(!GameBarController.IsBlocked(testUser), "missing Game Bar setting leaves the blocker unchecked");
                    GameBarController.SetBlocked(testUser, true);
                    using (var key = testUser.OpenSubKey(@"Software\Microsoft\GameBar"))
                    {
                        const string value = "UseNexusForGameBarEnabled";
                        Check(GameBarController.IsBlocked(testUser) && object.Equals(key.GetValue(value), 0) &&
                                  key.GetValueKind(value) == RegistryValueKind.DWord,
                              "Game Bar blocker writes the documented per-user DWORD");
                    }
                    GameBarController.SetBlocked(testUser, false);
                    Check(!GameBarController.IsBlocked(testUser), "Game Bar controller launch can be re-enabled");
                }
            }
            finally
            {
                Registry.CurrentUser.DeleteSubKeyTree(path, false);
            }
        }
    }
}
