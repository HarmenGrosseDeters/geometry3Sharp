# The integer sign predicates of ExactMeshArrangement on fixed-width points, for tools/PredicateCompiler (bounds of
# docs/design/FIXED_WIDTH_EXACT_POINTS.md section 5). Each one: 'ipredicate Name(points) bound b' (every input
# component has |v| <= 2^b - 1; FixedPoint3 admits b = 255), definitions
# 'x = expression' (+, -, * and parentheses over the inputs and earlier names), then 'sign expression'. The inputs are
# p.x, p.y and p.w of a point p: its components i and j (the projection axes) and W. Every operation is one FixedArith
# operation of the kernel (the same operations and sharing; the order of independent values may differ, which
# changes neither the bounds nor the scratch); a value the kernel uses twice is named and used by its name.

# FixedArith.Orient (ExactMeshArrangement.Orient): Det3 of the rows (x_i, x_j, W) of a, b, c, expanded along the
# first row as FixedArith.Det3Sign and ExactPoint3.Det3: a (e i - f h) - b (d i - f g) + c (d h - e g).
ipredicate Orient(a, b, c) bound 255
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
ipredicate InCircle(a, b, c, d) bound 255
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
