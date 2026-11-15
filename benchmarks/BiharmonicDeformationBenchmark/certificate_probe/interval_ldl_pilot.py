"""Independent small-grid decimal interval LDL experiment; not production code.

The inputs are exact binary64 points and a binary64 shift. Decimal arithmetic uses
outward rounding. SciPy chooses only a permutation and a trial shift; neither is
trusted as a proof. A positive interval pivot sequence encloses the LDL elimination
of the shifted matrix, subject to this pilot's arithmetic and implementation review.
"""
import decimal, hashlib, json, math, struct, sys, time, resource
import numpy as np
import scipy.sparse as sp
import scipy.sparse.linalg as sla
from scipy.sparse.csgraph import reverse_cuthill_mckee

n=int(sys.argv[1]); precision=int(sys.argv[2]) if len(sys.argv)>2 else 40
shift_multiplier=float(sys.argv[3]) if len(sys.argv)>3 else 0.5
perturb=float(sys.argv[4]) if len(sys.argv)>4 else 0.0
backend=sys.argv[5] if len(sys.argv)>5 else "indexed"
if backend not in ("indexed", "scan"): raise ValueError("backend must be indexed or scan")
if n<3 or n>50: raise ValueError('pilot grid size must be 3..50')
loctx=decimal.Context(prec=precision,rounding=decimal.ROUND_FLOOR,Emin=-999999,Emax=999999)
hictx=decimal.Context(prec=precision,rounding=decimal.ROUND_CEILING,Emin=-999999,Emax=999999)
nearctx=decimal.Context(prec=precision,rounding=decimal.ROUND_HALF_EVEN,Emin=-999999,Emax=999999)
D=decimal.Decimal
class I:
 __slots__=('lo','hi')
 def __init__(self,lo,hi=None): self.lo=lo; self.hi=lo if hi is None else hi
 def __add__(a,b): return I(loctx.add(a.lo,b.lo),hictx.add(a.hi,b.hi))
 def __neg__(a): return I(a.hi.copy_negate(),a.lo.copy_negate())
 def __sub__(a,b): return a+(-b)
 def __mul__(a,b):
  p=(loctx.multiply(a.lo,b.lo),loctx.multiply(a.lo,b.hi),loctx.multiply(a.hi,b.lo),loctx.multiply(a.hi,b.hi))
  q=(hictx.multiply(a.lo,b.lo),hictx.multiply(a.lo,b.hi),hictx.multiply(a.hi,b.lo),hictx.multiply(a.hi,b.hi))
  return I(min(p),max(q))
 def __truediv__(a,b):
  if b.lo<=0<=b.hi: raise ArithmeticError('interval divisor contains zero')
  return a*I(loctx.divide(D(1),b.hi),hictx.divide(D(1),b.lo))
 def sqrt(a):
  if a.hi<0: raise ArithmeticError('negative square root')
  l=max(a.lo,D(0)); u=a.hi
  # Decimal.sqrt is correctly rounded to nearest; widening one representable
  # decimal in each direction encloses the exact roots at this precision.
  x=nearctx.sqrt(l);y=nearctx.sqrt(u)
  return I(max(D(0),nearctx.next_minus(x)),nearctx.next_plus(y))
def exact(v): return I(D.from_float(float(v)))
def dot(a,b): return sum((a[k]*b[k] for k in range(3)),I(D(0)))
def sub(a,b): return [a[k]-b[k] for k in range(3)]
def cross(a,b): return [a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]]
def add(row,key,v): row[key]=row.get(key,I(D(0)))+v

theta=math.radians(31);axis=np.array([1.,2.,3.]);axis/=np.linalg.norm(axis);x,y,z=axis
K=np.array([[0.,-z,y],[z,0.,-x],[-y,x,0.]])
R=np.eye(3)*math.cos(theta)+(1-math.cos(theta))*np.outer(axis,axis)+math.sin(theta)*K
points=np.array([R@np.array([i/(n-1),j/(n-1),0.]) for j in range(n) for i in range(n)])
origin=points[0];scale=float(np.max(np.abs(points-origin))); N=n*n
p=[[ (exact(points[i,k])-exact(origin[k]))/exact(scale) for k in range(3)] for i in range(N)]
faces=[]
for j in range(n-1):
 for i in range(n-1):
  a=i+n*j;b=a+1;d=a+n;c=d+1;faces.extend(((a,b,c),(a,c,d)))
fixed=np.array([i%n in (0,n-1) for i in range(N)]);free=np.flatnonzero(~fixed);mapfree={int(v):i for i,v in enumerate(free)}
rows=[{} for _ in range(N)];mass=[I(D(0)) for _ in range(N)];start=time.perf_counter()
for a,b,c in faces:
 ab=sub(p[b],p[a]);ac=sub(p[c],p[a]);bc=sub(p[c],p[b]);ba=sub(p[a],p[b]);ca=sub(p[a],p[c]);cb=sub(p[b],p[c])
 cr=cross(ab,ac); area=dot(cr,cr).sqrt()
 if area.lo<=0: raise ArithmeticError('triangle area interval contains zero')
 weights=[dot(ab,ac)/area*I(D('0.5')),dot(ba,bc)/area*I(D('0.5')),dot(ca,cb)/area*I(D('0.5'))]
 for (u,v),w in zip(((b,c),(a,c),(a,b)),weights):
  add(rows[u],u,w);add(rows[v],v,w);add(rows[u],v,-w);add(rows[v],u,-w)
 for v in (a,b,c): mass[v]=mass[v]+area/I(D(6))
q={}
for k,row in enumerate(rows):
 entries=[(mapfree[j],value) for j,value in row.items() if not fixed[j]]
 for i,(a,wa) in enumerate(entries):
  for b,wb in entries[:i+1]:
   key=(max(a,b),min(a,b));q[key]=q.get(key,I(D(0)))+wa*wb/mass[k]
Nf=len(free); rr=[];cc=[];vv=[]
for (a,b),v in q.items():
 mid=float((v.lo+v.hi)/D(2));rr.append(a);cc.append(b);vv.append(mid)
 if a!=b:rr.append(b);cc.append(a);vv.append(mid)
Q=sp.coo_matrix((vv,(rr,cc)),shape=(Nf,Nf)).tocsc()
est=float(sla.eigsh(Q,k=1,sigma=0,which='LM',return_eigenvectors=False,v0=np.ones(Nf))[0]);mu=shift_multiplier*est
if not math.isfinite(mu) or mu <= 0: raise ValueError('trial shift must be finite and strictly positive')
order=reverse_cuthill_mckee(Q,symmetric_mode=True);inverse=np.empty(Nf,dtype=int);inverse[order]=np.arange(Nf)
a={}
for (r,c),value in q.items():
 i,j=int(inverse[r]),int(inverse[c]);a[(max(i,j),min(i,j))]=value
for i in range(Nf):a[(i,i)]=a[(i,i)]-exact(mu)
if len(sys.argv)>6:
 with open(sys.argv[6],"w",encoding="utf-8") as f:
  bits=lambda value:struct.unpack(">q",struct.pack(">d",float(value)))[0]
  json.dump({"size":Nf,"precision":precision,"shift_binary64":mu.hex(),"shift_bits":bits(mu),"scale_bits":bits(scale),
             "vertices_bits":[[bits(v) for v in point] for point in points],"faces":faces,"fixed":[bool(v) for v in fixed],
             "order":[int(v) for v in order],"entries":[[i,j,str(v.lo),str(v.hi)] for (i,j),v in sorted(a.items())]},f,separators=(",",":"))
print(f'grid={n} vertices={N} free={Nf} precision={precision} numeric_eigen_estimate={est:.12g} mu={mu:.12g} mu_binary64={mu.hex()}')
# Right-looking sparse LDL. Every interval update encloses the exact Schur complement.
pivots=[];min_pivot_interval=None;min_pivot_index=-1;updates=0; pivot_digest=hashlib.sha256(); factor_start=time.perf_counter()
pivot_file=open(sys.argv[7],"w",encoding="utf-8") if len(sys.argv)>7 else None
if backend == "indexed":
 columns=[{} for _ in range(Nf)]
 for (i,j),value in a.items():columns[j][i]=value
 del a
for k in range(Nf):
 pivot=columns[k][k] if backend == "indexed" else a[(k,k)]
 if pivot.lo<=0: print(f'grid={n} precision={precision} status=uncertain pivot={k} interval=[{pivot.lo},{pivot.hi}]');sys.exit(1)
 if min_pivot_interval is None or pivot.lo < min_pivot_interval.lo: min_pivot_interval=pivot;min_pivot_index=k
 pivots.append(pivot.lo)
 pivot_digest.update((str(pivot.lo)+"|"+str(pivot.hi)+"\n").encode("ascii"))
 if pivot_file is not None:pivot_file.write(f"{k} {pivot.lo} {pivot.hi}\n")
 if backend == "indexed":
  neighbors=sorted(i for i in columns[k] if i>k)
  col=[columns[k][i] for i in neighbors]
 else:
  neighbors=sorted(i for (i,j) in a if j==k and i>k)
  col=[a[(i,k)] for i in neighbors]
 for ii,i in enumerate(neighbors):
  factor=col[ii]/pivot
  for jj in range(ii+1):
   j=neighbors[jj]
   if backend == "indexed":columns[j][i]=columns[j].get(i,I(D(0)))-factor*col[jj]
   else:
    key=(i,j);a[key]=a.get(key,I(D(0)))-factor*col[jj]
   updates+=1
 if backend == "indexed": columns[k].clear()
 else:
  for i in neighbors: del a[(i,k)]
if pivot_file is not None:pivot_file.close()
print(f'status=positive_interval_pivots backend={backend} min_pivot_lower={min(pivots)} min_pivot_upper={min_pivot_interval.hi} min_pivot_index={min_pivot_index} updates={updates} pivot_sha256={pivot_digest.hexdigest()} elapsed_factor_seconds={time.perf_counter()-factor_start:.3f} elapsed_total_seconds={time.perf_counter()-start:.3f} peak_rss_kib={resource.getrusage(resource.RUSAGE_SELF).ru_maxrss}')

# Form an independent binary64 candidate using midpoint Q; this candidate is not trusted.
# The right boundary moves by 0.1 in world z; exact double targets are retained.
targets=points.copy()
for i in range(N):
 if i%n==n-1: targets[i,2]+=0.1
# This fixture leaves x/y unchanged at every handle and every free vertex.
# Positive definiteness makes the exact x/y displacements zero as well.
assert np.array_equal(targets[:, :2], points[:, :2])
mid=lambda v: float((v.lo+v.hi)/D(2))
displacement=[I(D(0)) for _ in range(N)]
for i in range(N):
 if fixed[i]: displacement[i]=(exact(targets[i,2])-exact(points[i,2]))/exact(scale)
rhs=np.zeros(Nf)
for k,row in enumerate(rows):
 constrained=sum(mid(w)*mid(displacement[j]) for j,w in row.items() if fixed[j])
 for j,w in row.items():
  if not fixed[j]:rhs[mapfree[j]]-=mid(w)/mid(mass[k])*constrained
solution=sla.spsolve(Q,rhs)
if perturb:solution[Nf//2]+=perturb
# Treat the final rounded world coordinate, rather than an intermediate normalized
# value, as the candidate; all subsequent residual arithmetic is outward-rounded.
candidate=points.copy();candidate[fixed]=targets[fixed]
for i in free:
 world=float(points[i,2]+scale*solution[mapfree[int(i)]])
 candidate[i,2]=world
 displacement[i]=(exact(world)-exact(points[i,2]))/exact(scale)
if len(sys.argv)>6:
 with open(sys.argv[6],"r",encoding="utf-8") as f:export=json.load(f)
 export["target_bits"]=[[bits(v) for v in point] for point in targets]
 export["candidate_bits"]=[[bits(v) for v in point] for point in candidate]
 with open(sys.argv[6],"w",encoding="utf-8") as f:json.dump(export,f,separators=(",",":"))
grad=[I(D(0)) for _ in range(Nf)]
for k,row in enumerate(rows):
 lap=sum((w*displacement[j] for j,w in row.items()),I(D(0)))
 for j,w in row.items():
  if not fixed[j]:grad[mapfree[j]]=grad[mapfree[j]]+w*lap/mass[k]
squared=D(0)
for g in grad:
 magnitude=max(g.lo.copy_abs(),g.hi.copy_abs())
 squared=hictx.add(squared,hictx.multiply(magnitude,magnitude))
residual_upper=nearctx.next_plus(nearctx.sqrt(squared))
error_upper=hictx.divide(residual_upper,D.from_float(mu))
print(f'interval_residual_norm_upper={residual_upper} normalized_error_upper={error_upper} requested_tolerance=1e-8 perturb={perturb}')
if error_upper>D('1e-8'):
 print('position_certificate=not_established');sys.exit(2)
if pivot_file is not None:
 with open(sys.argv[6],"rb") as f: input_digest=hashlib.sha256(f.read()).hexdigest()
 with open(sys.argv[7],"r",encoding="utf-8") as f: pivot_data=f.read()
 with open(sys.argv[7],"w",encoding="utf-8") as f: f.write(f"# input_sha256={input_digest}\n"+pivot_data)
print('position_certificate=within_tolerance x_y_displacement=exactly_zero')
