#!/usr/bin/env python3
"""Tables of README.md from results/levers_all.md (dotnet run ... mode=levers) and levers_all.err (the solve times)."""
import re, sys, os
here = os.path.dirname(os.path.abspath(__file__))
rows = [[c.strip() for c in l.split('|')[1:-1]] for l in open(os.path.join(here, 'levers_all.md')) if l.startswith('| ') and 'shape' not in l]
shapes, levs, d = [], [], {}
for r in rows:
    if len(r) < 12: continue
    if r[0] not in shapes: shapes.append(r[0])
    if r[1] not in levs: levs.append(r[1])
    d[(r[0], r[1])] = r
sing = {s: d[(s, 'base')][7].split('(')[1].rstrip(')') for s in shapes}
print('| lever | ' + ' | '.join(f'{s} ({sing[s]})' for s in shapes) + ' |')
print('|---|' + '---|' * len(shapes))
for lv in levs:
    print(f'| {lv} | ' + ' | '.join((f"{d[(s, lv)][6]} / {d[(s, lv)][3]}" if (s, lv) in d else '-') for s in shapes) + ' |')
solve = [float(m.group(1)) for m in re.finditer(r'integrable-scale: .*solve ([0-9.]+) s', open(os.path.join(here, 'levers_all.err')).read())]
print()
print('| shape | base: position + extraction (s) | integrable-scale: solve + position + extraction (s) |')
print('|---|---|---|')
for k, s in enumerate(shapes):
    b = d[(s, 'base')]; i = d[(s, 'integrable-scale')]
    print(f'| {s} | {float(b[12]) + float(b[13]):.2f} | {(solve[k] if k < len(solve) else float("nan")):.2f} + {i[12]} + {i[13]} |')
