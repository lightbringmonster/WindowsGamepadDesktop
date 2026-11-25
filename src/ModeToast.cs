using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Gamepad
{
    internal sealed class ModeToast : NativeWindow, IDisposable
    {
        const int Popup = unchecked((int)0x80000000);
        const int LayeredToolWindow = 0x080800a0;
        static readonly IntPtr Topmost = new IntPtr(-1);
        const uint NoActivate = 0x0010;
        const uint KeepBounds = 0x0003;
        const uint KeepZOrder = 0x0004;
        const uint ShowWindow = 0x0040;
        const uint HideWindow = 0x0080;

        [StructLayout(LayoutKind.Sequential)]
        struct PaintInfo
        {
            internal IntPtr Dc;
            internal int Erase;
            internal int Left, Top, Right, Bottom;
            internal int Restore, Incremental;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            internal byte[] Reserved;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool SetWindowText(IntPtr window, string text);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr window, IntPtr order, int x, int y, int width, int height,
                                        uint flags);
        [DllImport("user32.dll")]
        static extern bool SetLayeredWindowAttributes(IntPtr window, uint color, byte alpha, uint flags);
        [DllImport("user32.dll")]
        static extern bool InvalidateRect(IntPtr window, IntPtr bounds, bool erase);
        [DllImport("user32.dll")]
        static extern IntPtr BeginPaint(IntPtr window, out PaintInfo paint);
        [DllImport("user32.dll")]
        static extern bool EndPaint(IntPtr window, ref PaintInfo paint);
        [DllImport("gdi32.dll")]
        static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);
        [DllImport("user32.dll")]
        static extern int SetWindowRgn(IntPtr window, IntPtr region, bool redraw);
        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr handle);

        readonly Timer timeout = new Timer { Interval = 1250 };
        Bitmap frame;
        Font headlineFont, detailFont;
        float scale = 1;

        public ModeToast()
        {
            timeout.Tick += (sender, args) =>
            {
                timeout.Stop();
                if (Handle != IntPtr.Zero)
                    SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                                 NoActivate | KeepBounds | KeepZOrder | HideWindow);
            };
        }

        public void Present(string text)
        {
            int divider = text.IndexOf(" · ", StringComparison.Ordinal);
            string headline = divider < 0 ? text : text.Substring(0, divider);
            string detail = divider < 0 ? "" : text.Substring(divider + 3);
            var work = Screen.FromHandle(Win.GetForegroundWindow()).WorkingArea;
            scale = Math.Max(.78f, Math.Min(2.2f, work.Height / 1040f));
            int width = Math.Min(Scale(360), Math.Max(1, work.Width - Scale(24)));
            int height = Scale(detail.Length == 0 ? 60 : 82);

            if (Handle == IntPtr.Zero)
            {
                CreateHandle(new CreateParams { Caption = text, Style = Popup, ExStyle = LayeredToolWindow,
                                                Width = 1, Height = 1 });
                SetLayeredWindowAttributes(Handle, 0, 255, 2);
            }
            else
                SetWindowText(Handle, text);

            RebuildFonts();
            if (frame == null || frame.Width != width || frame.Height != height)
            {
                if (frame != null)
                    frame.Dispose();
                frame = new Bitmap(width, height);
            }
            DrawFrame(headline, detail, PopupTheme.Current());

            var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, Scale(26), Scale(26));
            if (region != IntPtr.Zero && SetWindowRgn(Handle, region, false) == 0)
                DeleteObject(region);

            SetWindowPos(Handle, Topmost, work.Left + (work.Width - width) / 2, work.Top + Scale(23), width,
                         height, NoActivate | ShowWindow);
            InvalidateRect(Handle, IntPtr.Zero, false);
            timeout.Stop();
            timeout.Start();
        }

        int Scale(int value)
        {
            return (int)Math.Round(value * scale);
        }

        void RebuildFonts()
        {
            int headlineSize = Scale(23), detailSize = Scale(16);
            if (headlineFont != null && headlineFont.Size == headlineSize && detailFont.Size == detailSize)
                return;
            if (headlineFont != null)
                headlineFont.Dispose();
            if (detailFont != null)
                detailFont.Dispose();
            headlineFont = new Font("Microsoft YaHei UI", headlineSize, FontStyle.Bold, GraphicsUnit.Pixel);
            detailFont = new Font("Microsoft YaHei UI", detailSize, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        static GraphicsPath Rounded(Rectangle bounds, int radius)
        {
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();
            if (diameter <= 1)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        void DrawFrame(string headline, string detail, PopupTheme theme)
        {
            using (var g = Graphics.FromImage(frame))
            {
                g.Clear(theme.Surface);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                using (var outline = Rounded(new Rectangle(0, 0, frame.Width - 1, frame.Height - 1),
                                             Scale(13))) using (var border = new Pen(theme.Border))
                    g.DrawPath(border, outline);

                int inset = Scale(18);
                var accent = new Rectangle(inset, inset, Math.Max(3, Scale(4)), frame.Height - inset * 2);
                using (var shape = Rounded(accent, Scale(3))) using (var brush = new SolidBrush(theme.Accent))
                    g.FillPath(brush, shape);

                int textX = Scale(36);
                int width = Math.Max(1, frame.Width - textX - inset);
                using (var layout = new StringFormat {
                    FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center,
                    Trimming = StringTrimming.EllipsisCharacter
                }) using (var primary = new SolidBrush(theme.Text))
                {
                    if (detail.Length == 0)
                    {
                        g.DrawString(headline, headlineFont, primary,
                                     new RectangleF(textX, 0, width, frame.Height), layout);
                        return;
                    }

                    g.DrawString(headline, headlineFont, primary,
                                 new RectangleF(textX, Scale(12), width, Scale(29)), layout);
                    using (var secondary = new SolidBrush(theme.Secondary)) g.DrawString(
                        detail, detailFont, secondary, new RectangleF(textX, Scale(43), width, Scale(23)), layout);
                }
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x000F)
            {
                PaintInfo paint;
                var dc = BeginPaint(message.HWnd, out paint);
                try
                {
                    if (dc != IntPtr.Zero && frame != null)
                        using (var g = Graphics.FromHdc(dc)) g.DrawImageUnscaled(frame, 0, 0);
                }
                finally
                {
                    EndPaint(message.HWnd, ref paint);
                }
                message.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            timeout.Dispose();
            if (Handle != IntPtr.Zero)
                DestroyHandle();
            if (frame != null)
                frame.Dispose();
            if (headlineFont != null)
                headlineFont.Dispose();
            if (detailFont != null)
                detailFont.Dispose();
        }
    }
}