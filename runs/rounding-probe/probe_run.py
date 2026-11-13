import subprocess, json, sys, os
out=open(sys.argv[1],'a'); rep=int(sys.argv[2]); ops=sys.argv[3].split(','); cases=sys.argv[4:]
env=dict(os.environ, G3_ROUND_PROBE='1')
for c in cases:
    for op in ops:
        r=subprocess.run(['dotnet','tool_probe/BooleanComparison.dll','profile','--repeat',str(rep),'--safe-rounding','--ops',op,c],capture_output=True,text=True,env=env)
        probes=[json.loads(l[len('ROUNDPROBE '):]) for l in r.stderr.splitlines() if l.startswith('ROUNDPROBE ')][-rep:]
        prof=[json.loads(l) for l in r.stdout.splitlines() if l.startswith('{')]
        if not probes or not prof: print('no data',c,op,flush=True); continue
        probes.sort(key=lambda d:d['total']); m=probes[len(probes)//2]
        d=prof[-1]
        ms=d['ms']; ms=sorted(ms)[len(ms)//2] if isinstance(ms,list) else ms
        m.update(case=c,op=op,boolean_ms=ms,phase_rounding=d['phases_ms'].get('rounding'),rounding_clean=d.get('rounding_clean'),limit=d.get('rounding_limit'))
        out.write(json.dumps(m)+'\n'); out.flush()
