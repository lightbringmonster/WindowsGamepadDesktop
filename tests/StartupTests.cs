using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Gamepad
{
    internal static partial class Tests
    {
        static void StartupConfigChecks()
        {
            var previous = SettingsStore.Decode("{\"Version\":2}");
            Check(previous.StartupMode == "Desktop" && !previous.StartsNative,
                  "old settings and first launch default to desktop");
            var native = new Settings { StartupMode = "Native" };
            Check(SettingsStore.Decode(SettingsStore.Encode(native)).StartsNative,
                  "explicit native startup persists in a profile");
            var remembered = new Settings { StartupMode = "LastUsed", LastNativeMode = true };
            Check(SettingsStore.Decode(SettingsStore.Encode(remembered)).StartsNative,
                  "remembered mode round-trips with the startup choice");
            remembered.LastNativeMode = false;
            Check(!remembered.StartsNative, "remembered desktop mode starts with mapping enabled");
        }

        static void StartupModeChecks()
        {
            Application.EnableVisualStyles();
            string folder = Path.Combine(Path.GetTempPath(), "GamepadStartupTest-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "settings.json");
            var device = new SimDevice { Id = "startup", PhysicalKey = "startup", State = Pad() };
            var provider = new SimProvider { Devices = new[] { device } };
            var output = new Capture();
            try
            {
                var native = new Settings { StartupMode = "Native", GameApps = new string[0] };
                using (var host = new Host(native, provider, output, false, path))
                {
                    try
                    {
                        Await(() => host.ActiveId == device.Id, "startup test handoff to the primary controller", 1400);
                        Pump(250);
                        Hit(device, "B");
                        Check(host.ModeName == "原生手柄" && output.Events.Count == 0,
                              "native startup remains selected after foreground detection");
                        HoldModeChord(device, 2150);
                        Check(host.ModeName == "桌面键鼠", "startup native mode can switch to desktop");
                        Hit(device, "B");
                        Check(output.Events.SequenceEqual(new[] { "M1True", "M1False" }),
                              "desktop input works after switching out of native startup");

                        var remember = new Settings { StartupMode = "LastUsed", GameApps = new string[0] };
                        host.Apply(remember);
                        HoldModeChord(device, 2150);
                        Check(host.ModeName == "原生手柄", "mode changes after saving the remember option");
                    }
                    finally
                    {
                        host.ExitThread();
                    }
                }

                var restored = SettingsStore.Load(path);
                Check(restored.StartupMode == "LastUsed" && restored.LastNativeMode,
                      "normal exit saves the actual last mode in the profile");
                using (var host = new Host(restored, provider, new Capture(), false, path))
                {
                    try
                    {
                        Await(() => host.ActiveId == device.Id, "restarted host reads the same controller", 1400);
                        Check(host.ModeName == "原生手柄", "new host starts in the remembered mode");
                    }
                    finally
                    {
                        host.ExitThread();
                    }
                }
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
                if (Directory.Exists(folder))
                    Directory.Delete(folder);
            }
        }
    }
}