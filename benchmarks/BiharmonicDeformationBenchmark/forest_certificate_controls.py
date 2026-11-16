"""Independent exact certificates/solutions for small represented planar meshes.

Reconstructs cotangent L, barycentric M, Q and the handle system with Fraction;
checks positive exact LDL pivots of Qff minus the reported geometric gap and
compares final production positions to the exact solution. Shares no forest,
interval arithmetic or deformer implementation. Meshes are generated temporarily.
"""
import argparse
from fractions import Fraction as F
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import tempfile


def number(raw):
    return F(struct.unpack('>d',struct.pack('>q',raw))[0])


def build(data):
    original=[[number(v) for v in p] for p in data['vertices_bits']]
    scale=number(data['scale_bits'])
    p=[[(v[k]-original[0][k])/scale for k in range(3)] for v in original]
    assert all(v[2]==0 for v in p)
    rows=[{} for _ in p];mass=[F(0) for _ in p]
    for face in data['faces']:
        a,b,c=[p[i] for i in face]
        area2=abs((b[0]-a[0])*(c[1]-a[1])-(b[1]-a[1])*(c[0]-a[0]))
        assert area2>0
        for i in face:mass[i]+=area2/6
        for k in range(3):
            i,j,o=face[k],face[(k+1)%3],face[(k+2)%3]
            w=sum((p[i][d]-p[o][d])*(p[j][d]-p[o][d]) for d in range(2))/(2*area2)
            for r,s,sign in ((i,i,1),(j,j,1),(i,j,-1),(j,i,-1)):
                rows[r][s]=rows[r].get(s,F(0))+sign*w
    n=len(p);q=[[F(0) for _ in p] for _ in p]
    for k,row in enumerate(rows):
        assert mass[k]>0
        for i,li in row.items():
            for j,lj in row.items():q[i][j]+=li*lj/mass[k]
    return original,p,scale,q


def ldl(a):
    n=len(a);lower=[[F(i==j) for j in range(n)] for i in range(n)];diagonal=[]
    for j in range(n):
        d=a[j][j]-sum(lower[j][k]**2*diagonal[k] for k in range(j))
        if d<=0:return None
        diagonal.append(d)
        for i in range(j+1,n):
            lower[i][j]=(a[i][j]-sum(lower[i][k]*lower[j][k]*diagonal[k] for k in range(j)))/d
    return lower,diagonal


def solve(factor,b):
    lower,d=factor;n=len(b);y=[]
    for i in range(n):y.append(b[i]-sum(lower[i][j]*y[j] for j in range(i)))
    x=[F(0)]*n
    for i in reversed(range(n)):x[i]=y[i]/d[i]-sum(lower[j][i]*x[j] for j in range(i+1,n))
    return x


def check(data):
    original,p,scale,q=build(data)
    free=[i for i,fixed in enumerate(data['fixed']) if not fixed]
    fixed=[i for i,fixed in enumerate(data['fixed']) if fixed]
    a=[[q[i][j] for j in free] for i in free]
    gap=F(int(data['geometric_gap_units']),2**data['gap_fraction_bits'])
    assert gap>0
    assert ldl([[v-(gap if i==j else 0) for j,v in enumerate(row)] for i,row in enumerate(a)])
    bad=max(a[i][i] for i in range(len(a)))+1
    assert ldl([[v-(bad if i==j else 0) for j,v in enumerate(row)] for i,row in enumerate(a)]) is None
    factor=ldl(a);assert factor
    handles=[[(number(data['target_bits'][i][axis])-original[i][axis])/scale for axis in range(3)] for i in range(len(p))]
    exact=[solve(factor,[-sum(q[i][j]*handles[j][axis] for j in fixed) for i in free]) for axis in range(3)]
    candidate=[[(number(data['candidate_bits'][i][axis])-original[i][axis])/scale for axis in range(3)] for i in range(len(p))]
    for i in fixed:assert data['candidate_bits'][i]==data['target_bits'][i]
    error=max(sum((candidate[i][axis]-exact[axis][k])**2 for axis in range(3)) for k,i in enumerate(free))
    rest=max(sum(v*v for v in point) for point in p)
    response=max(sum(exact[axis][k]**2 for axis in range(3)) for k in range(len(free)))
    tol=F(1e-8)
    assert error<=tol*tol*rest and error<=tol*tol*response
    corrupted=[point[:] for point in candidate];corrupted[free[0]][2]+=F(1,1000)
    bad_error=max(sum((corrupted[i][axis]-exact[axis][k])**2 for axis in range(3)) for k,i in enumerate(free))
    assert bad_error>tol*tol*rest
    return gap,float(error)


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('dotnet');parser.add_argument('producer');args=parser.parse_args()
    producer=Path(args.producer).resolve()
    print('producer_sha256='+hashlib.sha256(producer.read_bytes()).hexdigest(),flush=True)
    print('library_sha256='+hashlib.sha256((producer.parent/'geometry3Sharp.dll').read_bytes()).hexdigest(),flush=True)
    with tempfile.TemporaryDirectory(prefix='bih-forest-control-') as tmp:
        for n,orientation in ((5,'aligned'),(6,'aligned'),(5,'sheared')):
            path=Path(tmp)/f'{n}-{orientation}.json'
            result=subprocess.run([args.dotnet,str(producer),'--certificate-geometric',str(n),orientation,'strict',str(path)],capture_output=True,text=True,timeout=30)
            print(result.stdout+result.stderr,end='',flush=True)
            assert result.returncode==0 and 'certificate_route=forest' in result.stdout
            gap,error=check(json.loads(path.read_text()))
            print(f'grid={n} orientation={orientation} input_sha256={hashlib.sha256(path.read_bytes()).hexdigest()} exact_gap_ldl=True exact_solution_accuracy=True bad_gap_rejected=True corrupted_output_rejected=True gap_lower={float(gap):.12g} max_normalized_squared_error={error:.12g}',flush=True)
