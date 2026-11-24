using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Gamepad
{
    internal sealed class PinyinCandidate
    {
        internal readonly string Text;
        internal readonly int Consumed;

        internal PinyinCandidate(string text, int consumed)
        {
            Text = text;
            Consumed = consumed;
        }
    }

    internal sealed class PinyinDictionary
    {
        const int BeamWidth = 24;
        const int AlternativesPerWord = 6;
        readonly Dictionary<string, List<string>> entries =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        readonly Dictionary<string, string[]> ranked = new Dictionary<string, string[]>(StringComparer.Ordinal);
        readonly Dictionary<string, int> preferredCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        int longestKey;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int LCMapStringEx(string locale, uint flags, string text, int length, StringBuilder result,
                                        int size, IntPtr version, IntPtr reserved, IntPtr sort);

        internal static string Simplify(string text)
        {
            var result = new StringBuilder(text.Length * 2 + 2);
            return LCMapStringEx("zh-CN", 0x02000000, text, -1, result, result.Capacity, IntPtr.Zero, IntPtr.Zero,
                                 IntPtr.Zero) > 0
                       ? result.ToString()
                       : text;
        }

        static readonly Encoding Gb =
            Encoding.GetEncoding(936, new EncoderReplacementFallback("?"), new DecoderReplacementFallback("?"));
        static bool CommonCharacter(string value)
        {
            var bytes = Gb.GetBytes(value);
            return bytes.Length == 2 && bytes[0] >= 0xb0 && bytes[0] <= 0xd7;
        }

        internal PinyinDictionary(string data)
        {
            foreach (string line in File.ReadLines(Path.Combine(data, "common.tsv"), Encoding.UTF8))
            {
                var fields = line.Split('\t');
                if (fields.Length != 2)
                    continue;
                var words = fields[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                entries[fields[0]] = words;
                preferredCounts[fields[0]] = words.Count;
            }
            bool started = false;
            foreach (string line in File.ReadLines(Path.Combine(data, "luna_pinyin.dict.yaml"), Encoding.UTF8))
            {
                if (!started)
                {
                    started = line == "...";
                    continue;
                }
                var fields = line.Split('\t');
                if (fields.Length < 2 || fields[1].Length == 0)
                    continue;
                string key = fields[1].Replace(" ", "").Replace("ü", "v");
                List<string> words;
                if (!entries.TryGetValue(key, out words))
                    entries[key] = words = new List<string>();
                words.Add(fields[0]);
            }
            longestKey = entries.Keys.Max(key => key.Length);
        }

        string[] Lookup(string key)
        {
            string[] candidates;
            if (ranked.TryGetValue(key, out candidates))
                return candidates;
            List<string> matches;
            if (!entries.TryGetValue(key, out matches))
                return null;
            int preferred;
            preferredCounts.TryGetValue(key, out preferred);
            return ranked[key] = matches.Take(preferred)
                                     .Select(Simplify)
                                     .Concat(matches.Skip(preferred).Select(Simplify).OrderBy(
                                         value => value.Length == 1 && !CommonCharacter(value) ? 1 : 0))
                                     .Distinct(StringComparer.Ordinal)
                                     .ToArray();
        }

        sealed class Combination
        {
            internal string Text;
            internal int Cost;
        }

        internal List<PinyinCandidate> Find(string code)
        {
            var candidates = new List<PinyinCandidate>();
            if (code.Length == 0)
                return candidates;

            var suffixes = new List<Combination>[code.Length + 1];
            suffixes[code.Length] = new List<Combination> { new Combination { Text = "" } };
            var prefixes = new List<PinyinCandidate>();
            for (int start = code.Length - 1; start >= 0; start--)
            {
                var options = new List<Combination>();
                int limit = Math.Min(start + longestKey, code.Length);
                for (int end = start + 1; end <= limit && code[end - 1] != '\''; end++)
                {
                    string key = code.Substring(start, end - start);
                    var words = Lookup(key);
                    if (words == null)
                        continue;

                    int next = end;
                    if (next < code.Length && code[next] == '\'')
                        next++;
                    var suffix = suffixes[next];
                    int preferred;
                    preferredCounts.TryGetValue(key, out preferred);
                    for (int rank = 0; rank < Math.Min(AlternativesPerWord, words.Length); rank++)
                    {
                        // Prefer fewer words and then the most common reading of each piece.
                        int cost = 20 + rank * 4 + (rank < preferred ? 0 : 5);
                        string word = words[rank];
                        if (start == 0 && next < code.Length && rank < 3)
                            prefixes.Add(new PinyinCandidate(word, next));
                        foreach (var rest in suffix)
                            options.Add(new Combination { Text = word + rest.Text, Cost = cost + rest.Cost });
                    }
                }
                suffixes[start] = options.OrderBy(option => option.Cost)
                                      .GroupBy(option => option.Text, StringComparer.Ordinal)
                                      .Select(group => group.First())
                                      .Take(BeamWidth)
                                      .ToList();
            }

            // A complete dictionary entry keeps its original priority over generated combinations.
            var exact = Lookup(code) ?? new string[0];
            foreach (string text in exact.Concat(suffixes[0].Select(option => option.Text))
                         .Distinct(StringComparer.Ordinal))
                candidates.Add(new PinyinCandidate(text, code.Length));
            // An unfinished or ambiguous reading can still commit a word from the start of the buffer.
            candidates.AddRange(prefixes.OrderByDescending(candidate => candidate.Consumed).Take(15));
            return candidates;
        }
    }
}