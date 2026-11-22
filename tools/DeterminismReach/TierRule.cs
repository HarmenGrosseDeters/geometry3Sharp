// Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
// The tier rule of the golden-master policy (docs/research/DETERMINISM_CROSS_RUNTIME_2026-11.md section 6, docs/design/DETERMINISM.md
// "Golden masters, two tiers"): a documented operation whose reachable set has no platform libm (the upper bound `cha`) is tier 1,
// bit-exact on every platform; a list of them is kept in tier1.txt, and the check fails when a listed type gains a libm edge
// (or disappears). A type with a proof argument (a libm call that cannot change a result) is listed with `<TAB>argument: ...`;
// it is exempt from the edge check, and its argument has to name its test.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DeterminismReach
{
    public sealed class TierBaseline
    {
        /// <summary> the tier 1 types by simple name; the value is the argument (a type that reaches libm and is tier 1 anyway) or "" </summary>
        public readonly SortedDictionary<string, string> Tier1 = new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary> lines: `Type` or `Type<TAB>argument: ...`; blank lines and lines starting with # are comments </summary>
        public static TierBaseline Parse(string text)
        {
            var b = new TierBaseline();
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n')) {
                string line = raw.TrimEnd();
                if (line.Length == 0 || line[0] == '#') continue;
                int tab = line.IndexOf('\t');
                string name = tab < 0 ? line : line.Substring(0, tab), arg = tab < 0 ? "" : line.Substring(tab + 1).Trim();
                if (arg.Length > 0 && !arg.StartsWith("argument:", StringComparison.Ordinal))
                    throw new FormatException("tier1: the text after the tab of " + name + " has to start with `argument:`");
                b.Tier1[name] = arg;
            }
            return b;
        }

        public string Format(string header)
        {
            var sb = new StringBuilder(header);
            foreach (var kv in Tier1) sb.Append(kv.Key).Append(kv.Value.Length > 0 ? "\t" + kv.Value : "").Append('\n');
            return sb.ToString();
        }
    }

    public static class TierRule
    {
        public sealed class Result
        {
            /// <summary> listed types that now reach the platform libm (cha) without an argument </summary>
            public readonly List<string> Violations = new List<string>();
            /// <summary> listed types that are no longer documented public types of the library (renamed or removed: the list is stale) </summary>
            public readonly List<string> Stale = new List<string>();
            /// <summary> documented types without a libm edge that are not listed (could be added: --update) </summary>
            public readonly List<string> Candidates = new List<string>();
            /// <summary> listed types with an argument that no longer reach libm (the argument can go) </summary>
            public readonly List<string> ArgumentsNotNeeded = new List<string>();
            public bool Ok { get { return Violations.Count == 0 && Stale.Count == 0; } }
        }

        /// <param name="documented">the documented public types by simple name, with whether they reach the platform libm on `cha` edges</param>
        public static Result Check(IReadOnlyDictionary<string, bool> documented, TierBaseline baseline)
        {
            var r = new Result();
            foreach (var kv in baseline.Tier1) {
                if (!documented.TryGetValue(kv.Key, out bool libm)) { r.Stale.Add(kv.Key); continue; }
                if (libm && kv.Value.Length == 0) r.Violations.Add(kv.Key);
                if (!libm && kv.Value.Length > 0) r.ArgumentsNotNeeded.Add(kv.Key);
            }
            foreach (var kv in documented.OrderBy(k => k.Key, StringComparer.Ordinal))
                if (!kv.Value && !baseline.Tier1.ContainsKey(kv.Key)) r.Candidates.Add(kv.Key);
            return r;
        }

        /// <summary> the baseline of the current library: every documented type without a libm edge, and the existing arguments kept </summary>
        public static TierBaseline Update(IReadOnlyDictionary<string, bool> documented, TierBaseline old)
        {
            var b = new TierBaseline();
            foreach (var kv in documented)
                if (!kv.Value) b.Tier1[kv.Key] = old.Tier1.TryGetValue(kv.Key, out string a) ? a : "";
            // (an argument type reaches libm by design: it stays while it is documented)
            foreach (var kv in old.Tier1)
                if (kv.Value.Length > 0 && documented.ContainsKey(kv.Key)) b.Tier1[kv.Key] = kv.Value;
            return b;
        }
    }
}
