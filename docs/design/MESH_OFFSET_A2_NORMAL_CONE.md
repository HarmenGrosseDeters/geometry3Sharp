# The normal-cone claim of E'' (for the probe; goes into the addendum if the probe passes)

**Edges (both sides).** Let p be in the relative interior of a mesh edge e shared by exactly two triangles t1, t2 (M
closed and manifold), with outward unit normals N1, N2, and let p be the nearest point of M to z, z not on M, with
sigma = +1 if z is outside S and -1 inside. Then p is also the nearest point of t1 and of t2 to z (they are subsets of
M that contain p), so w = z - p lies in the normal cone of t1 at p and in that of t2. Both cones lie in the plane P
orthogonal to e (p is interior to e, so the direction of e is tangent to both triangles). In P, each triangle is a
ray r_i from p (into t_i), and the normal cone of t_i at p is the closed half-plane {w : w . r_i <= 0}. So w lies in the
polar of cone{r1, r2}. The rays split P into the solid's sector (angle beta) and the outside sector (360 - beta).
(i) beta < 180 (a convex edge): cone{r1, r2} is the solid's sector, its polar is cone{N1, N2}, which points into the
outside sector, so z is outside: sigma = +1 and w in cone{N1, N2}; an inside z never has its nearest point on e.
(ii) beta > 180 (a reflex edge): cone{r1, r2} is the outside sector, its polar is cone{-N1, -N2} in the solid's sector:
sigma = -1 and w in cone{-N1, -N2}; an outside z never has its nearest point on e. (iii) beta = 180 (t1, t2 coplanar,
N1 = N2 = N): the polar of the line is the normal line, and w = sigma |w| N, since the open segment from p to z misses
M, so z is on the side of S that p + eps sigma N is on. In all three cases w is in sigma * cone{N1, N2} (empty when the
edge is convex from z's side, ie (i) with sigma = -1 or (ii) with sigma = +1). The convexity is the exact sign of
N1 . (c2 - A), c2 the vertex of t2 off e (negative: convex, positive: reflex, zero: coplanar).

**Faces and vertices.** If p is in the relative interior of a triangle t, then w = sigma |w| N_t (the same segment
argument as (iii)). If p is a vertex v of M, then w = z - v exactly: the probe uses the set {z - v : z in X} for the
vertices that can be nearest (E' rule: not excluded by a linear test over the corners), so **no cone claim is needed at
vertices**, convex, reflex or saddle, on either side; this part is sound by the same argument as in E' and narrow
(its directions spread by about diam(X) / u). So the inset side (sigma = -1) is covered as cleanly as the offset side.
Not covered: non-manifold edges (excluded: the input is clean), degenerate triangles (N = 0; excluded the same way).
