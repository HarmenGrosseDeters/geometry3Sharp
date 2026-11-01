# Integer sign predicates of ExactMeshArrangement on fixed-width points, for tools/PredicateCompiler
# (docs/design/PREDICATE_COMPILER.md). Each one: 'ipredicate Name(points)', definitions 'x = expression' (+, -, *
# and parentheses over the inputs and earlier names), then 'sign expression'. The inputs are p.x, p.y and p.w of a
# point p: its components i and j (the projection axes, parameters of the generated method) and W. The comment lines
# right before a predicate go into the generated code.

# ExactMeshArrangement.Orient: the sign of Det3 of the rows (x_i, x_j, W) of a, b, c, expanded along the first row
# as ExactPoint3.Det3.
ipredicate Orient(a, b, c)
    sign a.x * (b.y * c.w - b.w * c.y) - a.y * (b.x * c.w - b.w * c.x) + a.w * (b.x * c.y - b.y * c.x)

# ExactMeshArrangement.InCircle: Det3 of the rows (A s, B s, A^2 + B^2) of each of a, b, c against d, with
# A = x w_d - x_d w, B = y w_d - y_d w, s = w w_d (the derivation is at ExactMeshArrangement.InCircle), expanded
# along the first row. Two points with W = 1 give the row (x - x_d, y - y_d, ...): the same values.
ipredicate InCircle(a, b, c, d)
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
    sign r1 * (s2 * u3 - s3 * u2) - r2 * (s1 * u3 - s3 * u1) + r3 * (s1 * u2 - s2 * u1)
