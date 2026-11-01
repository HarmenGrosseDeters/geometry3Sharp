# Indirect predicates for tools/PredicateCompiler (docs/design/INDIRECT_PREDICATES.md): sign predicates on implicit
# points, evaluated without constructing the point. Format as predicates.spec; 'inputs xyz': the inputs are the
# coordinates p.x, p.y, p.z of integer points (W = 1); 'sign e1, e2': the product of the signs.
#
# Orient3d of a point x and the points a, b, c is the sign of det(x - c, a - c, b - c). For an implicit point
# x = N / d (N a vector, d a scalar, both polynomials in the inputs): det(N - d c, a - c, b - c) / d, so the sign of
# det(N - d c, a - c, b - c) times the sign of d.

# Orient3d(LPI(p, q; r, s, t), a, b, c): x is where the line p-q meets the plane through r, s, t,
# x = (p lq - q lp) / (lq - lp) with lp = n.(p - r), lq = n.(q - r), n = (s - r) x (t - r) (0 sign if the line is
# parallel to the plane: d = 0).
ipredicate Orient3dLPI(p, q, r, s, t, a, b, c) bound 64 inputs xyz
    ux = s.x - r.x
    uy = s.y - r.y
    uz = s.z - r.z
    wx = t.x - r.x
    wy = t.y - r.y
    wz = t.z - r.z
    nx = uy * wz - uz * wy
    ny = uz * wx - ux * wz
    nz = ux * wy - uy * wx
    prx = p.x - r.x
    pry = p.y - r.y
    prz = p.z - r.z
    qrx = q.x - r.x
    qry = q.y - r.y
    qrz = q.z - r.z
    lp = nx * prx + ny * pry + nz * prz
    lq = nx * qrx + ny * qry + nz * qrz
    d = lq - lp
    Nx = p.x * lq - q.x * lp - c.x * d
    Ny = p.y * lq - q.y * lp - c.y * d
    Nz = p.z * lq - q.z * lp - c.z * d
    acx = a.x - c.x
    acy = a.y - c.y
    acz = a.z - c.z
    bcx = b.x - c.x
    bcy = b.y - c.y
    bcz = b.z - c.z
    m1 = acy * bcz - acz * bcy
    m2 = acx * bcz - acz * bcx
    m3 = acx * bcy - acy * bcx
    t1 = Nx * m1
    t2 = Ny * m2
    t3 = Nz * m3
    sign t1 - t2 + t3, d

# Orient3d(TPI(planes of (a1, b1, c1), (a2, b2, c2), (a3, b3, c3)), a, b, c): x is the point common to the three
# planes, x = (D1 (n2 x n3) + D2 (n3 x n1) + D3 (n1 x n2)) / det(n1, n2, n3), n_k = (b_k - a_k) x (c_k - a_k),
# D_k = n_k . a_k (0 sign if the planes are dependent: d = 0).
ipredicate Orient3dTPI(a1, b1, c1, a2, b2, c2, a3, b3, c3, a, b, c) bound 64 inputs xyz
    u1_x = b1.x - a1.x
    u1_y = b1.y - a1.y
    u1_z = b1.z - a1.z
    w1_x = c1.x - a1.x
    w1_y = c1.y - a1.y
    w1_z = c1.z - a1.z
    n1x = u1_y * w1_z - u1_z * w1_y
    n1y = u1_z * w1_x - u1_x * w1_z
    n1z = u1_x * w1_y - u1_y * w1_x
    D1 = n1x * a1.x + n1y * a1.y + n1z * a1.z
    u2_x = b2.x - a2.x
    u2_y = b2.y - a2.y
    u2_z = b2.z - a2.z
    w2_x = c2.x - a2.x
    w2_y = c2.y - a2.y
    w2_z = c2.z - a2.z
    n2x = u2_y * w2_z - u2_z * w2_y
    n2y = u2_z * w2_x - u2_x * w2_z
    n2z = u2_x * w2_y - u2_y * w2_x
    D2 = n2x * a2.x + n2y * a2.y + n2z * a2.z
    u3_x = b3.x - a3.x
    u3_y = b3.y - a3.y
    u3_z = b3.z - a3.z
    w3_x = c3.x - a3.x
    w3_y = c3.y - a3.y
    w3_z = c3.z - a3.z
    n3x = u3_y * w3_z - u3_z * w3_y
    n3y = u3_z * w3_x - u3_x * w3_z
    n3z = u3_x * w3_y - u3_y * w3_x
    D3 = n3x * a3.x + n3y * a3.y + n3z * a3.z
    e23x = n2y * n3z - n2z * n3y
    e23y = n2z * n3x - n2x * n3z
    e23z = n2x * n3y - n2y * n3x
    e31x = n3y * n1z - n3z * n1y
    e31y = n3z * n1x - n3x * n1z
    e31z = n3x * n1y - n3y * n1x
    e12x = n1y * n2z - n1z * n2y
    e12y = n1z * n2x - n1x * n2z
    e12z = n1x * n2y - n1y * n2x
    d = n1x * e23x + n1y * e23y + n1z * e23z
    Nx = D1 * e23x + D2 * e31x + D3 * e12x - c.x * d
    Ny = D1 * e23y + D2 * e31y + D3 * e12y - c.y * d
    Nz = D1 * e23z + D2 * e31z + D3 * e12z - c.z * d
    acx = a.x - c.x
    acy = a.y - c.y
    acz = a.z - c.z
    bcx = b.x - c.x
    bcy = b.y - c.y
    bcz = b.z - c.z
    m1 = acy * bcz - acz * bcy
    m2 = acx * bcz - acz * bcx
    m3 = acx * bcy - acy * bcx
    t1 = Nx * m1
    t2 = Ny * m2
    t3 = Nz * m3
    sign t1 - t2 + t3, d
