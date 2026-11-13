# review of #767: the chord identity sum_i S(A_i, A_i+1) = S(A_0, A_k) + Omega, and the dipole remainder
import math, random
def sub(a,b): return (a[0]-b[0],a[1]-b[1],a[2]-b[2])
def add(a,b): return (a[0]+b[0],a[1]+b[1],a[2]+b[2])
def mul(a,s): return (a[0]*s,a[1]*s,a[2]*s)
def dot(a,b): return a[0]*b[0]+a[1]*b[1]+a[2]*b[2]
def cross(a,b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def norm(a): return math.sqrt(dot(a,a))
def unit(a): n=norm(a); return (a[0]/n,a[1]/n,a[2]/n)
def S(A,B,C): return 2*math.atan2(dot(A,cross(B,C)), 1+dot(A,B)+dot(B,C)+dot(C,A))
def rnd_unit(r):
    while True:
        v=(r.uniform(-1,1),r.uniform(-1,1),r.uniform(-1,1)); n=norm(v)
        if 0.1<n<=1: return unit(v)
def chain(r, kind, k, radius):
    pts=[]
    if kind=='loop':
        n=rnd_unit(r); u=unit(cross(n,rnd_unit(r))); v=cross(n,u)
        for i in range(k+1):
            t=2*math.pi*i/(k+1)*r.uniform(0.9,1.0); rr=radius*r.uniform(0.3,1)
            pts.append(add(mul(u,rr*math.cos(t)),add(mul(v,rr*math.sin(t)),mul(n,radius*r.uniform(-0.3,0.3)))))
    elif kind=='zigzag':
        for i in range(k+1): pts.append(mul(rnd_unit(r), radius*r.uniform(0.2,1)))
    else:
        d=rnd_unit(r)
        for i in range(k+1): pts.append(add(mul(d,radius*(2*i/k-1)*0.9), mul(rnd_unit(r),radius*0.1*r.random())))
    return pts
def run(n, theta_lo, theta_hi, seed):
    r=random.Random(seed)
    stats={}
    for it in range(n):
        kind=r.choice(['loop','zigzag','line']); k=r.randint(2,12)
        pts=chain(r,kind,k,1.0)
        c=mul((sum(p[0] for p in pts),sum(p[1] for p in pts),sum(p[2] for p in pts)),1.0/len(pts))
        rad=max(norm(sub(p,c)) for p in pts)
        theta=r.uniform(theta_lo,theta_hi); d=rad/theta
        p=add(c,mul(rnd_unit(r),d))
        u=unit(sub(c,p)); cosa=math.sqrt(1-(rad/d)**2)
        # x1: half of the cases near the cone, the rest anywhere
        x1=rnd_unit(r)
        if r.random()<0.6:
            # a direction at angle near alpha (inside or outside) from +u or -u
            alpha=math.asin(rad/d); ang=alpha*r.uniform(0.0,2.0)
            t=unit(cross(u,rnd_unit(r)))
            x1=add(mul(u,math.cos(ang)),mul(t,math.sin(ang)))
            if r.random()<0.5: x1=mul(x1,-1)
        A=[unit(sub(q,p)) for q in pts]
        lhs=sum(S(A[i],A[i+1],x1) for i in range(k))
        chord=S(A[0],A[k],x1)
        C=unit(sub(c,p))
        omega=sum(S(A[i],A[(i+1)%(k+1)],C) for i in range(k+1))     # the fan from the centre: the solid angle of the closed polygon
        diff=(lhs-chord-omega)/(4*math.pi)
        xu=dot(x1,u)
        cls=('x1 in cap' if xu>cosa else '-x1 in cap' if xu<-cosa else 'both outside')
        s=stats.setdefault(cls,[0,0,0.0])
        s[0]+=1
        if abs(diff)>1e-9: s[1]+=1
        s[2]=max(s[2],abs(diff))
        # the dipole remainder
        va=(0,0,0); aabs=0
        for i in range(k+1):
            cr=cross(sub(pts[i],c),sub(pts[(i+1)%(k+1)],c)); va=add(va,mul(cr,0.5)); aabs+=0.5*norm(cr)
        x=sub(c,p); om1=dot(va,mul(x,1/norm(x)**3))
        bound=aabs*2*rad/(d-rad)**3
        t=stats.setdefault('dipole',[0,0,0.0])
        t[0]+=1
        ratio=abs(omega-om1)/bound if bound>0 else 0
        if ratio>1+1e-9: t[1]+=1
        t[2]=max(t[2],ratio)
    return stats
for (lo,hi) in [(0.02,0.25),(0.25,0.6),(0.6,0.95)]:
    st=run(200000,lo,hi,7)
    print("theta in [%.2f, %.2f]:"%(lo,hi))
    for k,v in sorted(st.items()):
        if k=='dipole': print("   dipole remainder: %d cases, above the bound %d, largest |Omega - va.f(c)| / bound %.3f; sign convention: Omega as the fan from c seen from p"%(v[0],v[1],v[2]))
        else: print("   %-14s %7d cases, identity off by more than 1e-9 turns: %6d, largest |difference| %.3g turns"%(k,v[0],v[1],v[2]))
