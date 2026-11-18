import json, subprocess, sys, os, math, re
sys.path.insert(0,'/tmp/claude-0/pb/tools'); import classify as C
env = dict(os.environ, DOTNET_PROCESSOR_COUNT='1', PATH=os.environ['PATH'] + ':/root/.dotnet/tools')
rows=[l.split('|') for l in open('/tmp/claude-0/pb/si_plain.txt') if l.startswith('| ') and not l.startswith('| case')]
out=open('/tmp/claude-0/pb/prof_selfint.jsonl','w')
for r in rows:
    name=r[1].strip(); ms=float(r[5]); runs=int(min(max(math.ceil(2000/max(ms,5)),1),60))
    tr='/tmp/claude-0/pb/tr_si'
    subprocess.run(['dotnet-trace','collect','--profile','dotnet-sampled-thread-time','--format','Speedscope','-o',tr,'--','/tmp/claude-0/pb/SelfIntersectionBenchmark/SelfIntersectionBenchmark','only=^'+name+'$','runs=%d'%runs],env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    b=C.analyse(tr+'.speedscope.json'); s=C.summary(b); s.update(case=name,runs=runs,ms=ms,buckets={k:round(v,1) for k,v in b.items()})
    out.write(json.dumps(s)+'\n'); out.flush(); print(name,runs,round(s['T_ms']),s['samples'],round(s['filter_pct'],1),round(s['exact_pct'],1),flush=True)
