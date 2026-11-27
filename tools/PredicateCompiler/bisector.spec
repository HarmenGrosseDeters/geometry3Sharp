# Bisector predicates for tools/PredicateCompiler (docs/design/BISECTOR_PREDICATES.md): the exact decisions of the
# restricted Voronoi diagram of sites on a triangle mesh (docs/design/CERTIFIED_REMESHING.md sections 14.4 and 14.8).
# Format as indirect.spec ('inputs xyz': the coordinates p.x, p.y, p.z of integer points; 'sign e1, e2': the product
# of the signs). Every formula is written relative to the site a (coordinate differences only, translation invariant).
#
# The points of the restricted Voronoi diagram, with y = x - a:
#   a mesh vertex v                                   y = v - a
#   the crossing of mesh edge q0 q1 with bisector(a, b)  y = N / d,  l_k = 2 b'.q'_k - |b'|^2,  N = q'_0 l_1 - q'_1 l_0,
#                                                     d = l_1 - l_0  (N degree 3, d degree 2)
#   the point of bisector(a, b), bisector(a, c) and the plane of triangle t0 t1 t2 (BTPI, a point of a Voronoi edge)
#                                                     y = N / (2 det),  n = (t1 - t0) x (t2 - t0),  h = n.(t0 - a),
#                                                     N = |b'|^2 (c' x n) + |c'|^2 (n x b') + 2 h (b' x c'),
#                                                     det = b'.(c' x n)  (N degree 5, det degree 4)
# (Cramer's rule for the rows 2 b', 2 c', n and the right-hand side |b'|^2, |c'|^2, h, numerator and denominator
# divided by 2.) The side of bisector(a, e) at a point: |x - e|^2 - |x - a|^2 = |e'|^2 - 2 e'.y, so the sign of
# 2 e'.y - |e'|^2 is < 0 where x is closer to a than to e, 0 on the bisector, > 0 where x is closer to e.

# P3: the side of bisector(a, e) at the mesh vertex v; < 0: v is closer to a (degree 2)
ipredicate BisectorSide1(v, a, e) bound 64 inputs xyz
    # the exact stage on FixedArith follows the filter (an expansion stage is not generated)
    expansion none
    vax = v.x - a.x
    vay = v.y - a.y
    vaz = v.z - a.z
    eax = e.x - a.x
    eay = e.y - a.y
    eaz = e.z - a.z
    ev = eax * vax + eay * vay + eaz * vaz
    ee = eax * eax + eay * eay + eaz * eaz
    sign ev + ev - ee

# P4: the side of bisector(a, c) at the crossing y of mesh edge q0 q1 with bisector(a, b); < 0: y is closer to a than
# to c (degree 4; 0 also when the edge is parallel to the bisector, d = 0)
ipredicate BisectorSide2(q0, q1, a, b, c) bound 64 inputs xyz
    bax = b.x - a.x
    bay = b.y - a.y
    baz = b.z - a.z
    p0x = q0.x - a.x
    p0y = q0.y - a.y
    p0z = q0.z - a.z
    p1x = q1.x - a.x
    p1y = q1.y - a.y
    p1z = q1.z - a.z
    bb = bax * bax + bay * bay + baz * baz
    bp0 = bax * p0x + bay * p0y + baz * p0z
    bp1 = bax * p1x + bay * p1y + baz * p1z
    l0 = bp0 + bp0 - bb
    l1 = bp1 + bp1 - bb
    d = l1 - l0
    Nx = p0x * l1 - p1x * l0
    Ny = p0y * l1 - p1y * l0
    Nz = p0z * l1 - p1z * l0
    cax = c.x - a.x
    cay = c.y - a.y
    caz = c.z - a.z
    cN = cax * Nx + cay * Ny + caz * Nz
    cc = cax * cax + cay * cay + caz * caz
    ccd = cc * d
    sign cN + cN - ccd, d

# P1: the side of bisector(a, e) at the point x = BTPI(a, b, c; t0, t1, t2); < 0: x is closer to a than to e
# (degree 6; 0 also when the point does not exist, det = 0)
ipredicate BisectorSide3(a, b, c, t0, t1, t2, e) bound 64 inputs xyz
    # the sites and the triangle, tested against many sites e
    prepare a, b, c, t0, t1, t2
    bax = b.x - a.x
    bay = b.y - a.y
    baz = b.z - a.z
    cax = c.x - a.x
    cay = c.y - a.y
    caz = c.z - a.z
    ux = t1.x - t0.x
    uy = t1.y - t0.y
    uz = t1.z - t0.z
    wx = t2.x - t0.x
    wy = t2.y - t0.y
    wz = t2.z - t0.z
    nx = uy * wz - uz * wy
    ny = uz * wx - ux * wz
    nz = ux * wy - uy * wx
    tax = t0.x - a.x
    tay = t0.y - a.y
    taz = t0.z - a.z
    h = nx * tax + ny * tay + nz * taz
    hh = h + h
    cnx = cay * nz - caz * ny
    cny = caz * nx - cax * nz
    cnz = cax * ny - cay * nx
    nbx = ny * baz - nz * bay
    nby = nz * bax - nx * baz
    nbz = nx * bay - ny * bax
    bcx = bay * caz - baz * cay
    bcy = baz * cax - bax * caz
    bcz = bax * cay - bay * cax
    bb = bax * bax + bay * bay + baz * baz
    cc = cax * cax + cay * cay + caz * caz
    det = bax * cnx + bay * cny + baz * cnz
    Nx = bb * cnx + cc * nbx + hh * bcx
    Ny = bb * cny + cc * nby + hh * bcy
    Nz = bb * cnz + cc * nbz + hh * bcz
    eax = e.x - a.x
    eay = e.y - a.y
    eaz = e.z - a.z
    eN = eax * Nx + eay * Ny + eaz * Nz
    ee = eax * eax + eay * eay + eaz * eaz
    eed = ee * det
    sign eN - eed, det

# The coefficients of the symbolic perturbation (docs/design/BISECTOR_PREDICATES.md section 3): with weights w_s on
# the sites, BisectorSide2 and BisectorSide3 change by terms linear in the weights, whose coefficients are these signs.
# DotSites: the sign of (p - o).(q1 - q0) (degree 2)
ipredicate DotSites(o, p, q0, q1) bound 64 inputs xyz
    # the exact stage on FixedArith follows the filter (an expansion stage is not generated)
    expansion none
    ox = p.x - o.x
    oy = p.y - o.y
    oz = p.z - o.z
    qx = q1.x - q0.x
    qy = q1.y - q0.y
    qz = q1.z - q0.z
    sign ox * qx + oy * qy + oz * qz

# DetSitesNormal: the sign of det(p - o, q - o, n) = ((p - o) x (q - o)).n with n = (t1 - t0) x (t2 - t0) (degree 4)
ipredicate DetSitesNormal(o, p, q, t0, t1, t2) bound 64 inputs xyz
    px = p.x - o.x
    py = p.y - o.y
    pz = p.z - o.z
    qx = q.x - o.x
    qy = q.y - o.y
    qz = q.z - o.z
    ux = t1.x - t0.x
    uy = t1.y - t0.y
    uz = t1.z - t0.z
    wx = t2.x - t0.x
    wy = t2.y - t0.y
    wz = t2.z - t0.z
    nx = uy * wz - uz * wy
    ny = uz * wx - ux * wz
    nz = ux * wy - uy * wx
    mx = py * qz - pz * qy
    my = pz * qx - px * qz
    mz = px * qy - py * qx
    sign mx * nx + my * ny + mz * nz

# R2 and the surface Delaunay ball (CERTIFIED_REMESHING.md rule R2, Theorem D): the sign of |x - a|^2 - r^2 for the
# three kinds of points, with r = r.x (a length; r.y, r.z are not read). > 0: x is farther than r from a.
# At a mesh vertex (degree 2)
ipredicate DistanceAbove1(v, a, r) bound 64 inputs xyz
    # the exact stage on FixedArith follows the filter (an expansion stage is not generated)
    expansion none
    vax = v.x - a.x
    vay = v.y - a.y
    vaz = v.z - a.z
    sign vax * vax + vay * vay + vaz * vaz - r.x * r.x

# At the crossing of mesh edge q0 q1 with bisector(a, b): |N|^2 - r^2 d^2 (degree 6; 0 when the point does not exist,
# d = 0: the factor d^2)
ipredicate DistanceAbove2(q0, q1, a, b, r) bound 64 inputs xyz
    bax = b.x - a.x
    bay = b.y - a.y
    baz = b.z - a.z
    p0x = q0.x - a.x
    p0y = q0.y - a.y
    p0z = q0.z - a.z
    p1x = q1.x - a.x
    p1y = q1.y - a.y
    p1z = q1.z - a.z
    bb = bax * bax + bay * bay + baz * baz
    bp0 = bax * p0x + bay * p0y + baz * p0z
    bp1 = bax * p1x + bay * p1y + baz * p1z
    l0 = bp0 + bp0 - bb
    l1 = bp1 + bp1 - bb
    d = l1 - l0
    Nx = p0x * l1 - p1x * l0
    Ny = p0y * l1 - p1y * l0
    Nz = p0z * l1 - p1z * l0
    rd = r.x * d
    dd = d * d
    sign Nx * Nx + Ny * Ny + Nz * Nz - rd * rd, dd

# At x = BTPI(a, b, c; t0, t1, t2): |N|^2 - 4 r^2 det^2 (degree 10; 0 when the point does not exist, det = 0: the
# factor det^2)
ipredicate DistanceAbove3(a, b, c, t0, t1, t2, r) bound 64 inputs xyz
    bax = b.x - a.x
    bay = b.y - a.y
    baz = b.z - a.z
    cax = c.x - a.x
    cay = c.y - a.y
    caz = c.z - a.z
    ux = t1.x - t0.x
    uy = t1.y - t0.y
    uz = t1.z - t0.z
    wx = t2.x - t0.x
    wy = t2.y - t0.y
    wz = t2.z - t0.z
    nx = uy * wz - uz * wy
    ny = uz * wx - ux * wz
    nz = ux * wy - uy * wx
    tax = t0.x - a.x
    tay = t0.y - a.y
    taz = t0.z - a.z
    h = nx * tax + ny * tay + nz * taz
    hh = h + h
    cnx = cay * nz - caz * ny
    cny = caz * nx - cax * nz
    cnz = cax * ny - cay * nx
    nbx = ny * baz - nz * bay
    nby = nz * bax - nx * baz
    nbz = nx * bay - ny * bax
    bcx = bay * caz - baz * cay
    bcy = baz * cax - bax * caz
    bcz = bax * cay - bay * cax
    bb = bax * bax + bay * bay + baz * baz
    cc = cax * cax + cay * cay + caz * caz
    det = bax * cnx + bay * cny + baz * cnz
    Nx = bb * cnx + cc * nbx + hh * bcx
    Ny = bb * cny + cc * nby + hh * bcy
    Nz = bb * cnz + cc * nbz + hh * bcz
    rd = r.x * det
    rd2 = rd + rd
    dd = det * det
    sign Nx * Nx + Ny * Ny + Nz * Nz - rd2 * rd2, dd

# R3: the radius-edge test of the Delaunay triangle a b c against its edge p q (CERTIFIED_REMESHING.md Theorem E):
# the sign of k.y |b - a|^2 |c - b|^2 |a - c|^2 - 4 k.x |q - p|^2 |n|^2, n = (b - a) x (c - a), with k = (rho^2, 1, -):
# > 0 iff the circumradius R = |b - a| |c - b| |a - c| / (2 |n|) exceeds rho |q - p| (degree 7 as written; k.x, k.y
# are dimensionless, so the sign is homogeneous of degree 6 in the points)
ipredicate RadiusEdgeAbove(a, b, c, p, q, k) bound 64 inputs xyz
    abx = b.x - a.x
    aby = b.y - a.y
    abz = b.z - a.z
    bcx = c.x - b.x
    bcy = c.y - b.y
    bcz = c.z - b.z
    cax = a.x - c.x
    cay = a.y - c.y
    caz = a.z - c.z
    acx = c.x - a.x
    acy = c.y - a.y
    acz = c.z - a.z
    lab = abx * abx + aby * aby + abz * abz
    lbc = bcx * bcx + bcy * bcy + bcz * bcz
    lca = cax * cax + cay * cay + caz * caz
    nx = aby * acz - abz * acy
    ny = abz * acx - abx * acz
    nz = abx * acy - aby * acx
    nn = nx * nx + ny * ny + nz * nz
    pqx = q.x - p.x
    pqy = q.y - p.y
    pqz = q.z - p.z
    lpq = pqx * pqx + pqy * pqy + pqz * pqz
    L = lab * lbc * lca * k.y
    R1 = lpq * nn * k.x
    R2 = R1 + R1
    R4 = R2 + R2
    sign L - R4
