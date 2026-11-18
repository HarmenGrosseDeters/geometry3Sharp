import json, subprocess, sys, os
sys.path.insert(0,'/tmp/claude-0/pb/tools'); import classify as C
env = dict(os.environ, DOTNET_PROCESSOR_COUNT='1', PATH=os.environ['PATH'] + ':/root/.dotnet/tools')
# argv: out.jsonl then groups separated by '--' : label cmd...
out=open(sys.argv[1],'w'); groups=[]; cur=[]
for a in sys.argv[2:]:
    if a=='--':
        groups.append(cur); cur=[]
    else: cur.append(a)
groups.append(cur)
for g in groups:
    label=g[0]; cmd=g[1:]; tr='/tmp/claude-0/pb/tr_gen'
    subprocess.run(['dotnet-trace','collect','--profile','dotnet-sampled-thread-time','--format','Speedscope','-o',tr,'--']+cmd,env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=900)
    b=C.analyse(tr+'.speedscope.json'); s=C.summary(b); s.update(case=label,buckets={k:round(v,1) for k,v in b.items()})
    out.write(json.dumps(s)+'\n'); out.flush(); print(label,round(s['T_ms']),s['samples'],round(s['filter_pct'],1),round(s['exact_pct'],1),flush=True)
