#!/usr/bin/env python3
"""Writes N part configs for one mutated file, cut at method starts so that the parts hold about the same number of
mutants (a count proxy: operators, statements, branches per method; no report needed).
Usage (in the repository root): python3 jobs/tools/make_parts.py <N> [file] [base config] [out dir]
Defaults: mesh_ops/MeshRoundingRepair.cs, tests/mutation/stryker-mesh-rounding.json, jobs/parts."""
import json, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import suggest_split as sp


def proxy(seg):
    seg = re.sub(r"//[^\n]*", "", seg)
    n = len(re.findall(r"(?<![=!<>+\-*/%&|^])(?:==|!=|<=|>=|<|>)(?![=<>])", seg))
    n += len(re.findall(r"(?<![+\-])[+\-*/%](?![+\-=])", seg))
    n += len(re.findall(r"&&|\|\|", seg)) + len(re.findall(r"\+\+|--|\+=|-=|\*=|/=", seg))
    n += len(re.findall(r";", seg)) + len(re.findall(r"\b(?:true|false)\b", seg))
    n += len(re.findall(r"\bif\b|\bwhile\b|\bfor\b", seg)) + len(re.findall(r"!(?!=)", seg))
    return n


def main(argv):
    parts = int(argv[0])
    path = argv[1] if len(argv) > 1 else "mesh_ops/MeshRoundingRepair.cs"
    base = argv[2] if len(argv) > 2 else "tests/mutation/stryker-mesh-rounding.json"
    out = argv[3] if len(argv) > 3 else "jobs/parts"
    with open(path, encoding="utf-8-sig", newline="") as f:
        text = f.read()
    if "\r" in text:
        sys.exit("CRLF source: Stryker's offsets would differ; use a checkout with LF")
    starts = [o for o, _, _ in sp.method_starts(text) if 0 < o < len(text)]
    offs = [0] + starts + [len(text)]
    cost = [float(proxy(text[offs[i]:offs[i + 1]])) for i in range(len(offs) - 1)]
    cuts = sp.partition(cost, parts)
    bounds = [0] + cuts + [len(cost)]
    with open(base, encoding="utf-8") as f:
        cfg = json.load(f)
    os.makedirs(out, exist_ok=True)
    for k, (a, z) in enumerate(zip(bounds, bounds[1:]), 1):
        c = json.loads(json.dumps(cfg))["stryker-config"]
        end = 1000000 if z == len(cost) else offs[z]
        c["mutate"] = ["**/%s{%d..%d}" % (path, offs[a], end)]
        c["thresholds"]["break"] = 0                     # measuring: no break
        c["concurrency"] = int(os.environ.get("G3_CONCURRENCY", "3"))
        name = os.path.join(out, "part-%d-of-%d.json" % (k, parts))
        with open(name, "w", encoding="utf-8") as f:
            json.dump({"stryker-config": c}, f, indent=2)
        print("%s  %d..%d  ~%d proxy units" % (name, offs[a], end, sum(cost[a:z])))


if __name__ == "__main__":
    main(sys.argv[1:])
