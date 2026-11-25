using System;
using System.Threading;

namespace Gamepad
{
    internal static partial class Tests
    {
        sealed class MotionCounter : IInputSink
        {
            internal int Moves;
            internal int PixelsX;
            public bool Key(int vk, bool down) { return true; }
            public bool Mouse(int button, bool down) { return true; }
            public void Text(string text) { }
            public void Wheel(int horizontal, int vertical) { }
            public void Move(int x, int y)
            {
                Interlocked.Add(ref PixelsX, x);
                Interlocked.Increment(ref Moves);
            }
        }

        static void PointerWorkerChecks()
        {
            var moved = new ControllerState { Connected = true, LX = 1 };
            var pad = new SimDevice { State = moved };
            var sink = new MotionCounter();
            var profile = new Profile();
            int window = 1;
            using (var worker = new PointerWorker(sink, () => (IntPtr)Volatile.Read(ref window)))
            {
                worker.Bind(pad, pad.State);
                worker.Allow(profile, (IntPtr)1, PointerGear.Standard);
                Check(SpinWait.SpinUntil(() => Volatile.Read(ref sink.Moves) >= 4, 1500),
                      "pointer worker emits motion without a UI message pump");

                // Wait until the worker has seen the unapproved foreground.
                Volatile.Write(ref window, 2);
                var next = new ControllerState { Connected = true, LX = .9 };
                Volatile.Write(ref pad.State, next);
                int foregroundSeen = SpinWait.SpinUntil(() => worker.Latest() == next, 1500)
                                     ? Volatile.Read(ref sink.Moves)
                                     : -1;
                Volatile.Write(ref window, 1);
                Thread.Sleep(70);
                Check(foregroundSeen > 0 && Volatile.Read(ref sink.Moves) == foregroundSeen,
                      "foreground change revokes motion until reapproved");

                worker.Allow(profile, (IntPtr)1, PointerGear.Standard);
                Check(SpinWait.SpinUntil(() => Volatile.Read(ref sink.Moves) > foregroundSeen, 1500),
                      "current foreground approval restores motion");
                worker.Suspend();
                int paused = Volatile.Read(ref sink.Moves);
                Thread.Sleep(70);
                Check(Volatile.Read(ref sink.Moves) == paused, "suspension stops motion synchronously");

                worker.Allow(profile, (IntPtr)1, PointerGear.Standard);
                Volatile.Write(ref pad.State, new ControllerState());
                Check(SpinWait.SpinUntil(() => !worker.Latest().Connected, 1500), "worker publishes disconnection");
                int disconnected = Volatile.Read(ref sink.Moves);
                Volatile.Write(ref pad.State, moved);
                Thread.Sleep(70);
                Check(!worker.Latest().Connected && Volatile.Read(ref sink.Moves) == disconnected,
                      "rapid reconnection waits for the host to rearm the controller");
            }
        }
    }
}
