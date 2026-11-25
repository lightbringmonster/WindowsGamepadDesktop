using System;
using System.Linq;
using System.Collections.Generic;
namespace Gamepad
{
    internal sealed class SimDevice : IControllerDevice
    {
        public string Id { get; set; }
        public string PhysicalKey { get; set; }
        public string Name
        {
            get {
                return Id;
            }
        }
        public ControllerCapabilities Capabilities
        {
            get {
                return new ControllerCapabilities { Controls = XInputDevice.Standard };
            }
        }
        public ControllerState State = new ControllerState();
        public ControllerState Read()
        {
            return System.Threading.Volatile.Read(ref State);
        }
    }
    internal sealed class SimProvider : IControllerProvider
    {
        public IEnumerable<IControllerDevice> Devices { get; set; }
    }
    internal static partial class Tests
    {
        static int count;
        static void Check(bool ok, string name)
        {
            if (!ok)
                throw new Exception(name);
            Console.WriteLine("PASS " + name);
            count++;
        }
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetType().FullName + " / " + ex.Message);
                Environment.ExitCode = 1;
            }
        }
        static void Run(string[] args)
        {
            if (args.Length == 2 && args[0] == "--explorer")
            {
                ExplorerChecks(args[1]);
                return;
            }
            if (args.Length == 2 && args[0] == "--popup-preview")
            {
                PopupChecks(args[1]);
                return;
            }
            if (args.Length == 2 && args[0] == "--radial-preview")
            {
                RadialWindowChecks(args[1]);
                return;
            }
            if (args.Contains("--startup-mode"))
            {
                StartupModeChecks();
                return;
            }
            if (args.Contains("--title-loop"))
            {
                TitleLoopChecks();
                return;
            }
            if (args.Contains("--minimize"))
            {
                MinimizeChecks();
                return;
            }
            if (args.Contains("--ime"))
            {
                ImeChecks();
                return;
            }
            if (args.Contains("--search"))
            {
                SearchChecks();
                return;
            }
            if (args.Length == 3 && args[0] == "--electron")
            {
                ElectronChecks(args[1], args[2]);
                return;
            }
            if ((args.Length == 2 || args.Length == 3) && args[0] == "--browser")
            {
                BrowserChecks(args[1], args.Length == 3 ? args[2] : null);
                return;
            }
            if (args.Length == 2 && args[0] == "--defaults")
            {
                var c = new Settings();
                foreach (var d in new XInputControllerProvider().Devices)
                    c.For(d.Id);
                SettingsStore.Save(args[1], c);
                return;
            }
            if (args.Length == 2 && args[0] == "--preview")
            {
                Preview(args[1]);
                return;
            }
            if (args.Contains("--fixture"))
            {
                System.Windows.Forms.Application.Run(new Fixture());
                return;
            }
            if (args.Contains("--windows"))
            {
                try
                {
                    WindowsChecks();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.GetType().Name + " " + ex.Message);
                    Environment.ExitCode = 1;
                }
                return;
            }
            var s = XInputDevice.Decode(
                new XInputDevice.Packet { Buttons = 0x3101, LX = short.MinValue, RY = short.MaxValue, LT = 255 });
            Check(s.Buttons.SetEquals(new[] { "Up", "LB", "A", "B" }) && s.LX == -1 && s.RY == 1 && s.LT == 1,
                  "XInput digital and analog decode");
            var a = new SimDevice { Id = "one", PhysicalKey = "same" };
            var b = new SimDevice { Id = "two", PhysicalKey = "other" };
            var p = new SimProvider { Devices = new[] { a, b } };
            var dm = new DeviceManager(p, p);
            Check(dm.Devices.Count == 2, "deduplicate providers");
            Check(dm.Select("", new[] { "one", "two" }) == a && dm.Select("", new[] { "two" }) == null,
                  "standby cannot take over");
            Check(dm.Select("", new[] { "one" }) == a && dm.Select("two", new[] { "two" }) == b,
                  "reconnect and explicit primary");
            foreach (var d in new XInputControllerProvider().Devices)
                Console.WriteLine("PROBE " + d.Id + " " + d.Read().Connected);
            More();
            FinalChecks();
            PointerWorkerChecks();
            DesktopGestureChecks();
            Console.WriteLine("Passed " + count + " checks");
        }
        static partial void More();
    }
}
