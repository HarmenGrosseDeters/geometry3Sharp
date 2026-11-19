#!/usr/bin/env python3
"""Table of the pinch_pull3.sh rows (one JSON line per run): per mesh, mode and PullPinchPoints setting.
Usage: pinch_pull3_table.py results.jsonl"""
import json, sys

def row_of(rec):
    r = rec["row"]
    if not isinstance(r, dict):
        return None
    return r

def pick(r, mode):
    # wrap mode: the raw wrap is the output (out_*); wrappipe: raw = rawx_*, pipeline output = out_*
    if mode == "wrap":
        return {k: r.get("out_" + k) for k in ("tris", "circ_over_alpha_max", "cent_over_alphaoff_max", "min_angle",
                "p01_min_angle", "lt05", "gt170", "closed", "bowties", "self_pairs", "vdist_max_off")}, "raw"
    return {k: r.get("out_" + k) for k in ("tris", "circ_over_alpha_max", "cent_over_alphaoff_max", "min_angle",
            "p01_min_angle", "lt05", "gt170", "closed", "bowties", "self_pairs", "vdist_max_off")}, "pipeline"

def fmt(v):
    if v is None:
        return "-"
    if isinstance(v, bool):
        return "yes" if v else "NO"
    return str(v)

print("| mesh | run | setting | tris | circ/α max | centroid/(α+off) max | min angle | p01 | <0.5° | >170° | "
      "closed | bowties | self-int pairs | max vtx dist/off | flips | pinch_skipped | wall s |")
print("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")
for line in open(sys.argv[1]):
    rec = json.loads(line)
    r = row_of(rec)
    if r is None:
        print(f"| {rec['id']} | {rec['mode']} | pull={rec['pull']} | no row |")
        continue
    vals, which = pick(r, rec["mode"])
    counts = r.get("wrap_counts", {})
    wall = r.get("wrap_ms") if rec["mode"] == "wrap" else r.get("pipe_ms")
    wall = None if wall is None else round(wall / 1000, 2)
    print(f"| {rec['id']} | {rec['mode']} ({which}) | {'PullPinchPoints' if rec['pull'] else 'default'} | "
          f"{fmt(vals['tris'])} | {fmt(vals['circ_over_alpha_max'])} | {fmt(vals['cent_over_alphaoff_max'])} | "
          f"{fmt(vals['min_angle'])} | {fmt(vals['p01_min_angle'])} | {fmt(vals['lt05'])} | {fmt(vals['gt170'])} | "
          f"{fmt(vals['closed'])} | {fmt(vals['bowties'])} | {fmt(vals['self_pairs'])} | {fmt(vals['vdist_max_off'])} | "
          f"{fmt(counts.get('manifold_flips'))} | {fmt(counts.get('pinch_skipped'))} | {fmt(wall)} |")
