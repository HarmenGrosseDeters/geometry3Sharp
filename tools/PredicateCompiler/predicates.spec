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
