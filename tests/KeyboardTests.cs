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
            TypeReading(m, "nihao");
            Check(m.Candidates.Any(candidate => candidate.Text == "你好") && m.Composition == "nihao",
                  "offline pinyin word lookup");
            m.Row = m.Col = 0;
            m.Command("Select");
            Check(buf == "qw你好" && m.Composition == "", "candidate commit");
            m.Open("拼音");
            buf = "";
            TypeReading(m, "woxiangwanyouxi");
            Check(m.PreviewCandidate == "我想玩游戏",
                  "whole sentence suggested without an exact dictionary entry");
            m.Command("Space");
            Check(buf == "我想玩游戏" && m.Composition == "", "Y commits the full sentence in one action");

            foreach (var example in new[] { new[] { "nihaohuanyingshiyong", "你好欢迎使用" },
                                            new[] { "jintianbuxiangshangban", "今天不想上班" },
                                            new[] { "woxiangshuruzhongwen", "我想输入中文" },
                                            new[] { "xi'an", "西安" } })
                Check(dictionary.Value.Find(example[0])[0].Text == example[1], "continuous pinyin " + example[0]);

            m.Open("拼音");
            buf = "";
            TypeReading(m, "nihaok");
            Check(m.PreviewCandidate == "你好 →", "unfinished reading shows a selectable prefix");
            m.Row = m.Col = 0;
            m.Command("Select");
            Check(buf == "你好" && m.Composition == "k", "prefix commit retains the unconverted reading");
            TypeReading(m, "aixin");
            m.Command("Space");
            Check(buf == "你好开心" && m.Composition == "",
                  "remaining reading can continue after a prefix commit");
            m.Open("拼音");
            TypeReading(m, "shi");
            Check(m.CandidatePages > 1, "single syllable still exposes alternative candidates");
            m.Row = 0;
            m.Col = m.Rows[0].Length - 1;
            m.Command("Select");
            Check(m.Page == 1 && m.Rows[0][m.Col] == "›", "candidate next-page control retains its selection");

            m.Open("系统 IME");
            m.Command("Select");
            Check(key == "D1", "IME candidate virtual number");
            m.Command("Done");
            Check(closed == 1 && key == "Enter", "finish closes overlay");
            var target = new Rectangle(-1000, 800, 150, 40);
            var pos = KeyboardWindow.Place(new Rectangle(-1920, 0, 1920, 1080), target, 41);
            Check(pos.Left < 0 && !pos.IntersectsWith(target), "multi-monitor placement avoids edited control");
            var searchPanel = new Rectangle(743, 267, 1073, 1113);
            var shellPosition =
                KeyboardWindow.Place(new Rectangle(0, 0, 2560, 1390), target, 41, searchPanel);
            Check(!shellPosition.IsEmpty && !shellPosition.IntersectsWith(searchPanel),
                  "keyboard stays clear of the Windows Search panel");
            MoreFocus();
        }
        static void TypeReading(KeyboardModel model, string reading)
        {
            foreach (char letter in reading)
            {
                string label = letter == '\'' ? "隔音符" : letter.ToString();
                model.Row = model.Rows.FindIndex(row => row.Contains(label));
                model.Col = Array.IndexOf(model.Rows[model.Row], label);
                model.Command("Select");
            }
        }
        static partial void MoreFocus();
    }
}
