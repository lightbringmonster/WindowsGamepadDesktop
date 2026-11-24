using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Gamepad
{
    public sealed class Profile
    {
        public double Speed { get; set; }
        public double DeadZone { get; set; }
        public double Curve { get; set; }
        public string ModeChord { get; set; }
        public Dictionary<string, string> Desktop { get; set; }
        public Dictionary<string, string> Keyboard { get; set; }
        public Profile()
        {
            Speed = 940;
            DeadZone = .18;
            Curve = 1.65;
            ModeChord = "LB+RB";
            Desktop =
                Pairs("LeftStick=Cursor|RightStick=Scroll|B=MouseLeft|A=Key:Escape|X=MouseRight|Y=Key:Enter|" +
                      "LB=Key:Alt+Tab|RB=Keyboard|LT=Precision|RT=MouseLeft|Up=Key:Up|Down=Key:Down|Left=Key:" +
                      "Left|Right=Key:Right|Start=Key:Win|Back=Key:Win+D|LS=None|RS=None");
            Keyboard = Pairs(
                "LeftStick=Navigate|RightStick=None|Up=Up|Down=Down|Left=Left|Right=Right|B=Select|A=Close|X=Delete|" +
                "Y=Space|LB=PreviousLayout|RB=NextLayout|RT=Done|LT=None|Start=None|Back=None|LS=None|RS=None");
        }
        static Dictionary<string, string> Pairs(string value)
        {
            return value.Split('|').Select(x => x.Split('=')).ToDictionary(a => a[0], a => a[1]);
        }
        public static readonly string[] DesktopActions = {
            "None",       "MouseLeft", "MouseRight", "MouseMiddle", "Precision", "Keyboard",   "Mode",
            "Key:Escape", "Key:Enter", "Key:Tab",    "Key:Alt+Tab", "Key:Win+D", "Key:Ctrl+C", "Key:Ctrl+V",
            "Key:Up",     "Key:Down",  "Key:Left",   "Key:Right",   "Key:Win"
        };
        public static readonly string[] KeyboardActions = {
            "None",   "Up",    "Down",           "Left",       "Right", "Select", "Close",
            "Delete", "Space", "PreviousLayout", "NextLayout", "Done",  "Mode"
        };
        public void Validate()
        {
            if (!(DeadZone >= 0.02 && DeadZone <= .75 && Speed >= 100 && Speed <= 4000 && Curve >= .5 &&
                  Curve <= 4))
                throw new FormatException("摇杆参数超过范围");
            if (Desktop == null || Keyboard == null || String.IsNullOrEmpty(ModeChord))
                throw new FormatException("缺少映射");
            var chord = ModeChord.Split('+');
            if (chord.Length != 2 || chord[0] == chord[1] ||
                chord.Any(c => String.IsNullOrWhiteSpace(c) || IsAxis(c) || !Desktop.ContainsKey(c)))
                throw new FormatException("切换组合需要两个已配置的不同按键");
            if (Desktop.Count > 256 || Keyboard.Count > 256)
                throw new FormatException("按键数量超过限制");
            foreach (var pair in Desktop)
            {
                if (String.IsNullOrWhiteSpace(pair.Key) || !ValidDesktop(pair.Key, pair.Value))
                    throw new FormatException("无效桌面映射 " + pair.Key + " / " + pair.Value);
            }
            foreach (var pair in Keyboard)
            {
                if (!(IsAxis(pair.Key) ? new[] { "Navigate", "None" } : KeyboardActions).Contains(pair.Value))
                    throw new FormatException("无效键盘映射 " + pair.Key);
            }
        }
        public static bool IsAxis(string key)
        {
            return key == "LeftStick" || key == "RightStick" ||
                   (key != null && key.StartsWith("Axis.", StringComparison.Ordinal));
        }
        public static bool ValidDesktop(string control, string action)
        {
            if (IsAxis(control))
                return new[] { "Cursor", "Scroll", "None" }.Contains(action);
            return DesktopActions.Contains(action) || KeyBinding.Parse(action) != null;
        }
    }
    public static class KeyBinding
    {
        public static int[] Parse(string action)
        {
            if (action == null || !action.StartsWith("Key:", StringComparison.Ordinal))
                return null;
            var result = new List<int>();
            foreach (var word in action.Substring(4).Split('+'))
            {
                Keys key;
                if (word == "Ctrl")
                    key = Keys.ControlKey;
                else if (word == "Alt")
                    key = Keys.Menu;
                else if (word == "Shift")
                    key = Keys.ShiftKey;
                else if (word == "Win")
                    key = Keys.LWin;
                else if (!Enum.TryParse<Keys>(word, true, out key) || !Enum.IsDefined(typeof(Keys), key))
                    return null;
                if ((int)key < 8 || (int)key > 254 || result.Contains((int)key))
                    return null;
                result.Add((int)key);
            }
            return result.Count > 0 && result.Count <= 5 ? result.ToArray() : null;
        }
    }
    public sealed class Settings
    {
        public int Version { get; set; }
        public string Primary { get; set; }
        public bool AutoKeyboard { get; set; }
        public string Display { get; set; }
        public string StartLayout { get; set; }
        public int KeyboardHeight { get; set; }
        public Dictionary<string, string> AssignedProfiles { get; set; }
        public string[] GameApps { get; set; }
        public Dictionary<string, Profile> Profiles { get; set; }
        public Settings()
        {
            Version = 2;
            AssignedProfiles = new Dictionary<string, string>();
            Primary = "";
            AutoKeyboard = true;
            Display = "";
            StartLayout = "English";
            KeyboardHeight = 41;
            GameApps = new[] { "Playnite.FullscreenApp.exe" };
            Profiles = new Dictionary<string, Profile>();
        }
        public Profile For(string id)
        {
            string name;
            if (AssignedProfiles.TryGetValue(id, out name))
                id = "profile/" + name;
            Profile p;
            if (!Profiles.TryGetValue(id, out p))
                Profiles[id] = p = new Profile();
            return p;
        }
        public void Validate()
        {
            if (Profiles == null || AssignedProfiles == null ||
                AssignedProfiles.Any(p => String.IsNullOrWhiteSpace(p.Key) || String.IsNullOrWhiteSpace(p.Value) ||
                                          p.Value.Length > 64 || !Profiles.ContainsKey("profile/" + p.Value)))
                throw new FormatException("设备配置引用无效");
            if (Version != 2 || Profiles == null || GameApps == null || Primary == null || Display == null ||
                KeyboardHeight < 28 || KeyboardHeight > 52 ||
                !new[] { "English", "拼音", "数字符号", "系统 IME", "编辑" }.Contains(StartLayout))
                throw new FormatException("配置版本或键盘设置无效");
            if (GameApps.Length > 256 ||
                GameApps.Any(s => String.IsNullOrWhiteSpace(s) ||
                                  s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                                  !s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                throw new FormatException("应用规则需要进程文件名，例如 game.exe");
            foreach (var entry in Profiles)
            {
                if (entry.Value == null || String.IsNullOrWhiteSpace(entry.Key))
                    throw new FormatException("设备配置无效");
                entry.Value.Validate();
            }
        }
    }
    public static class SettingsStore
    {
        public static string Location
        {
            get {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "WindowsGamepadDesktop", "settings.json");
            }
        }
        public static string Encode(Settings cfg)
        {
            cfg.Validate();
            return new JavaScriptSerializer().Serialize(cfg);
        }
        public static Settings Decode(string json)
        {
            var js = new JavaScriptSerializer();
            var raw = js.DeserializeObject(json) as Dictionary<string, object>;
            if (raw == null || !raw.ContainsKey("Version"))
                throw new FormatException("缺少版本号");
            int v = Convert.ToInt32(raw["Version"]);
            if (v != 1 && v != 2)
                throw new FormatException("不支持此配置版本");
            var cfg = js.Deserialize<Settings>(json);
            if (v == 1)
            {
                cfg.Version = 2;
                if (String.IsNullOrEmpty(cfg.StartLayout))
                    cfg.StartLayout = "English";
            }
            cfg.Validate();
            return cfg;
        }
        public static Settings Load(string path)
        {
            if (new FileInfo(path).Length > 1024 * 1024)
                throw new FormatException("配置文件过大");
            return Decode(File.ReadAllText(path, Encoding.UTF8));
        }
        public static void Save(string path, Settings cfg)
        {
            string full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            var tmp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, Encode(cfg), new UTF8Encoding(false));
                if (File.Exists(full))
                    File.Replace(tmp, full, null);
                else
                    File.Move(tmp, full);
            }
            finally
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
        }
    }
}