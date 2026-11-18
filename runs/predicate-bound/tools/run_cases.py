import json, subprocess, sys, os, math, collections
sys.path.insert(0, '/tmp/claude-0/pb/tools'); import classify as C
env = dict(os.environ, DOTNET_PROCESSOR_COUNT='1', PATH=os.environ['PATH'] + ':/root/.dotnet/tools')
counts = collections.defaultdict(float)
for l in open('/tmp/claude-0/pb/counts_boolean.jsonl'):
    d = json.loads(l); counts[d['case']] += (d.get('ms') or [50])[0]
out = open(sys.argv[1], 'w')
for case in [l.strip() for l in open('/tmp/claude-0/pb/cases.txt')]:
    ms = max(counts.get(case, 50), 5)
    rep = int(min(max(math.ceil(2500 / ms), 2), 150))
    lst = '/tmp/claude-0/pb/one.txt'; open(lst, 'w').write(case + '\n')
    tr = '/tmp/claude-0/pb/tr_case'
    subprocess.run(['dotnet-trace', 'collect', '--profile', 'dotnet-sampled-thread-time', '--format', 'Speedscope', '-o', tr, '--',
                    '/tmp/claude-0/pb/bc/BooleanComparison', 'profile', '--repeat', str(rep), lst], env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    b = C.analyse(tr + '.speedscope.json'); s = C.summary(b)
    s.update(case=case, repeat=rep, ms_one_run=ms, buckets={k: round(v, 1) for k, v in b.items()})
    out.write(json.dumps(s) + '\n'); out.flush()
    print(os.path.basename(case), rep, round(s['T_ms']), s['samples'], round(s['filter_pct'], 1), round(s['exact_pct'], 1), round(s['construct_pct'], 1), flush=True)
