# per case, configs alternately (same tool), each one process `profile --repeat R --median --safe-rounding --ops union`
#   python3 ab_env.py <out.jsonl> <R> <tool> <case>...    configs: default, gen0 (DOTNET_GCgen0size=1 GB), server
import json, subprocess, sys, os
out, R, T, cases = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4:]
CFG = {'default': {}, 'gen0': {'DOTNET_GCgen0size': '0x40000000'}, 'server': {'DOTNET_gcServer': '1'}}
done = set()
if os.path.exists(out):
    done = {(json.loads(l)['case'], json.loads(l)['tool']) for l in open(out)}
for c in cases:
    for name, env in CFG.items():
        if (c, name) in done: continue
        r = subprocess.run(['dotnet', T + '/BooleanComparison.dll', 'profile', '--repeat', R, '--median', '--safe-rounding', '--ops', 'union', c],
                           capture_output=True, text=True, env={**os.environ, **env})
        d = json.loads(r.stdout); d['case'] = c; d['tool'] = name
        open(out, 'a').write(json.dumps(d) + '\n')
