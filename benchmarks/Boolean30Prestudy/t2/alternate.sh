#!/bin/bash
# alternating: for round in 1..3, for each tool: profile --repeat 5 on a case; prints min-of-mins and median-of-medians per tool
S=${S:-/path/to/work/dir}
case=$1; shift
for round in 1 2 3; do for t in "$@"; do
  dotnet tool_$t/BooleanComparison.dll profile --repeat 5 cases/$case | python3 -c "
import json,sys
for l in sys.stdin:
    r=json.loads(l); print('$t',r['op'],' '.join(map(str,r['ms'])))"
done; done > alt_$case.txt
python3 - <<PY
import collections,statistics
d=collections.defaultdict(list)
for l in open('alt_$case.txt'):
    p=l.split(); d[(p[1],p[0])]+= [int(x) for x in p[2:]]
for k in sorted(d): print('$case',k[0],k[1],'median',statistics.median(d[k]),'min',min(d[k]))
PY
