using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace Gamepad
{
    internal static partial class Tests
    {
        static void ImeChecks()
        {
            Application.EnableVisualStyles();
            var prior = Win.GetForegroundWindow();
            var saved = InputLanguage.CurrentInputLanguage;
            var cursor = Cursor.Position;
            using (var form = new Form { TopMost = true, StartPosition = FormStartPosition.CenterScreen,
                                         Size = new Size(480, 180), Text = "手柄桌面 · IME 验收" })
            {
                var field = new TextBox { Dock = DockStyle.Top, Width = 360, ImeMode = ImeMode.On };
                form.Controls.Add(field);
                try
                {
                    form.Show();
                    FocusOwnedWindow(form.Handle);
                    if (Win.GetForegroundWindow() != form.Handle)
                    {
                        var point = field.PointToScreen(new Point(10, 10));
                        Cursor.Position = point;
                        Pump(70);
                        if (Win.GetAncestor(WindowFromPoint(point), 2) != form.Handle)
                            throw new Exception("IME fixture covered");
                        var input = new NativeInput();
                        input.Mouse(1, true);
                        input.Mouse(1, false);
                        Pump(120);
                    }
                    Check(Win.GetForegroundWindow() == form.Handle, "IME test edit foreground");
                    var zh = InputLanguage.InstalledInputLanguages.Cast<InputLanguage>().FirstOrDefault(
                        x => x.Culture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
                    if (zh == null)
                        throw new Exception("No Chinese input profile reported by Windows");
                    InputLanguage.CurrentInputLanguage = zh;
                    field.Focus();
                    field.ImeMode = ImeMode.On;
                    Pump(550);
                    Check(InputLanguage.CurrentInputLanguage.Culture.Name.StartsWith(
                              "zh", StringComparison.OrdinalIgnoreCase),
                          "Chinese input profile active in fixture");
                    var sink = new GuardedSink(form.Handle);
                    var ledger = new InputLedger(sink);
                    var model = new KeyboardModel(() =>
                                                  {
                                                      throw new Exception("unexpected offline dictionary");
                                                  }) { Insert = s =>
                                                       { throw new Exception("unexpected direct character"); },
                                                       Key = k => ledger.Tap(KeyBinding.Parse("Key:" + k)),
                                                       Close = () =>
                                                       {} };
                    model.Open("系统 IME");
                    foreach (char c in "nihao1")
                    {
                        string value = c.ToString();
                        model.Row = model.Rows.FindIndex(r => r.Contains(value));
                        model.Col = Array.IndexOf(model.Rows[model.Row], value);
                        model.Command("Select");
                        Pump(c == 'o' ? 430 : 140);
                    }
                    Check(field.Text.Any(c => c >= '\u3400' && c <= '\u9fff'),
                          "system IME let the number key commit a Chinese candidate");
                    Console.WriteLine("Exact nihao candidate matched 你好=" + (field.Text == "你好"));
                }
                finally
                {
                    InputLanguage.CurrentInputLanguage = saved;
                    form.Close();
                    Cursor.Position = cursor;
                    if (prior != IntPtr.Zero)
                        SetForegroundWindow(prior);
                }
            }
        }
    }
}