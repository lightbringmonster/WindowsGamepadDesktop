using System;
using System.Runtime.InteropServices;

namespace Gamepad
{
    internal sealed class ShellBackKeyFilter : IDisposable
    {
        const int LowLevelKeyboard = 13;
        const int GamepadB = 0xC4;
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
        bool consumed;

        public ShellBackKeyFilter(Func<bool> shouldConsume)
        {
            this.shouldConsume = shouldConsume;
            callback = Receive;
            handle = SetWindowsHookEx(LowLevelKeyboard, callback, GetModuleHandle(null), 0);
        }

        IntPtr Receive(int code, IntPtr message, IntPtr data)
        {
            if (code < 0 || Marshal.ReadInt32(data) != GamepadB)
                return CallNextHookEx(handle, code, message, data);

            try
            {
                int kind = message.ToInt32();
                if (kind == 0x0100 || kind == 0x0104)
                {
                    if (consumed || shouldConsume())
                    {
                        consumed = true;
                        return new IntPtr(1);
                    }
                }
                else if ((kind == 0x0101 || kind == 0x0105) && consumed)
                {
                    // A press that we consumed keeps its release paired, even after a focus change.
                    consumed = false;
                    return new IntPtr(1);
                }
            }
            catch (Exception)
            {
                // Windows keeps its normal handling if the game's foreground changes mid-query.
            }
            return CallNextHookEx(handle, code, message, data);
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(handle);
                handle = IntPtr.Zero;
            }
        }
    }
}