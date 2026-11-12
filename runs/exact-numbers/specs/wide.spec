# PROTOTYPE SPEC (exact-number study, runs/exact-numbers): the arrangement's integer predicates of
# tools/PredicateCompiler/predicates.spec at wider input bounds, for a variable-width limb kernel behind FixedPoint3
# (P3b). Same formulas, same rules (Compiler.cs: value rule and length rule); only the bound b changes:
#   b = 320: points from three planes beyond the fixed width (X up to 309 bits, W up to 255; EXACT_BOOLEAN_TAIL_55928.md)
#   b = 832: unreduced piece centroids (X up to 807 bits, W up to 753)
# Run: dotnet run --project tools/PredicateCompiler -- --table specs/wide.spec

ipredicate Orient320(a, b, c) bound 320
    m1 = b.y * c.w - b.w * c.y
    m2 = b.x * c.w - b.w * c.x
    m3 = b.x * c.y - b.y * c.x
    t1 = a.x * m1
    t2 = a.y * m2
    t3 = a.w * m3
    sign t1 - t2 + t3

# FixedArith.InCircle (ExactMeshArrangement.InCircle), the general row: Det3 of the rows (A s, B s, A^2 + B^2) of
# each of a, b, c against d, with A = x w_d - x_d w, B = y w_d - y_d w, s = w w_d, expanded along the first row
# (Det3Sign). The W = 1 row (A = x - x_d, s = 1) has the same values and fewer operations, so these bounds cover it.
ipredicate InCircle320(a, b, c, d) bound 320
    aA = a.x * d.w - d.x * a.w
    aB = a.y * d.w - d.y * a.w
    as = a.w * d.w
    bA = b.x * d.w - d.x * b.w
    bB = b.y * d.w - d.y * b.w
    bs = b.w * d.w
    cA = c.x * d.w - d.x * c.w
    cB = c.y * d.w - d.y * c.w
    cs = c.w * d.w
    r1 = aA * as
    r2 = aB * as
    r3 = aA * aA + aB * aB
    s1 = bA * bs
    s2 = bB * bs
    s3 = bA * bA + bB * bB
    u1 = cA * cs
    u2 = cB * cs
    u3 = cA * cA + cB * cB
    n1 = s2 * u3 - s3 * u2
    n2 = s1 * u3 - s3 * u1
    n3 = s1 * u2 - s2 * u1
    t1 = r1 * n1
    t2 = r2 * n2
    t3 = r3 * n3
    sign t1 - t2 + t3

ipredicate Orient832(a, b, c) bound 832
    m1 = b.y * c.w - b.w * c.y
    m2 = b.x * c.w - b.w * c.x
    m3 = b.x * c.y - b.y * c.x
    t1 = a.x * m1
    t2 = a.y * m2
    t3 = a.w * m3
    sign t1 - t2 + t3

# FixedArith.InCircle (ExactMeshArrangement.InCircle), the general row: Det3 of the rows (A s, B s, A^2 + B^2) of
# each of a, b, c against d, with A = x w_d - x_d w, B = y w_d - y_d w, s = w w_d, expanded along the first row
# (Det3Sign). The W = 1 row (A = x - x_d, s = 1) has the same values and fewer operations, so these bounds cover it.
ipredicate InCircle832(a, b, c, d) bound 832
    aA = a.x * d.w - d.x * a.w
    aB = a.y * d.w - d.y * a.w
    as = a.w * d.w
    bA = b.x * d.w - d.x * b.w
    bB = b.y * d.w - d.y * b.w
    bs = b.w * d.w
    cA = c.x * d.w - d.x * c.w
    cB = c.y * d.w - d.y * c.w
    cs = c.w * d.w
    r1 = aA * as
    r2 = aB * as
    r3 = aA * aA + aB * aB
    s1 = bA * bs
    s2 = bB * bs
    s3 = bA * bA + bB * bB
    u1 = cA * cs
    u2 = cB * cs
    u3 = cA * cA + cB * cB
    n1 = s2 * u3 - s3 * u2
    n2 = s1 * u3 - s3 * u1
    n3 = s1 * u2 - s2 * u1
    t1 = r1 * n1
    t2 = r2 * n2
    t3 = r3 * n3
    sign t1 - t2 + t3

