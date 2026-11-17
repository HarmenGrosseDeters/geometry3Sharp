import sys
sys.argv=['x']; exec(open('gen.py').read().split('s=icosphere(3,1')[0])
import os
out=sys.argv[0]
D='/home/user/geometry3Sharp/benchmarks/MeshRepairBenchmark/data/ids/meshes'
lst=open('sweep_list.tsv','w')
def q(V): return [tuple(round(x*4096)/4096 for x in v) for v in V]
big=icosphere(3,1,(0,0,0)); big=(q(big[0]),big[1])
for r in (4,8,12,16,20,24):
    for f in (2,8,16):
        h=2.0**-r; c=(2.0**f,0,0)
        # v0 clean box, v1 two overlapping boxes, v2 open small sphere
        b=cube(h,c); b2=cube(h,(c[0]+h,c[1]+h*0.5,c[2]+h*0.25))
        sV,sF=icosphere(2,h,c)
        for name,parts in (("v0",[big,b]),("v1",[big,b,b2]),("v2",[big,(sV,sF[:-6])])):
            k=f"sw_{name}_r{r}_f{f}"
            write(f"{D}/{k}.obj",parts); lst.write(f"{k}\t{'first' if name!='v2' else 'holdout'}\t{name}\n")
