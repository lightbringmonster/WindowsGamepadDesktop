using System;
using System.Collections.Generic;

namespace Gamepad
{
    internal sealed class BindingCapture
    {
        const double Released = .30, StickPressed = .60;
        readonly HashSet<string> allowed;
        bool armed;

        internal BindingCapture(IEnumerable<string> controls)
        {
            allowed = new HashSet<string>(controls, StringComparer.Ordinal);
        }

        internal string Sample(ControllerState state)
        {
            if (!armed)
            {
                armed = state.Buttons.Count == 0 && state.LT <= Released && state.RT <= Released &&
                        !Moved(state.LX, state.LY, Released) && !Moved(state.RX, state.RY, Released);
                foreach (var axis in state.ExtraAxes.Values)
                    armed &= !Moved(axis.X, axis.Y, Released);
                return null;
            }

            string candidate = null;
            bool ambiguous = false;
            foreach (string button in state.Buttons)
                Consider(button, ref candidate, ref ambiguous);
            if (state.LT >= MappingEngine.TriggerPressThreshold)
                Consider("LT", ref candidate, ref ambiguous);
            if (state.RT >= MappingEngine.TriggerPressThreshold)
                Consider("RT", ref candidate, ref ambiguous);
            if (Moved(state.LX, state.LY, StickPressed))
                Consider("LeftStick", ref candidate, ref ambiguous);
            if (Moved(state.RX, state.RY, StickPressed))
                Consider("RightStick", ref candidate, ref ambiguous);
            foreach (var axis in state.ExtraAxes)
                if (Moved(axis.Value.X, axis.Value.Y, StickPressed))
                    Consider(axis.Key, ref candidate, ref ambiguous);

            if (!ambiguous)
                return candidate;
            armed = false;
            return null;
        }

        static bool Moved(double x, double y, double threshold)
        {
            return x * x + y * y > threshold * threshold;
        }

        void Consider(string id, ref string candidate, ref bool ambiguous)
        {
            if (!allowed.Contains(id))
                return;
            if (candidate == null)
                candidate = id;
            else if (candidate != id)
                ambiguous = true;
        }
    }

    internal sealed partial class Host
    {
        sealed class CaptureSession
        {
            internal string DeviceId;
            internal IntPtr Window;
            internal long Deadline;
            internal BindingCapture Capture;
            internal Action<string, string> Completed;
        }

        CaptureSession bindingCapture;

        internal bool StartBindingCapture(string deviceId, IntPtr window, IEnumerable<string> controls,
                                          Action<string, string> completed)
        {
            ControllerState state;
            if (closing || systemBlocked || bindingCapture != null || String.IsNullOrEmpty(deviceId) ||
                window == IntPtr.Zero || Win.GetForegroundWindow() != window ||
                !States.TryGetValue(deviceId, out state) || !state.Connected)
                return false;

            CloseOverlay();
            engine.ResetInput();
            var capture = new BindingCapture(controls);
            capture.Sample(state);
            bindingCapture = new CaptureSession {
                DeviceId = deviceId, Window = window, Deadline = clock.ElapsedMilliseconds + 10000,
                Capture = capture, Completed = completed
            };
            messages.SetPollInterval(15);
            return true;
        }

        internal void CancelBindingCapture(IntPtr window)
        {
            if (bindingCapture != null && bindingCapture.Window == window)
                FinishBindingCapture(null, "识别已取消。");
        }

        void FinishBindingCapture(string input, string message)
        {
            var session = bindingCapture;
            if (session == null)
                return;
            bindingCapture = null;
            engine.ResetInput();
            session.Completed(input, message);
        }

        void SampleBindingCapture(IntPtr foreground, long now)
        {
            engine.Ledger.Release();
            var session = bindingCapture;
            if (systemBlocked || foreground != session.Window || !Win.IsWindow(session.Window))
                FinishBindingCapture(null, "识别已取消。");
            else if (now >= session.Deadline)
                FinishBindingCapture(null, "识别超时，可再次点击重试。");
            else
            {
                ControllerState state;
                if (!States.TryGetValue(session.DeviceId, out state) || !state.Connected)
                    FinishBindingCapture(null, "所选手柄已断开。");
                else
                {
                    string result = session.Capture.Sample(state);
                    if (result != null)
                        FinishBindingCapture(result, null);
                }
            }
        }
    }
}
