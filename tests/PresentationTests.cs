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
            var largePad = new SimDevice
            {
                Id = "Test Controller",
                PhysicalKey = "test:large",
                State = Pad("Button.48"),
                DeviceCapabilities = new ControllerCapabilities
                {
                    Controls = Enumerable.Range(1, 48).Select(i => "Button." + i).Concat(new[] { "Paddle4" }).ToArray()
                }
            };
            var devices = new XInputControllerProvider().Devices.Concat(new[] { largePad });
            using (var host = new Host(cfg, new SimProvider { Devices = devices }, new Capture(), false)) using (
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
                            CheckQuickBindings(settings, grid, directory);
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
                    TypeReading(model, "nihaohuanyingshiyong");
                    model.Row = model.Col = 0;
                    using (var window = new KeyboardWindow(model))
                    {
                        window.Present(settings.Handle, Rectangle.Empty, cfg);
                        Pump(80);
                        Snap(window, Path.Combine(directory, "keyboard.png"));
                        model.Open("English");
                        window.Invalidate();
                        Pump(60);
                        Snap(window, Path.Combine(directory, "keyboard-english.png"));
                        window.Hide();
                    }
                }
                finally
                {
                    host.ExitThread();
                }
            }
        }
        static void CheckQuickBindings(SettingsWindow settings, DataGridView grid, string directory)
        {
            var controls = Descendants(settings).ToArray();
            var device = controls.OfType<ComboBox>().Single(combo => combo.AccessibleName == "编辑设备");
            device.SelectedIndex = 0;
            var confirm = controls.OfType<ComboBox>().Single(combo => combo.AccessibleName == "确认按键");
            var back = controls.OfType<ComboBox>().Single(combo => combo.AccessibleName == "返回按键");
            controls.OfType<Button>().Single(button => button.Text == "互换").PerformClick();
            Check((string)confirm.SelectedItem == "A" && (string)back.SelectedItem == "B",
                  "settings swaps default confirm and back");
            device.SelectedItem = "Test Controller";
            Pump(150);
            Check(grid.Rows.Count == 49 && confirm.Items.Contains("Button.48") && back.Items.Contains("Paddle4"),
                  "settings lists all 49 reported extended controls");
            Check(Descendants(settings).OfType<Label>().Any(label => label.Text.Contains("按下 Button.48")),
                  "settings identifies the extra button being held");
            confirm.SelectedItem = "Button.48";
            back.SelectedItem = "Paddle4";
            controls.OfType<Button>().Single(button => button.Text == "应用").PerformClick();
            var row = grid.Rows.Cast<DataGridViewRow>()
                               .Single(item => (string)item.Cells[0].Value == "Button.48");
            Check((string)row.Cells[1].Value == "MouseLeft" && (string)row.Cells[2].Value == "Select",
                  "quick settings writes the extra button to both mapping columns");
            Snap(settings, Path.Combine(directory, "mappings-extra.png"));
            device.SelectedIndex = 0;
            Check((string)confirm.SelectedItem == "A" && (string)back.SelectedItem == "B",
                  "returning to first device restores its separate draft");
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