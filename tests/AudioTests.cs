using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Gamepad
{
    internal static partial class Tests
    {
        sealed class FakeAudioDevices : IAudioDevices
        {
            internal AudioFlow Listed, Applied;
            internal string Selected;

            public AudioEndpoint[] List(AudioFlow flow)
            {
                Listed = flow;
                return flow == AudioFlow.Input
                    ? new[] { new AudioEndpoint("mic1", "麦克风阵列（内置设备）", true),
                              new AudioEndpoint("mic2", "麦克风（HyperX Cloud III）", false) }
                    : new[] { new AudioEndpoint("speaker1", "扬声器（Realtek Audio）", true),
                              new AudioEndpoint("speaker2", "耳机（HyperX Cloud III）", false) };
            }

            public void SetDefault(AudioFlow flow, string id)
            {
                Applied = flow;
                Selected = id;
            }
        }

        static void AudioWindowChecks(string directory)
        {
            Directory.CreateDirectory(directory);
            Application.EnableVisualStyles();
            var pad = new SimDevice { Id = "audio", PhysicalKey = "audio", State = Pad() };
            var provider = new SimProvider { Devices = new[] { pad } };
            var audio = new FakeAudioDevices();
            var output = new Capture();
            using (var anchor = new Form { Text = "Audio device test", Size = new Size(420, 270) })
            using (var host = new Host(new Settings { GameApps = new string[0] }, provider, output, false,
                                       audioDevices: audio))
            {
                try
                {
                    anchor.Show();
                    FocusOwnedWindow(anchor.Handle);
                    Await(() => host.ActiveId == pad.Id, "audio test controller selected", 1200);
                    OpenAudioPicker(pad, AudioFlow.Output);
                    Check(audio.Listed == AudioFlow.Output && audio.Selected == null &&
                              Win.GetForegroundWindow() == anchor.Handle,
                          "output list opens without changing the default or stealing focus");
                    Hit(pad, "Down");
                    SnapPopupWindow(FindWindow(null, "声音设备选择"), Path.Combine(directory, "audio-output.png"));
                    Hit(pad, "B");
                    Check(audio.Applied == AudioFlow.Output && audio.Selected == "speaker2" &&
                              !IsWindowVisible(FindWindow(null, "声音设备选择")) && output.Events.Count == 0,
                          "confirm applies the highlighted output without sending desktop input");

                    OpenAudioPicker(pad, AudioFlow.Input);
                    Check(audio.Listed == AudioFlow.Input, "input action opens the input list");
                    SnapPopupWindow(FindWindow(null, "声音设备选择"), Path.Combine(directory, "audio-input.png"));
                    Hit(pad, "A");
                    Check(audio.Selected == "speaker2" && !IsWindowVisible(FindWindow(null, "声音设备选择")),
                          "cancel closes the input list without selecting a device");

                    OpenAudioPicker(pad, AudioFlow.Input);
                    Hit(pad, "Down");
                    Hit(pad, "B");
                    Check(audio.Applied == AudioFlow.Input && audio.Selected == "mic2",
                          "confirm applies the selected input device");
                }
                finally
                {
                    host.ExitThread();
                }
            }
        }

        static void OpenAudioPicker(SimDevice pad, AudioFlow flow)
        {
            int sector = Array.FindIndex(RadialAction.Items, item => item.Audio == flow);
            pad.State = AimRadial(sector, true);
            Await(() => VisibleRadialWindow() != IntPtr.Zero, "radial menu opens for audio selection", 1200);
            pad.State = AimRadial(sector, false);
            Await(() => IsWindowVisible(FindWindow(null, "声音设备选择")), "sound device list opens", 1200);
            pad.State = Pad();
            Pump(80);
        }
    }
}
