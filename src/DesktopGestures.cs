using System;

namespace Gamepad
{
    internal enum PointerGear
    {
        Standard,
        Fine,
        Ultra
    }

    internal sealed class RadialAction
    {
        internal static readonly RadialAction[] Items = {
            new RadialAction("开始菜单", "Win"),
            new RadialAction("任务视图", "Win+Tab"),
            new RadialAction("显示桌面", "Win+D"),
            new RadialAction("声音输出", AudioFlow.Output),
            new RadialAction("声音输入", AudioFlow.Input),
            new RadialAction("撤销", "Ctrl+Z"),
            new RadialAction("粘贴", "Ctrl+V"),
            new RadialAction("复制", "Ctrl+C"),
        };
        internal static readonly double SectorAngle = 360.0 / Items.Length;

        internal readonly string Label;
        internal readonly int[] Keys;
        internal readonly AudioFlow? Audio;

        RadialAction(string label, string shortcut)
        {
            Label = label;
            Keys = KeyBinding.Parse("Key:" + shortcut);
        }

        RadialAction(string label, AudioFlow flow)
        {
            Label = label;
            Audio = flow;
        }

        internal static int Select(ControllerState state, int previous)
        {
            double left = state.LX * state.LX + state.LY * state.LY;
            double right = state.RX * state.RX + state.RY * state.RY;
            double limit = previous < 0 ? .58 : .38;
            if (Math.Max(left, right) < limit * limit)
                return -1;

            // Zero is straight up, with directions increasing clockwise.
            double x = left >= right ? state.LX : state.RX, y = left >= right ? state.LY : state.RY;
            double angle = (Math.Atan2(x, y) * 180 / Math.PI + 360) % 360;
            if (previous >= 0)
            {
                double distance = Math.Abs(angle - previous * SectorAngle);
                if (Math.Min(distance, 360 - distance) <= SectorAngle / 2 + 7)
                    return previous;
            }
            return (int)((angle + SectorAngle / 2) / SectorAngle) % Items.Length;
        }
    }
}
