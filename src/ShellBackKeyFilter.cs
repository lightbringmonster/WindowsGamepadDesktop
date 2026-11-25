using System;
using System.Runtime.InteropServices;

namespace Gamepad
{
    internal sealed class ShellBackKeyFilter : IDisposable
    {
        const int LowLevelKeyboard = 13, GamepadB = 0xc4;
        delegate IntPtr KeyboardHook(int code, IntPtr message, IntPtr data);

        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
        static extern IntPtr SetWindowsHookEx(int type, KeyboardHook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string module);

        readonly Func<bool> shouldConsume;
        readonly KeyboardHook callback;
        IntPtr handle;
        bool held, consumed;

        internal ShellBackKeyFilter(Func<bool> shouldConsume)
        {
            this.shouldConsume = shouldConsume;
            callback = Receive;
            handle = SetWindowsHookEx(LowLevelKeyboard, callback, GetModuleHandle(null), 0);
        }

        IntPtr Receive(int code, IntPtr message, IntPtr data)
        {
            if (code == 0 && Marshal.ReadInt32(data) == GamepadB)
            {
                int kind = message.ToInt32();
                if (kind == 0x0100 || kind == 0x0104)
                {
                    // Keep the decision from the initial press when a click changes foreground.
                    if (!held)
                    {
                        held = true;
                        consumed = shouldConsume();
                    }
                    if (consumed)
                        return (IntPtr)1;
                }
                else if (kind == 0x0101 || kind == 0x0105)
                {
                    bool consumeRelease = consumed;
                    held = consumed = false;
                    if (consumeRelease)
                        return (IntPtr)1;
                }
            }
            return CallNextHookEx(handle, code, message, data);
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero)
                return;
            UnhookWindowsHookEx(handle);
            handle = IntPtr.Zero;
        }
    }
}
