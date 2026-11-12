# PROTOTYPE SPEC (exact-number study): P4, the double filter on row-scaled differences. MEASURED AND NOT PROPOSED
# (it decides 20 % of the orient3d and 66 % of the orient2d fallback calls of #711 and saves under 0.2 s of the
# 15.5 s there once P2 is in; see the report). Kept as the worked example of the rule.
# PROPOSED modifier 'scale rows u, v, w' on an 'ipredicate ... inputs xyz' with a double entry:
#   - the leaves named in the modifier are vectors of input differences; each vector is multiplied by 2^-k, k the
#     binary exponent of its largest magnitude (exact: a power of two, results checked to be 0 or normal by the gate);
#     the sign expression is multilinear in these vectors, so its sign is unchanged (positive factors);
#   - the range gate of filter_bounds applies to the scaled leaves instead of the inputs: each is 0 or of magnitude
#     in [2^-GateLow, 2], GateLow = 960 / degree - 52 (268 at degree 3, 428 at degree 2); an undecided gate goes to
#     the exact stage;
#   - the error rule is unchanged (leaf difference c = u; x +- y: (u + max(cx, cy)) / (1 - u);
#     x y: (u + cx + cy + cx cy) / (1 - u); decided if |e~| > fl(K m~), K = ceil_double(c / (1 - u))).
# Constants by that rule (scripts/check_bounds.py, exact rationals): orient2d c = 4.0000000000000009 u,
# K = 0x1.0000000000002p-51; orient3d c = 8.0000000000000036 u, K = 0x1.0000000000003p-50.
# The per-operation lemmas are the ones tools/Z3Pilot/filter_rules.py already checks (leaf, sum, product, decision,
# exponent grid); the scaling adds no floating-point error inside the gate.
ipredicate Orient3dRows(a, b, c, d) bound 1 inputs xyz
    # scale rows u, v, w
    ux = b.x - a.x
    uy = b.y - a.y
    uz = b.z - a.z
    vx = c.x - a.x
    vy = c.y - a.y
    vz = c.z - a.z
    wx = d.x - a.x
    wy = d.y - a.y
    wz = d.z - a.z
    m1 = uy * vz - uz * vy
    m2 = uz * vx - ux * vz
    m3 = ux * vy - uy * vx
    t1 = wx * m1
    t2 = wy * m2
    t3 = wz * m3
    sign t1 + t2 + t3
