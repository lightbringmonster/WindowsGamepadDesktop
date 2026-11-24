using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace Gamepad
{
    public interface IInputSink
    {
        bool Key(int vk, bool down);
        bool Mouse(int button, bool down);
        void Move(int x, int y);
        void Wheel(int horizontal, int vertical);
        void Text(string text);
    }
    internal static class Win
    {
        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr w, System.Text.StringBuilder name, int size);
        internal static bool IsShellMenu(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return true;
            var s = new System.Text.StringBuilder(128);
            GetClassName(hwnd, s, s.Capacity);
            var name = s.ToString();
            return name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd" ||
                   name == "NotifyIconOverflowWindow" || name == "TopLevelWindowForOverflowXamlIsland" ||
                   name == "#32768";
        }
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        internal static extern IntPtr WindowFromPoint(System.Drawing.Point point);
        [DllImport("user32.dll")]
        internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("user32.dll")]
        internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flag);
        [DllImport("user32.dll")]
        internal static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
        [DllImport("user32.dll")]
        internal static extern bool CloseDesktop(IntPtr desktop);
    }
    internal sealed class NativeInput : IInputSink
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MouseData
        {
            public int X, Y, Data;
            public uint Flags, Time;
            public UIntPtr Extra;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct KeyData
        {
            public ushort Vk, Scan;
            public uint Flags, Time;
            public UIntPtr Extra;
        }
        [StructLayout(LayoutKind.Explicit)]
        struct Union
        {
            [FieldOffset(0)]
            public MouseData Mouse;
            [FieldOffset(0)]
            public KeyData Key;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct Input
        {
            public uint Type;
            public Union U;
        }
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint n, Input[] inputs, int size);
        const uint Extended = 1, Up = 2, Unicode = 4;
        static Input K(ushort vk, ushort scan, uint flags)
        {
            return new Input { Type = 1,
                               U = new Union { Key = new KeyData { Vk = vk, Scan = scan, Flags = flags } } };
        }
        static bool Send(params Input[] inputs)
        {
            return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input))) == inputs.Length;
        }
        public bool Key(int vk, bool down)
        {
            bool extended = (vk >= 0x21 && vk <= 0x2e) || vk == 0x5b || vk == 0x5c || vk == 0xa3 || vk == 0xa5;
            return Send(K((ushort)vk, 0, (down ? 0 : Up) | (extended ? Extended : 0)));
        }
        static bool M(uint flags, int x, int y, int data)
        {
            return Send(new Input { U = new Union { Mouse = new MouseData { Flags = flags, X = x, Y = y,
                                                                            Data = data } } });
        }
        public bool Mouse(int button, bool down)
        {
            return M((uint)((button == 1 ? 2 : button == 2 ? 8 : 32) * (down ? 1 : 2)), 0, 0, 0);
        }
        public void Move(int x, int y)
        {
            M(1, x, y, 0);
        }
        public void Wheel(int horizontal, int vertical)
        {
            if (horizontal != 0)
                M(0x1000, 0, 0, horizontal);
            if (vertical != 0)
                M(0x800, 0, 0, vertical);
        }
        public void Text(string text)
        {
            var packets = new List<Input>();
            foreach (char c in text)
            {
                packets.Add(K(0, c, Unicode));
                packets.Add(K(0, c, Unicode | Up));
            }
            if (packets.Count > 0)
                Send(packets.ToArray());
        }
    }
    // Positive tokens are virtual keys. Negative tokens denote mouse buttons.
    public sealed class InputLedger
    {
        readonly IInputSink sink;
        readonly List<int> held = new List<int>();
        internal Action<bool> LeftButtonChanged;
        public InputLedger(IInputSink output)
        {
            sink = output;
        }
        bool Send(int token, bool down)
        {
            return token < 0 ? sink.Mouse(-token, down) : sink.Key(token, down);
        }
        public void Sync(List<int> wanted)
        {
            Sync(wanted, true);
        }
        void Sync(List<int> wanted, bool reportClicks)
        {
            for (int i = held.Count - 1; i >= 0; i--)
            {
                int token = held[i];
                if (!wanted.Contains(token) && Send(token, false))
                {
                    held.RemoveAt(i);
                    ReportLeftButton(token, false, reportClicks);
                }
            }
            foreach (int token in wanted)
            {
                if (!held.Contains(token) && Send(token, true))
                {
                    held.Add(token);
                    ReportLeftButton(token, true, reportClicks);
                }
            }
        }
        void ReportLeftButton(int token, bool down, bool reportClicks)
        {
            if (token == -1 && reportClicks && LeftButtonChanged != null)
                LeftButtonChanged(down);
        }
        public void Tap(int[] keys)
        {
            if (keys == null)
                return;
            var previous = new List<int>(held);
            var desired = new List<int>(held);
            desired.AddRange(keys);
            Sync(desired);
            Sync(previous);
        }
        public void Repeat(int token)
        {
            if (held.Contains(token) && token > 0)
                Send(token, true);
        }
        public void Release()
        {
            Sync(new List<int>(), false);
        }
    }
}