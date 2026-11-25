using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Gamepad
{
    public sealed class MotionVector
    {
        public double X, Y, Z;
    }
    public struct Axis2D
    {
        public double X, Y;
    }
    public sealed class ControllerState
    {
        public readonly Dictionary<string, Axis2D> ExtraAxes = new Dictionary<string, Axis2D>();
        public bool Connected;
        public readonly HashSet<string> Buttons = new HashSet<string>(StringComparer.Ordinal);
        public double LX, LY, RX, RY, LT, RT;
        public MotionVector Gyroscope, Accelerometer;
        public int? BatteryLevel; // 0..3. Null for wired or unavailable.
    }
    public sealed class ControllerCapabilities
    {
        public string[] Controls;
        public bool HasGyroscope, HasAccelerometer;
        public volatile bool HasBattery;
    }
    public interface IControllerDevice
    {
        string Id { get; }
        string PhysicalKey { get; } // Shared across providers only when correlation is trustworthy.
        string Name { get; }
        ControllerCapabilities Capabilities { get; }
        // Return a stable snapshot. The primary device is read on the input worker.
        ControllerState Read();
    }
    public interface IControllerProvider
    {
        IEnumerable<IControllerDevice> Devices { get; }
    }
    public sealed class DeviceManager
    {
        public readonly List<IControllerDevice> Devices;
        public DeviceManager(params IControllerProvider[] providers)
        {
            Devices =
                providers.SelectMany(p => p.Devices).GroupBy(d => d.PhysicalKey).Select(g => g.First()).ToList();
        }
        // Automatic choice is latched for the session, including across disconnection.
        public string Primary;
        public IControllerDevice Select(string preferred, IEnumerable<string> connected)
        {
            var ids = new HashSet<string>(connected);
            if (!String.IsNullOrEmpty(preferred))
                Primary = preferred;
            if (String.IsNullOrEmpty(Primary))
                Primary = Devices.Where(d => ids.Contains(d.Id)).Select(d => d.Id).FirstOrDefault();
            return Devices.FirstOrDefault(d => d.Id == Primary && ids.Contains(d.Id));
        }
    }
    public sealed class XInputControllerProvider : IControllerProvider
    {
        readonly IControllerDevice[] devices =
            Enumerable.Range(0, 4).Select(i => (IControllerDevice) new XInputDevice(i)).ToArray();
        public IEnumerable<IControllerDevice> Devices
        {
            get {
                return devices;
            }
        }
    }
    internal sealed class XInputDevice : IControllerDevice
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Packet
        {
            public uint Number;
            public ushort Buttons;
            public byte LT, RT;
            public short LX, LY, RX, RY;
        }
        [DllImport("xinput1_4.dll")]
        static extern uint XInputGetState(uint slot, out Packet packet);
        [DllImport("xinput1_4.dll")]
        static extern uint XInputGetBatteryInformation(uint slot, byte type, out Battery battery);
        [StructLayout(LayoutKind.Sequential)]
        struct Battery
        {
            public byte Type, Level;
        }
        static readonly string[] bits = { "Up", "Down", "Left", "Right", "Start", "Back", "LS", "RS",
                                          "LB", "RB",   null,   null,    "A",     "B",    "X",  "Y" };
        internal static readonly string[] Standard =
            bits.Where(b => b != null).Concat(new[] { "LT", "RT", "LeftStick", "RightStick" }).ToArray();
        readonly uint index;
        readonly ControllerCapabilities caps = new ControllerCapabilities { Controls = Standard };
        long batteryAt;
        int? level;
        uint packetNumber;
        ControllerState snapshot;
        public XInputDevice(int slot)
        {
            index = (uint)slot;
        }
        public string Id
        {
            get {
                return "xinput:" + index;
            }
        }
        public string PhysicalKey
        {
            get {
                return Id;
            }
        }
        public string Name
        {
            get {
                return "XInput  " + (index + 1);
            }
        }
        public ControllerCapabilities Capabilities
        {
            get {
                return caps;
            }
        }
        public ControllerState Read()
        {
            Packet packet;
            if (XInputGetState(index, out packet) != 0)
            {
                batteryAt = 0;
                level = null;
                caps.HasBattery = false;
                snapshot = null;
                return new ControllerState();
            }
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (now >= batteryAt)
            {
                Battery b;
                level = XInputGetBatteryInformation(index, 0, out b) == 0 && (b.Type == 2 || b.Type == 3)
                            ? (int?)b.Level
                            : null;
                caps.HasBattery = level.HasValue;
                batteryAt = now + 15 * System.Diagnostics.Stopwatch.Frequency;
            }
            if (snapshot == null || packet.Number != packetNumber || snapshot.BatteryLevel != level)
            {
                snapshot = Decode(packet);
                snapshot.BatteryLevel = level;
                packetNumber = packet.Number;
            }
            return snapshot;
        }
        static double Axis(short value)
        {
            return value < 0 ? value / 32768.0 : value / 32767.0;
        }
        internal static ControllerState Decode(Packet p)
        {
            var s = new ControllerState { Connected = true, LX = Axis(p.LX),   LY = Axis(p.LY),  RX = Axis(p.RX),
                                          RY = Axis(p.RY),  LT = p.LT / 255.0, RT = p.RT / 255.0 };
            for (int i = 0; i < bits.Length; i++)
                if (bits[i] != null && (p.Buttons & (1 << i)) != 0)
                    s.Buttons.Add(bits[i]);
            return s;
        }
    }
}
