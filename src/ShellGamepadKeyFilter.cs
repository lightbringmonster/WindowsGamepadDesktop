using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Gamepad
{
    internal sealed class ShellGamepadKeyFilter : IDisposable
    {
        const int LowLevelKeyboard = 13;
        internal const int GamepadB = 0xC4;
        delegate IntPtr KeyboardHook(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
        static extern IntPtr SetWindowsHookEx(int type, KeyboardHook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string module);

        readonly Func<int, bool> shouldConsume;
        readonly KeyboardHook callback;
        IntPtr handle;
        readonly HashSet<int> consumed = new HashSet<int>();

        public ShellGamepadKeyFilter(Func<int, bool> shouldConsume)
        {
            this.shouldConsume = shouldConsume;
            callback = Receive;
            handle = SetWindowsHookEx(LowLevelKeyboard, callback, GetModuleHandle(null), 0);
        }

        internal static bool IsNavigationKey(int key)
        {
            return (key >= 0xCB && key <= 0xCE) || (key >= 0xD3 && key <= 0xDA);
        }

        internal static string StickForNavigationKey(int key)
        {
            if (key >= 0xD3 && key <= 0xD6)
                return "LeftStick";
            if (key >= 0xD7 && key <= 0xDA)
                return "RightStick";
            return null;
        }

        IntPtr Receive(int code, IntPtr message, IntPtr data)
        {
            if (code < 0)
                return CallNextHookEx(handle, code, message, data);
            int key = Marshal.ReadInt32(data);
            if (key != GamepadB && !IsNavigationKey(key))
                return CallNextHookEx(handle, code, message, data);

            try
            {
                int kind = message.ToInt32();
                if (kind == 0x0100 || kind == 0x0104)
                {
                    if (consumed.Contains(key) || shouldConsume(key))
                    {
                        consumed.Add(key);
                        return new IntPtr(1);
                    }
                }
                // Preserve keyboard event pairs across foreground changes.
                else if ((kind == 0x0101 || kind == 0x0105) && consumed.Remove(key))
                    return new IntPtr(1);
            }
            catch (Exception)
            {
                // Let Windows keep handling input if the foreground changes mid-query.
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