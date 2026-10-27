# Design: slope construction and daylighting on TINs (open-pit mining)

Status: **phase 0, design only, for review.** Nothing here is implemented. The open questions for the user are
in [section 12](#12-open-questions-for-the-user). Section numbers are used in review comments.

Scope: a slope surface (Böschung) that starts at a design edge (the toe line, Böschungsunterkante, or crest line
for fill), with a slope given per toe vertex, and its intersection with the terrain (catch line, Schnittlinie with
the terrain, "daylight line"). All inputs are TINs, at open-pit scale (kilometres, UTM coordinates, TINs of
10^6..10^7 triangles). Cut and fill volumes come from the same overlay. Berms are not built yet, but the design
has to leave room for them ([section 10](#10-berms)).

**Hard requirement (KO criterion):** the generated slope is never steeper than specified, anywhere. This must hold
for every output triangle as stored in doubles (after rounding), and must be checkable per triangle by an exact
oracle.

## Summary

1. **What "never steeper" means has to be fixed first** ([section 2](#2-what-never-steeper-means)). The slope of a
   triangle is the magnitude of its gradient over XY (the tangent of the angle of its fall line against the
   horizontal, in any direction). What needs deciding is the *allowed* slope at a point of the wall, given only
   slopes at the toe vertices. There are two natural readings. They agree wherever the slope is constant along
   the toe, and they differ near changes of slope. **The user has to choose** (question Q1).
2. **Recommended construction (core hypothesis, confirmed):** one continuous slope surface over XY, the *lower
   envelope of slope cones* with apexes on the toe line ("Böschungskegel", the textbook construction of
   descriptive geometry). It is built conservatively as a minimum of convex polyhedral functions whose planes all
   have gradient ≤ the governing slope. Concave corners are clipped and convex corners get fans, both automatically
   from the minimum, and offset fronts can never self-intersect. The catch line is the zero set of slope minus
   terrain on a *topological* overlay of the two TINs, with exact signs from `ExactPredicates.Orient2d` and
   `Orient3d`. Lines are connected through shared overlay vertices, so no tolerance-based chaining is needed.
   Volumes come from the same overlay.
3. **The KO guarantee comes from conservative construction plus exact verification inside `Compute()`.** The
   surface is built with a small slope margin. Each output triangle is checked by an exact predicate on its double
   coordinates, slivers that rounding could tip over are collapsed, and `Compute()` fails rather than return a
   violating triangle. Even exact construction alone would not be enough. Section 2.4 shows three ways a TIN
   becomes steeper than the surface it samples: an inscribed fan, interpolation across a crease, and rounding.
4. **Today's ray-per-point approach breaks the KO criterion by construction** ([section 3](#3-the-ray-per-point-approach-of-today)).
   It is steeper than specified at every convex corner (inscribed fans) and wherever the slope changes between
   neighbouring points (triangles up to sqrt(s² + (Δs·r/L)²), eg 10 instead of 1). Its chaining problem with
   grazing intersections is a missing-topology problem, which the overlay removes.
5. **Rejected as the core:** the weighted straight skeleton (weights per edge, not per vertex; mitred corners
   over-excavate; degenerate and non-planar with weights), Fast Marching (approximate, not conservative; kept as
   the solver for the second reading of section 2 if the user picks it), and contour lofting (no slope guarantee
   between contours).

## 1. Terms and conventions

- **Toe line**: the 3D polyline (open or closed) where the slope starts, normally the boundary of the design TIN
  (pit floor). For cut the slope rises from it outwards; for fill it falls from it outwards (the "toe" is then the
  crest of the dump, the term is kept for both).
- **Slope** s at a toe vertex: s = tan(angle) = rise / run, dimensionless, s > 0. Mining gives an angle in degrees
  (bench face angle, inter-ramp angle); civil engineering gives 1:n (s = 1/n). Converting either one to a double
  can round *up* (steeper). The API therefore converts conservatively: `FromDegrees` takes `Math.Tan` and steps
  down a few ulps, because `Math.Tan` is not guaranteed correctly rounded and may differ between net48 and net10.
  `FromRatio(n)` takes 1/n rounded down, checked exactly (s·n ≤ 1). The oracle compares with the double s actually
  used.
- **Slope of a triangle**: with n = (b−a)×(c−a), ‖∇z‖ = sqrt(nx² + ny²) / |nz|. It is independent of the
  direction, and the fall line has exactly this slope. The angle is measured against the horizontal plane, so the
  reference is the vertical axis. This is why only rotations **about z** are admissible similarities for this
  operation (section 7).
- **Catch line** (daylight line): where the slope surface meets the terrain.
- **Cut / fill**: cut where the final surface lies below the terrain, fill where it lies above.

## 2. What "never steeper" means

### 2.1 Interpolation along the toe

Slopes are given at toe vertices. Between two vertices the specified slope is **linear in the arc length** of
the toe, s(t). This is the only interpolation that needs no further input. Linear interpolation has its minimum
over any stretch of the toe at one of the stretch's endpoints, which makes conservative bounds cheap: the
smallest allowed slope over a sub-segment is the smaller of its two end values.

### 2.2 Two readings of "allowed slope at a point of the wall"

A wall point x is not on the toe, so it needs a rule for *which* toe point's slope applies:

- **R1, fall line (slope cones).** The allowed slope at x is the slope of the toe point where the fall line
  through x reaches the toe. The surface that is as steep as allowed under R1 is the **lower envelope of the
  slope cones** (cut; the upper envelope for fill):

      S(x) = min over toe points q of  z(q) + s(q) · |x − q|xy

  This is the classical construction of descriptive geometry ("Böschungsfläche als Hüllfläche der Böschungskegel"
  in the kotierte Projektion). It is the McShane extension (1934) of the toe heights, and its Hopf–Lax form
  solves ‖∇S‖ = s(q*) along straight characteristics. It also matches the precedence cones of Lerchs–Grossmann
  pit optimization, including variable-slope variants (Khalokakaie 1999/2000; Gilani & Sattarvand 2015). The
  closed form needs no PDE solve. Every point's gradient is exactly the slope of the active cone's apex, and the
  fall line runs straight to that apex.
- **R2, nearest toe point (slope field).** The allowed slope at x is σ(x) = s at the toe point nearest to x in
  XY, ie the slope "in front of" each toe point. This is the mental model of rays per point and of geotechnical
  sectors. The steepest surface under R2 is the viscosity solution of the eikonal equation ‖∇S‖ = σ(x), with S
  equal to the toe heights on the toe: the weighted geodesic distance, which refracts where σ changes (weighted
  region problem, Mitchell & Papadimitriou 1991).

**Where they agree.** With a constant slope along a toe stretch, both give the same surface near that stretch:
planes through the toe segments, cones at convex corners, valleys at concave corners.

**Where they differ** (formulas derived in [appendix A](#appendix-a-derivations), numbers checked with a throwaway script):

| situation | R1 surface (min of cones) | R2 surface (eikonal) |
|---|---|---|
| straight toe, slope jumps from s_a = 1/3 (1:3, overburden) to s_b = 1 (rock) | the gentle cone from the transition point spreads into the steep sector up to **h · sqrt(s_b²/s_a² − 1) = 2.83 h** (h = height above the toe) | the transition zone in the steep sector is **h · sqrt((s_b−s_a)/(s_b+s_a)) = 0.71 h** wide, at slope s_b |
| 300 m wall at that transition | about 850 m of the steep wall is excavated at 1:3 (more volume, never steeper) | about 210 m of transition at 45° |
| ramp in the toe (grade g) with slightly varying slope | at most a factor **sqrt(1 + g²/s²)** steeper than the nearest toe point's slope (g = 0.1, s = 0.33: 4.5 %; s = 1: 0.5 %), in a narrow sector in front of the upper end | never steeper than σ by definition |
| point in the R2 transition zone | slope = the slope of its fall line's apex | slope s_b, while its fall line ends at a gentle toe point (it violates R1) |

So neither reading's surface satisfies the other reading in all cases. Under R1, R2's transition zones are too
steep. Under R2, R1 overshoots by up to sqrt(1 + g²/s²) on ramps. And R1 over-excavates massively at sector
transitions. **Which one is "the specified slope" is the user's decision (Q1).** If the toe slope is constant
along long stretches (the usual case), both readings give the same result, so the choice only matters at
changes of slope.

### 2.3 Recommendation

- **Phase 1: R1** (min of cones). It has a closed form, its facets are exact planes and cones, and the KO
  criterion follows from construction plus a per-triangle check without any PDE discretization. It is also the
  established textbook and pit-optimization definition.
- **R2 as an option** (`SlopeReference.NearestToePoint`) if the user needs sector transitions without
  over-excavation. Construction: an eikonal solve on a refined triangulation (Fast Marching on triangles, Kimmel &
  Sethian 1998; or an ordered upwind method), followed by a conservative correction. Each triangle is lowered
  (cut) until its gradient is ≤ min σ over the triangle, upwind in order of height. The same exact oracle then
  runs as in R1. More work, and not exact; plan it as a second phase.
- A strict variant of R1 that is also R2-safe on ramps is possible: scale the slope of each piece near a change
  of slope by 1/sqrt(1 + g²/s²). It costs volume, so it waits for Q1.

### 2.4 Why a TIN of the right surface can still be too steep

The construction must avoid three effects that each break the KO criterion, all reproduced in the prototype:

1. **Inscribed fan.** A convex corner approximated by a fan whose outer vertices lie *on* the cone has facets
   with slope s / cos(θ/2) (15° steps: +0.86 %). The fix is facets **tangent** to the cone: outer vertices
   lowered by the factor cos(θ/2), so each facet has slope exactly s and lies below the cone (cut).
2. **Interpolation across a crease.** If a triangle's vertices lie on a piecewise-linear surface but the triangle
   straddles a crease (ridge or valley), its slope can be arbitrarily large (vertices (−1,0), (1,0), (0,ε) on
   −s|x|: slope s/ε). So sampling the surface at points and triangulating is not enough. **Every output triangle
   must lie in a single facet** of the slope surface, so the TIN contains all creases as edges.
3. **Rounding.** Facet vertices are intersections of planes, so they are not doubles. Rounding moves them by up
   to ulp(coordinate), about 1e-9 m at UTM northings. That changes the slope of a triangle with inradius ρ by
   about ulp/ρ relative: harmless for metre-sized triangles, unbounded for slivers. The fix is a margin,
   collapsing slivers, and an exact check (section 5.4).

## 3. The ray-per-point approach of today

Described from the user's account only; that code is not looked at or used (license). It casts rays per toe
point, builds slope triangles between neighbouring rays, intersects each triangle with the terrain separately,
then chains the pieces into lines.

- **Convex corners:** fans of rays with their ends on the cone are inscribed fans (section 2.4, item 1), steeper
  than s.
- **Change of slope between neighbouring points:** the quad between two rays of length r, toe spacing L and
  slopes s_1, s_2 is twisted. Its triangle (p_2, e_2, e_1) has slope sqrt(s_2² + ((s_1 − s_2)·r/L)²). For
  s = 1 → 0.5, r = 100 m, L = 5 m that is **10.0 instead of ≤ 1** (prototype; it equals the formula). This is the
  vertical step of section 2.2 showing up as triangles: different slopes in front of neighbouring points can only
  be reconciled by a transition zone, and linear interpolation between rays does not provide one.
- **Concave corners:** rays cross and the slope triangles overlap (self-intersections). They have to be
  clipped afterwards.
- **Chaining:** each piece is intersected with the terrain on its own, in floating point, so neighbouring pieces
  produce end points that do not coincide. With grazing intersections (flat slopes, nearly parallel to the
  terrain) the intersection positions are ill-conditioned and the chaining tolerance either joins wrong pieces
  or leaves gaps. The cure is not a better tolerance but shared topology. Each crossing point has to belong to
  one edge of a common overlay, computed once, and its sign has to be decided exactly (section 5.5).

## 4. State of research and practice

| Area | What exists | Relevance | Code / license |
|---|---|---|---|
| Descriptive geometry, earthworks ("kotierte Projektion", Erdbau) | Slope surfaces as envelopes of slope cones with apexes on the edge; slope planes through inclined edges tangent to the cones; Kehlen (valleys) and Grate (ridges) where faces meet; intersection with terrain contours. Textbooks, eg R. Fucke, K. Kirch, H. Nickel, *Darstellende Geometrie für Ingenieure*, Hanser (chapter on kotierte Projektion) | The R1 definition and the corner rules | none (textbooks) |
| Lipschitz extension, Hamilton–Jacobi | McShane 1934 and Whitney 1934: the largest / smallest extension with a Lipschitz bound is the min / max of cones. Hopf–Lax formula (L. C. Evans, *Partial Differential Equations*, ch. 3 and 10) | Maximality: no surface that satisfies R1 can be steeper anywhere than the min of cones (cut: least excavation) | none |
| Weighted Voronoi diagrams, lower envelopes of cones | Additively / multiplicatively weighted Voronoi diagrams are projections of lower envelopes of cones (Aurenhammer & Edelsbrunner 1984; Fortune 1987 sweep with cones; Okabe, Boots, Sugihara, Chiu, *Spatial Tessellations*, 2000). Lower envelopes of triangles / patches in 3D (Edelsbrunner, Guibas, Sharir 1989; Sharir & Agarwal, *Davenport–Schinzel Sequences*, 1995) | The structure of the R1 surface (an additively and multiplicatively weighted, "compoundly weighted" diagram of the toe) and how to compute the envelope | CGAL has Apollonius graphs and envelopes: **GPL, not used** |
| Weighted straight skeleton | Aichholzer, Aurenhammer, Alberts, Gärtner 1995 (straight skeleton, roof model); Aichholzer & Aurenhammer 1996; Eppstein & Erickson 1999 (*Raising roofs, crashing cycles, and playing pool*, weighted variant); Kelly & Wonka 2011 (procedural extrusions, per-edge angles); Biedl, Held, Huber, Kaaser, Palfrader 2015 (*Weighted straight skeletons in the plane*, CGTA 48: with weights not unique by the procedural definition, can be non-planar and contain cycles) | Gives a roof (slope) surface from a polygon with per-edge slopes | CGAL straight skeleton (also weighted, since 6.0): **GPL**. Kelly's campskeleton (Java): license to be checked, not needed |
| Fast Marching, eikonal on meshes | Sethian 1996; Kimmel & Sethian 1998 (*Computing geodesic paths on manifolds*, PNAS, triangulated domains, obtuse triangles by unfolding); Sethian & Vladimirsky 2003 (ordered upwind, anisotropic); Bornemann & Rasch 2006 (FMM on triangles, consistency) | Solver for R2; approximate, first order on the mesh | many implementations; implement from the papers if R2 is chosen |
| Weighted region shortest paths | Mitchell & Papadimitriou 1991 (exact, refraction, high polynomial complexity); later approximations | Exact R2 with piecewise-constant σ: impractical | — |
| Open-pit design | Lerchs & Grossmann 1965 (ultimate pit by graph closure; slope as precedence cones); Khalokakaie 1999 (PhD Leeds, *Computer-aided optimal open pit design with variable slope angles*), Khalokakaie, Dowd, Fowell 2000 (Mining Technology 109, variable slope angles in LG, by domains and four principal directions); Gilani & Sattarvand 2015 (Acta Montanistica Slovaca, variable slopes heuristic); Hustrulid, Kuchta, Martin, *Open Pit Mine Planning and Design* (2013): converting shells to designs bench by bench (toe → crest by face angle, crest → next toe by berm width, ramps) | Mining defines slopes by geotechnical domains / sectors (in plan) or by wall azimuth: closer to R2 than to R1. Vendor tools (Datamine Studio OP: toe + ramp + crest, contour design; Maptek Vulcan Automated Pit Designer: batter angle and berm width per region, line conditioning, "OUT" / "IN" for pits and dumps) build strings by offsetting, then surfaces from the strings | proprietary; documentation only |
| Road / earthworks daylighting | Civil 3D grading criteria: projection to a surface at a slope or grade. Its conflict resolution *Interior Corner Overlap* offers "average slope", "hold slope as maximum", "hold slope as minimum" for corners with different elevations, ie it admits steeper slopes in one option. Forum practice: round the inner corners until the grading stops overlapping itself. Bentley OpenRoads, 12d: side-slope conditions per template | Shows the same problems (corner overlaps, different heights at corners) handled by heuristics and user cleanup; "hold slope as maximum" is the KO-safe mode | proprietary; documentation only |
| GIS | Cut / fill as DEM of difference; TIN-TIN volumes; daylighting in GIS mostly raster-based (slope-limited "flattening" on grids) | Grids are excluded by the requirements; volume reference only | — |
| Overlay of planar subdivisions | Map overlay (de Berg et al., *Computational Geometry*, ch. 2); convex polygon intersection (O'Rourke, Chien, Olson, Naddor 1982); Simulation of Simplicity (Edelsbrunner & Mücke 1990) for consistent degeneracy handling | Topological overlay with exact predicates for the catch line | implement from papers |

What I did *not* find is any publication or open implementation that builds a slope surface with a *per-vertex*
slope and a verified "never steeper" guarantee. The vendor tools use offset strings and cleanup, and the
literature has the pieces (cone envelopes, eikonal, skeletons) but no guarantee for this problem. That gap is
the contribution here.

## 5. Chosen approach (R1)

Pipeline. Each step is local and runs in parallel over toe pieces or terrain triangles:

1. Validate the inputs and choose cut or fill per toe stretch.
2. Split the toe into pieces with a constant conservative slope.
3. Each piece defines a convex polyhedral function whose planes all have gradient ≤ the piece's slope; the slope
   surface S̃ is the minimum of these (cut).
4. Build S̃ as a TIN in which every triangle lies in one facet.
5. Round, collapse slivers, and verify every triangle exactly.
6. Overlay S̃ with the terrain topologically: catch line, final surface, volumes.

### 5.1 Pieces and conservative slopes

- Split each toe segment into sub-segments so that the drop in s over a sub-segment stays within the tolerance.
  The over-excavation of the piecewise-constant approximation at horizontal distance D is at most Δs·D, so a
  sub-segment's Δs must be ≤ `Tolerance` / D_max, with D_max = (max terrain height − min toe height) / s_min for
  cut. Each sub-segment gets s_k = the minimum of s over it (the smaller endpoint value). A toe with constant
  slope is not split.
- Cost: with a 0.5 → 1.0 change over one segment, D_max = 500 m and `Tolerance` = 0.1 m, that is 2500
  sub-segments for that segment. This only happens at changes of slope. If it matters, the exact varying-slope
  envelope (a ruled surface) can be approximated by tangent planes instead, see Q4.

### 5.2 The slope surface as a minimum of convex polyhedral functions

For a sub-segment from P0 to P1 with grade g = Δz / L along it (unit direction u, outward normal n in XY) and a
constant slope s > |g|, the envelope of the cones of its points is:

- **in its strip** (the region swept from the segment along the fall direction): the plane through the segment with
  gradient G = g·u + sqrt(s² − g²)·n, so |G| = s. The strip's side lines are not perpendicular to the toe when
  g ≠ 0;
- **beyond the ends**: the cones at P0 and P1, which the plane touches along the strip's side lines (C¹).

This function f_k(x) = min over t of z(t) + s·|x − p(t)| is **convex**, because the cone term is jointly convex in
(x, t) and the minimum over t of a jointly convex function is convex. So f_k is the supremum of its tangent
planes. The conservative polyhedral version keeps finitely many of them: both strip planes and tangent planes of
the end cones, spaced by an angle θ with s·D_max·(1 − cos(θ/2)) ≤ `Tolerance` (D_max = 500 m, s = 1, 1 cm:
θ = 0.72°, 125 facets for a 90° corner). Then

    S̃(x) = min over pieces k of  max over j of  P_kj(x),     |∇P_kj| = s_k (cut; fill: z ↦ −z)

- **Every facet of S̃ lies in one plane P_kj with slope exactly s_k**, so in exact arithmetic the KO criterion
  holds at every point, for R1 with s_k ≤ the allowed slope of every toe point of piece k. Because each tangent
  plane lies below its cone (cut), S̃ ≤ S everywhere: conservative (more excavation), and by at most `Tolerance`.
- **Convex corners** get fans from the cone facets. **Concave corners** get the valley (Kehle) between the two
  strip planes, where the min clips everything that would cross. **Self-intersections of offset fronts cannot
  occur**: S̃ is a function, and its level sets are the offset curves after their self-intersections have been
  resolved. So the separate self-intersection step (further item 1) is unnecessary for this approach.
- **Toe steeper than slope** (|g| ≥ s on a sub-segment, eg a steep ramp with a flat fill slope): no plane through
  the segment has slope s. The lower end's cone undercuts the upper end, so the toe is not on S̃. Report
  `Status.ToeSteeperThanSlope` with the segments. No silent fix: the design is infeasible as specified.
- **Toe undercut from elsewhere** (R1 is Euclidean): a far, low, gentle part of the toe, for example across a
  narrow pit with floors at different levels, can have its cone pass below another part of the toe. Check:
  S̃(q) = z(q) at every toe vertex and on every toe edge, an exact comparison of plane values. If the check fails,
  report `Status.ToeUndercut`. Shown in 2.2: if the toe is not undercut, a far cone that wins at x is gentler than
  the toe point where the segment from x to its apex crosses the toe, so R1 holds at the first contact of the fall
  line with the toe. Q3 asks whether cones should be restricted to the outside of the design (an exterior
  geodesic distance), which would remove the cross-pit influence but needs a visibility structure.

### 5.3 Building S̃ as a TIN (every triangle in one facet)

Computing the lower envelope of polyhedral patches in general is heavy. Two properties make it local here:

- A piece only competes with pieces within reach. A spatial index (a grid or tree over the pieces' strips and
  cone sectors, bounded by D_max) gives the candidates.
- The facet active at a point can be decided exactly: compare plane values P1(x) − P2(x) = (a1−a2)x + (b1−b2)y +
  (c1−c2) at a double point. That is a short sum of products, decided with a filter and expansion arithmetic.

Proposed algorithm: **evaluate and split**.

1. Start with a CDT of the domain (the terrain footprint ∩ the D_max neighbourhood of the toe), with the toe as
   constraint.
2. For each triangle, decide exactly which facet is active at its vertices and at its centroid.
3. If they differ, insert the crease between the two facets (the line P_a = P_b, clipped) as a constraint.
4. Repeat until every triangle lies in one facet.

S̃ has finitely many facets in the domain, so the refinement ends at the minimization diagram. Near-degenerate
configurations (three creases nearly concurrent) could create tiny edges. They are capped by `MinFeatureSize`
and handled by section 5.4. The result has few triangles: big planar strips, fans of about 100 facets per convex
corner.

Considered but not chosen as the first implementation: a plane-based exact envelope with implicit vertices (three
planes, like `ExactPoint3` and the EMBER-style representation of the boolean). It is exact combinatorially, but
the plane coefficients contain sqrt(s² − g²), so they are not doubles anyway. The double-coefficient planes have
to be verified after rounding in either case. If evaluate-and-split turns out fragile, it remains the fallback.

### 5.4 Rounding, margin, exact oracle, repair

- **Margin.** Build with s_build = s·(1 − δ). δ is derived from the coordinate magnitude and `MinFeatureSize`:
  δ ≥ c·ulp(max |coordinate|) / `MinFeatureSize`. At UTM northings (ulp ≈ 9.3e-10 m) with a 5 mm minimum feature,
  δ ≈ 1e-6. A 45° slope then becomes 44.99997°, negligible against survey accuracy. Both quantities are relative,
  so 2^k scaling gives the same δ.
- **Exact oracle** (a new predicate, so it gets the review of new error bounds in CLAUDE.md): for a triangle
  (a, b, c) in doubles and a double s, decide `nx² + ny² ≤ s² · nz²` with n = (b−a)×(c−a). The left minus the
  right side is a polynomial of degree 4 in the coordinates and degree 2 in s. Evaluate it with a double filter
  (error bound derived like Shewchuk's), then exactly with expansions, or with `BigInteger` outside
  [2^-100, 2^100], following the structure of `ExactPredicates`. It runs on the **output coordinates as stored**
  (UTM, not local). It also rejects triangles that are vertical or degenerate in XY (nz = 0).
- **Repair.** Triangles that fail are slivers by construction, since everything with an inradius above
  `MinFeatureSize` has margin δ. Collapse their shortest edge onto the vertex that lies on more facets, with exact
  orient2d checks so that no XY flip is created (the pattern of `MeshRoundingRepair`, after Milenkovic & Sacks
  2019). Then re-check the triangles around the collapse. Every collapse removes a vertex, so the loop
  terminates.
- **Never silent.** If any triangle still fails, `Compute()` returns false with `Status.SlopeViolation` and the
  triangle ids. It never returns a violating result. Outputs: `MaxSlopeRatio` (the largest ‖∇‖ / s over the slope
  triangles, < 1, with an exact pass/fail per triangle) and the governing piece per triangle (triangle group), so
  the user can audit.

### 5.5 Overlay with the terrain, catch line, final surface

- **Topological overlay.** Each overlay face is the intersection of an S̃ triangle and a terrain triangle, as in
  `TerrainVolumeDifference`. Here, though, its vertices are identified symbolically:
  - a vertex of A inside B: (vertex id, triangle id);
  - a crossing of an A edge with a B edge: (edge id, edge id).

  All decisions use `ExactPredicates.Orient2d` on the input doubles: point in triangle, segment crossing, and the
  combinatorial convex intersection (O'Rourke et al. 1982). So both faces next to an overlay edge agree on its
  end points, and every constructed point is computed once from its identity by one canonical function, which
  makes it bitwise identical wherever it appears. XY degeneracies are frequent: grid-aligned survey data,
  coincident toe and terrain vertices, collinear edges. They are resolved by Simulation of Simplicity, with B
  shifted by (ε, ε²). The ε terms are differences and 2×2 determinants of doubles, so they are exact.
- **Exact sign of the difference D = S̃ − T at overlay vertices:**
  - vertex of A over triangle t of B: sign of `Orient3d(t, v)` times the XY orientation of t;
  - crossing of edges a and b: sign of `Orient3d(a0, a1, b0, b1)` times the sign of the XY crossing, which says
    which edge is above at the crossing.

  Both use the existing exact predicates, with no rationals and no tolerance. D = 0 (touching) is broken by a
  single global rule (S̃ perturbed up by ε: touching counts as "slope not below terrain").
- **Catch line.** D is linear on each overlay face, so a face has at most one zero segment, from edge crossing to
  edge crossing. A crossing point is identified by its overlay edge and placed at t = D0/(D0 − D1), clamped to
  (0,1). The D values are doubles with their signs forced to the exact ones. Segments join through these shared
  identities into polylines. These are simple, closed or ending at the domain boundary, by construction. **No
  chaining tolerance.** Grazing intersections give correct topology (possibly many small loops where the surfaces
  nearly coincide, which is the real geometry); only the positions are rounded.
- **Region and final surface.** For cut, the excavation is the set of faces with D < 0 that is **connected to the
  toe** through face adjacency. Components of {D < 0} not connected to the toe, eg a hill behind a valley that
  the cone passes under, are not excavated. Final surface = design inside the toe, S̃ on the region, terrain
  outside, stitched along the catch line through the shared vertices.
- **Mixed cut and fill** (the toe crosses the terrain): build S_cut (min of rising cones) and S_fill (max of
  falling cones). If the toe is consistent (5.2), then S_fill ≤ S_cut everywhere (triangle inequality). The final
  surface is the **clamp** max(S_fill, min(T, S_cut)), restricted to the components connected to the toe. It is
  continuous, it is the terrain where the terrain already lies within the allowed band, and the change from cut
  to fill needs no special case: where the toe meets the terrain, the slope length is zero.
- **Efficiency.** Only terrain triangles in the band where D changes sign need the detailed overlay. For all
  others the sign at their vertices (point location in S̃ plus one `Orient3d`) and at the S̃ vertices inside them
  decides whether they are in or out.
- **Catch line vertices.** A clipped slope triangle gets new vertices on the catch line. They are checked by the
  same oracle as all slope triangles (5.4). Their z is computed from the slope facet's plane and rounded towards
  the conservative side.

### 5.6 Special cases

| case | handling |
|---|---|
| **No daylight** (cut: the terrain rises faster than s, eg a mountain flank; fill: the terrain falls away faster) | the region reaches the boundary of the terrain footprint or of the D_max domain: `Status.NoDaylight`, with the boundary parts and the toe ranges that govern them (pieces of the faces there). The partial result is kept, never presented as complete |
| **Terrain holes, boundary** | the overlay covers the common footprint only; catch lines that end at a hole or at the boundary are open and flagged (`CatchLineOpen`) |
| **Flat slopes, grazing** | exact signs make the topology correct (5.5); the positions are as good as the rounding allows. Optional output: catch line segments whose D gradient is below a threshold (ill-conditioned positions), for the user to review |
| **Open toe line** (design edge, not a loop) | the end cones fan around the ends (R1). Whether the slope should instead stop at a lateral boundary is Q5 |
| **Terrain steeper than s inside the cut zone** (gullies where T < S̃) | not generated, so not under the KO criterion. Reported as area and as triangles, because the final wall contains it |
| **s = 0 or very small** (berms, flat fills) | s must be > 0 for the cone model. Horizontal pieces are berms (section 10). Small s gives large D_max, which only affects the domain size |
| **Not a height field** (overhangs, overlapping TINs) | rejected with `TerrainVolumeDifference.XYOverlaps` (exact), as `RequireHeightField` does today |

### 5.7 Volumes

On the same overlay, the region faces carry D linear on convex polygons, integrated exactly as in
`TerrainVolumeDifference` (split at D = 0, area times the centroid value), relative to a local origin. Cut and fill
volumes, areas, and per-toe-piece volumes (each slope triangle knows its piece) come out of one pass. The existing
`TerrainVolumeDifference(T, Result)` serves as the independent check in the tests.

### 5.8 Robustness at UTM magnitudes

- Exact predicates run on the **input doubles** (orient2d / orient3d are exact for all finite doubles), never on
  shifted copies.
- Constructions run in local coordinates relative to an origin snapped to a power-of-two grid coarser than the
  extent. Then x − x0 is **exact**: both are multiples of ulp(x), and |x − x0| ≤ |x|. So local inputs give
  bitwise the same predicates, and only constructed points are rounded, once, when mapped back.
- Tolerances (`Tolerance`, `MinFeatureSize`, δ) are relative to the extent and the coordinate magnitude, with no
  absolute constants. `float` appears nowhere in the path.
- Where exact predicates are needed:
  - the overlay (orient2d), the signs of D (orient3d), and SoS;
  - the slope oracle (a new predicate);
  - the comparison of plane values at points (new, small: a sum of three products);
  - the XY-flip checks during repair.

  Constructed coordinates themselves stay doubles.

### 5.9 Rejected alternatives

| alternative | why not as the core |
|---|---|
| **Weighted straight skeleton** | weights per edge, but slopes are per vertex: linearly varying slopes are not planar faces. It mitres convex corners, which gives the max of planes, *below* the cone, so needless over-excavation (the cone is the least-excavation surface under R1). Not unique or planar with weights (Biedl et al. 2015). Event handling is fragile, and the only mature implementation is GPL (CGAL). Its useful idea, faces that meet in valleys, is contained in the min of cones |
| **Fast Marching / eikonal** | first-order approximation that can err on either side, so not conservative without a correction step. For R1 the closed form is exact. Kept as the solver of the R2 option (2.3) |
| **Rays per point** (today) | breaks the KO criterion at convex corners and at changes of slope; needs clipping at concave corners and tolerance-based chaining (section 3) |
| **Offset strings per height + lofting** (vendor practice) | the offsets are the level sets of S̃ anyway, and lofting between them has no slope guarantee (further item 3) |
| **Grid / raster methods** | excluded by the requirements (TIN input), and no exact guarantee |

## 6. API sketch

Consistent with the existing terrain classes: public fields as options, `Compute()` returns bool, outputs as
fields, `CancelF`. A `Status` enum as in `ExactMeshBoolean` and `UVAtlasGenerator`.

```csharp
namespace gs
{
    /// Slope surface from a toe line with a slope per toe vertex, its catch line with the terrain, the final
    /// surface and cut / fill volumes. Never steeper than specified (verified per triangle, exactly).
    public class TerrainSlopeDesign
    {
        public enum Directions { Auto, Cut, Fill }
        public enum SlopeReferences { FallLine /* R1, phase 1 */, NearestToePoint /* R2, later */ }
        public enum Statuses { Success, InvalidInput, NotHeightField, ToeSteeperThanSlope, ToeUndercut,
                               NoDaylight, SlopeViolation, Cancelled }

        // inputs
        public DMesh3 Terrain;                 // TIN, height field
        public DMesh3 Design;                  // optional: design TIN inside the toe; its boundary is the toe if Toe is null
        public List<Vector3d> Toe;             // toe polyline (on the design boundary)
        public bool ToeClosed;
        public double[] Slopes;                // per toe vertex: s = rise / run > 0 (see Slope.FromDegrees / FromRatio)
        public Directions Direction = Directions.Auto;
        public SlopeReferences SlopeReference = SlopeReferences.FallLine;
        public double Tolerance = 0;           // max over-excavation of the conservative approximation; 0 = relative default
        public double MinFeatureSize = 0;      // slivers below are collapsed; 0 = relative default
        public bool ComputeVolumes = true;
        public Func<bool> CancelF = () => false;

        // outputs
        public Statuses Status;
        public DMesh3 SlopeSurface;            // slope triangles only; group id = governing toe piece
        public DMesh3 Result;                  // design + slope + terrain, stitched along the catch lines
        public List<List<Vector3d>> CatchLines; public List<bool> CatchLineClosed;
        public List<Interval1d> NoDaylightToeRanges;   // arc length ranges of the toe
        public double CutVolume, FillVolume, CutArea, FillArea;
        public double MaxSlopeRatio;           // max over slope triangles of |grad| / allowed, < 1 when Success
        public List<int> ViolatingTriangles;   // empty unless Status == SlopeViolation

        public TerrainSlopeDesign(DMesh3 terrain, List<Vector3d> toe, bool closed, double[] slopes) { ... }
        public bool Compute() { ... }
    }

    public static class Slope
    {
        public static double FromDegrees(double angle);   // conservative: never rounds steeper
        public static double FromRatio(double n);          // 1:n, conservative
        /// exact: is triangle (a, b, c) no steeper than s? false also for triangles vertical in XY
        public static bool IsNotSteeper(Vector3d a, Vector3d b, Vector3d c, double s);
    }
}
```

The oracle `Slope.IsNotSteeper` is public so that users (and the user's existing pipeline) can verify any TIN. A
`g3cli slope` command can follow later in the pattern of the other commands.

## 7. Test plan and acceptance criteria

All test inputs are procedural (no mesh files): analytic terrains (planes, tilted planes, sine hills, cones,
valleys), grids triangulated in both diagonal directions, and random perturbations with fixed seeds.

**Oracles** (independent of the construction code):

- **O1, KO, exact:** every slope triangle passes `Slope.IsNotSteeper` against the slope of its piece. Then,
  independently, against a **brute-force R1 reference**: at all three vertices and the centroid, find the minimal
  cone over all toe pieces (O(n) per point, separate code), and require ‖∇‖ ≤ min s of all pieces within
  `Tolerance` of the minimum. This catches a wrong attribution.
- **O2, conservative and tight:** at random points of the slope region, 0 ≤ S_ref(x) − S̃(x) ≤ `Tolerance` (cut;
  mirrored for fill), with S_ref the brute-force min of exact cones.
- **O3, topology:**
  - the catch lines are closed, or end on the footprint boundary or a hole;
  - they are simple (no self-crossings: an exact segment test);
  - on a known analytic case they match the analytic line within the rounding resolution;
  - the final surface is a height field (`XYOverlaps` = 0) with no XY-inverted triangles (exact) and no gaps
    (boundary edges only on the outer boundary);
  - all coordinates are finite.
- **O4, volumes:** against `TerrainVolumeDifference(Terrain, Result)` and against closed forms (a plane slope
  against a tilted plane terrain gives a prism or wedge volume).

**Cases**:

| case | checks |
|---|---|
| straight toe, constant s, flat terrain | the catch line is the offset at distance H/s, exact up to rounding; the volume is analytic |
| convex toe polygon (square, regular n-gon), concave L and U shapes, star-shaped toe | fans at convex corners with exactly s (no inscribed facets), valleys at concave corners, no overlaps (O3) |
| varying slope per point (jumps, linear ramps, random per vertex) | O1, O2; the R1 transition width matches 2.83 h for 1:3 → 1:1 (documents the semantics) |
| inclined toe (ramp, g = 0.1) | planes tilted correctly (strip boundaries not perpendicular); `ToeSteeperThanSlope` for g ≥ s |
| two-level pit floor (toe undercut across the pit) | `ToeUndercut` reported, not silent |
| **flat slopes, grazing**: 1:10 slope against terrain with relief of a few cm, and a slope exactly coplanar with a terrain region | correct topology (O3), SoS rule applied consistently, no gaps, no chaining failures |
| no daylight (terrain rising faster than s), terrain with a hole, toe partly outside the terrain | `NoDaylight` / open catch lines, flagged, partial result consistent |
| mixed cut and fill (toe crossing a tilted terrain) | clamp surface continuous, both volumes, transition at the crossing points |
| slivers forced by near-concurrent creases | repair collapses them, O1 passes, or `SlopeViolation` (never a silent violation; a mutation that disables the repair must make this test fail) |

**Required by CLAUDE.md:**

- **Invariance** over `Similarity.All()` with rotations replaced by rotations about z (like the `ZUp` helper of
  `TerrainSafetyAlongSurfaceInvarianceTests`: the slope is defined against the vertical).
  - 2^k scales: bit for bit equal after mapping back.
  - UTM, 1e-3, 1e3, rotated about z: same topology (number and closedness of catch lines, same pieces per
    triangle) and positions within `Resolution`.
  - **O1 passes in every transformed frame**, on the transformed output itself, the critical check for UTM
    rounding.
- **Reuse:** compute A, then B on the same instance, equal to a fresh instance (`Invariance.Difference`).
- **Determinism:** parallel equals serial and repeated runs are bitwise equal (the overlay and the evaluate-and-split
  refinement must not depend on the thread schedule; ties broken by ids).
- **Mutation checks:**
  - drop the margin δ, the tangent-plane lowering, or the sliver repair → O1 fails;
  - replace the exact D signs with double signs → the grazing tests fail;
  - remove the connected-to-toe filter → the hill-behind-a-valley test fails.

**Acceptance (phase 1 done):** all cases green including O1 at UTM. No status other than the documented ones.
The benchmark of section 8 runs at km scale with O1 and O3 green and the times recorded.

## 8. Benchmark plan

A new case `slope` in `benchmarks/TerrainBenchmark`, on the real Hambach data (DGM1 2022, dl-de/zero, downloaded by
`fetch_data.py`, nothing committed):

- **Toe lines:** level lines of the DGM in the pit (eg the pit bottom at a given elevation), simplified,
  1–20 km long, with procedural slopes per vertex (constant; sectors 1:3 / 1:1; random within 30–60°).
- **Sizes:** 1, 4 and 12 km² terrain windows (2M–24M triangles), `coords=utm` and `coords=local`.
- **Recorded:**
  - time and peak memory per stage (pieces, S̃, verification, overlay, volumes);
  - triangle counts of S̃ and of the result;
  - `MaxSlopeRatio`, the counts of repaired slivers and of `NoDaylight` toe metres;
  - the O1 / O3 results.
- **Reference:** a numpy brute-force min of cones on a grid. It checks O2 and the volumes (the tolerance against
  the reference must stay within `Tolerance`·area).
- **Comparison:** R1 against R2 volumes on the sector case, once R2 exists. This quantifies Q1 on real data.
- **Target** (to be confirmed by the first implementation): a 4 km² window with a 10 km toe in minutes, dominated
  by the overlay. `TerrainVolumeDifference` does 8M + 8M triangles in 69 s today, and the overlay here is needed
  only in the catch-line band.

## 9. Further items

### 9.1 (Item 1) Self-intersections of open offset polylines

**Unnecessary for the slope construction.** S̃ is a function, so its level sets (the offset curves, open or closed)
are extracted from the TIN topologically and are simple by construction. The min resolves what would be
self-intersections of the fronts. Still relevant for 2D tools outside this design (eg offsetting a polyline for a
haul road): there the library should use Clipper2 (Boost-1.0, official C# package; see ROADMAP Phase 5 #3), which
handles open paths, rather than build its own. Background: Held 1998 (Voronoi-based offsetting), Chen & McMains
2005 (offsetting by winding numbers).

### 9.2 (Item 3) Contour lofting

Classic surface reconstruction between planar contours:

- Keppel 1975 (max volume), Fuchs, Kedem, Uselton 1977 (*Optimal surface reconstruction from planar contours*,
  CACM; min area via shortest paths in a toroidal graph), Christiansen & Sederberg 1978 (greedy);
- branching and multiple contours: Ekoule, Peyrin, Odet 1991; Meyers, Skinner, Sloan 1992; Barequet & Sharir
  1996; Bajaj, Coyle, Lin 1996;
- topology-constrained: Zou et al. 2015.

Relevance: designs that exist only as bench strings (the vendor workflow) can be turned into TINs. **Lofting has
no slope guarantee**, even between exact offset curves with height step Δz and offset Δz/s. A triangle with an
edge on one contour and its third vertex on the other has slope Δz / (distance of that vertex to the edge's
*line*). If the vertex lies beyond the edge's end, that distance can be much smaller than the offset distance, so
the triangle can be steeper than s. Lofting is therefore not used for slopes. As a later utility, a lofting
variant could choose its triangles under the exact oracle (reject steep candidates in the FKU graph search), with
a status when no compliant triangulation exists.

### 9.3 (Item 4) Volume per cell

Use case: cut / fill per block-model cell or per grid cell, for reserves and scheduling.

- **2D cells (columns):** clip the overlay polygons (D linear) by the cell lines in XY and integrate exactly, the
  same integration as 5.7. Simple, no 3D clipping needed.
- **3D blocks (z-extent):** per column, integrate the clamp of [lower, upper] surface to [z0, z1]. This is
  piecewise linear after splitting the polygons at the lines where either surface crosses z0 or z1, so it is
  exact again in 2D.
- **r3d** (Powell & Abel 2015, *An exact general remeshing scheme applied to physically conservative
  voxelization*, J. Comput. Phys. 297) does general polyhedron clipping and voxelization with moments. License
  checked at github.com/devonmpowell/r3d (October 2026): the current r2d / r3d / rNd files carry a LANL / DOE
  notice that grants copying and use, with no explicit right to modify or redistribute modified code, so they are
  **not MIT / Apache / BSD: paper only**. The 2015 code in `deprecated/` (Stanford / SLAC) is BSD-3-clause plus an
  "enhancements" clause (a license-back for improvements sent to the authors), so check it before any port. It is
  not needed: the height-field structure makes the 2D integration above exact and simpler. r3d's conservative
  voxelization stays a reference for general solids.

## 10. Berms

A bench design alternates a face at face slope s_f (bench height H, usually between **absolute** bench elevations
L_k) with a horizontal berm of width w. The overall (inter-ramp) slope is H / (H/s_f + w).

**How the chosen approach extends:**

- For a flat toe and constant parameters, the stepped surface is z_toe + φ(dist(x, toe)), with φ the monotone
  staircase profile. A monotone profile commutes with the min (min of φ(d) = φ(min d)). The bench crests and toes
  are then exactly offset curves of the toe at cumulative distances (offsetting by a and then b is offsetting by
  a + b for the Euclidean distance), as in the vendor practice, with no self-intersections.
- **General case** (inclined toe, absolute bench levels, slopes and widths per vertex): **iterate the core**.
  1. Face k is the min of cones from toe line k (with s_f per vertex), clipped at level L_k.
  2. Crest k is the level line {S̃ = L_k}, extracted topologically from the TIN (a simple polyline).
  3. The berm is the horizontal region from crest k out to distance w (per vertex).
  4. Toe k+1 is the outer boundary of the berm. Equivalently, it is the level set of the min of "flat-then-vertical"
     functions, ie an offset of crest k by w, again a min of distances.
  5. Repeat until daylight.

  Every step uses the same machinery (envelope, exact level-line extraction, overlay), so berms are a loop around
  phase 1, not a new algorithm.
- **New guarantees for berms:**
  - faces never steeper than s_f (O1 per face triangle);
  - berms horizontal (z exactly L_k, an exact check);
  - **berm width never less than w**: the distance function is 1-Lipschitz, so the distance between crest k and
    toe k+1 is ≥ w (an oracle: the exact minimum distance between the two polylines, as `TerrainSafetyLines`
    computes `MinLineDistance`).
- **Catch line with berms:** the overlay of the stepped surface with the terrain. Grazing contact between a flat
  berm and flat terrain is handled by the exact signs and SoS as before.
- **Not covered:** ramps (haul roads crossing benches), which are a design element of their own with grade
  constraints along a path. Leave room: the toe-line input may carry a ramp as an inclined stretch, and the
  status `ToeSteeperThanSlope` already guards against ramps steeper than the face.

So the phase 1 API needs one addition for berms: the ability to clip the slope at a height (`MaxHeight`) and
return the level line. The loop can then be built on top without changing the core.

## 11. Implementation phases (after review)

1. The slope oracle (`Slope.IsNotSteeper`, `FromDegrees`, `FromRatio`) with its exact predicate, error-bound
   review, and tests.
2. S̃ for constant slope: pieces, tangent planes, evaluate-and-split, margin, repair, O1 / O2 tests, invariance.
3. Per-vertex slopes (sub-segments), toe checks (`ToeSteeperThanSlope`, `ToeUndercut`).
4. Topological overlay with exact signs, catch line, connected region, final surface, volumes, special cases.
5. Mixed cut and fill (clamp).
6. Benchmark at km scale; docs (OPERATIONS.md, SOURCES.md).
7. Later: R2 option (if Q1 says so), berms loop, volume per cell, lofting utility.

## 12. Open questions for the user

- **Q1 (decisive): R1 or R2?** Under R1 (fall line, slope cones), a point of the wall may be no steeper than the
  toe point its fall line runs to. Under R2 (nearest toe point, sectors), it may be no steeper than the toe point
  nearest to it in plan.
  - With constant slopes both give the same result.
  - At a 1:3 → 1:1 transition, R1 flattens the steep wall over about 2.8 times the wall height; R2 makes a
    transition zone of about 0.7 times the height.
  - On ramps, R1 can be up to sqrt(1 + g²/s²) steeper than the nearest toe point allows (4.5 % at g = 0.1,
    s = 0.33).

  Which reading does the KO criterion mean? Proposal: R1 first, R2 as an option.
- **Q2: tolerances.** Are an approximation tolerance (more excavation than the exact surface, default eg 1 cm), a
  slope margin of about 1e-6 relative, and a minimum feature size of a few mm acceptable? The margin is what makes
  the guarantee hold under rounding.
- **Q3: cross-design influence.** Should a cone act across the design area (Euclidean, simpler; may make walls
  more conservative or report `ToeUndercut` with multi-level floors)? Or only around it (exterior distance)? Do
  the real designs have floors at different levels within one toe line?
- **Q4: how slopes change.** Do slopes change mostly in steps between sectors (few toe vertices) or continuously
  along the toe? This decides whether the sub-segment approach (5.1) is enough.
- **Q5: open toe lines.** At the ends of an open design edge, fan around the end (R1 default), or stop at a given
  lateral boundary?
- **Q6: angle convention.** Are slopes given as angles (degrees), as ratio 1:n, or as percent? Which one is the
  reference for the check (degrees need a conservative tan)?
- **Q7: berms and ramps.** Absolute bench elevations or relative to the toe? Berm widths per vertex? Are ramps
  part of the toe input?
- **Q8: output.** Does the user need the final merged surface, the slope faces alone, the catch line, or all
  three, and in which form (DMesh3 with groups, polylines)?

## Appendix A: derivations

Toe along the x axis at z = 0 unless stated, slope region y > 0, h = y.

- **Segment envelope.** For p(t) = (t, 0), z(t) = g·t, minimizing g·t + s·sqrt((x−t)² + h²) over t gives
  (x − t)/sqrt((x−t)² + h²) = g/s, ie a fixed direction of the generator, so the value is linear in (x, h):
  z = g·x + sqrt(s² − g²)·h. That is a plane with gradient (g, sqrt(s² − g²)), |G| = s, defined while the
  minimizing t lies in the segment (the strip). There is no solution for |g| ≥ s.
- **R1 transition width.** Slopes s_a for t < 0 and s_b for t ≥ 0. At (x, h) with x > 0, the steep plane gives
  s_b·h. The gentle part's best cone is at t = 0 (nearest gentle point): s_a·sqrt(x² + h²). The cone wins while
  s_a·sqrt(x² + h²) < s_b·h, ie x < h·sqrt(s_b²/s_a² − 1) (2.83 h for 1/3 → 1; the prototype found the switch
  between x = 2.8 and 2.9).
- **R2 transition width.** σ = s_a for x < 0, s_b for x > 0. A path from the gentle toe runs at slope s_a to
  (0, h') and then at s_b to (x, h). Minimizing s_a·h' + s_b·sqrt(x² + (h − h')²) over h' gives
  s_a·h + x·sqrt(s_b² − s_a²) (refraction). This is below the steep plane s_b·h while
  x < h·(s_b − s_a)/sqrt(s_b² − s_a²) = h·sqrt((s_b − s_a)/(s_b + s_a)) (0.71 h).
- **R1 against R2 on ramps.** The nearest toe point is q_n = (L, 0, g·L) with slope s_n, at x = (L, h). A lower toe
  point q* = (0, 0, 0) with s* > s_n wins iff s*·sqrt(L² + h²) < g·L + s_n·h. By Cauchy–Schwarz the right side is
  ≤ sqrt(g² + s_n²)·sqrt(L² + h²), so a win needs s* < sqrt(g² + s_n²). The slope at x then exceeds σ(x) = s_n by
  at most the factor sqrt(1 + g²/s_n²). It is attained near h/L = s_n/g.
- **Inscribed fan.** Apex at 0, outer vertices at radius R and angles ±θ/2 at height s·R. The facet's fall line runs
  along the bisector to the chord at distance R·cos(θ/2), so its slope is s / cos(θ/2). With the outer heights
  lowered to s·R·cos(θ/2) it is s (a tangent plane).
- **Twisted ray quad.** p_2 = (L, 0, 0), e_2 = (L, r, s_2·r), e_1 = (0, r, s_1·r). The facet has slope s_2 along
  y and (s_1 − s_2)·r/L along x, so its gradient magnitude is sqrt(s_2² + ((s_1 − s_2)·r/L)²).
- **Mixed cut and fill.** For any x, S_fill(x) = z_p − s_p·d(x, p) and S_cut(x) = z_q + s_q·d(x, q) for some toe
  points p, q. Toe consistency gives z_p − z_q ≤ s_min·d(p, q) ≤ s_min·(d(x, p) + d(x, q))
  ≤ s_p·d(x, p) + s_q·d(x, q), so S_fill ≤ S_cut.

## References

The sources this design was written from are listed in `docs/SOURCES.md` under "Terrain slope design (planned)".
The user's company code (Böschung / MsMassenberechnung) was not looked at and is not a source.
