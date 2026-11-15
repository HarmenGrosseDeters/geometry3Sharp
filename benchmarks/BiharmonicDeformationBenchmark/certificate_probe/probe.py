import math, sys, time
import numpy as np
import scipy.sparse as sp
import scipy.sparse.linalg as sla
import mpmath as mp
mp.mp.dps = 70
n=int(sys.argv[1]); theta=math.radians(31); axis=np.array([1.,2.,3.]);axis=axis/np.linalg.norm(axis); x,y,z=axis
K=np.array([[0.,-z,y],[z,0.,-x],[-y,x,0.]])
R=np.eye(3)*math.cos(theta)+(1-math.cos(theta))*np.outer(axis,axis)+math.sin(theta)*K
points=np.array([R @ np.array([i/(n-1),j/(n-1),0.]) for j in range(n) for i in range(n)])
faces=[]
for j in range(n-1):
 for i in range(n-1):
  a=i+n*j; b=a+1; d=a+n; c=d+1; faces.extend(((a,b,c),(a,c,d)))
N=len(points); origin=points[0]; scale=np.max(np.abs(points-origin)); p=(points-origin)/scale
fixed=np.array([i for i in range(N) if i%n in (0,n-1)],dtype=int); isfixed=np.zeros(N,bool);isfixed[fixed]=True; free=np.flatnonzero(~isfixed)
target=points.copy();target[fixed[fixed%n==n-1],2]+=0.1
h=np.zeros((N,3));h[fixed]=(target[fixed]-points[fixed])/scale
rr=[];cc=[];vv=[];mass=np.zeros(N)
for a,b,c in faces:
 ab=p[b]-p[a]; ac=p[c]-p[a]; area=np.linalg.norm(np.cross(ab,ac)); w=[np.dot(ab,ac)/(2*area),np.dot(-ab,ac-ab)/(2*area),np.dot(-ac,ab-ac)/(2*area)]
 for (u,v),q in zip(((b,c),(a,c),(a,b)),w): rr.extend((u,v,u,v));cc.extend((u,v,v,u));vv.extend((q,q,-q,-q))
 for v in (a,b,c):mass[v]+=area/6
L=sp.coo_matrix((vv,(rr,cc)),shape=(N,N)).tocsr();Q=L.T@sp.diags(1/mass)@L
if len(sys.argv)>3 and sys.argv[3]=="eigen":
 start=time.perf_counter(); estimate=sla.eigsh(Q[free][:,free], k=1, sigma=0.0, which="LM", return_eigenvectors=False, v0=np.ones(len(free)))[0]
 print(f"numerical_eigenvalue_estimate={estimate:.12g} elapsed_seconds={time.perf_counter()-start:.3f} (not a verified lower bound)")
 # Rump's simple trace-based bound, evaluated in ordinary arithmetic as a
 # feasibility indicator only. It does not bound mesh assembly errors and is
 # not rounded outwards. The shifted LU below is not a Cholesky certificate.
 nf=len(free); eps=2.0**-53; gamma=(nf+1)*eps/(1-(nf+1)*eps)
 trace=float(Q[free][:,free].diagonal().sum()); roundoff=gamma/(1-gamma)*trace
 print(f"rump_trace_indicator={roundoff:.12g} proposed_shift={0.5*estimate:.12g} indicator_over_shift={roundoff/(0.5*estimate):.12g} (not a certificate)")
 start=time.perf_counter(); fac=sla.splu(Q[free][:,free].tocsc(),permc_spec="MMD_AT_PLUS_A",diag_pivot_thresh=0)
 lower=fac.L.tocsr(); diagonal=fac.U.diagonal(); symmetric_permutation=np.array_equal(fac.perm_r,fac.perm_c)
 factor_difference=(fac.U-sp.diags(diagonal)@lower.T).tocoo(); factor_mismatch=np.max(np.abs(factor_difference.data)) if factor_difference.nnz else 0.0
 comparison=sp.eye(len(free),format="csr") - abs(lower-sp.eye(len(free),format="csr"))
 ones=np.ones(len(free)); row=sla.spsolve_triangular(comparison,ones,lower=True); col=sla.spsolve_triangular(comparison.T.tocsr(),ones,lower=False)
 with np.errstate(over="ignore",invalid="ignore",divide="ignore"):
  estimate_lower=np.min(diagonal)/(np.max(row)*np.max(col))
 print(f"comparison_inverse_bound_estimate={estimate_lower:.12g} row_norm={np.max(row):.12g} col_norm={np.max(col):.12g} L_nnz={lower.nnz} symmetric_permutation={symmetric_permutation} factor_mismatch={factor_mismatch:.12g} elapsed_seconds={time.perf_counter()-start:.3f} (not interval certified)")
 start=time.perf_counter(); shifted=Q[free][:,free].tocsc()-sp.eye(len(free),format="csc")*(0.5*estimate)
 shifted_factor=sla.splu(shifted,permc_spec="MMD_AT_PLUS_A",diag_pivot_thresh=0)
 print(f"shifted_float_LDL mu={0.5*estimate:.12g} minimum_pivot={np.min(shifted_factor.U.diagonal()):.12g} L_nnz={shifted_factor.L.nnz} symmetric_permutation={np.array_equal(shifted_factor.perm_r,shifted_factor.perm_c)} elapsed_seconds={time.perf_counter()-start:.3f} (not a proof of definiteness)")
for k in range(3):h[free,k]=sla.spsolve(Q[free][:,free],-(Q[free][:,fixed]@h[fixed,k]))
# Original binary64 input, normalized in high precision, not the rounded scipy assembly.
s=mp.mpf(float(scale)); P=[[ (mp.mpf(float(points[i,k]))-mp.mpf(float(origin[k])))/s for k in range(3)] for i in range(N)]
H=[[mp.mpf(float(h[i,k])) for k in range(3)] for i in range(N)]
for i in fixed:
 H[i]=[(mp.mpf(float(target[i,k]))-mp.mpf(float(points[i,k])))/s for k in range(3)]
rows=[{} for _ in range(N)]; M=[mp.mpf(0) for _ in range(N)]; minc=None
for a,b,c in faces:
 ab=[P[b][k]-P[a][k] for k in range(3)];ac=[P[c][k]-P[a][k] for k in range(3)]
 cross=[ab[1]*ac[2]-ab[2]*ac[1],ab[2]*ac[0]-ab[0]*ac[2],ab[0]*ac[1]-ab[1]*ac[0]]; area=mp.sqrt(sum(v*v for v in cross))
 dot=lambda u,v:sum(u[k]*v[k] for k in range(3))
 w=(dot(ab,ac)/(2*area),dot([-v for v in ab],[ac[k]-ab[k] for k in range(3)])/(2*area),dot([-v for v in ac],[ab[k]-ac[k] for k in range(3)])/(2*area))
 edge2=dot(ab,ab)+dot(ac,ac)+dot([ac[k]-ab[k] for k in range(3)],[ac[k]-ab[k] for k in range(3)])
 bound=area/(2*edge2);minc=bound if minc is None else min(minc,bound)
 for (u,v),q in zip(((b,c),(a,c),(a,b)),w):
  for i,j,val in ((u,u,q),(v,v,q),(u,v,-q),(v,u,-q)):rows[i][j]=rows[i].get(j,mp.mpf(0))+val
 for i in (a,b,c):M[i]+=area/6
def residual():
 lap=[[sum(w*H[j][k] for j,w in row.items()) for k in range(3)] for row in rows]
 grad=[[mp.mpf(0) for _ in range(3)] for i in range(N)]
 for i,row in enumerate(rows):
  for j,w in row.items():
   if not isfixed[j]:
    for k in range(3):grad[j][k]+=w*lap[i][k]/M[i]
 return grad

tau=n*sum(min(i,n-1-i) for i in range(1,n-1));eigen=minc**2/(max(M)*tau*tau)
print(f'grid={n}x{n} vertices={N} free={len(free)} precision=70dps')
refinements=int(sys.argv[2]) if len(sys.argv)>2 else 0
solve=sla.factorized(Q[free][:,free].tocsc()) if refinements else None
for iteration in range(refinements+1):
 grad=residual();norms=[mp.sqrt(sum(grad[i][k]**2 for i in free)) for k in range(3)]
 print('refinements=',iteration,'eigenvalue_lower_bound=',mp.nstr(eigen,12),'residual_2norm=',','.join(mp.nstr(t,12) for t in norms))
 print('estimated_error_bound=',','.join(mp.nstr(t/eigen,12) for t in norms),'relative_tolerance=1e-8')
 if iteration==refinements: break
 for k in range(3):
  correction=solve(-np.array([float(grad[i][k]) for i in free]))
  h[free,k]+=correction
  for j in free: H[j][k]=mp.mpf(float(h[j,k]))
