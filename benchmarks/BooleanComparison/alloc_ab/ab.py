# A/B: per case, base and cand tool alternately, each one process `profile --repeat R --median --safe-rounding --ops union`
#   python3 ab.py <out.jsonl> <R> <toolA> <toolB> <case>...
import json, subprocess, sys, os
out, R, A, B, cases = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5:]
done = set()
if os.path.exists(out):
    done = {(json.loads(l)['case'], json.loads(l)['tool']) for l in open(out)}
for c in cases:
    for t in (A, B):
        if (c, t) in done: continue
        r = subprocess.run(['dotnet', t + '/BooleanComparison.dll', 'profile', '--repeat', R, '--median', '--safe-rounding', '--ops', 'union', c], capture_output=True, text=True)
        d = json.loads(r.stdout); d['case'] = c; d['tool'] = t
        open(out, 'a').write(json.dumps(d) + '\n')
