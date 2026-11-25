# Sums the per-cell table of CandidateProbeRun (the output of a probe run) into the bounds per case.
# usage: python3 sum_candidates.py <probe output>
import re,sys
cur=None; net={}; rows={}
for line in open(sys.argv[1]):
    m=re.match(r'#### (\S+): caller .* net (\d+) ms; filter',line)
    if m: cur=m.group(1); net[cur]=float(m.group(2)); rows[cur]=[]; continue
    if line.startswith('| ') and cur and line.count('|')==14:
        c=[x.strip() for x in line.strip().strip('|').split('|')]
        if c[0]=='site' or not re.match(r'^[\d.]+$',c[3]): continue
        rows[cur].append(c)
print("| case | net ms | filter (micro) | (b) pretest all | (b) neighbours only | (a) unshared pairs' filter time | (c) ideal 2D unshared | preamble share |")
print("|---|---:|---:|---:|---:|---:|---:|---:|")
for k,v in rows.items():
    n=net[k]
    f=sum(float(c[9]) for c in v)
    allp=sum(float(c[10]) for c in v)
    nb=sum(float(c[10]) for c in v if c[1]!='0')
    g0=sum(float(c[9]) for c in v if c[1]=='0')
    i2=sum(float(c[12]) for c in v)
    # preamble: calls*preamble ns = filter ms * pre/filter ns
    pre=sum(float(c[9])*float(c[4])/float(c[3]) for c in v)
    print(f"| {k} | {n:.0f} | {100*f/n:.1f} % | {100*allp/n:+.1f} % | {100*nb/n:+.1f} % | {100*g0/n:.1f} % | {100*i2/n:.1f} % | {100*pre/n:.1f} % |")
