#!/usr/bin/env python3
"""Counts, per corpus output file, the runs whose oracle report says '... excused at a small angle of constraints that meet in the
tube' (NearMeetAngleExcused, PR #666) and the sum of those counts. usage: count_near_meet.py FILE...  Exit code 0 always."""
import re
import sys

PAT = re.compile(r'(\d+) excused at a small angle of constraints that meet in the tube')
for path in sys.argv[1:]:
    runs, total = [], 0
    for line in open(path, encoding='utf-8', errors='replace'):
        m = PAT.search(line)
        if m:
            n = int(m.group(1)); total += n; runs.append((line.split(':')[0], n))
    print(f'{path}: {len(runs)} runs, {total} triangles excused by the near-meet rule')
    for name, n in runs:
        print(f'  {name}: {n}')
