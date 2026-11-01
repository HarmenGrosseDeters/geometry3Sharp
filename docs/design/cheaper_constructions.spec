# The exact constructions of ExactMeshArrangement (docs/design/CHEAPER_CONSTRUCTIONS.md), for tools/PredicateCompiler
# --table. Bounds only: 'sign' lists the outputs of a construction (the generator needs one sign line; the product
# of signs has no meaning here). Integer input points (inputs xyz, W = 1) have |v| <= m = 2^64 - 1: the grid
# coordinates of the input vertices, admitted to the fast path when they fit one limb.

# Triangle plane (InputTriangle.compute_planes): N = (b - a) x (c - a), D = N . a.
ipredicate TrianglePlane(a, b, c) bound 64 inputs xyz
    ux = b.x - a.x
    uy = b.y - a.y
    uz = b.z - a.z
    vx = c.x - a.x
    vy = c.y - a.y
    vz = c.z - a.z
    nx = uy * vz - uz * vy
    ny = uz * vx - ux * vz
    nz = ux * vy - uy * vx
    d = nx * a.x + ny * a.y + nz * a.z
    sign nx, ny, nz, d

# Edge plane k (InputTriangle.edge_plane): through p = corner k, q = corner k+1, perpendicular to the triangle:
# E = (q - p) x N, F = E . p.
ipredicate EdgePlane(a, b, c) bound 64 inputs xyz
    ux = b.x - a.x
    uy = b.y - a.y
    uz = b.z - a.z
    vx = c.x - a.x
    vy = c.y - a.y
    vz = c.z - a.z
    nx = uy * vz - uz * vy
    ny = uz * vx - ux * vz
    nz = ux * vy - uy * vx
    ex = uy * nz - uz * ny
    ey = uz * nx - ux * nz
    ez = ux * ny - uy * nx
    f = ex * a.x + ey * a.y + ez * a.z
    sign ex, ey, ez, f

# EdgePlane point on a triangle plane (compute_exact, edge_plane_exact): the edge p-q of input points crosses the
# plane (N, D) of the triangle a, b, c: s_p = N . p - D, x = s_p q - s_q p, w = s_p - s_q.
ipredicate PointEdgeTrianglePlane(p, q, a, b, c) bound 64 inputs xyz
    ux = b.x - a.x
    uy = b.y - a.y
    uz = b.z - a.z
    vx = c.x - a.x
    vy = c.y - a.y
    vz = c.z - a.z
    nx = uy * vz - uz * vy
    ny = uz * vx - ux * vz
    nz = ux * vy - uy * vx
    d = nx * a.x + ny * a.y + nz * a.z
    sp = nx * p.x + ny * p.y + nz * p.z - d
    sq = nx * q.x + ny * q.y + nz * q.z - d
    x = sp * q.x - sq * p.x
    y = sp * q.y - sq * p.y
    z = sp * q.z - sq * p.z
    w = sp - sq
    sign x, y, z, w

# Segments point (segments_exact): edges p-q and r-s of input points crossing in a common plane, projected to the
# axes i, j (here x, y): s_p = Det3 of the rows (r_i, r_j, 1), (s_i, s_j, 1), (p_i, p_j, 1), as ExactPoint3.Det3
# expands it with W = 1; x = s_p q - s_q p, w = s_p - s_q.
ipredicate PointSegments(p, q, r, s) bound 64 inputs xyz
    sp = r.x * (s.y - p.y) - r.y * (s.x - p.x) + (s.x * p.y - s.y * p.x)
    sq = r.x * (s.y - q.y) - r.y * (s.x - q.x) + (s.x * q.y - s.y * q.x)
    x = sp * q.x - sq * p.x
    y = sp * q.y - sq * p.y
    z = sp * q.z - sq * p.z
    w = sp - sq
    sign x, y, z, w

# Planes point (planes_exact) of three triangle planes (Cramer).
ipredicate PointThreeTrianglePlanes(a1, b1, c1, a2, b2, c2, a3, b3, c3) bound 64 inputs xyz
    u1x = b1.x - a1.x
    u1y = b1.y - a1.y
    u1z = b1.z - a1.z
    v1x = c1.x - a1.x
    v1y = c1.y - a1.y
    v1z = c1.z - a1.z
    n1x = u1y * v1z - u1z * v1y
    n1y = u1z * v1x - u1x * v1z
    n1z = u1x * v1y - u1y * v1x
    d1 = n1x * a1.x + n1y * a1.y + n1z * a1.z
    u2x = b2.x - a2.x
    u2y = b2.y - a2.y
    u2z = b2.z - a2.z
    v2x = c2.x - a2.x
    v2y = c2.y - a2.y
    v2z = c2.z - a2.z
    n2x = u2y * v2z - u2z * v2y
    n2y = u2z * v2x - u2x * v2z
    n2z = u2x * v2y - u2y * v2x
    d2 = n2x * a2.x + n2y * a2.y + n2z * a2.z
    u3x = b3.x - a3.x
    u3y = b3.y - a3.y
    u3z = b3.z - a3.z
    v3x = c3.x - a3.x
    v3y = c3.y - a3.y
    v3z = c3.z - a3.z
    n3x = u3y * v3z - u3z * v3y
    n3y = u3z * v3x - u3x * v3z
    n3z = u3x * v3y - u3y * v3x
    d3 = n3x * a3.x + n3y * a3.y + n3z * a3.z
    c23x = n2y * n3z - n2z * n3y
    c23y = n2z * n3x - n2x * n3z
    c23z = n2x * n3y - n2y * n3x
    c31x = n3y * n1z - n3z * n1y
    c31y = n3z * n1x - n3x * n1z
    c31z = n3x * n1y - n3y * n1x
    c12x = n1y * n2z - n1z * n2y
    c12y = n1z * n2x - n1x * n2z
    c12z = n1x * n2y - n1y * n2x
    w = n1x * c23x + n1y * c23y + n1z * c23z
    x = d1 * c23x + d2 * c31x + d3 * c12x
    y = d1 * c23y + d2 * c31y + d3 * c12y
    z = d1 * c23z + d2 * c31z + d3 * c12z
    sign x, y, z, w

# Planes point of a triangle plane and two edge planes (TEE), the worst reachable case of planes_exact: its only
# caller, PlanesPointImplicit(4 ti, a.Line, b.Line), takes the triangle's own plane and two segment lines, each a
# triangle plane or an edge plane; three edge planes cannot occur. (The bound does not depend on which position the
# triangle plane sorts to: the expression is symmetric in the planes up to sign.)
ipredicate PointTriangleTwoEdgePlanes(a1, b1, c1, a2, b2, c2, a3, b3, c3) bound 64 inputs xyz
    u1x = b1.x - a1.x
    u1y = b1.y - a1.y
    u1z = b1.z - a1.z
    v1x = c1.x - a1.x
    v1y = c1.y - a1.y
    v1z = c1.z - a1.z
    e1x = u1y * v1z - u1z * v1y
    e1y = u1z * v1x - u1x * v1z
    e1z = u1x * v1y - u1y * v1x
    f1 = e1x * a1.x + e1y * a1.y + e1z * a1.z
    u2x = b2.x - a2.x
    u2y = b2.y - a2.y
    u2z = b2.z - a2.z
    v2x = c2.x - a2.x
    v2y = c2.y - a2.y
    v2z = c2.z - a2.z
    n2x = u2y * v2z - u2z * v2y
    n2y = u2z * v2x - u2x * v2z
    n2z = u2x * v2y - u2y * v2x
    e2x = u2y * n2z - u2z * n2y
    e2y = u2z * n2x - u2x * n2z
    e2z = u2x * n2y - u2y * n2x
    f2 = e2x * a2.x + e2y * a2.y + e2z * a2.z
    u3x = b3.x - a3.x
    u3y = b3.y - a3.y
    u3z = b3.z - a3.z
    v3x = c3.x - a3.x
    v3y = c3.y - a3.y
    v3z = c3.z - a3.z
    n3x = u3y * v3z - u3z * v3y
    n3y = u3z * v3x - u3x * v3z
    n3z = u3x * v3y - u3y * v3x
    e3x = u3y * n3z - u3z * n3y
    e3y = u3z * n3x - u3x * n3z
    e3z = u3x * n3y - u3y * n3x
    f3 = e3x * a3.x + e3y * a3.y + e3z * a3.z
    c23x = e2y * e3z - e2z * e3y
    c23y = e2z * e3x - e2x * e3z
    c23z = e2x * e3y - e2y * e3x
    c31x = e3y * e1z - e3z * e1y
    c31y = e3z * e1x - e3x * e1z
    c31z = e3x * e1y - e3y * e1x
    c12x = e1y * e2z - e1z * e2y
    c12y = e1z * e2x - e1x * e2z
    c12z = e1x * e2y - e1y * e2x
    w = e1x * c23x + e1y * c23y + e1z * c23z
    x = f1 * c23x + f2 * c31x + f3 * c12x
    y = f1 * c23y + f2 * c31y + f3 * c12y
    z = f1 * c23z + f2 * c31z + f3 * c12z
    sign x, y, z, w

# Planes point of two triangle planes and one edge plane (TTE).
ipredicate PointTwoTrianglePlanesEdgePlane(a1, b1, c1, a2, b2, c2, a3, b3, c3) bound 64 inputs xyz
    u1x = b1.x - a1.x
    u1y = b1.y - a1.y
    u1z = b1.z - a1.z
    v1x = c1.x - a1.x
    v1y = c1.y - a1.y
    v1z = c1.z - a1.z
    e1x = u1y * v1z - u1z * v1y
    e1y = u1z * v1x - u1x * v1z
    e1z = u1x * v1y - u1y * v1x
    f1 = e1x * a1.x + e1y * a1.y + e1z * a1.z
    u2x = b2.x - a2.x
    u2y = b2.y - a2.y
    u2z = b2.z - a2.z
    v2x = c2.x - a2.x
    v2y = c2.y - a2.y
    v2z = c2.z - a2.z
    e2x = u2y * v2z - u2z * v2y
    e2y = u2z * v2x - u2x * v2z
    e2z = u2x * v2y - u2y * v2x
    f2 = e2x * a2.x + e2y * a2.y + e2z * a2.z
    u3x = b3.x - a3.x
    u3y = b3.y - a3.y
    u3z = b3.z - a3.z
    v3x = c3.x - a3.x
    v3y = c3.y - a3.y
    v3z = c3.z - a3.z
    n3x = u3y * v3z - u3z * v3y
    n3y = u3z * v3x - u3x * v3z
    n3z = u3x * v3y - u3y * v3x
    e3x = u3y * n3z - u3z * n3y
    e3y = u3z * n3x - u3x * n3z
    e3z = u3x * n3y - u3y * n3x
    f3 = e3x * a3.x + e3y * a3.y + e3z * a3.z
    c23x = e2y * e3z - e2z * e3y
    c23y = e2z * e3x - e2x * e3z
    c23z = e2x * e3y - e2y * e3x
    c31x = e3y * e1z - e3z * e1y
    c31y = e3z * e1x - e3x * e1z
    c31z = e3x * e1y - e3y * e1x
    c12x = e1y * e2z - e1z * e2y
    c12y = e1z * e2x - e1x * e2z
    c12z = e1x * e2y - e1y * e2x
    w = e1x * c23x + e1y * c23y + e1z * c23z
    x = f1 * c23x + f2 * c31x + f3 * c12x
    y = f1 * c23y + f2 * c31y + f3 * c12y
    z = f1 * c23z + f2 * c31z + f3 * c12z
    sign x, y, z, w

# Line direction of two triangle planes (intersect): N_T x N_U.
ipredicate DirectionTwoTriangles(a1, b1, c1, a2, b2, c2) bound 64 inputs xyz
    u1x = b1.x - a1.x
    u1y = b1.y - a1.y
    u1z = b1.z - a1.z
    v1x = c1.x - a1.x
    v1y = c1.y - a1.y
    v1z = c1.z - a1.z
    n1x = u1y * v1z - u1z * v1y
    n1y = u1z * v1x - u1x * v1z
    n1z = u1x * v1y - u1y * v1x
    u2x = b2.x - a2.x
    u2y = b2.y - a2.y
    u2z = b2.z - a2.z
    v2x = c2.x - a2.x
    v2y = c2.y - a2.y
    v2z = c2.z - a2.z
    n2x = u2y * v2z - u2z * v2y
    n2y = u2z * v2x - u2x * v2z
    n2z = u2x * v2y - u2y * v2x
    dx = n1y * n2z - n1z * n2y
    dy = n1z * n2x - n1x * n2z
    dz = n1x * n2y - n1y * n2x
    sign dx, dy, dz

# Line direction of a triangle plane and an edge plane of another triangle (line_direction(t, PlaneOf(line)) with
# line = 4 u + 1 + k, from clip_into and the coplanar segments): N_T x E_U.
ipredicate DirectionTriangleEdge(a1, b1, c1, a2, b2, c2) bound 64 inputs xyz
    u1x = b1.x - a1.x
    u1y = b1.y - a1.y
    u1z = b1.z - a1.z
    v1x = c1.x - a1.x
    v1y = c1.y - a1.y
    v1z = c1.z - a1.z
    n1x = u1y * v1z - u1z * v1y
    n1y = u1z * v1x - u1x * v1z
    n1z = u1x * v1y - u1y * v1x
    u2x = b2.x - a2.x
    u2y = b2.y - a2.y
    u2z = b2.z - a2.z
    v2x = c2.x - a2.x
    v2y = c2.y - a2.y
    v2z = c2.z - a2.z
    n2x = u2y * v2z - u2z * v2y
    n2y = u2z * v2x - u2x * v2z
    n2z = u2x * v2y - u2y * v2x
    e2x = u2y * n2z - u2z * n2y
    e2y = u2z * n2x - u2x * n2z
    e2z = u2x * n2y - u2y * n2x
    dx = n1y * e2z - n1z * e2y
    dy = n1z * e2x - n1x * e2z
    dz = n1x * e2y - n1y * e2x
    sign dx, dy, dz

# Centroid (ExactMeshBoolean.centroid) of three fixed-width points (|v| <= 2^255 - 1; one coordinate x and W; the
# others are the same): x = a.x wbc + b.x wac + c.x wab, w = 3 a.w wbc.
ipredicate Centroid(a, b, c) bound 255
    wbc = b.w * c.w
    wac = a.w * c.w
    wab = a.w * b.w
    x = a.x * wbc + b.x * wac + c.x * wab
    w3 = a.w + a.w + a.w
    w = w3 * wbc
    sign x, w

# The kernel 'point against a triangle plane' (the exact stage of Orient3d on input points): the coordinate
# differences in units of their common lowest bit, admitted when |difference| <= 2^62 - 1 (bound 62 on the
# differences themselves: the inputs here are u = b - a, v = c - a, w = d - a).
ipredicate Orient3dInputDiffs(u, v, w) bound 62 inputs xyz
    m1 = u.y * v.z - u.z * v.y
    m2 = u.z * v.x - u.x * v.z
    m3 = u.x * v.y - u.y * v.x
    t1 = w.x * m1
    t2 = w.y * m2
    t3 = w.z * m3
    sign t1 + t2 + t3
