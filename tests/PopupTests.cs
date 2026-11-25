using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Gamepad
{
    internal static partial class Tests
    {
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr window, out WindowRect bounds);

        static void SnapPopupWindow(IntPtr window, string path)
        {
            WindowRect bounds;
            if (!GetWindowRect(window, out bounds))
                throw new InvalidOperationException("Could not capture the toast window.");
            using (var bitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        static void PopupChecks(string directory)
        {
            Application.EnableVisualStyles();
            Directory.CreateDirectory(directory);
            using (var owner =
                       new Form { Text = "Popup preview anchor", StartPosition = FormStartPosition.CenterScreen,
                                  Size = new Size(420, 200) })
            {
                owner.Show();
                Pump(150);
                FocusOwnedWindow(owner.Handle);
                var focus = Win.GetForegroundWindow();
                using (var toast = new ModeToast())
                {
                    toast.Present("松开切换 · 原生手柄");
                    Pump(100);
                    Check(IsWindowVisible(toast.Handle) &&
                              toast.Handle == FindWindow(null, "松开切换 · 原生手柄") &&
                              Win.GetForegroundWindow() == focus,
                          "mode cue stays visible without claiming focus");
                    SnapPopupWindow(toast.Handle, Path.Combine(directory, "mode-switch.png"));
                    toast.Present("桌面键鼠 · 已锁定");
                    Pump(90);
                    SnapPopupWindow(toast.Handle, Path.Combine(directory, "mode-lock.png"));
                    toast.Present("已暂停");
                    Pump(90);
                    SnapPopupWindow(toast.Handle, Path.Combine(directory, "mode-pause.png"));
                    Await(() => !IsWindowVisible(toast.Handle),
                          "status cue hides on timeout", 1800);
                }

                const string example = "当前按键组合无法保存。请选择两个不同的按键，再保存设置。";
                using (var notice = new PopupNotice(example, "无法保存设置", true)) using (
                    var action = new Timer { Interval = 150 })
                {
                    action.Tick += (sender, args) =>
                    {
                        action.Stop();
                        try
                        {
                            SnapPopupWindow(notice.Handle, Path.Combine(directory, "save-error.png"));
                            var details = Descendants(notice).OfType<TextBox>().Single();
                            Check(details.ReadOnly && details.Text == example &&
                                      notice.CancelButton == notice.AcceptButton,
                                  "owned confirmation dialog presents the complete message");
                        }
                        finally
                        {
                            notice.AcceptButton.PerformClick();
                        }
                    };
                    action.Start();
                    Check(notice.ShowDialog(owner) == DialogResult.OK, "error acknowledgement closes the dialog");
                }

                var longDetails =
                    String.Join(Environment.NewLine, Enumerable.Repeat("导入配置时遇到无效的按键映射。", 24));
                using (var longNotice = new PopupNotice(longDetails, "导入失败", false)) using (
                    var action = new Timer { Interval = 150 })
                {
                    action.Tick += (sender, args) =>
                    {
                        action.Stop();
                        try
                        {
                            SnapPopupWindow(longNotice.Handle, Path.Combine(directory, "import-details.png"));
                            var details = Descendants(longNotice).OfType<TextBox>().Single();
                            Check(details.Text == longDetails && details.ScrollBars == ScrollBars.Vertical,
                                  "unowned prompt keeps long details scrollable");
                        }
                        finally
                        {
                            longNotice.CancelButton.PerformClick();
                        }
                    };
                    action.Start();
                    Check(longNotice.ShowDialog() == DialogResult.OK,
                          "startup-style prompt returns on acknowledgement");
                }

                owner.FormBorderStyle = FormBorderStyle.None;
                owner.Bounds = Screen.FromControl(owner).Bounds;
                owner.TopMost = true;
                FocusOwnedWindow(owner.Handle);
                GameRuleToast(owner, "Native", "原生手柄 · 应用规则",
                              Path.Combine(directory, "game-mode-continued.png"));
                GameRuleToast(owner, "Desktop", "已切换 · 原生手柄",
                              Path.Combine(directory, "game-mode-switched.png"));
            }
        }

        static void GameRuleToast(Form gameWindow, string startupMode, string message, string screenshot)
        {
            string process;
            using (var current = Process.GetCurrentProcess())
                process = current.ProcessName + ".exe";
            var cfg = new Settings { StartupMode = startupMode, GameApps = new[] { process } };
            var provider = new SimProvider { Devices = new IControllerDevice[0] };
            using (var host = new Host(cfg, provider, new Capture(), false))
            {
                try
                {
                    Await(() => host.ModeName == "原生手柄" && IsWindowVisible(FindWindow(null, message)),
                          "foreground game rule displays " + message, 1100);
                    Check(Win.GetForegroundWindow() == gameWindow.Handle, "game mode toast preserves game focus");
                    SnapPopupWindow(FindWindow(null, message), screenshot);
                }
                finally
                {
                    host.ExitThread();
                }
            }
        }
    }
}
