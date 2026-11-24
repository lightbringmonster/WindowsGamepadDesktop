using System;
using System.Collections.Generic;
using System.Linq;
namespace Gamepad
{
    public sealed class MappingEngine
    {
        readonly IInputSink sink;
        public readonly InputLedger Ledger;
        public Action ToggleMode, ToggleKeyboard, Interaction;
        public Action<string> KeyboardCommand;
        public bool Game { get; private set; }
        public bool Paused { get; private set; }
        public bool Overlay { get; private set; }
        HashSet<string> prior = new HashSet<string>();
        readonly Dictionary<string, long> repeats = new Dictionary<string, long>();
        bool neutral = true, chordConsumed, lt, rt;
        long chordAt = -1, previousTime = -1;
        double mx, my, wx, wy;
        public MappingEngine(IInputSink output)
        {
            sink = output;
            Ledger = new InputLedger(output);
        }
        public void Reset()
        {
            Ledger.Release();
            prior.Clear();
            repeats.Clear();
            neutral = true;
            chordConsumed = false;
            chordAt = -1;
            previousTime = -1;
            mx = my = wx = wy = 0;
            lt = rt = false;
        }
        public void SetContext(bool game, bool paused, bool overlay)
        {
            Game = game;
            Paused = paused;
            Overlay = overlay;
            Reset();
        }
        static void Trigger(HashSet<string> set, string id, double value, ref bool latched)
        {
            latched = value >= (latched ? .32 : .52);
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
            if (Overlay)
            {
                AddNav(down, state.LX, state.LY, Get(p.Keyboard, "LeftStick"));
                AddNav(down, state.RX, state.RY, Get(p.Keyboard, "RightStick"));
                foreach (var axis in state.ExtraAxes)
                    AddNav(down, axis.Value.X, axis.Value.Y, Get(p.Keyboard, axis.Key));
            }
            double dt = previousTime < 0 ? 0 : Math.Min(.05, Math.Max(0, (now - previousTime) / 1000.0));
            previousTime = now;
            if (neutral)
            {
                Ledger.Release();
                neutral = down.Count != 0 ||
                          Math.Max(Math.Max(Math.Abs(state.LX), Math.Abs(state.LY)),
                                   Math.Max(Math.Abs(state.RX), Math.Abs(state.RY))) > p.DeadZone ||
                          state.ExtraAxes.Values.Any(a => Math.Max(Math.Abs(a.X), Math.Abs(a.Y)) > p.DeadZone);
                return;
            }
            var chord = p.ModeChord.Split('+');
            bool both = chord.All(down.Contains);
            if (both)
            {
                if (chordAt < 0)
                    chordAt = now;
                if (!chordConsumed && now - chordAt >= 1000)
                {
                    chordConsumed = true;
                    if (ToggleMode != null)
                        ToggleMode();
                    return;
                }
            }
            bool overlapped = chordAt >= 0 || chordConsumed;
            if (!both && chordAt >= 0)
            {
                chordConsumed = true;
                chordAt = -1;
            }
            if (Paused || Game)
            {
                Ledger.Release();
                if (!Paused &&
                    down.Any(k => !prior.Contains(k) && !chord.Contains(k) && Get(p.Desktop, k) == "Mode"))
                {
                    if (ToggleMode != null)
                        ToggleMode();
                    return;
                }
                prior = down;
                if (!down.Overlaps(chord))
                    chordConsumed = false;
                return;
            }
            var wanted = new List<int>();
            bool precise = false;
            var mapping = Overlay ? p.Keyboard : p.Desktop;
            foreach (string id in prior.Union(down).ToArray())
            {
                if (Profile.IsAxis(id))
                    continue;
                bool isDown = down.Contains(id), was = prior.Contains(id), deferred = chord.Contains(id);
                bool edge = deferred ? (was && !isDown && !overlapped) : (isDown && !was);
                string action = id.StartsWith("@") ? id.Substring(1) : Get(mapping, id);
                if (!isDown)
                    repeats.Remove(id);
                if (action == "Mode" && edge)
                {
                    if (ToggleMode != null)
                        ToggleMode();
                    return;
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
                if (action == "Precision")
                {
                    precise |= isDown && !deferred;
                    continue;
                }
                int[] keys = Tokens(action);
                if (keys == null)
                    continue;
                // Escape dismisses UI even when the same editable field keeps focus.
                if (edge && Interaction != null && keys[keys.Length - 1] != (int)System.Windows.Forms.Keys.Escape)
                    Interaction();
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
            if (!Overlay)
            {
                Axis(state.LX, state.LY, Get(mapping, "LeftStick"), p, dt, precise);
                Axis(state.RX, state.RY, Get(mapping, "RightStick"), p, dt, precise);
                foreach (var axis in state.ExtraAxes)
                    Axis(axis.Value.X, axis.Value.Y, Get(mapping, axis.Key), p, dt, precise);
            }
            prior = down;
            if (!down.Overlaps(chord))
                chordConsumed = false;
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
        static int Whole(ref double value)
        {
            int i = (int)value;
            value -= i;
            return i;
        }
        void Axis(double x, double y, string action, Profile p, double dt, bool precise)
        {
            double mag = Math.Sqrt(x * x + y * y);
            if (mag <= p.DeadZone || action == "None")
                return;
            double power = Math.Pow((Math.Min(1, mag) - p.DeadZone) / (1 - p.DeadZone), p.Curve) * dt / mag;
            if (action == "Cursor")
            {
                double v = power * p.Speed * (precise ? .22 : 1);
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