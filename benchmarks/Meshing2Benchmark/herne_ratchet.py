#!/usr/bin/env python3
"""Herne ratchet: mesh the Herne ALKIS inputs, judge them, and hold the known violations to fixed limits.

The Herne inputs (prepared ALKIS tiles in the plain text input format, `base_<tile>_q20s.txt` for the default mode and
`cq_<tile>_q20s.txt` for the constrained-quality mode, snap 0.01 m) are not in the repository. Run:

    python3 benchmarks/Meshing2Benchmark/herne_ratchet.py --inputs <dir> [--outputs <dir>] [--snapped-area]
        [--dll benchmarks/Meshing2Benchmark/bin/Release/net10.0/Meshing2Benchmark.dll]

Every run must be VALID, and its number of RefinementPinch known violations (a vertex of refinement where two chains meet that
the input's own rounding keeps apart; owner decision on Q17, 2026-11-03, not enforced yet) must equal the limit below exactly:
more is a regression, fewer means the limit goes down with the change that removed them. Exit status 0 when all runs hold,
1 when one does not, 2 when no input was found. With --snapped-area the oracle also reports the mesh area against the snapped
domain (slow: 2.3 to 3.6 times the oracle's time on these inputs).
"""
import argparse
import os
import re
import subprocess
import sys
import time

# RefinementPinch per run, measured at the change that introduced the rule (MESHING2D_HULL_GRID.md section 7); cq 371800 q20 85 -> 92
# with lenses (A), Chew's deletion before a bisection (#370): the deletion changes the mesh, and seven more split points land where two
# chains meet (all pinch vertices are split points, none a circumcentre). A temporary exception (coordinator 2026-11-04): the
# mesher's pinch fix (1) brings this limit back to 85 or below, and its acceptance includes that; the other limits stay
LIMITS = {
    "base_371800_5710600_q20s": 215, "base_371800_5710600_q30s": 217,
    "base_373600_5709000_q20s": 64, "base_373600_5709000_q30s": 66,
    "base_381000_5710400_q20s": 21, "base_381000_5710400_q30s": 21,
    "cq_371800_5710600_q20s": 92, "cq_371800_5710600_q30s": 217,
    "cq_373600_5709000_q20s": 44, "cq_373600_5709000_q30s": 66,
    "cq_381000_5710400_q20s": 11, "cq_381000_5710400_q30s": 21,
}


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--inputs", required=True)
    ap.add_argument("--outputs", default=None)
    ap.add_argument("--dll", default=os.path.join(here, "bin", "Release", "net10.0", "Meshing2Benchmark.dll"))
    ap.add_argument("--snapped-area", action="store_true")
    args = ap.parse_args()
    outputs = args.outputs or os.path.join(args.inputs, "out")
    os.makedirs(outputs, exist_ok=True)
    failed = 0
    found = 0
    for name in sorted(LIMITS):
        path = os.path.join(args.inputs, name + ".txt")
        if not os.path.exists(path):
            print(f"{name}: missing, skipped")
            continue
        found += 1
        out = os.path.join(outputs, name + ".out")
        subprocess.run(["dotnet", args.dll, "mesh", "--no-oracle", "--input", path, "--output", out], check=True, capture_output=True)
        cmd = ["dotnet", args.dll, "oracle", "--milestone", "M3", "--input", path, "--output", out]
        if args.snapped_area:
            cmd.append("--snapped-area")
        start = time.time()
        text = subprocess.run(cmd, capture_output=True, text=True).stdout
        seconds = time.time() - start
        valid = any(line.startswith("VALID") for line in text.splitlines())
        match = re.search(r"(\d+) RefinementPinch", text)
        pinches = int(match.group(1)) if match else 0
        area = re.search(r"mesh area - snapped area (-?[0-9.]+) px2", text)
        ok = valid and pinches == LIMITS[name]
        failed += 0 if ok else 1
        print(f"{name}: {'VALID' if valid else 'INVALID'}, RefinementPinch {pinches} (limit {LIMITS[name]})"
              + (f", mesh - snapped area {area.group(1)} px2" if area else "") + f", oracle {seconds:.1f} s" + ("" if ok else "  <-- FAILS"))
    if found == 0:
        print("no Herne input found in " + args.inputs)
        return 2
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
