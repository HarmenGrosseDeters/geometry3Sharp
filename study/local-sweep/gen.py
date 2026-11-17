import math, sys
def icosphere(n, r, c):
    t=(1+5**.5)/2
    V=[(-1,t,0),(1,t,0),(-1,-t,0),(1,-t,0),(0,-1,t),(0,1,t),(0,-1,-t),(0,1,-t),(t,0,-1),(t,0,1),(-t,0,-1),(-t,0,1)]
    F=[(0,11,5),(0,5,1),(0,1,7),(0,7,10),(0,10,11),(1,5,9),(5,11,4),(11,10,2),(10,7,6),(7,1,8),(3,9,4),(3,4,2),(3,2,6),(3,6,8),(3,8,9),(4,9,5),(2,4,11),(6,2,10),(8,6,7),(9,8,1)]
    V=[tuple(x/math.sqrt(sum(y*y for y in v)) for x in v) for v in V]
    for _ in range(n):
        mid={}; NF=[]
        def m(a,b):
            k=(min(a,b),max(a,b))
            if k not in mid:
                p=[(V[a][i]+V[b][i])/2 for i in range(3)]; l=math.sqrt(sum(x*x for x in p)); V.append(tuple(x/l for x in p)); mid[k]=len(V)-1
            return mid[k]
        for a,b,cc in F:
            ab,bc,ca=m(a,b),m(b,cc),m(cc,a); NF+= [(a,ab,ca),(b,bc,ab),(cc,ca,bc),(ab,bc,ca)]
        F=NF
    return [tuple(c[i]+r*v[i] for i in range(3)) for v in V], F
def cube(h, c):
    V=[(c[0]+sx*h,c[1]+sy*h,c[2]+sz*h) for sx in(-1,1) for sy in(-1,1) for sz in(-1,1)]
    F=[(0,1,3),(0,3,2),(4,6,7),(4,7,5),(0,4,5),(0,5,1),(2,3,7),(2,7,6),(0,2,6),(0,6,4),(1,5,7),(1,7,3)]
    return V,F
def write(path, parts):
    with open(path,'w') as f:
        off=0
        for V,F in parts:
            for v in V: f.write("v %.17g %.17g %.17g\n"%v)
            for a,b,c in F: f.write("f %d %d %d\n"%(a+1+off,b+1+off,c+1+off))
            off+=len(V)
s=icosphere(3,1,(0,0,0))
write("probe/far_box.obj",[s,cube(2**-20,(2**12,0,0))])
V,F=icosphere(3,1,(0,0,0)); write("probe/open_sphere_far_box.obj",[(V,F[:-6]),cube(2**-10,(2**4,0,0))])
write("probe/two_spheres.obj",[s,icosphere(2,0.3,(5,0,0))])
write("probe/sphere.obj",[s])
