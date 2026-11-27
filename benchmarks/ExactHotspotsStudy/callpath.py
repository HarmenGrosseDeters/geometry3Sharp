#!/usr/bin/env python3
"""Fold a dotnet-trace speedscope profile (thread-time sampling) by call path, for the hot-spot shares of the note.

Usage: python3 callpath.py <file.speedscope.json>

For each target frame: its inclusive time, its callers (the two innermost g3 frames above it, outside the predicate classes)
and the deepest g3 frame below it (where its own time goes); then the inclusive time of the rounding repair's pair tests and
of the phases, as shares of ExactMeshBoolean.Compute.
"""
import collections
import json
import sys

TARGETS = ["g3.ExactPredicates.orient3d_exact", "g3.TriangleTriangleFilter.by_projection", "g3.MeshRoundingRepair.improves"]
INCLUSIVE = ["g3.ExactMeshBoolean.Compute", "g3.MeshRoundingRepair.Apply", "g3.MeshRoundingRepair.improves",
             "g3.MeshRoundingRepair.intersects", "g3.ExactMeshArrangement.Compute"]


def main(path):
    d = json.load(open(path))
    fr = [f["name"].split("(")[0].replace("geometry3Sharp!", "") for f in d["shared"]["frames"]]
    callers = {t: collections.Counter() for t in TARGETS}
    below = {t: collections.Counter() for t in TARGETS}
    tot = collections.Counter()
    tgt = collections.Counter()
    for p in d["profiles"]:
        st, last = [], None
        for ev in p["events"]:
            t = ev["at"]
            if last is not None and st:
                dt = t - last
                names = [fr[f] for f in st]
                present = set(names)
                for k in INCLUSIVE:
                    if k in present:
                        tot[k] += dt
                for tg in TARGETS:
                    if tg in present:
                        i = names.index(tg)
                        tgt[tg] += dt
                        up = [n for n in names[:i] if n.startswith("g3.") and "ExactPredicates" not in n and "TriangleTriangleFilter" not in n]
                        callers[tg][" < ".join(up[-2:][::-1])] += dt
                        down = [n for n in names[i:] if n.startswith("g3.")]
                        below[tg][down[-1]] += dt
            last = t
            if ev["type"] == "O":
                st.append(ev["frame"])
            elif st:
                st.pop()
    c = tot["g3.ExactMeshBoolean.Compute"]
    for k in INCLUSIVE:
        print(f"{k}: {tot[k]:.0f} ms = {100 * tot[k] / c:.1f} % of Compute")
    for tg in TARGETS:
        print(f"== {tg}: {tgt[tg]:.0f} ms")
        for name, ms in callers[tg].most_common(6):
            print(f"  caller {100 * ms / tgt[tg]:5.1f} %  {name}")
        for name, ms in below[tg].most_common(8):
            print(f"  below  {100 * ms / tgt[tg]:5.1f} %  {name}")


if __name__ == "__main__":
    main(sys.argv[1])
