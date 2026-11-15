# Exact sign predicates on double coordinates of any exponent, for the monomial pass of tools/PredicateCompiler
# (Monomials.cs; docs/design/EXACT_FALLBACK_MONOMIALS.md): the stages of ExactPredicates beyond the range of the
# expansions. 'dpredicate Name(points) inputs xy|xyz': the inputs are the double coordinates p.x, p.y (, p.z) of the
# points; definitions and one 'sign' line as for ipredicate. Each formula is the homogeneous determinant with a
# column of ones, so every leaf is an input and every monomial has the same degree; the pass expands it into its
# monomials and generates math/Generated/DoublePredicates.cs. (A value is named and used by its name, as in the other
# specs.)

# ExactPredicates.Orient2d: the sign of (b - a) x (c - a) = det [a 1; b 1; c 1] = bc - ac + ab, with pq = p x q
dpredicate Orient2d(a, b, c) inputs xy
    bc = b.x * c.y - b.y * c.x
    ac = a.x * c.y - a.y * c.x
    ab = a.x * b.y - a.y * b.x
    sign bc - ac + ab

# ExactPredicates.Orient3d: the sign of ((b - a) x (c - a)) . (d - a) = det(b - a, c - a, d - a)
# = det(b, c, d) - det(a, c, d) + det(a, b, d) - det(a, b, c), det(p, q, r) = p . (q x r)
dpredicate Orient3d(a, b, c, d) inputs xyz
    cdx = c.y * d.z - c.z * d.y
    cdy = c.z * d.x - c.x * d.z
    cdz = c.x * d.y - c.y * d.x
    bdx = b.y * d.z - b.z * d.y
    bdy = b.z * d.x - b.x * d.z
    bdz = b.x * d.y - b.y * d.x
    bcx = b.y * c.z - b.z * c.y
    bcy = b.z * c.x - b.x * c.z
    bcz = b.x * c.y - b.y * c.x
    bcd = b.x * cdx + b.y * cdy + b.z * cdz
    acd = a.x * cdx + a.y * cdy + a.z * cdz
    abd = a.x * bdx + a.y * bdy + a.z * bdz
    abc = a.x * bcx + a.y * bcy + a.z * bcz
    sign bcd - acd + abd - abc

# ExactPredicates.InCircle: the determinant of the rows (p - d, |p - d|^2) of a, b, c equals
# det [p.x p.y |p|^2 1] over a, b, c, d (column operations, then subtracting row d), expanded along the column of
# ones: abc - abd + acd - bcd with pqr = det of the rows (x, y, |.|^2) of p, q, r, expanded along its first row
dpredicate InCircle(a, b, c, d) inputs xy
    la = a.x * a.x + a.y * a.y
    lb = b.x * b.x + b.y * b.y
    lc = c.x * c.x + c.y * c.y
    ld = d.x * d.x + d.y * d.y
    cdxy = c.x * d.y - c.y * d.x
    cdxl = c.x * ld - lc * d.x
    cdyl = c.y * ld - lc * d.y
    bdxy = b.x * d.y - b.y * d.x
    bdxl = b.x * ld - lb * d.x
    bdyl = b.y * ld - lb * d.y
    bcxy = b.x * c.y - b.y * c.x
    bcxl = b.x * lc - lb * c.x
    bcyl = b.y * lc - lb * c.y
    bcd = b.x * cdyl - b.y * cdxl + lb * cdxy
    acd = a.x * cdyl - a.y * cdxl + la * cdxy
    abd = a.x * bdyl - a.y * bdxl + la * bdxy
    abc = a.x * bcyl - a.y * bcxl + la * bcxy
    sign abc - abd + acd - bcd
