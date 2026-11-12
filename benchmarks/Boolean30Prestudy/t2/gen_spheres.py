import sys, math, random
# UV sphere (n x 2n quads), radius r, center c, rotated by a fixed rotation, with a bumpy radius so it is not too regular
def sphere(n, r, c, seed, bump):
    rnd = random.Random(seed)
    ph = rnd.random()*6; 
    verts=[]; tris=[]
    verts.append((0,0,1))
    for i in range(1,n):
        th=math.pi*i/n
        for j in range(2*n):
            f=2*math.pi*j/(2*n)
            rr=1+bump*math.sin(5*f+ph)*math.sin(3*th)
            verts.append((rr*math.sin(th)*math.cos(f), rr*math.sin(th)*math.sin(f), rr*math.cos(th)))
    verts.append((0,0,-1))
    m=2*n
    def v(i,j): return 1+(i-1)*m+(j%m)
    for j in range(m): tris.append((0,v(1,j),v(1,j+1)))
    for i in range(1,n-1):
        for j in range(m):
            a,b,c2,d=v(i,j),v(i+1,j),v(i+1,j+1),v(i,j+1)
            tris.append((a,b,c2)); tris.append((a,c2,d))
    last=len(verts)-1
    for j in range(m): tris.append((last,v(n-1,j+1),v(n-1,j)))
    # rotate about axis
    ax,ay,az=0.3,0.5,0.8; l=math.sqrt(ax*ax+ay*ay+az*az); ax/=l;ay/=l;az/=l
    ca,sa=math.cos(0.7),math.sin(0.7)
    out=[]
    for x,y,z in verts:
        d=ax*x+ay*y+az*z
        cx,cy,cz=ay*z-az*y,az*x-ax*z,ax*y-ay*x
        X=x*ca+cx*sa+ax*d*(1-ca); Y=y*ca+cy*sa+ay*d*(1-ca); Z=z*ca+cz*sa+az*d*(1-ca)
        out.append((r*X+c[0],r*Y+c[1],r*Z+c[2]))
    return out,tris
def write(p,vt):
    v,t=vt
    with open(p,'w') as f:
        f.write("OFF\n%d %d 0\n"%(len(v),len(t)))
        for x,y,z in v: f.write("%r %r %r\n"%(x,y,z))
        for a,b,c in t: f.write("3 %d %d %d\n"%(a,b,c))
name,n=sys.argv[1],int(sys.argv[2])
import os
d=os.path.join(sys.argv[3],name); os.makedirs(d,exist_ok=True)
write(d+"/A.off",sphere(n,1.0,(0,0,0),1,0.03))
write(d+"/B.off",sphere(n,0.9,(0.55,0.2,0.1),2,0.03))
