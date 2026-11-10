"""The table of the D1 probe: per model and operation, the apex rule (master) against the reference rule (the
prototype), from the rows that `BooleanComparison d1` prints (run.sh).  python3 table.py thingi_2026-11-09.txt"""
import re
import sys

rows = [dict(kv.split('=', 1) for kv in re.findall(r"(\w+=(?:'[^']*'|\S+))", line)) for line in open(sys.argv[1])]
by = {}
for r in rows:
    by.setdefault((r['model'], r['op']), {})[r['rule']] = r
clean = lambda r: r['notClean'] == "''" and r['roundingClean'] == 'True'
print('| model | operation | apex: degenerate left | drift | smallest rejected | ref: degenerate left | drift | '
      'measured distance | contractions / flips / moves / zips | ms apex / ref |')
print('|---|---|---|---|---|---|---|---|---|---|')
count = dict(apex=0, ref=0, new=0, ops=0)
for key in sorted(by, key=lambda k: (int(k[0]), k[1])):
    a, b = by[key]['apex'], by[key]['ref']
    count['ops'] += 1
    count['apex'] += not clean(a)
    count['ref'] += not clean(b)
    count['new'] += clean(a) and not clean(b)
    measured = max(float(b['sampledH_S_to_S0']), float(b['sampledH_S0_to_S']))
    print(f"| {key[0]} | {key[1]} | {a['remDeg']} | {a['maxDrift']} | {a['smallestRejected']} | {b['remDeg']} | "
          f"{b['maxDrift']} | {measured:.4f} | {b['collapses']} / {b['flips']} / {b['moves']} / {b['zips']} | "
          f"{a['ms']} / {b['ms']} |")
print(f"\noperations {count['ops']}, not clean: apex rule {count['apex']}, reference rule {count['ref']}, "
      f"newly not clean {count['new']}")
