using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Gamepad
{
    internal static partial class Tests
    {
        static void BindingCaptureChecks()
        {
            var capture = new BindingCapture(new[] { "B", "RS", "Paddle1", "RT", "LeftStick", "Axis.Motion" });
            Check(capture.Sample(Pad("B")) == null && capture.Sample(Pad("B")) == null &&
                      capture.Sample(Pad()) == null && capture.Sample(Pad("B")) == "B",
                  "identification ignores the input held while it starts");

            capture = new BindingCapture(new[] { "B", "RS", "Paddle1" });
            capture.Sample(Pad());
            Check(capture.Sample(Pad("B", "RS")) == null && capture.Sample(Pad("RS")) == null &&
                      capture.Sample(Pad()) == null && capture.Sample(Pad("Paddle1")) == "Paddle1",
                  "an ambiguous press rearms after releasing and can identify a declared extra button");

            capture = new BindingCapture(new[] { "RT", "LeftStick", "Axis.Motion" });
            capture.Sample(Pad());
            var axis = Pad();
            axis.RT = .4;
            axis.LX = .4;
            Check(capture.Sample(axis) == null, "small analog movements do not select a mapping row");
            axis.RT = .75;
            Check(capture.Sample(axis) == "RT", "trigger crossing its press threshold identifies its row");

            capture = new BindingCapture(new[] { "LeftStick", "Axis.Motion" });
            capture.Sample(Pad());
            var extended = Pad();
            extended.ExtraAxes["Axis.Motion"] = new Axis2D { X = .5, Y = .5 };
            Check(capture.Sample(extended) == "Axis.Motion", "declared extension axis can be identified");
        }

        static void BindingCaptureWindowChecks(string directory)
        {
            Directory.CreateDirectory(directory);
            Application.EnableVisualStyles();
            var primary = new SimDevice { Id = "Controller 1", PhysicalKey = "primary", State = Pad() };
            var spare = new SimDevice { Id = "Controller 2", PhysicalKey = "spare", State = Pad() };
            var provider = new SimProvider { Devices = new[] { primary, spare } };
            var output = new Capture();
            using (var host = new Host(new Settings { GameApps = new string[0] }, provider, output, false))
            using (var form = new SettingsWindow(host))
            {
                try
                {
                    form.Show();
                    FocusOwnedWindow(form.Handle);
                    Await(() => host.ActiveId == primary.Id && host.States.ContainsKey(spare.Id),
                          "both controllers are available to the settings page", 1500);
                    Descendants(form).OfType<Button>().Single(b => b.Text == "按键映射").PerformClick();
                    var device = Descendants(form).OfType<ComboBox>().Single(box => box.Items.Count == 2 &&
                                box.Items.Contains(spare.Name));
                    device.SelectedIndex = 1;
                    var start = Descendants(form).OfType<Button>().Single(button => button.Text == "识别按键");
                    var grid = Descendants(form).OfType<DataGridView>().Single();
                    start.PerformClick();
                    Check(start.Text == "取消识别", "the mapping page starts identification on the edited device");
                    Check(Descendants(form).OfType<Label>().Any(label => label.Visible && label.Bottom <= grid.Top &&
                              label.Text.Contains("摇杆回中")), "the mapping page reserves room for the identification prompt");
                    Pump(70);
                    Snap(form, Path.Combine(directory, "waiting-for-input.png"));

                    Volatile.Write(ref primary.State, Pad("B"));
                    Pump(140);
                    Check(start.Text == "取消识别" && output.Events.Count == 0,
                          "the primary neither completes identification for the spare nor clicks");
                    Volatile.Write(ref spare.State, Pad("RS"));
                    Await(() => start.Text == "识别按键", "the spare button completes identification", 1200);
                    Check((string)grid.CurrentRow.Cells[0].Value == "RS" &&
                              (string)grid.CurrentRow.Cells[1].Value == "None" &&
                              grid.CurrentRow.Displayed, "the visible RS row is selected without editing the binding");
                    Pump(70);
                    Snap(form, Path.Combine(directory, "identified-rs.png"));
                    Pump(110);
                    Check(output.Events.Count == 0, "the held B cannot leak through when identification ends");
                    Volatile.Write(ref primary.State, Pad());
                    Volatile.Write(ref spare.State, Pad());
                    Pump(100);
                    Hit(primary, "B");
                    Check(output.Events.SequenceEqual(new[] { "M1True", "M1False" }),
                          "ordinary mapping resumes after the controller returns to neutral");

                    start.PerformClick();
                    start.PerformClick();
                    Check(start.Text == "识别按键" && form.Visible, "the same button cancels identification");
                    start.PerformClick();
                    Volatile.Write(ref spare.State, new ControllerState());
                    Await(() => start.Text == "识别按键", "disconnecting the edited controller cancels identification", 1200);
                    Check(Descendants(form).OfType<Label>().Any(label => label.Text.Contains("已断开")),
                          "disconnection is shown on the mapping page");
                }
                finally
                {
                    host.ExitThread();
                }
            }
        }
    }
}
