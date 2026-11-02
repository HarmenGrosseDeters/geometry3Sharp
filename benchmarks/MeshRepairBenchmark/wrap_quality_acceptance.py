#!/usr/bin/env python3
"""Acceptance of the alpha wrap quality pass (stage 7b, owner decisions of 2026-11-02), per mesh, against the
simplified wrap (the run without the pass) on the same wrap cases:
  (c1a) no triangle with a smallest angle below 0.5 degrees (q_lt05 = 0), and the global smallest angle not below
        the simplified wrap's;
  (c1b) the global largest angle not above the simplified wrap's;
  (c1c) at most 0.1 % of the triangles with a largest angle above 170 degrees;
  (c2)  better than the simplified wrap in the 1 %-worst smallest angle and in the shares below 10 and 20 degrees
        and above 150 degrees (the normal budget of (d) is judged by paired_fidelity.py --noise).
The fields are the q_* fields of the benchmark (Program.cs, Fidelity) and fm_q_out. The global angles are compared
with a margin of ANGLE_EPS degrees: an untouched triangle whose corners another operation renumbered gets its angles
rounded differently (seen: 1e-13 degrees), which is not a change of the mesh.
Usage: wrap_quality_acceptance.py results/baseline.jsonl results/candidate.jsonl"""
import json, sys

ANGLE_EPS = 1e-9


def load(path):
    return {r['file']: r for r in (json.loads(l) for l in open(path) if l.strip())}


def check(base, cand):
    """{file: [failed criteria]} for the wrap cases of the candidate"""
    out = {}
    for f, c in sorted(cand.items()):
        if c.get('robust_used_wrap') is not True:
            continue
        b = base.get(f)
        fails = []
        if b is None or 'q_min_angle' not in b or 'q_min_angle' not in c:
            out[f] = ['missing q_* fields']
            continue
        if c['q_lt05'] != 0 or c['q_min_angle'] < b['q_min_angle'] - ANGLE_EPS:
            fails.append('c1a')
        if c['q_max_angle'] > b['q_max_angle'] + ANGLE_EPS:
            fails.append('c1b')
        if c['q_gt170_share'] > 0.001:
            fails.append('c1c')
        p01 = lambda r: r['fm_q_out']['minangle'][1]
        if not (p01(c) > p01(b) and c['q_lt10_share'] < b['q_lt10_share'] and c['q_lt20_share'] < b['q_lt20_share']
                and c['q_gt150_share'] < b['q_gt150_share']):
            fails.append('c2')
        out[f] = fails
    return out


def main(argv):
    res = check(load(argv[0]), load(argv[1]))
    failed = {f: v for f, v in res.items() if v}
    for f, v in res.items():
        print(f"{f:14} {' '.join(v) if v else 'pass'}")
    print(f"{len(res) - len(failed)} of {len(res)} wrap cases pass (c1), (c2)")
    return 1 if failed else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
