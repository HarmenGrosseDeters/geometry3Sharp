"""Exact ideal rational-grid trace/correction and trial-vector Rayleigh diagnostics.

Uses the ideal rational grid, not the rounded binary64 production fixture.
The rounding difference is not enclosed; this is a model diagnosis only.
Rayleigh quotients bound the smallest eigenvalue from above; they are not lower
spectral certificates. Comparing them to the global trace correction diagnoses
conservatism but does not predict floating-point Cholesky's outcome.
"""
from fractions import Fraction


def closed_grid(nx, ny, diagonals=False):
    X, Y = nx-1, ny-1
    trace_units = energy_units = norm = 0
    diagonal_units = [0]*(nx*ny)
    w = [x*x*(X-x)*(X-x) for x in range(nx)]
    for y in range(ny):
        for x in range(nx):
            edges = []
            for xx in (x-1, x+1):
                if 0 <= xx < nx:
                    edges.append((xx, y, X*X*(1 if y in (0,Y) else 2)))
            for yy in (y-1, y+1):
                if 0 <= yy < ny:
                    edges.append((x, yy, Y*Y*(1 if x in (0,X) else 2)))
            diagonal = sum(value for _, _, value in edges)
            incidence = (2 if (x,y) in ((0,0),(X,Y)) else 1) if x in (0,X) and y in (0,Y) else 3 if x in (0,X) or y in (0,Y) else 6
            multiplier = 6//incidence
            trace_units += multiplier*(sum(value*value for xx,yy,value in edges if 0 < xx < X)+(diagonal*diagonal if 0 < x < X else 0))
            if 0 < x < X: diagonal_units[x+nx*y] += multiplier*diagonal*diagonal
            for xx, yy, value in edges:
                if 0 < xx < X: diagonal_units[xx+nx*yy] += multiplier*value*value
            lap = diagonal*w[x]-sum(value*w[xx] for xx,yy,value in edges)
            energy_units += multiplier*lap*lap
            norm += w[x]*w[x]
    if diagonals:
        assert sum(diagonal_units)==trace_units
        return [Fraction(diagonal_units[x+nx*y],4*X*Y) for y in range(ny) for x in range(1,X)]
    return Fraction(trace_units,4*X*Y), Fraction(energy_units,4*X*Y*norm)


def independent_small(nx, ny, diagonals=False):
    # Triangle-by-triangle assembly, independently from the grid stencil above.
    p=[(Fraction(x,nx-1),Fraction(y,ny-1)) for y in range(ny) for x in range(nx)]
    rows=[{} for _ in p]; mass=[Fraction(0) for _ in p]
    for y in range(ny-1):
        for x in range(nx-1):
            a=x+nx*y; b=a+1; d=a+nx; c=d+1
            for face in ((a,b,c),(a,c,d)):
                u,v,t=(p[i] for i in face)
                area2=abs((v[0]-u[0])*(t[1]-u[1])-(v[1]-u[1])*(t[0]-u[0]))
                for i in face:mass[i]+=area2/6
                for k in range(3):
                    i,j,o=face[k],face[(k+1)%3],face[(k+2)%3]
                    weight=sum((p[i][axis]-p[o][axis])*(p[j][axis]-p[o][axis]) for axis in range(2))/(2*area2)
                    for r,s,sign in ((i,i,1),(j,j,1),(i,j,-1),(j,i,-1)):
                        rows[r][s]=rows[r].get(s,Fraction(0))+sign*weight
    free=lambda i:0<i%nx<nx-1
    trace=sum(sum(value*value for j,value in row.items() if free(j))/mass[k] for k,row in enumerate(rows))
    if diagonals:
        return [sum(row.get(i,Fraction(0))**2/mass[k] for k,row in enumerate(rows)) for i in range(len(p)) if free(i)]
    w=[(i%nx)**2*(nx-1-i%nx)**2 for i in range(len(p))]
    energy=sum(sum(value*w[j] for j,value in row.items())**2/mass[k] for k,row in enumerate(rows))
    return trace,energy/sum(value*value for value in w)


def sparsity_bound(data):
    # Rump (2006), section 3 bound III, no-underflow model.
    # L row i is R column i. Count structural entries, including numeric zeros.
    diagonal = closed_grid(data['width'], data['height'], diagonals=True)
    unit = 1 << 128
    upper_units = 0
    for old, preceding in zip(data['order'], data['preceding_counts']):
        k = preceding+2
        term = diagonal[old]*Fraction(k, 2**53-2*k)
        upper_units += (term.numerator*unit+term.denominator-1)//term.denominator
    return Fraction(upper_units, unit)


def perron_bound(data):
    # Rump (2026), Lemma 2.10 / Corollary 2.12, ideal-grid model only.
    # v_i = mu_i+1, D_i^2 = A_ii/(1-v_i*u), Phi_ij=min(v_i,v_j).
    # Floating iterations propose x; exact integer Collatz evaluation proves
    # the returned upper bound for this model. No solver execution is verified.
    import math
    diagonal = closed_grid(data['width'], data['height'], diagonals=True)
    unit = 1 << 64
    terms = []
    for old, preceding in zip(data['order'], data['preceding_counts']):
        v = preceding+2
        q = diagonal[old]*Fraction(2**53,2**53-v)
        scaled = (q.numerator*unit*unit+q.denominator-1)//q.denominator
        d = math.isqrt(scaled)
        if d*d < scaled: d += 1
        assert Fraction(d*d,unit*unit) >= q
        terms.append((v,d))
    terms.sort()
    ds = [d/unit for _,d in terms]
    x = [1.0]*len(terms)
    for _ in range(8):
        z = [d*t for d,t in zip(ds,x)]
        suffix = sum(z); prefix = 0.0; y = []
        for (v,_),d,t in zip(terms,ds,z):
            y.append(d*(prefix+v*suffix))
            prefix += v*t; suffix -= t
        largest = max(y)
        x = [max(value/largest,2**-32) for value in y]
    xi = [max(1,int(value*unit)) for value in x]
    z = [d*t for (_,d),t in zip(terms,xi)]
    suffix = sum(z); prefix = 0; best_num = 0; best_den = 1
    for (v,d),t,xvalue in zip(terms,z,xi):
        numerator = d*(prefix+v*suffix)
        if numerator*best_den > best_num*xvalue:
            best_num,best_den = numerator,xvalue
        prefix += v*t; suffix -= t
    return Fraction(best_num,best_den*unit*unit*2**53)


if __name__ == '__main__':
    for dimensions in ((3,3),(5,7),(7,5)):
        assert closed_grid(*dimensions)==independent_small(*dimensions)
        assert closed_grid(*dimensions,diagonals=True)==independent_small(*dimensions,diagonals=True)
        print(f'grid={dimensions} exact_triangle_reference_match=True',flush=True)
    for nx,ny in ((100,100),(200,125),(250,200),(400,250)):
        trace, rayleigh=closed_grid(nx,ny); nf=(nx-2)*ny
        correction=trace*Fraction(nf+1,2**53-2*(nf+1))
        print(f'model=ideal_rational_grid grid={nx}x{ny} free={nf} trace_exact={trace} rayleigh_upper_exact={rayleigh} trace_correction_exact={correction}',flush=True)
        print(f'trace_correction={float(correction):.12g} rayleigh_upper={float(rayleigh):.12g} correction_exceeds_rayleigh={correction>rayleigh} correction_over_rayleigh={float(correction/rayleigh):.6g} position_certificate=not_checked',flush=True)
