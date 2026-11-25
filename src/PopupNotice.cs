using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Gamepad
{
    internal sealed class PopupNotice : Form
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        readonly PopupTheme theme = PopupTheme.Current();
        readonly Font bodyFont = new Font("Microsoft YaHei UI", 10, FontStyle.Regular);
        readonly Font headingFont = new Font("Microsoft YaHei UI", 15, FontStyle.Bold);

        internal PopupNotice(string message, string title, bool hasOwner)
        {
            message = message.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            Text = "Windows Gamepad Desktop";
            AccessibleName = title;
            ShowInTaskbar = false;
            ShowIcon = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = hasOwner ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            Font = bodyFont;
            BackColor = theme.Surface;
            ForeColor = theme.Text;

            var measured = TextRenderer.MeasureText(message, bodyFont, new Size(388, 0),
                                                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int bodyHeight = Math.Max(72, Math.Min(220, measured.Height + 30));
            ClientSize = new Size(456, bodyHeight + 126);
            var layout = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(22, 15, 22, 17),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));

            layout.Controls.Add(new Label { Text = title, Font = headingFont, Dock = DockStyle.Fill,
                                            ForeColor = theme.Text, TextAlign = ContentAlignment.MiddleLeft,
                                            AutoEllipsis = true, Margin = Padding.Empty },
                                0, 0);

            var detail = new Panel { Dock = DockStyle.Fill, BackColor = theme.Inset, Margin = Padding.Empty,
                                     Padding = new Padding(12, 10, 12, 10) };
            detail.Controls.Add(new TextBox { AccessibleName = "提示详情", Text = message, Dock = DockStyle.Fill,
                                              Multiline = true, BorderStyle = BorderStyle.None, ReadOnly = true,
                                              BackColor = theme.Inset, ForeColor = theme.Text,
                                              ScrollBars = measured.Height > bodyHeight - 20 ? ScrollBars.Vertical
                                                                                             : ScrollBars.None });
            layout.Controls.Add(detail, 0, 1);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                                                Margin = Padding.Empty, Padding = new Padding(0, 12, 0, 0) };
            var accept = new Button { Text = "确定",
                                      DialogResult = DialogResult.OK,
                                      Width = 104,
                                      Height = 36,
                                      FlatStyle = FlatStyle.Flat,
                                      Margin = Padding.Empty,
                                      BackColor = theme.Accent,
                                      ForeColor = theme.AccentText,
                                      UseVisualStyleBackColor = false };
            accept.FlatAppearance.BorderColor = theme.AccentText;
            accept.FlatAppearance.BorderSize = 1;
            buttons.Controls.Add(accept);
            layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout);
            AcceptButton = accept;
            CancelButton = accept;
            ActiveControl = accept;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (theme.Dark)
            {
                const int UseImmersiveDarkMode = 20;
                int enabled = 1;
                DwmSetWindowAttribute(Handle, UseImmersiveDarkMode, ref enabled, sizeof(int));
            }
        }

        internal static void ShowNotice(string message, string title, IWin32Window owner = null)
        {
            using (var dialog = new PopupNotice(message, title, owner != null))
            {
                if (owner != null)
                    dialog.ShowDialog(owner);
                else
                    dialog.ShowDialog();
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                headingFont.Dispose();
                bodyFont.Dispose();
            }
        }
    }
}