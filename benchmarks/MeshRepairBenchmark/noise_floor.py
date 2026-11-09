#!/usr/bin/env python3
"""Sampling noise of the fidelity measures, per mesh and metric: the spread (max - min) of each value over runs of the
same results with different sampling seeds (BENCH_FM_SEED). The noise-aware gate of paired_fidelity.py (--noise) lets a
value be worse than the baseline only by more than max(5 %, this spread). Deterministic values (triangle quality,
topology) have spread 0.
Usage: noise_floor.py out.json results/base_seed1.jsonl results/base_seed2.jsonl ... [--report]
   or: noise_floor.py out.json results/base.jsonl [--report], a run with BENCH_FM_SEEDS=2,3,4,5 (the other seeds
       under fm_seeds of each row)"""
import json, math, statistics, sys


def finite(v):
    if isinstance(v, bool):
        return None
    try:
        v = float(v)
        return v if math.isfinite(v) else None
    except (TypeError, ValueError):
        return None


def values(row):
    """the values paired_fidelity.py judges, under the names it judges them"""
    out = {}
    for k, v in row.items():
        if k.startswith(('fm_', 'hd_', 'iou')) and not k.endswith('_ms') and finite(v) is not None:
            out[k] = finite(v)
    q = row.get('fm_q_out') or {}
    for key in ('minangle', 'sj', 'maxangle', 'aspect', 'cond'):
        dist = q.get(key)
        if isinstance(dist, list):
            for n, v in enumerate(dist):
                if finite(v) is not None:
                    out[f'fm_q_out.{key}[{n}]'] = finite(v)
    tris = finite(q.get('tris'))
    for key in ('minangle_lt30', 'minangle_lt10', 'minangle_lt1', 'maxangle_gt90', 'maxangle_gt160', 'aspect_gt13', 'sj_lt05', 'cond_gt13'):
        if tris and finite(q.get(key)) is not None:
            out[f'fm_q_out.{key}/tris'] = finite(q.get(key)) / tris
    if finite(q.get('degenerate')) is not None:
        out['fm_q_out.degenerate'] = finite(q.get('degenerate'))
    return out


def noise(runs):
    files = set.intersection(*[set(r) for r in runs])
    result = {}
    for f in sorted(files):
        per = [values(r[f]) for r in runs]
        keys = set.intersection(*[set(p) for p in per])
        result[f] = {k: max(p[k] for p in per) - min(p[k] for p in per) for k in sorted(keys)}
    return result


def main(argv):
    out, paths = argv[0], [a for a in argv[1:] if not a.startswith('--')]
    runs = [{r['file']: r for r in (json.loads(l) for l in open(p) if l.strip())} for p in paths]
    if len(runs) == 1:
        # one run with the other seeds under fm_seeds: one run per seed (the deterministic fields from the row)
        base = runs[0]
        seeds = sorted({s for r in base.values() for s in (r.get('fm_seeds') or {})}, key=int)
        runs = [base] + [{f: {**r, **(r.get('fm_seeds') or {}).get(s, {})} for f, r in base.items() if s in (r.get('fm_seeds') or {})}
                         for s in seeds]
    n = noise(runs)
    json.dump(n, open(out, 'w'), indent=1, sort_keys=True)
    if '--report' in argv:
        # metrics whose spread exceeds 5 % of the seed-1 value on some mesh: where the #199 gate is below the noise
        base = runs[0]
        over = {}
        for f, m in n.items():
            v = values(base[f])
            for k, s in m.items():
                if s > .05 * abs(v.get(k, 0)):
                    over.setdefault(k, []).append(f)
        print(f'{len(runs)} seeds, {len(n)} meshes; metrics with spread above 5 % of the baseline (meshes):')
        for k, fs in sorted(over.items(), key=lambda x: -len(x[1])):
            print(f'  {k:28} {len(fs):3}  median spread {statistics.median(n[f][k] for f in fs):.4g}')


if __name__ == '__main__':
    main(sys.argv[1:])
