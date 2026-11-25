using System;
using System.Collections.Generic;
using System.Linq;
namespace Gamepad
{
    public sealed class MappingEngine
    {
        readonly PointerMotion motion;
        public readonly InputLedger Ledger;
        public Action ToggleMode, ToggleModeLock, ToggleKeyboard;
        internal Action<int[]> MappedAction;
        internal Action ModeSwitchReady;
        internal Action<PointerGear> PrecisionChanged;
        internal Action<int> RadialPreview, RadialEnded;
        public Action<string> KeyboardCommand;
        public bool Game { get; private set; }
        public bool Paused { get; private set; }
        public bool Overlay { get; private set; }
        HashSet<string> prior = new HashSet<string>();
        readonly Dictionary<string, long> repeats = new Dictionary<string, long>();
        readonly ModeGesture modeGesture = new ModeGesture();
        bool neutral = true, lt, rt;
        const int PrecisionHoldMs = 150, RadialHoldMs = 150;
        internal const double TriggerPressThreshold = .52;
        long precisionAt = -1, radialAt = -1;
        int radialChoice = -1;
        bool radialVisible;
        PointerGear selectedGear;
        internal PointerGear Gear { get; private set; }
        internal bool RadialEngaged { get { return radialAt >= 0; } }
        internal bool CanMove { get { return !neutral && !Game && !Paused && !Overlay && !RadialEngaged; } }
        public MappingEngine(IInputSink output) : this(output, true)
        {
        }
        internal MappingEngine(IInputSink output, bool mapMotion)
        {
            if (mapMotion)
                motion = new PointerMotion(output);
            Ledger = new InputLedger(output);
        }
        public void Reset()
        {
            selectedGear = PointerGear.Standard;
            ResetInput();
        }
        internal void ResetInput()
        {
            modeGesture.Reset();
            ResetMapping();
        }
        void ResetMapping()
        {
            EndRadial(-1);
            Ledger.Release();
            prior.Clear();
            repeats.Clear();
            neutral = true;
            precisionAt = -1;
            Gear = selectedGear;
            if (motion != null)
                motion.Reset();
            lt = rt = false;
        }
        internal void CancelRadial()
        {
            if (RadialEngaged)
                ResetMapping();
        }
        public void SetContext(bool game, bool paused, bool overlay)
        {
            Game = game;
            Paused = paused;
            Overlay = overlay;
            ResetMapping();
        }
        static void Trigger(HashSet<string> set, string id, double value, ref bool latched)
        {
            latched = value >= (latched ? .32 : TriggerPressThreshold);
            if (latched)
                set.Add(id);
        }
        public void Tick(ControllerState state, Profile p, long now)
        {
            if (!state.Connected)
            {
                Reset();
                return;
            }
            var down = new HashSet<string>(state.Buttons);
            Trigger(down, "LT", state.LT, ref lt);
            Trigger(down, "RT", state.RT, ref rt);
            // Sample the mode chord independently of the active mapping and the overlay's focus.
            var gesture = modeGesture.Sample(p.ModeChord, down, now);
            if (gesture == ModeGestureAction.ReadyToSwitch && ModeSwitchReady != null)
                ModeSwitchReady();
            else if (gesture == ModeGestureAction.ToggleLock && ToggleModeLock != null)
                ToggleModeLock();
            else if (gesture == ModeGestureAction.SwitchMode && ToggleMode != null)
                ToggleMode();

            if (Overlay)
            {
                AddNav(down, state.LX, state.LY, Get(p.Keyboard, "LeftStick"));
                AddNav(down, state.RX, state.RY, Get(p.Keyboard, "RightStick"));
                foreach (var axis in state.ExtraAxes)
                    AddNav(down, axis.Value.X, axis.Value.Y, Get(p.Keyboard, axis.Key));
            }
            double dt = motion == null ? 0 : motion.Elapsed(now);
            if (neutral)
            {
                Ledger.Release();
                neutral = down.Count != 0 ||
                          Math.Max(Math.Max(Math.Abs(state.LX), Math.Abs(state.LY)),
                                   Math.Max(Math.Abs(state.RX), Math.Abs(state.RY))) > p.DeadZone ||
                          state.ExtraAxes.Values.Any(a => Math.Max(Math.Abs(a.X), Math.Abs(a.Y)) > p.DeadZone);
                return;
            }
            if (Paused || Game)
            {
                Ledger.Release();
                if (!Paused &&
                    down.Any(k => !prior.Contains(k) && !modeGesture.Contains(k) && Get(p.Desktop, k) == "Mode"))
                {
                    if (ToggleMode != null)
                        ToggleMode();
                    if (neutral)
                        return;
                }
                prior = down;
                return;
            }
            if (!Overlay)
            {
                if (HandleRadial(Holding(down, p, "RadialMenu"), state, now))
                    return;
                UpdatePrecision(Holding(down, p, "Precision"), now);
            }
            var wanted = new List<int>();
            var mapping = Overlay ? p.Keyboard : p.Desktop;
            foreach (string id in prior.Union(down).ToArray())
            {
                if (Profile.IsAxis(id))
                    continue;
                bool isDown = down.Contains(id), was = prior.Contains(id), deferred = modeGesture.Contains(id);
                bool edge = deferred ? (was && !isDown && !modeGesture.SuppressSingles) : (isDown && !was);
                string action = id.StartsWith("@") ? id.Substring(1) : Get(mapping, id);
                if (!isDown)
                    repeats.Remove(id);
                if (action == "Mode" && edge)
                {
                    if (ToggleMode != null)
                        ToggleMode();
                    if (neutral)
                        return;
                    continue;
                }
                if (Overlay)
                {
                    bool repeat = isDown && !deferred &&
                                  (action == "Delete" || action == "Up" || action == "Down" || action == "Left" ||
                                   action == "Right") &&
                                  Due(id, now, edge, action == "Delete" ? 64 : 120);
                    if ((edge || repeat) && action != "None")
                    {
                        if (KeyboardCommand != null)
                            KeyboardCommand(action);
                        if (neutral)
                            return;
                    }
                    continue;
                }
                if (action == "Keyboard" && edge)
                {
                    if (ToggleKeyboard != null)
                        ToggleKeyboard();
                    return;
                }
                int[] keys = Tokens(action);
                if (keys == null)
                    continue;
                if (edge && MappedAction != null)
                    MappedAction(keys);
                if (deferred)
                {
                    if (edge)
                        Ledger.Tap(keys);
                    continue;
                }
                if (isDown)
                {
                    wanted.AddRange(keys);
                    if (keys.Length == 1 && keys[0] > 0 && Due(id, now, edge, 70))
                        Ledger.Repeat(keys[0]);
                }
            }
            Ledger.Sync(wanted);
            if (motion != null && !Overlay)
                motion.Apply(state, p, dt, Gear);
            prior = down;
        }
        bool Holding(HashSet<string> down, Profile profile, string action)
        {
            return down.Any(id => !modeGesture.Contains(id) && Get(profile.Desktop, id) == action);
        }
        void UpdatePrecision(bool held, long now)
        {
            if (held && precisionAt < 0)
                precisionAt = now;
            if (!held && precisionAt >= 0)
            {
                if (now - precisionAt < PrecisionHoldMs)
                    selectedGear = selectedGear == PointerGear.Standard ? PointerGear.Fine : PointerGear.Standard;
                precisionAt = -1;
            }
            var next = precisionAt >= 0 && now - precisionAt >= PrecisionHoldMs ? PointerGear.Ultra : selectedGear;
            if (Gear != next)
            {
                Gear = next;
                if (PrecisionChanged != null)
                    PrecisionChanged(next);
            }
        }
        bool HandleRadial(bool held, ControllerState state, long now)
        {
            if (!held && !RadialEngaged)
                return false;
            if (!held)
            {
                EndRadial(RadialAction.Select(state, radialChoice) < 0 ? -1 : radialChoice);
                ResetMapping();
                return true;
            }
            if (!RadialEngaged)
            {
                radialAt = now;
                precisionAt = -1;
                Gear = selectedGear;
            }
            Ledger.Release();
            if (now - radialAt >= RadialHoldMs)
            {
                int next = RadialAction.Select(state, radialChoice);
                if ((!radialVisible || radialChoice != next) && RadialPreview != null)
                    RadialPreview(next);
                radialVisible = true;
                radialChoice = next;
            }
            return true;
        }
        void EndRadial(int selected)
        {
            bool shown = radialVisible;
            radialAt = -1;
            radialVisible = false;
            radialChoice = -1;
            if (shown && RadialEnded != null)
                RadialEnded(selected);
        }
        static string Get(Dictionary<string, string> map, string id)
        {
            string action;
            return map.TryGetValue(id, out action) ? action : "None";
        }
        static int[] Tokens(string action)
        {
            switch (action)
            {
            case "MouseLeft":
                return new[] { -1 };
            case "MouseRight":
                return new[] { -2 };
            case "MouseMiddle":
                return new[] { -3 };
            default:
                return KeyBinding.Parse(action);
            }
        }
        bool Due(string id, long now, bool first, int interval)
        {
            long next;
            if (first || !repeats.TryGetValue(id, out next))
            {
                repeats[id] = now + 400;
                return false;
            }
            if (now < next)
                return false;
            repeats[id] = now + interval;
            return true;
        }
        static void AddNav(HashSet<string> down, double x, double y, string binding)
        {
            if (binding != "Navigate" || Math.Max(Math.Abs(x), Math.Abs(y)) < .55)
                return;
            down.Add("@" + (Math.Abs(x) > Math.Abs(y) ? (x > 0 ? "Right" : "Left") : (y > 0 ? "Up" : "Down")));
        }
    }

    internal sealed class PointerMotion
    {
        readonly IInputSink sink;
        double previousTime = -1;
        double mx, my, wx, wy;

        internal PointerMotion(IInputSink sink)
        {
            this.sink = sink;
        }
        internal void Reset()
        {
            previousTime = -1;
            mx = my = wx = wy = 0;
        }
        internal double Elapsed(double now)
        {
            double dt = previousTime < 0 ? 0 : Math.Min(.05, Math.Max(0, (now - previousTime) / 1000));
            previousTime = now;
            return dt;
        }
        internal void Apply(ControllerState state, Profile profile, double dt, PointerGear gear)
        {
            Axis(state.LX, state.LY, "LeftStick", profile, dt, gear);
            Axis(state.RX, state.RY, "RightStick", profile, dt, gear);
            foreach (var axis in state.ExtraAxes)
                Axis(axis.Value.X, axis.Value.Y, axis.Key, profile, dt, gear);
        }
        static int Whole(ref double value)
        {
            int i = (int)value;
            value -= i;
            return i;
        }
        void Axis(double x, double y, string id, Profile p, double dt, PointerGear gear)
        {
            string action;
            if (!p.Desktop.TryGetValue(id, out action) || action == "None")
                return;
            double mag = Math.Sqrt(x * x + y * y), speed = p.Speed, deadZone = p.DeadZone, curve = p.Curve;
            if (action == "Cursor" && gear == PointerGear.Fine)
            {
                speed = Math.Min(420, speed * (420.0 / 940));
                deadZone = .20;
                curve = 1.25;
            }
            else if (action == "Cursor" && gear == PointerGear.Ultra)
            {
                speed = Math.Min(130, speed * (130.0 / 940));
                deadZone = .24;
                curve = 1;
            }
            if (mag <= deadZone)
                return;
            double power = Math.Pow((Math.Min(1, mag) - deadZone) / (1 - deadZone), curve) * dt / mag;
            if (action == "Cursor")
            {
                double v = power * speed;
                mx += x * v;
                my -= y * v;
                int dx = Whole(ref mx), dy = Whole(ref my);
                if (dx != 0 || dy != 0)
                    sink.Move(dx, dy);
            }
            if (action == "Scroll")
            {
                wx += x * power * 9;
                wy += y * power * 9;
                int dx = Whole(ref wx), dy = Whole(ref wy);
                if (dx != 0 || dy != 0)
                    sink.Wheel(dx * 120, dy * 120);
            }
        }
    }
}
