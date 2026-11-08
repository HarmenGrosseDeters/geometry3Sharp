#!/usr/bin/env python3
"""Lock spinning in sampled traces (trace.sh output): time in Monitor.Enter_Slowpath by the nearest geometry3Sharp
caller, against the busy time (waits and spinning dropped) of the whole operation.

usage: python3 lock_spin.py <trace.speedscope.json>...
Thread-time samples: spinning threads compete for the cores with working ones, so this is an upper bound of what a
lock-free variant could save, not a measured gain (an A/B decides)."""
import collections, json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import trace_split as T

for path in sys.argv[1:]:
    d = json.load(open(path))
    names = [f['name'] for f in d['shared']['frames']]
    spin = ['Enter_Slowpath' in n for n in names]
    wait = [any(w in n for w in T.WAIT if w != 'Enter_Slowpath') for n in names]
    g3 = ['geometry3Sharp!' in n for n in names]
    by, busy, spun = collections.Counter(), 0.0, 0.0
    for prof in d['profiles']:
        for st, w in T.stacks(prof):
            if any(wait[f] for f in st):
                continue
            if any(spin[f] for f in st):
                spun += w
                caller = next((names[f] for f in reversed(st) if g3[f]), '?')
                by[caller.split('!')[-1].split('(')[0]] += w
            else:
                busy += w
    print(json.dumps({'trace': os.path.basename(path), 'busy_ms': round(busy), 'spin_ms': round(spun),
                      'spin_share_of_busy': round(spun / busy, 3) if busy else 0,
                      'by_caller': {k: round(v) for k, v in by.most_common(5)}}))
