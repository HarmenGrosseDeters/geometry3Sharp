#!/usr/bin/env python3
"""Thread scaling per phase and runtime from the output of run.ps1 -Scaling (#858 / #929).

usage: python3 report.py <results dir | phases.csv> [--stratum scaling-b30] [--subphases]
Reads phases.csv (stratum,runtime,round,threads,mask,case,op,phase,ms) and, for the wall time of the operations, cases.csv next to it.
Per runtime, a markdown table per phase: the time at each thread count (median over the rounds of the sum over all cases and operations),
the speed-up t1 / tn, and the serial floor: t1 * s with s the Amdahl serial fraction fitted to all thread counts (least squares of
t_n = t_1 (s + (1 - s) / n)), ie the time the phase keeps with any number of cores. Phases are the exclusive top-level phases of
ExactMeshBoolean as in benchmarks/Boolean30Prestudy/analyze_scaling.py (assembly without simplify); --subphases adds the sub-phases
(intersections.pairs, merge.points, ...). The last table compares the runtimes per thread count (net48 time / net10.0 time)."""
import csv
import os
import sys
from collections import defaultdict
from statistics import median

PHASES = ['input', 'setup', 'intersections', 'merge', 'triangulation', 'classification', 'simplify', 'assembly', 'rounding']
SUB = ['intersections.trees', 'intersections.cull', 'intersections.pairs', 'intersections.planes', 'intersections.intersect',
       'merge.points', 'merge.crossings', 'merge.points2']


def serial_fraction(times):
    """Least-squares s of t_n = t_1 (s + (1 - s) / n) over {n: t_n} (t_1 from n = 1; None without it or when t_1 is 0)."""
    t1 = times.get(1)
    if not t1:
        return None
    # t_n / t_1 - 1/n = s (1 - 1/n)
    num = sum((t / t1 - 1.0 / n) * (1 - 1.0 / n) for n, t in times.items() if n > 1)
    den = sum((1 - 1.0 / n) ** 2 for n in times if n > 1)
    if den == 0:
        return None
    return min(1.0, max(0.0, num / den))


def load(phases_csv, stratum=None):
    """{(runtime, threads, round, phase): ms summed over cases and operations}, with the exclusive phase values."""
    sums = defaultdict(float)
    with open(phases_csv, newline='') as f:
        for r in csv.DictReader(f):
            if stratum and r['stratum'] != stratum:
                continue
            sums[(r['runtime'], int(r['threads']), int(r['round']), r['phase'])] += float(r['ms'])
    out = {}
    for (rt, n, rd, ph), ms in sums.items():
        out[(rt, n, rd, ph)] = ms
    for (rt, n, rd, ph) in list(out):
        if ph == 'assembly':
            out[(rt, n, rd, ph)] = ms_of(out, rt, n, rd, 'assembly') - ms_of(out, rt, n, rd, 'simplify')
    return out


def ms_of(d, rt, n, rd, ph):
    return d.get((rt, n, rd, ph), 0.0)


def per_phase(values, subphases=False):
    """{runtime: {phase: {threads: median over rounds}}} plus the 'total' (sum of the top-level phases)."""
    rounds = defaultdict(set)
    for (rt, n, rd, ph) in values:
        rounds[(rt, n)].add(rd)
    res = defaultdict(lambda: defaultdict(dict))
    names = PHASES + (SUB if subphases else [])
    for (rt, n), rds in rounds.items():
        for ph in names:
            res[rt][ph][n] = median(ms_of(values, rt, n, rd, ph) for rd in rds)
        res[rt]['total'][n] = median(sum(ms_of(values, rt, n, rd, ph) for ph in PHASES) for rd in rds)
    return res


def table(rt, phases):
    counts = sorted({n for ph in phases.values() for n in ph})
    lines = [f'### {rt}', '',
             '| phase | ' + ' | '.join(f'{n} thr s' for n in counts) + ' | ' + ' | '.join(f'speed-up {n}' for n in counts if n > 1)
             + ' | serial fraction | serial floor s |',
             '|---|' + '---|' * (len(counts) + len([n for n in counts if n > 1]) + 2)]
    for ph, times in phases.items():
        t1 = times.get(1)
        ups = [f'{t1 / times[n]:.2f}' if t1 and times.get(n) else '-' for n in counts if n > 1]
        s = serial_fraction(times)
        floor = f'{t1 * s / 1000:.1f}' if s is not None else '-'
        row = [ph.replace('total', '**total**')] + [f'{times[n] / 1000:.1f}' if n in times else '-' for n in counts] + ups \
            + [f'{s:.2f}' if s is not None else '-', floor]
        lines.append('| ' + ' | '.join(row) + ' |')
    return '\n'.join(lines)


def compare(res):
    rts = sorted(res)
    if len(rts) < 2 or 'net48' not in res:
        return ''
    base = [r for r in rts if r.startswith('net10.0')]
    lines = ['### net48 total against net10.0 per thread count', '', '| threads | ' + ' | '.join(f'{b} s | net48 s | net48 / {b}' for b in base) + ' |',
             '|---|' + '---|---|---|' * len(base)]
    for n in sorted(res['net48']['total']):
        cells = []
        for b in base:
            a, c = res[b]['total'].get(n), res['net48']['total'][n]
            cells += [f'{a / 1000:.1f}' if a else '-', f'{c / 1000:.1f}', f'{c / a:.2f}' if a else '-']
        lines.append(f'| {n} | ' + ' | '.join(cells) + ' |')
    return '\n'.join(lines)


def main(argv):
    path = argv[0]
    stratum = argv[argv.index('--stratum') + 1] if '--stratum' in argv else None
    if os.path.isdir(path):
        path = os.path.join(path, 'phases.csv')
    res = per_phase(load(path, stratum), '--subphases' in argv)
    for rt in sorted(res):
        print(table(rt, res[rt]) + '\n')
    print(compare(res))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
