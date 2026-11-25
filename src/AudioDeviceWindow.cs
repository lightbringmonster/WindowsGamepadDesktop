using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Gamepad
{
    internal sealed class AudioDeviceWindow : NativeWindow, IDisposable
    {
        const int StylePopup = unchecked((int)0x80000000), StyleLayeredTool = 0x080800a0;
        const uint NoActivate = 0x0010, ShowWindow = 0x0040, HideWindow = 0x0080, KeepPosition = 0x0007;
        const int MaxRows = 5;
        AudioEndpoint[] devices;
        int selection, first;
        string title, legend;
        PopupTheme theme;
        Bitmap frame;
        Font heading, itemFont, hintFont;
        float scale;
        internal bool Visible { get; private set; }
        internal AudioEndpoint Selection { get { return devices.Length == 0 ? null : devices[selection]; } }

        internal void Present(IntPtr foreground, AudioFlow flow, AudioEndpoint[] endpoints, Profile profile)
        {
            devices = endpoints;
            selection = Math.Max(0, Array.FindIndex(devices, d => d.IsDefault));
            first = Math.Max(0, selection - MaxRows + 1);
            title = flow == AudioFlow.Input ? "声音输入" : "声音输出";
            var confirm = String.Join("/", profile.Keyboard.Where(x => x.Value == "Select" || x.Value == "Done")
                                         .Select(x => x.Key));
            var cancel = String.Join("/", profile.Keyboard.Where(x => x.Value == "Close").Select(x => x.Key));
            legend = "上下选择" + (confirm.Length == 0 ? "" : "       " + confirm + "  确认") +
                     (cancel.Length == 0 ? "" : "       " + cancel + "  返回");
            var work = Screen.FromHandle(foreground).WorkingArea;
            if (work.Width < 120 || work.Height < 120)
                return;
            scale = Math.Max(.68f, Math.Min(1.7f, work.Height / 940f));
            scale = Math.Min(scale, (work.Height - 16f) / (116 + 54 * Rows));
            int width = Math.Min(Scale(550), work.Width - 16), height = Scale(116 + 54 * Rows);
            var bounds = new Rectangle(work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2,
                                       width, height);
            if (Handle == IntPtr.Zero)
            {
                CreateHandle(new CreateParams { Caption = "声音设备选择", Style = StylePopup,
                                                ExStyle = StyleLayeredTool, Width = 1, Height = 1 });
                Win.SetLayeredWindowAttributes(Handle, 0, 255, 2);
            }
            if (frame != null)
                frame.Dispose();
            frame = new Bitmap(width, height);
            DisposeFonts();
            heading = new Font("Microsoft YaHei UI", Scale(26), FontStyle.Bold, GraphicsUnit.Pixel);
            itemFont = new Font("Microsoft YaHei UI", Scale(18), FontStyle.Regular, GraphicsUnit.Pixel);
            hintFont = new Font("Microsoft YaHei UI", Scale(14), FontStyle.Regular, GraphicsUnit.Pixel);
            var region = Win.CreateRoundRectRgn(0, 0, width + 1, height + 1, Scale(26), Scale(26));
            if (region != IntPtr.Zero && Win.SetWindowRgn(Handle, region, false) == 0)
                Win.DeleteObject(region);
            theme = PopupTheme.Current();
            Render();
            Visible = Win.SetWindowPos(Handle, (IntPtr)(-1), bounds.X, bounds.Y, width, height,
                                       NoActivate | ShowWindow);
            Win.InvalidateRect(Handle, IntPtr.Zero, false);
        }

        int Rows { get { return Math.Max(1, Math.Min(devices.Length, MaxRows)); } }
        int Scale(float value) { return (int)Math.Round(value * scale); }

        internal void Move(int amount)
        {
            if (devices.Length == 0)
                return;
            int next = Math.Max(0, Math.Min(devices.Length - 1, selection + amount));
            if (next == selection)
                return;
            selection = next;
            first = Math.Min(selection, Math.Max(first, selection - Rows + 1));
            Render();
            Win.InvalidateRect(Handle, IntPtr.Zero, false);
        }

        void Render()
        {
            using (var g = Graphics.FromImage(frame))
            using (var text = new SolidBrush(theme.Text))
            using (var secondary = new SolidBrush(theme.Secondary))
            using (var itemBackground = new SolidBrush(theme.Inset))
            using (var chosen = new SolidBrush(theme.Accent))
            using (var chosenText = new SolidBrush(theme.AccentText))
            using (var border = new Pen(theme.Border))
            using (var layout = new StringFormat { LineAlignment = StringAlignment.Center,
                                                   Trimming = StringTrimming.EllipsisCharacter,
                                                   FormatFlags = StringFormatFlags.NoWrap })
            {
                g.Clear(theme.Surface);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                int inset = Scale(24), width = frame.Width - 2 * inset, rowHeight = Scale(46);
                g.DrawString(title, heading, text, new RectangleF(inset, Scale(15), width, Scale(42)), layout);
                if (devices.Length == 0)
                    g.DrawString("没有可用设备", itemFont, secondary,
                                 new RectangleF(inset, Scale(65), width, rowHeight), layout);
                for (int i = first; i < devices.Length && i < first + Rows; i++)
                {
                    var row = new Rectangle(inset, Scale(64 + (i - first) * 54), width, rowHeight);
                    bool selected = i == selection;
                    g.FillRectangle(selected ? chosen : itemBackground, row);
                    if (selected)
                        g.DrawRectangle(border, row);
                    int labelWidth = row.Width - Scale(devices[i].IsDefault ? 105 : 24);
                    g.DrawString(devices[i].Name, itemFont, selected ? chosenText : text,
                                 new RectangleF(row.X + Scale(13), row.Y, labelWidth, row.Height), layout);
                    if (devices[i].IsDefault)
                        g.DrawString("系统默认", hintFont, selected ? chosenText : secondary,
                                     new RectangleF(row.Right - Scale(75), row.Y, Scale(70), row.Height), layout);
                }
                if (devices.Length > Rows)
                    g.DrawString(String.Format("{0} / {1}", selection + 1, devices.Length), hintFont, secondary,
                                 new RectangleF(frame.Width - inset - Scale(55), Scale(15), Scale(55), Scale(42)), layout);
                g.DrawString(legend, hintFont, secondary,
                             new RectangleF(inset, frame.Height - Scale(47), width, Scale(35)), layout);
            }
        }

        void DisposeFonts()
        {
            if (heading == null)
                return;
            heading.Dispose();
            itemFont.Dispose();
            hintFont.Dispose();
        }

        internal void Hide()
        {
            if (!Visible)
                return;
            Win.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, NoActivate | KeepPosition | HideWindow);
            Visible = false;
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
                var dc = Win.BeginPaint(message.HWnd, out paint);
                try
                {
                    if (dc != IntPtr.Zero && frame != null)
                        using (var graphics = Graphics.FromHdc(dc))
                            graphics.DrawImageUnscaled(frame, 0, 0);
                }
                finally
                {
                    Win.EndPaint(message.HWnd, ref paint);
                }
                message.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
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
