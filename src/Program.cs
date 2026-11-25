using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
namespace Gamepad
{
    internal static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll",
                                                  CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        static void SendCommand(bool exit)
        {
            var w = FindWindowEx((IntPtr)(-3), IntPtr.Zero, null, "WindowsGamepadDesktop.System");
            if (w != IntPtr.Zero)
                PostMessage(w, exit ? 0x800a : 0x8009, IntPtr.Zero, IntPtr.Zero);
        }
        [STAThread]
        static void Main(string[] args)
        {
            bool first;
            using (var mutex = new Mutex(true, @"Local\WindowsGamepadDesktop.Portable.v1", out first))
            {
                bool exit = Array.IndexOf(args, "--exit") >= 0;
                bool openSettings = !exit && Array.IndexOf(args, "--settings") >= 0 &&
                                    Array.IndexOf(args, "--tray") < 0;
                if (!first)
                {
                    if (exit || openSettings)
                        SendCommand(exit);
                    return;
                }
                if (exit)
                    return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var cfg = new Settings();
                string error = null;
                if (File.Exists(SettingsStore.Location))
                    try
                    {
                        cfg = SettingsStore.Load(SettingsStore.Location);
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }
                var host = new Host(cfg, new XInputControllerProvider(), new NativeInput(), openSettings,
                                    configurationLoadWarning: error);
                Application.ThreadException += (s, e) =>
                {
                    host.Pause(true);
                    PopupNotice.ShowNotice(e.Exception.GetType().Name, "映射已暂停");
                };
                try
                {
                    Application.Run(host);
                }
                finally
                {
                    host.ExitThread();
                    mutex.ReleaseMutex();
                }
            }
        }
    }
}
