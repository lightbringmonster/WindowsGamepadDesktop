using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Gamepad
{
    internal sealed class PointerWorker : IDisposable
    {
        const int IntervalMs = 8;
        readonly object gate = new object();
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        readonly AutoResetEvent bindingChanged = new AutoResetEvent(false);
        readonly Thread thread;
        readonly PointerMotion motion;
        readonly Func<IntPtr> foreground;
        IControllerDevice device;
        ControllerState latest;
        Profile profile;
        string[] radialBindings;
        IntPtr approvedWindow;
        PointerGear gear;

        internal PointerWorker(IInputSink output, Func<IntPtr> foreground)
        {
            motion = new PointerMotion(output);
            this.foreground = foreground;
            thread = new Thread(Run) { IsBackground = true, Name = "Gamepad pointer" };
            thread.Start();
        }

        internal void Bind(IControllerDevice active, ControllerState initialState)
        {
            lock (gate)
            {
                SuspendLocked();
                device = active;
                latest = initialState;
            }
            bindingChanged.Set();
        }

        internal ControllerState Latest()
        {
            lock (gate)
                return latest;
        }

        internal void Allow(Profile currentProfile, IntPtr window, PointerGear nextGear)
        {
            lock (gate)
            {
                if (profile != currentProfile || approvedWindow != window || gear != nextGear)
                    motion.Reset();
                if (profile != currentProfile)
                {
                    var chord = currentProfile.ModeChord.Split('+');
                    radialBindings = currentProfile.Desktop
                        .Where(binding => binding.Value == "RadialMenu" && !chord.Contains(binding.Key))
                        .Select(binding => binding.Key).ToArray();
                }
                // Active settings are read-only. The settings window edits a separate draft.
                profile = currentProfile;
                approvedWindow = window;
                gear = nextGear;
            }
        }

        internal void Suspend()
        {
            lock (gate)
                SuspendLocked();
        }

        void SuspendLocked()
        {
            profile = null;
            motion.Reset();
        }

        void Sample(Stopwatch clock)
        {
            lock (gate)
            {
                // Hold a disconnect until the UI can release held inputs and rearm the device.
                if (device == null || !latest.Connected)
                    return;
                latest = device.Read();
                if (profile == null)
                    return;
                if (!latest.Connected || foreground() != approvedWindow)
                {
                    SuspendLocked();
                    return;
                }
                // The stick can move in the same sample that starts a radial hold.
                foreach (string button in radialBindings)
                {
                    if (latest.Buttons.Contains(button) ||
                        (button == "LT" && latest.LT >= MappingEngine.TriggerPressThreshold) ||
                        (button == "RT" && latest.RT >= MappingEngine.TriggerPressThreshold))
                    {
                        motion.Reset();
                        return;
                    }
                }
                motion.Apply(latest, profile, motion.Elapsed(clock.Elapsed.TotalMilliseconds), gear);
            }
        }

        void Run()
        {
            var signals = new WaitHandle[] { stop, bindingChanged };
            var clock = Stopwatch.StartNew();
            using (var timer = new SampleTimer())
            {
                var timedSignals = new WaitHandle[] { stop, bindingChanged, timer };
                while (!stop.WaitOne(0))
                {
                    bool bound;
                    lock (gate)
                        bound = device != null;
                    if (!bound)
                    {
                        if (WaitHandle.WaitAny(signals) == 0)
                            return;
                        continue;
                    }
                    bool timed = timer.Arm();
                    Sample(clock);
                    if (WaitHandle.WaitAny(timed ? timedSignals : signals, timed ? Timeout.Infinite : IntervalMs) == 0)
                        return;
                }
            }
        }

        public void Dispose()
        {
            stop.Set();
            thread.Join();
            bindingChanged.Dispose();
            stop.Dispose();
        }

        sealed class SampleTimer : WaitHandle
        {
            const uint HighResolution = 0x2, Synchronize = 0x100000, ModifyState = 0x2;

            [DllImport("kernel32.dll", ExactSpelling = true)]
            static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes, IntPtr name, uint flags, uint access);
            [DllImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period,
                                               IntPtr callback, IntPtr state, bool resume);

            internal SampleTimer()
            {
                SafeWaitHandle = CreateWaitableTimerExW(IntPtr.Zero, IntPtr.Zero, HighResolution,
                                                       Synchronize | ModifyState);
            }

            internal bool Arm()
            {
                long dueTime = -IntervalMs * 10000L;
                return !SafeWaitHandle.IsInvalid &&
                       SetWaitableTimer(SafeWaitHandle, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false);
            }
        }
    }
}
