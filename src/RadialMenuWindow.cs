using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Gamepad
{
    internal sealed class RadialMenuWindow : NativeWindow, IDisposable
    {
        const int Popup = unchecked((int)0x80000000);
        const int LayeredToolWindow = 0x080800a0;
        const uint NoActivate = 0x0010, KeepBounds = 0x0003, KeepZOrder = 0x0004;
        const uint ShowWindow = 0x0040, HideWindow = 0x0080;
        static readonly IntPtr Topmost = new IntPtr(-1);
        [DllImport("gdi32.dll")]
        static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);

        int diameter, selected = -1;
        Font labelFont, titleFont, hintFont;
        PopupTheme theme;
        Bitmap frame;
        internal bool Visible { get; private set; }

        internal void Present(IntPtr target, int selection)
        {
            var work = Screen.FromHandle(target).WorkingArea;
            int size = Math.Min(Math.Min(work.Width, work.Height) - 12, Math.Max(342, (int)(work.Height * .44)));
            theme = PopupTheme.Current();
            if (Handle == IntPtr.Zero)
            {
                CreateHandle(new CreateParams { Caption = "手柄快捷菜单", Style = Popup, ExStyle = LayeredToolWindow,
                                                Width = 1, Height = 1 });
                Win.SetLayeredWindowAttributes(Handle, 0, 255, 2);
            }
            if (diameter != size)
                ResizeMenu(size);
            selected = selection;
            DrawFrame();
            Visible = Win.SetWindowPos(Handle, Topmost, work.Left + (work.Width - size) / 2,
                                       work.Top + (work.Height - size) / 2, size, size, NoActivate | ShowWindow);
            Win.InvalidateRect(Handle, IntPtr.Zero, false);
        }

        internal void Hide()
        {
            Win.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, NoActivate | KeepBounds | KeepZOrder | HideWindow);
            Visible = false;
        }

        internal void SelectAction(int selection)
        {
            if (selected == selection)
                return;
            selected = selection;
            DrawFrame();
            Win.InvalidateRect(Handle, IntPtr.Zero, false);
        }

        void ResizeMenu(int size)
        {
            diameter = size;
            var region = CreateEllipticRgn(0, 0, size + 1, size + 1);
            if (region != IntPtr.Zero && Win.SetWindowRgn(Handle, region, false) == 0)
                Win.DeleteObject(region);
            if (frame != null)
                frame.Dispose();
            frame = new Bitmap(size, size);
            DisposeFonts();
            labelFont = new Font("Microsoft YaHei UI", Math.Max(11, size * .036f), FontStyle.Bold, GraphicsUnit.Pixel);
            titleFont = new Font("Microsoft YaHei UI", Math.Max(14, size * .052f), FontStyle.Bold, GraphicsUnit.Pixel);
            hintFont = new Font("Microsoft YaHei UI", Math.Max(10, size * .029f), FontStyle.Regular, GraphicsUnit.Pixel);
        }

        void DrawFrame()
        {
            float center = diameter / 2f;
            float outer = center * .953f, inner = center * .44f;
            var outerCircle = RingBounds(center, outer);
            var innerCircle = RingBounds(center, inner);
            float step = (float)RadialAction.SectorAngle;
            using (var g = Graphics.FromImage(frame))
            using (var normal = new SolidBrush(theme.Inset))
            using (var accent = new SolidBrush(theme.Accent))
            using (var text = new SolidBrush(theme.Text))
            using (var highlightedText = new SolidBrush(theme.AccentText))
            using (var secondary = new SolidBrush(theme.Secondary))
            using (var border = new Pen(theme.Border))
            using (var centered = new StringFormat { Alignment = StringAlignment.Center,
                                                      LineAlignment = StringAlignment.Center,
                                                      FormatFlags = StringFormatFlags.NoWrap })
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(theme.Surface);
                for (int i = 0; i < RadialAction.Items.Length; i++)
                {
                    float angle = -90 + i * step;
                    float start = angle - step / 2 + 1.1f, sweep = step - 2.2f;
                    using (var sector = new GraphicsPath())
                    {
                        sector.AddArc(outerCircle, start, sweep);
                        sector.AddArc(innerCircle, start + sweep, -sweep);
                        sector.CloseFigure();
                        g.FillPath(i == selected ? accent : normal, sector);
                        g.DrawPath(border, sector);
                    }
                    float radius = (inner + outer) / 2;
                    double radians = angle * Math.PI / 180;
                    var label = new PointF(center + radius * (float)Math.Cos(radians),
                                           center + radius * (float)Math.Sin(radians));
                    g.DrawString(RadialAction.Items[i].Label, labelFont, i == selected ? highlightedText : text,
                                 label, centered);
                }
                g.DrawEllipse(border, RingBounds(center, center - 1.5f));
                string title = selected < 0 ? "常用操作" : RadialAction.Items[selected].Label;
                string hint = selected < 0 ? "拨动摇杆选择" : "松开执行 · 回中取消";
                g.DrawString(title, titleFont, text, new PointF(center, center - diameter * .036f), centered);
                g.DrawString(hint, hintFont, secondary, new PointF(center, center + diameter * .044f), centered);
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x21)
            {
                message.Result = (IntPtr)3;
                return;
            }
            if (message.Msg == 0x0f)
            {
                Win.PaintInfo paint;
                var dc = Win.BeginPaint(Handle, out paint);
                try
                {
                    if (dc != IntPtr.Zero && frame != null)
                        using (var graphics = Graphics.FromHdc(dc))
                            graphics.DrawImageUnscaled(frame, 0, 0);
                }
                finally
                {
                    Win.EndPaint(Handle, ref paint);
                }
                message.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
        }

        static RectangleF RingBounds(float center, float radius)
        {
            return new RectangleF(center - radius, center - radius, radius * 2, radius * 2);
        }

        void DisposeFonts()
        {
            if (labelFont == null)
                return;
            labelFont.Dispose();
            titleFont.Dispose();
            hintFont.Dispose();
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
                DestroyHandle();
            if (frame != null)
                frame.Dispose();
            DisposeFonts();
        }
    }
}
