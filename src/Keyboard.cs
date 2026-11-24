using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
namespace Gamepad
{
    internal sealed class PinyinDictionary
    {
        readonly Dictionary<string, List<string>> entries =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int LCMapStringEx(string locale, uint flags, string text, int length, StringBuilder result,
                                        int size, IntPtr version, IntPtr reserved, IntPtr sort);
        internal static string Simplify(string s)
        {
            var b = new StringBuilder(s.Length * 2 + 2);
            return LCMapStringEx("zh-CN", 0x02000000, s, -1, b, b.Capacity, IntPtr.Zero, IntPtr.Zero,
                                 IntPtr.Zero) > 0
                       ? b.ToString()
                       : s;
        }
        static readonly Encoding gb =
            Encoding.GetEncoding(936, new EncoderReplacementFallback("?"), new DecoderReplacementFallback("?"));
        static int Rank(string value)
        {
            var b = gb.GetBytes(value);
            return b.Length == 2 && b[0] >= 0xb0 && b[0] <= 0xd7 ? 0 : 1;
        }
        internal PinyinDictionary(string data)
        {
            foreach (string line in File.ReadLines(Path.Combine(data, "common.tsv"), Encoding.UTF8))
            {
                var a = line.Split('\t');
                if (a.Length == 2)
                    entries[a[0]] = a[1].Split(' ').ToList();
            }
            bool started = false;
            foreach (string line in File.ReadLines(Path.Combine(data, "luna_pinyin.dict.yaml"), Encoding.UTF8))
            {
                if (!started)
                {
                    started = line == "...";
                    continue;
                }
                var a = line.Split('\t');
                if (a.Length < 2 || a[1].Length == 0)
                    continue;
                string key = a[1].Replace(" ", "").Replace("ü", "v");
                List<string> list;
                if (!entries.TryGetValue(key, out list))
                    entries[key] = list = new List<string>();
                list.Add(a[0]);
            }
        }
        public List<string> Find(string code)
        {
            List<string> matches;
            if (!entries.TryGetValue(code, out matches))
                return new List<string>();
            var candidates = matches.Select(Simplify).Distinct().ToList();
            // Keep curated words and common characters ahead of rare Unicode ideographs.
            return candidates.Take(1)
                .Concat(candidates.Skip(1).OrderBy(s => s.Length == 1 ? Rank(s) : 0))
                .ToList();
        }
    }
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
        public List<string> Candidates = new List<string>();
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
            Candidates = Composition.Length == 0 ? new List<string>() : dictionary().Find(Composition);
            Page = 0;
        }
        public void Build()
        {
            Rows.Clear();
            if (Layout == 1 && Composition.Length > 0)
                Rows.Add(Candidates.Skip(Page * 5).Take(5).Concat(new[] { "‹", "›" }).ToArray());
            if (Layout == 3)
                Rows.Add("1 2 3 4 5 6 7 8 9 0".Split(' '));
            foreach (var r in Layout == 4 ? Edit : Layout == 2 ? Symbols : Letters)
                Rows.Add((Layout == 0 && Upper ? r.ToUpperInvariant() : r).Split(' '));
            Rows.Add((Layout == 0   ? "大小写 空格 退格 完成"
                      : Layout == 3 ? "上页 下页 空格 退格 切换输入法 完成"
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
                if (Layout == 1 && Composition.Length > 0 && Row == 0 && Col < Rows[0].Length - 2)
                {
                    Commit(Rows[0][Col]);
                    break;
                }
                Activate(Rows[Row][Col]);
                break;
            }
            Build();
        }
        void Commit(string value)
        {
            Insert(value);
            ResetText();
            Row = Col = 0;
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
                int pages = Math.Max(1, (Candidates.Count + 4) / 5);
                Page = (Page + (value == "›" ? 1 : pages - 1)) % pages;
                return;
            }
            if (value == "完成")
            {
                if (Composition.Length > 0)
                {
                    if (Candidates.Count > 0)
                        Commit(Candidates[Page * 5]);
                    else
                        return;
                }
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
                        Commit(Candidates[Page * 5]);
                }
                else
                    Key("Space");
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
                bool addedRow = Composition.Length == 0;
                if (Composition.Length < 40)
                    Composition += value;
                Query();
                if (addedRow)
                    Row++;
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
        static readonly Color Base = Color.FromArgb(19, 26, 37), Tile = Color.FromArgb(47, 60, 76),
                              Ink = Color.FromArgb(240, 246, 249), Accent = Color.FromArgb(137, 237, 205);
        readonly List<KeyValuePair<Rectangle, int[]>> hits = new List<KeyValuePair<Rectangle, int[]>>();
        internal KeyboardWindow(KeyboardModel m)
        {
            model = m;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Base;
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
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Base);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int w = ClientSize.Width, h = ClientSize.Height, pad = Math.Max(8, w / 50), head = h / 7, foot = h / 9;
            Label(g,
                  model.Composition.Length > 0
                      ? "拼音  /  " + model.Composition + "     " +
                            (model.Candidates.Count == 0
                                 ? "暂无候选"
                                 : (model.Page + 1) + " / " + ((model.Candidates.Count + 4) / 5))
                      : "手柄键盘   /   " + KeyboardModel.Layouts[model.Layout],
                  new Rectangle(pad, 0, w - pad * 2, head), head * .47f, Ink, true, false);
            int area = h - head - foot, n = model.Rows.Count;
            hits.Clear();
            int gap = Math.Max(3, w / 320);
            for (int r = 0; r < n; r++)
            {
                var row = model.Rows[r];
                int y = head + area * r / n;
                for (int c = 0; c < row.Length; c++)
                {
                    int x = pad + (w - 2 * pad) * c / row.Length, end = pad + (w - 2 * pad) * (c + 1) / row.Length;
                    var box = new Rectangle(x, y, end - x - gap, area * (r + 1) / n - area * r / n - gap);
                    bool sel = r == model.Row && c == model.Col;
                    using (var brush = new SolidBrush(sel ? Accent : Tile)) g.FillRectangle(brush, box);
                    Label(g, row[c], box,
                          Math.Max(10,
                                   Math.Min(box.Height * .52f, box.Width / (float)Math.Max(2, row[c].Length + 1))),
                          sel ? Base : Ink, sel, true);
                    hits.Add(new KeyValuePair<Rectangle, int[]>(box, new[] { r, c }));
                }
            }
            Label(g, Legend, new Rectangle(pad, h - foot, w - 2 * pad, foot), Math.Min(foot * .43f, w / 62f),
                  Accent, false, false);
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