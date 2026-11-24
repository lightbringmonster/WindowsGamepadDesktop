using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
namespace Gamepad
{
    internal sealed class KeyboardModel
    {
        internal static readonly string[] Layouts = { "English", "拼音", "数字符号", "系统 IME", "编辑" };
        static readonly string[] Letters = { "q w e r t y u i o p", "a s d f g h j k l", "z x c v b n m" };
        static readonly string[] Symbols = { "1 2 3 4 5 6 7 8 9 0", "! @ # $ % ^ & * ( )", "- _ = + [ ] { } / \\",
                                             "， 。 ？ 、 . , ? : ; '", "\" ~ ` < > | · …" };
        static readonly string[] Edit = { "← → ↑ ↓ Home End", "选左 选右 选上 选下 全选",
                                          "复制 剪切 粘贴 撤销 重做", "Tab Shift+Tab 换行 退格 Delete",
                                          "切换输入法 上页 下页" };
        readonly Func<PinyinDictionary> dictionary;
        public Action<string> Insert, Key;
        public Action Close;
        public int Layout, Row, Col, Page;
        public bool Upper;
        public string Composition = "";
        const int CandidatesPerPage = 5;
        public List<PinyinCandidate> Candidates = new List<PinyinCandidate>();
        public int CandidatePages
        {
            get {
                return Math.Max(1, (Candidates.Count + CandidatesPerPage - 1) / CandidatesPerPage);
            }
        }
        public bool HasComposition
        {
            get {
                return Layout == 1 && Composition.Length > 0;
            }
        }
        public string PreviewCandidate
        {
            get {
                if (!HasComposition || Candidates.Count == 0)
                    return "";
                int first = Page * CandidatesPerPage;
                int selected =
                    Row == 0 && Col < Math.Min(CandidatesPerPage, Candidates.Count - first) ? first + Col : first;
                return CandidateLabel(Candidates[selected]);
            }
        }
        public List<string[]> Rows = new List<string[]>();
        public KeyboardModel(Func<PinyinDictionary> getDictionary)
        {
            dictionary = getDictionary;
        }
        public void Open(string layout)
        {
            Layout = Math.Max(0, Array.IndexOf(Layouts, layout));
            Upper = false;
            ResetText();
            Row = Col = 0;
            Build();
        }
        public void ResetText()
        {
            Composition = "";
            Page = 0;
            Candidates.Clear();
        }
        void Query()
        {
            Candidates = dictionary().Find(Composition);
            Page = 0;
        }
        public void Build()
        {
            Rows.Clear();
            if (HasComposition)
            {
                var labels = Candidates.Skip(Page * CandidatesPerPage)
                                 .Take(CandidatesPerPage)
                                 .Select(CandidateLabel)
                                 .ToList();
                if (labels.Count == 0)
                    labels.Add("继续输入拼音");
                if (CandidatePages > 1)
                    labels.AddRange(new[] { "‹", "›" });
                Rows.Add(labels.ToArray());
            }
            if (Layout == 3)
                Rows.Add("1 2 3 4 5 6 7 8 9 0".Split(' '));
            foreach (var r in Layout == 4 ? Edit : Layout == 2 ? Symbols : Letters)
                Rows.Add((Layout == 0 && Upper ? r.ToUpperInvariant() : r).Split(' '));
            Rows.Add((Layout == 0   ? "大小写 空格 退格 完成"
                      : Layout == 3 ? "上页 下页 空格 退格 切换输入法 完成"
                      : Layout == 1 ? "隔音符 空格 退格 完成"
                                    : "空格 退格 完成")
                         .Split(' '));
            Row = Math.Max(0, Math.Min(Row, Rows.Count - 1));
            Col = Math.Max(0, Math.Min(Col, Rows[Row].Length - 1));
        }
        public void Command(string action)
        {
            switch (action)
            {
            case "Close":
                Close();
                return;
            case "PreviousLayout":
            case "NextLayout":
                Open(Layouts[(Layout + (action == "NextLayout" ? 1 : 4)) % 5]);
                return;
            case "Up":
            case "Down":
                int old = Rows[Row].Length;
                Row = (Row + (action == "Down" ? 1 : Rows.Count - 1)) % Rows.Count;
                Col = Math.Min(Rows[Row].Length - 1, (int)((Col + .5) * Rows[Row].Length / old));
                return;
            case "Left":
            case "Right":
                int n = Rows[Row].Length;
                Col = (Col + (action == "Right" ? 1 : n - 1)) % n;
                return;
            case "Delete":
                Activate("退格");
                break;
            case "Space":
                Activate("空格");
                break;
            case "Done":
                Activate("完成");
                break;
            case "Select":
                if (HasComposition && Row == 0)
                {
                    int first = Page * CandidatesPerPage;
                    if (Col < Math.Min(CandidatesPerPage, Candidates.Count - first))
                        Commit(Candidates[first + Col]);
                    else if (CandidatePages > 1)
                        Activate(Rows[0][Col]);
                    break;
                }
                Activate(Rows[Row][Col]);
                break;
            }
            Build();
        }
        string CandidateLabel(PinyinCandidate candidate)
        {
            return candidate.Text + (candidate.Consumed < Composition.Length ? " →" : "");
        }
        bool Commit(PinyinCandidate candidate)
        {
            Insert(candidate.Text);
            Composition = Composition.Substring(candidate.Consumed);
            Query();
            Row = Col = 0;
            return Composition.Length == 0;
        }
        void AppendReading(char letter)
        {
            if (Composition.Length >= 64)
                return;
            bool addedRow = Composition.Length == 0;
            Composition += letter;
            Query();
            if (addedRow)
                Row++;
        }
        static readonly Dictionary<string, string> Commands =
            new Dictionary<string, string> { { "←", "Left" },
                                             { "→", "Right" },
                                             { "↑", "Up" },
                                             { "↓", "Down" },
                                             { "选左", "Shift+Left" },
                                             { "选右", "Shift+Right" },
                                             { "选上", "Shift+Up" },
                                             { "选下", "Shift+Down" },
                                             { "全选", "Ctrl+A" },
                                             { "复制", "Ctrl+C" },
                                             { "剪切", "Ctrl+X" },
                                             { "粘贴", "Ctrl+V" },
                                             { "撤销", "Ctrl+Z" },
                                             { "重做", "Ctrl+Y" },
                                             { "上页", "PageUp" },
                                             { "下页", "PageDown" },
                                             { "切换输入法", "Win+Space" },
                                             { "换行", "Enter" } };
        void Activate(string value)
        {
            if (value == "‹" || value == "›")
            {
                int pages = CandidatePages;
                Page = (Page + (value == "›" ? 1 : pages - 1)) % pages;
                Col = Math.Min(CandidatesPerPage, Candidates.Count - Page * CandidatesPerPage) +
                      (value == "›" ? 1 : 0);
                return;
            }
            if (value == "完成")
            {
                if (HasComposition && (Candidates.Count == 0 || !Commit(Candidates[Page * CandidatesPerPage])))
                    return;
                Key("Enter");
                Close();
                return;
            }
            if (value == "退格")
            {
                if (Composition.Length > 0)
                {
                    Composition = Composition.Substring(0, Composition.Length - 1);
                    Query();
                }
                else
                    Key("Back");
                return;
            }
            if (value == "空格")
            {
                if (Composition.Length > 0)
                {
                    if (Candidates.Count > 0)
                        Commit(Candidates[Page * CandidatesPerPage]);
                }
                else
                    Key("Space");
                return;
            }
            if (value == "隔音符")
            {
                if (HasComposition && !Composition.EndsWith("'", StringComparison.Ordinal))
                    AppendReading('\'');
                return;
            }
            if (value == "大小写")
            {
                Upper = !Upper;
                return;
            }
            string command;
            if (Commands.TryGetValue(value, out command))
            {
                Key(command);
                return;
            }
            if (Layout == 4)
            {
                Key(value);
                return;
            }
            if (Layout == 1 && value.Length == 1 && value[0] >= 'a' && value[0] <= 'z')
            {
                AppendReading(value[0]);
                return;
            }
            if (Layout == 3)
            {
                Key(Char.IsDigit(value[0]) ? "D" + value : value.ToUpperInvariant());
                return;
            }
            Insert(value);
        }
    }

    internal sealed class KeyboardWindow : Form
    {
        internal const string DefaultLegend =
            "方向键  选择       B  输入       A  返回       X  删除       Y  空格       LB / RB  布局       RT  完成";
        internal string Legend = DefaultLegend;
        internal static string MakeLegend(Profile p)
        {
            var parts = new List<string>();
            foreach (var pair in new[] { new[] { "Select", "输入" }, new[] { "Close", "返回" },
                                         new[] { "Delete", "删除" }, new[] { "Space", "空格" },
                                         new[] { "PreviousLayout", "上一布局" },
                                         new[] { "NextLayout", "下一布局" }, new[] { "Done", "完成" } })
            {
                var keys = p.Keyboard.Where(k => k.Value == pair[0]).Select(k => k.Key).ToArray();
                if (keys.Length > 0)
                    parts.Add(String.Join("/", keys) + "  " + pair[1]);
            }
            return String.Join("      ", parts);
        }
        readonly KeyboardModel model;
        static readonly Color PanelTop = Color.FromArgb(46, 47, 55), PanelBottom = Color.FromArgb(32, 33, 40),
                              Border = Color.FromArgb(83, 85, 100), KeyTop = Color.FromArgb(67, 68, 78),
                              KeyBottom = Color.FromArgb(56, 57, 67), Utility = Color.FromArgb(49, 51, 61),
                              Ink = Color.FromArgb(244, 244, 248), Muted = Color.FromArgb(183, 185, 201),
                              Accent = Color.FromArgb(156, 188, 255), AccentInk = Color.FromArgb(26, 39, 68);
        readonly List<KeyValuePair<Rectangle, int[]>> hits = new List<KeyValuePair<Rectangle, int[]>>();
        internal KeyboardWindow(KeyboardModel m)
        {
            model = m;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = PanelBottom;
            Opacity = .97;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            Text = "手柄键盘";
        }
        protected override bool ShowWithoutActivation
        {
            get {
                return true;
            }
        }
        protected override CreateParams CreateParams
        {
            get {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x80;
                return cp;
            }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21)
            {
                m.Result = (IntPtr)3;
                return;
            }
            base.WndProc(ref m);
        }
        internal static Rectangle Place(Rectangle work, Rectangle edit, int heightPercent)
        {
            int width = (int)(work.Width * .94), height = work.Height * heightPercent / 100;
            var r = new Rectangle(work.Left + (work.Width - width) / 2,
                                  work.Bottom - height - Math.Max(4, work.Height / 80), width, height);
            if (!edit.IsEmpty && r.IntersectsWith(edit))
            {
                var top = new Rectangle(r.Left, work.Top + work.Height / 80, width, height);
                if (Rectangle.Intersect(top, edit).Height < Rectangle.Intersect(r, edit).Height)
                    r = top;
            }
            return r;
        }
        public void Present(IntPtr target, Rectangle edit, Settings settings)
        {
            Screen screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == settings.Display) ??
                            (!edit.IsEmpty ? Screen.FromRectangle(edit) : Screen.FromHandle(target));
            Bounds = Place(screen.WorkingArea, edit, settings.KeyboardHeight);
            if (!Visible)
                Show();
            Invalidate();
        }
        static void Label(Graphics g, string text, Rectangle area, float size, Color color, bool bold, bool center)
        {
            using (var font =
                       new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular,
                                GraphicsUnit.Pixel)) using (var brush =
                                                                new SolidBrush(
                                                                    color)) using (var format = new StringFormat {
                Alignment = center ? StringAlignment.Center : StringAlignment.Near,
                LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            }) g.DrawString(text, font, brush, area, format);
        }
        static GraphicsPath Rounded(Rectangle area, int radius)
        {
            int diameter = Math.Min(radius * 2, Math.Min(area.Width, area.Height));
            var path = new GraphicsPath();
            if (diameter <= 1)
            {
                path.AddRectangle(area);
                return path;
            }
            path.AddArc(area.Left, area.Top, diameter, diameter, 180, 90);
            path.AddArc(area.Right - diameter, area.Top, diameter, diameter, 270, 90);
            path.AddArc(area.Right - diameter, area.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(area.Left, area.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        int PanelRadius
        {
            get {
                return Math.Max(14, Math.Min(32, ClientSize.Height / 16));
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (ClientSize.Width < 2 || ClientSize.Height < 2)
                return;
            using (var outline = Rounded(ClientRectangle, PanelRadius)) Region = new Region(outline);
        }

        static float KeyWeight(string text)
        {
            if (text == "‹" || text == "›")
                return .37f;
            if (text == "空格")
                return 2.2f;
            if (text == "完成" || text == "大小写" || text == "退格" || text == "隔音符")
                return 1.2f;
            return 1f;
        }

        void DrawHeader(Graphics g, Rectangle area)
        {
            float size = Math.Min(area.Height * .45f, area.Width / 16f);
            if (!model.HasComposition)
            {
                Label(g, "手柄键盘", new Rectangle(area.Left, area.Top, area.Width * 2 / 3, area.Height), size,
                      Ink, true, false);
                Label(g, KeyboardModel.Layouts[model.Layout],
                      new Rectangle(area.Right - area.Width / 4, area.Top, area.Width / 4, area.Height),
                      size * .78f, Muted, false, true);
                return;
            }
            var preview = model.PreviewCandidate;
            string reading = model.Composition.Length > 38
                                 ? "…" + model.Composition.Substring(model.Composition.Length - 37)
                                 : model.Composition;
            int split = area.Width * 44 / 100;
            var readingBounds = new Rectangle(area.Left, area.Top, split - 8, area.Height);
            Label(g, reading, readingBounds,
                  Math.Min(size * .86f, readingBounds.Width / Math.Max(11f, reading.Length * .60f)), Muted, false,
                  false);
            var previewBounds =
                new Rectangle(area.Left + split + 12, area.Top, area.Width * 45 / 100 - 14, area.Height);
            Label(g, preview.Length > 0 ? preview : "继续输入拼音", previewBounds, size, Ink, true, false);
            if (model.CandidatePages > 1)
                Label(g, (model.Page + 1) + " / " + model.CandidatePages,
                      new Rectangle(area.Right - area.Width / 11, area.Top, area.Width / 11, area.Height),
                      size * .64f, Muted, false, true);
        }

        void DrawKey(Graphics g, string label, Rectangle box, bool selected, bool utility, bool candidate)
        {
            int radius = Math.Max(5, Math.Min(14, box.Height / 7));
            using (var shape = Rounded(box, radius))
            {
                if (selected || utility || candidate)
                {
                    using (var fill = new SolidBrush(selected ? Accent : Utility)) g.FillPath(fill, shape);
                }
                else
                {
                    using (var fill = new LinearGradientBrush(box, KeyTop, KeyBottom, 90f))
                        g.FillPath(fill, shape);
                }
                using (var edge = new Pen(selected ? Color.FromArgb(218, 230, 255) : Border, selected ? 2f : 1f))
                    g.DrawPath(edge, shape);
            }
            var textBox = Rectangle.Inflate(box, -Math.Min(8, box.Width / 6), 0);
            float targetSize = box.Height * (utility ? .36f : .50f);
            float fit = textBox.Width / Math.Max(1.4f, label.Length + .4f);
            Label(g, label, textBox, Math.Max(10, Math.Min(targetSize, fit)), selected ? AccentInk : Ink, selected,
                  true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int width = ClientSize.Width, height = ClientSize.Height;
            if (width < 2 || height < 2 || model.Rows.Count == 0)
                return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var panel = new LinearGradientBrush(ClientRectangle, PanelTop, PanelBottom, 90f))
                g.FillRectangle(panel, ClientRectangle);
            using (var outline = Rounded(Rectangle.Inflate(ClientRectangle, -1, -1), PanelRadius)) using (
                var edge = new Pen(Border)) g.DrawPath(edge, outline);

            int padding = Math.Max(16, width / 40), header = height * 17 / 100, footer = height * 11 / 100;
            int contentWidth = width - padding * 2;
            DrawHeader(g, new Rectangle(padding, 0, contentWidth, header));
            using (var divider = new Pen(Border))
                g.DrawLine(divider, padding, header - 3, width - padding, header - 3);
            int area = height - header - footer - 8, rowCount = model.Rows.Count;
            int gap = Math.Max(4, width / 240);
            hits.Clear();

            for (int r = 0; r < rowCount; r++)
            {
                var row = model.Rows[r];
                bool candidate = model.HasComposition && r == 0;
                bool utility = r == rowCount - 1;
                int indent = !candidate && !utility && row.Length < 10 ? (10 - row.Length) * contentWidth / 44 : 0;
                int rowLeft = padding + indent, rowWidth = contentWidth - indent * 2;
                float totalWeight = row.Sum(label => KeyWeight(label));
                float leftWeight = 0;
                int top = header + 8 + area * r / rowCount;
                int keyHeight = area * (r + 1) / rowCount - area * r / rowCount - gap;
                for (int c = 0; c < row.Length; c++)
                {
                    string label = row[c];
                    int x = rowLeft + (int)(rowWidth * leftWeight / totalWeight);
                    leftWeight += KeyWeight(label);
                    int right = rowLeft + (int)Math.Round(rowWidth * leftWeight / totalWeight);
                    var box = new Rectangle(x, top, right - x - gap, keyHeight);
                    DrawKey(g, label, box, r == model.Row && c == model.Col, utility, candidate);
                    hits.Add(new KeyValuePair<Rectangle, int[]>(box, new[] { r, c }));
                }
            }
            string legend = model.Layout == 1 ? Legend.Replace("  空格", "  首选 / 空格") : Legend;
            Label(g, legend, new Rectangle(padding, height - footer, contentWidth, footer),
                  Math.Min(footer * .43f, contentWidth / 74f), Muted, false, false);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            foreach (var h in hits)
                if (h.Key.Contains(e.Location))
                {
                    model.Row = h.Value[0];
                    model.Col = h.Value[1];
                    model.Command("Select");
                    Invalidate();
                    break;
                }
        }
    }
}