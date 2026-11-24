using System;
using System.Drawing;
using System.Linq;
using System.IO;
namespace Gamepad
{
    internal static partial class Tests
    {
        static partial void MoreUI()
        {
            string buf = "";
            int closed = 0;
            string key = "";
            var dictionary = new Lazy<PinyinDictionary>(
                () => new PinyinDictionary(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data")));
            var m = new KeyboardModel(() => dictionary.Value) { Insert = s => buf += s, Key = s => key = s,
                                                                Close = () => closed++ };
            m.Open("English");
            m.Command("Select");
            m.Command("Right");
            m.Command("Select");
            Check(buf == "qw", "English pad navigation");
            m.Command("NextLayout");
            foreach (char ch in "nihao")
            {
                m.Row = m.Rows.FindIndex(row => row.Contains(ch.ToString()));
                m.Col = Array.IndexOf(m.Rows[m.Row], ch.ToString());
                m.Command("Select");
            }
            Check(m.Candidates.Contains("你好") && m.Composition == "nihao", "offline pinyin word lookup");
            m.Row = m.Col = 0;
            m.Command("Select");
            Check(buf == "qw你好" && m.Composition == "", "candidate commit");
            m.Open("系统 IME");
            m.Command("Select");
            Check(key == "D1", "IME candidate virtual number");
            m.Command("Done");
            Check(closed == 1 && key == "Enter", "finish closes overlay");
            var target = new Rectangle(-1000, 800, 150, 40);
            var pos = KeyboardWindow.Place(new Rectangle(-1920, 0, 1920, 1080), target, 41);
            Check(pos.Left < 0 && !pos.IntersectsWith(target), "multi-monitor placement avoids edited control");
            MoreFocus();
        }
        static partial void MoreFocus();
    }
}