# Sign predicates for tools/PredicateCompiler (docs/design/PREDICATE_COMPILER.md). Each one: 'predicate Name(inputs)',
# definitions 'x = expression' (+, -, * and parentheses over inputs and earlier names), then 'sign expression'.
# The comment lines before a predicate go into the generated code.

# sign of (b - a) x (c - a), as ExactPredicates.Orient2d: +1 if a, b, c are counter-clockwise
predicate Orient2d(ax, ay, bx, by, cx, cy)
    ux = bx - ax
    uy = by - ay
    vx = cx - ax
    vy = cy - ay
    sign ux * vy - uy * vx

# +1 if d lies inside the circle through the counter-clockwise a, b, c, as ExactPredicates.InCircle
predicate InCircle(ax, ay, bx, by, cx, cy, dx, dy)
    adx = ax - dx
    ady = ay - dy
    bdx = bx - dx
    bdy = by - dy
    cdx = cx - dx
    cdy = cy - dy
    alift = adx * adx + ady * ady
    blift = bdx * bdx + bdy * bdy
    clift = cdx * cdx + cdy * cdy
    sign alift * (bdx * cdy - cdx * bdy) + blift * (cdx * ady - adx * cdy) + clift * (adx * bdy - bdx * ady)

# InCircle of ExactMeshArrangement on homogeneous points (x/w, y/w) in the projection of a triangle: the 3x3
# determinant with rows (A s, B s, A^2 + B^2) of each of a, b, c against d, A = x w_d - x_d w, B = y w_d - y_d w,
# s = w w_d (the derivation is at ExactMeshArrangement.InCircle). Bounds: coordinates below 2^112 and weights below
# 2^58 (the n-ary grid of BooleanComparison needs 109 and 54).
ipredicate ArrInCircle(ax:112, ay:112, aw:58, bx:112, by:112, bw:58, cx:112, cy:112, cw:58, dx:112, dy:112, dw:58)
    a1 = ax * dw - dx * aw
    a2 = ay * dw - dy * aw
    as = aw * dw
    b1 = bx * dw - dx * bw
    b2 = by * dw - dy * bw
    bs = bw * dw
    c1 = cx * dw - dx * cw
    c2 = cy * dw - dy * cw
    cs = cw * dw
    r1 = a1 * as
    r2 = a2 * as
    r3 = a1 * a1 + a2 * a2
    s1 = b1 * bs
    s2 = b2 * bs
    s3 = b1 * b1 + b2 * b2
    u1 = c1 * cs
    u2 = c2 * cs
    u3 = c1 * c1 + c2 * c2
    sign r1 * (s2 * u3 - s3 * u2) - r2 * (s1 * u3 - s3 * u1) + r3 * (s1 * u2 - s2 * u1)
