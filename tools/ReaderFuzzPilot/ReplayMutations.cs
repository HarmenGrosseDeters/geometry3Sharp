// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// The deterministic inputs the replay tests feed to a fuzz target besides its seeds: every truncation, a few replaced bytes at
// every position, and for text each number replaced by extreme tokens. No corpus files: they follow from the seeds.
using System;
using System.Collections.Generic;
using System.Text;

namespace ReaderFuzz
{
    public static class ReplayMutations
    {
        static readonly byte[] Replacements = { 0x00, 0xFF, 0x7F, 0x80 };

        public static readonly string[] ExtremeTokens = {
            "1e400", "-1e400", "NaN", "inf", "-0", "1,5", "2147483647", "2147483648", "-2147483649", "4294967296",
            "99999999999999999999", "-1", "0", "1e-320", "0x10", "", "1e", "--1", "5.6348333975312060066e-8", "4.9406564584124654e-324"
        };

        /// <summary> at most about maxPositions positions are mutated (a stride over long seeds) </summary>
        public static IEnumerable<byte[]> All(byte[] seed, bool text, int maxPositions = 800)
        {
            yield return new byte[0];
            int n = seed.Length;
            int stride = Math.Max(1, n / maxPositions);
            for (int len = 0; len < n; len += stride) {
                var cut = new byte[len];
                Array.Copy(seed, cut, len);
                yield return cut;
            }
            for (int at = 0; at < n; at += stride)
                foreach (byte value in Replacements) {
                    if (seed[at] == value) continue;
                    var copy = (byte[])seed.Clone();
                    copy[at] = value;
                    yield return copy;
                }
            // the last four bytes and a 32-bit word at every aligned position set to extreme counts (binary headers, lengths)
            for (int at = 0; at + 4 <= n && at < 256; at += 4)
                foreach (uint word in new uint[] { 0xFFFFFFFFu, 0x7FFFFFFFu, 0x80000000u, 0x40000000u, 0u }) {
                    var copy = (byte[])seed.Clone();
                    copy[at] = (byte)word; copy[at + 1] = (byte)(word >> 8); copy[at + 2] = (byte)(word >> 16); copy[at + 3] = (byte)(word >> 24);
                    yield return copy;
                }
            if (!text) yield break;
            string s = Encoding.ASCII.GetString(seed);
            int i = 0, tokens = 0;
            while (i < s.Length && tokens < 400) {
                if (char.IsDigit(s[i]) || ((s[i] == '-' || s[i] == '.') && i + 1 < s.Length && char.IsDigit(s[i + 1]))) {
                    int j = i + 1;
                    while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '.' || s[j] == 'e' || s[j] == 'E' || ((s[j] == '-' || s[j] == '+') && (s[j - 1] == 'e' || s[j - 1] == 'E')))) ++j;
                    foreach (string token in ExtremeTokens)
                        yield return Encoding.ASCII.GetBytes(s.Substring(0, i) + token + s.Substring(j));
                    i = j; ++tokens;
                } else ++i;
            }
        }
    }
}
