#!/usr/bin/env python3
# Distributed under the Boost Software License, Version 1.0. http://www.boost.org/LICENSE_1_0.txt
"""What a determinism proof scope would have to register, from the build log of the library with the scope turned on
(the first step of a stage of docs/design/DETERMINISM_PROOF.md; used for the stage 2 estimate of
docs/research/DETERMINISM_CROSS_RUNTIME_2026-11.md section 5).

    python3 scope_report.py <build log> [--members]

The log is the output of `dotnet build geometry3Sharp_netstandard.csproj -c Release -f net10.0` after the files were
added to the scope section of .editorconfig (and a `type` line of DeterministicApis.txt removed): every G3D diagnostic
is one line. Output: the counts per rule, the unlisted external members by owner (the BCL, geometry3Sharp types outside
the scope, compiler constructs), the restricted entries with their number of sites, and with --members every member."""
import collections
import re
import sys

LINE = re.compile(r"[/\\]([A-Za-z0-9_.]+\.cs)\((\d+),(\d+)\): error (G3D\d{4}): Proof scope: (.*?)(?: \[[^\]]*proj\])?$")
API = re.compile(r"^'(.+)' is not on the allow list")
CONSTRUCT = re.compile(r"^'construct:([a-z-]+) \((\d+) in '([^']+)', registered: (\d+)\)' is not on the allow list")
SITE = re.compile(r"'([^']+)' is a restricted entry: '([^']+)' needs a 'site' line")
CAST = re.compile(r"(\d+) conversion\(s\) of a double to an integer type in '([^']+)'")
UNUSED = re.compile(r"DeterministicApis\.txt line (\d+)")


def owner(member):
    """'bcl:System.Math' (the declaring type), 'g3:DMesh3' or 'construct'"""
    if member.startswith("construct:"):
        return "construct"
    m = re.match(r"[A-Z]:g3\.([A-Za-z0-9_]+)", member)
    if m:
        return "g3:" + m.group(1)
    name = member[2:].split("(")[0]
    return "bcl:" + (name if member.startswith("T:") or "." not in name else name.rsplit(".", 1)[0])


def parse(lines):
    """-> dict: diagnostics (unique), per_rule, per_file, members {member: uses}, sites {entry: set of members},
    hash_sites, casts {member: count}, unused (allow-list line numbers)"""
    seen = set()
    r = {"per_rule": collections.Counter(), "per_file": collections.Counter(), "members": collections.Counter(),
         "sites": collections.defaultdict(set), "hash_sites": 0, "casts": {}, "unused": set(), "other": [],
         "constructs": {}}
    for line in lines:
        m = LINE.search(line.rstrip("\n"))
        if not m:
            continue
        key = m.groups()
        if key in seen:       # (msbuild prints every diagnostic twice: in the build and in its summary)
            continue
        seen.add(key)
        f, _, _, rule, text = key
        r["per_rule"][rule] += 1
        a, s, c, u = API.search(text), SITE.search(text), CAST.search(text), UNUSED.search(text)
        if rule == "G3D0009" and u:
            r["unused"].add(int(u.group(1)))
            continue
        r["per_file"][f] += 1
        k = CONSTRUCT.search(text)
        if rule == "G3D0001" and k:       # (counted per member, reported at each occurrence)
            r["constructs"][(k.group(1), k.group(3))] = int(k.group(2))
        elif rule == "G3D0001" and a:
            r["members"][a.group(1)] += 1
        elif rule == "G3D0002" and s:
            r["sites"][s.group(1)].add(s.group(2))
        elif rule == "G3D0002" and "hash value" in text:
            r["hash_sites"] += 1
        elif rule == "G3D0006" and c:
            r["casts"][c.group(2)] = int(c.group(1))
        else:
            r["other"].append(line.strip())
    r["diagnostics"] = len(seen)
    return r


def render(r, members=False):
    out = ["%d diagnostics: %s" % (r["diagnostics"], ", ".join("%s %d" % kv for kv in sorted(r["per_rule"].items()))),
           "per file: " + ", ".join("%s %d" % kv for kv in sorted(r["per_file"].items())), ""]
    owners = collections.Counter()
    for m in r["members"]:
        owners[owner(m)] += 1
    kinds = collections.Counter(o.split(":")[0] for o in owners.elements())
    out.append("unlisted external members (G3D0001): %d (%d uses): %s" % (len(r["members"]), sum(r["members"].values()),
               ", ".join("%s %d" % (k, kinds[k]) for k in ("bcl", "g3", "construct") if kinds[k])))
    out.append("  by owner: " + ", ".join("%s %d" % kv for kv in sorted(owners.items(), key=lambda kv: (-kv[1], kv[0]))))
    n_sites = sum(len(v) for v in r["sites"].values())
    out.append("restricted entries without a site line (G3D0002): %d entries, %d member sites; GetHashCode outside a hash implementation: %d"
               % (len(r["sites"]), n_sites, r["hash_sites"]))
    for e, v in sorted(r["sites"].items(), key=lambda kv: (-len(kv[1]), kv[0])):
        out.append("  %3d  %s" % (len(v), e))
    per_kind = collections.Counter()
    members_of = collections.Counter()
    for (kind, _), n in r["constructs"].items():
        per_kind[kind] += n
        members_of[kind] += 1
    out.append("counted constructs without a registration (G3D0001): " + (", ".join("%s %d in %d members" % (k, per_kind[k], members_of[k])
               for k in sorted(per_kind)) or "none"))
    out.append("double to integer conversions (G3D0006): %d in %d members" % (sum(r["casts"].values()), len(r["casts"])))
    out.append("allow-list lines no longer used (G3D0009): %d" % len(r["unused"]))
    if r["other"]:
        out.append("not classified: %d" % len(r["other"]))
        out.extend("  " + x for x in r["other"][:20])
    if members:
        out.append("")
        out.extend("%4d  %s" % (n, m) for m, n in sorted(r["members"].items(), key=lambda kv: (owner(kv[0]), kv[0])))
    return "\n".join(out)


def main(argv):
    if len(argv) < 2:
        sys.stderr.write(__doc__)
        return 2
    with open(argv[1], encoding="utf-8", errors="replace") as f:
        print(render(parse(f), "--members" in argv))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
