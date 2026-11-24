using System;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace Gamepad
{
    internal static partial class Tests
    {
        static void Preview(string directory)
        {
            Directory.CreateDirectory(directory);
            Application.EnableVisualStyles();
            var cfg = new Settings();
            using (var host = new Host(cfg, new XInputControllerProvider(), new Capture(), false)) using (
                var settings = new SettingsWindow(host))
            {
                try
                {
                    settings.Show();
                    Pump(160);
                    Check(settings.Visible, "settings window opens");
                    Snap(settings, Path.Combine(directory, "devices.png"));
                    foreach (string section in new[] { "按键映射", "虚拟键盘", "应用规则", "设备" })
                    {
                        var button = Descendants(settings).OfType<Button>().First(b => b.Text == section);
                        button.PerformClick();
                        Pump(25);
                        Check(button.Visible && settings.Visible, "settings page " + section);
                        if (section == "按键映射")
                        {
                            var grid = Descendants(settings).OfType<DataGridView>().Single();
                            Check(grid.Rows.Count >= 18, "mapping editor populated");
                            Snap(settings, Path.Combine(directory, "mappings.png"));
                        }
                    }
                    var model =
                        new KeyboardModel(() => new PinyinDictionary(Path.Combine(
                                              AppDomain.CurrentDomain.BaseDirectory, "data"))) { Insert = s =>
                                                                                                 {},
                                                                                                 Key = s =>
                                                                                                 {},
                                                                                                 Close = () =>
                                                                                                 {} };
                    model.Open("拼音");
                    foreach (char c in "nihao")
                    {
                        model.Row = model.Rows.FindIndex(r => r.Contains(c.ToString()));
                        model.Col = Array.IndexOf(model.Rows[model.Row], c.ToString());
                        model.Command("Select");
                    }
                    model.Row = model.Col = 0;
                    using (var window = new KeyboardWindow(model))
                    {
                        window.Present(settings.Handle, Rectangle.Empty, cfg);
                        Pump(80);
                        Snap(window, Path.Combine(directory, "keyboard.png"));
                        window.Hide();
                    }
                }
                finally
                {
                    host.ExitThread();
                }
            }
        }
        static System.Collections.Generic.IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (var child in Descendants(c))
                    yield return child;
            }
        }
        static void Snap(Form form, string path)
        {
            using (var bmp = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}