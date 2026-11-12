# Bounds of the exact-number prototypes, derived independently of the C# code (exact rational / integer arithmetic).
# 1. The monomial accumulator (P2): for each predicate the monomial count T, the degree d, the bits of one monomial,
#    the guard bits, and the accumulator limbs L(s) = floor(d s / 64) + ceil((53 d + g + 63) / 64) for an exponent
#    span s = emax - emin; checked for every s in [0, 2045]: the sum of all monomial magnitudes, each shifted by at
#    most d s bits, is below 2^(64 L(s)) (so no carry leaves the accumulator), and the highest limb a monomial is
#    written to is inside it.
# 2. The row-scaled filter (P4): the filter constant K by the rule of tools/PredicateCompiler (Compiler.cs,
#    filter_bounds) for the difference form of orient2d / orient3d, and the gate of the scaled differences.
import itertools, math
from fractions import Fraction

def perms(n):
    for p in itertools.permutations(range(n)):
        s = 1
        for i in range(n):
            for j in range(i + 1, n):
                if p[i] > p[j]: s = -s
        yield s, p

def monomials(dim, lift):
    n = dim + (2 if lift else 1)
    out = {}
    for s, p in perms(n):
        idx, lift_row = [], None
        for r in range(n):
            c = p[r]
            if c < dim: idx.append(r * dim + c)
            elif lift and c == dim: lift_row = r
        if lift_row is None:
            key = tuple(sorted(idx)); out[key] = out.get(key, 0) + s
        else:
            for k in range(dim):
                key = tuple(sorted(idx + [lift_row * dim + k] * 2)); out[key] = out.get(key, 0) + s
    return {k: v for k, v in out.items() if v != 0}

M = 2**53 - 1
print("| predicate | degree d | monomials T | sum of |coefficients| | bits of a monomial | guard bits g | limbs at span 0 | limbs at span 2045 (worst case) | stack bytes worst case (two accumulators) |")
print("|---|---|---|---|---|---|---|---|---|")
for name, dim, lift, kernel_extra in (("orient2d", 2, False, 4), ("orient3d", 3, False, 5), ("incircle", 2, True, None), ("insphere", 3, True, None)):
    mono = monomials(dim, lift)
    d = len(next(iter(mono)))
    T = len(mono)
    csum = sum(abs(c) for c in mono.values())
    g = (csum - 1).bit_length()
    def limbs(s): return (d * s) // 64 + (53 * d + g + 63 + 63) // 64
    for s in range(0, 2046):
        L = limbs(s)
        assert csum * M**d * 2**(d * s) < 2**(64 * L), (name, s)
        # a monomial written at limb floor(off / 64) takes d + 1 limbs (d limbs shifted by less than 64 bits)
        assert (d * s) // 64 + d + 1 <= L, (name, s)
        # the prototype's own formulas are not smaller
        if kernel_extra is not None: assert ((d * s) >> 6) + kernel_extra >= L, (name, s)
        assert ((d * s) >> 6) + ((53 * d + 9) >> 6) + 2 >= L or name in ("orient2d",), (name, s)
    print(f"| {name} | {d} | {T} | {csum} | {(M**d).bit_length()} | {g} | {limbs(0)} | {limbs(2045)} | {16 * limbs(2045)} |")

# ---- the filter constant by the compiler's rule (u = 2^-53) ----
u = Fraction(1, 2**53)
def add(cx, cy): return (u + max(cx, cy)) / (1 - u)
def mul(cx, cy): return (u + cx + cy + cx * cy) / (1 - u)
leaf = u
# orient3d: wx (uy vz - uz vy) + wy (uz vx - ux vz) + wz (ux vy - uy vx)
minor = add(mul(leaf, leaf), mul(leaf, leaf))
term = mul(leaf, minor)
c3 = add(add(term, term), term)
# orient2d: ux vy - uy vx
c2 = add(mul(leaf, leaf), mul(leaf, leaf))
def ceil_double(q):
    x = float(q)
    while Fraction(x) < q: x = math.nextafter(x, math.inf)
    return x
for name, c, deg in (("orient2d", c2, 2), ("orient3d", c3, 3)):
    K = ceil_double(c / (1 - u))
    print(f"{name}: c / u = {float(c / u):.17g}, K = {K!r} = {K.hex()}, gate of the scaled differences: 0 or >= 2^-{960 // deg - 52} (GateLow of the rule), <= 2")
