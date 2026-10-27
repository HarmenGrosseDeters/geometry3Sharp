# Design: slope construction and daylighting on TINs (open-pit mining)

Status: **phase 0, design only, for review.** Nothing here is implemented. Revised after an independent review
(its findings are worked in; the derivations it questioned are in appendix A). The open questions for the user are
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
   slopes at the toe. There are two natural readings. They agree wherever the slope is constant along the toe,
   and they differ near changes of slope. **Decided** (2.5): R1, the cone-envelope model, defined through the
   active cone source, as the first mode. Geotechnical sectors come later as a separate mode with explicit sector
   polygons.
2. **Recommended construction (core hypothesis, confirmed):** one continuous slope surface over XY, the *lower
   envelope of slope cones* with apexes on the toe line ("Böschungskegel", the textbook construction of
   descriptive geometry).
   - It is built conservatively as a minimum of convex polyhedral functions whose planes all have gradient ≤ the
     governing slope. The planes get double coefficients, and their envelope is computed exactly and
     combinatorially (three-plane vertices, symbolic labels).
   - Concave corners are clipped and convex corners get fans, both automatically from the minimum, and offset
     fronts can never self-intersect.
   - The catch line is the zero set of slope minus terrain on a *topological* overlay of the two TINs, with exact
     signs from `ExactPredicates.Orient2d` and `Orient3d`. Lines are connected through shared overlay vertices, so
     no tolerance-based chaining is needed.
   - Volumes come from the same overlay.
3. **The KO guarantee comes from conservative construction plus exact verification inside `Compute()`.**
   - Every slope triangle of the output is checked exactly on its stored doubles: it is no steeper than the slope
     of the toe piece it is attributed to, and it lies below all cones of that piece.
   - The surface is built with a small slope margin, whose error bound is derived from the triangle altitudes in
     5.4.
   - Slivers are repaired without leaving their cell.
   - `Compute()` fails rather than return a violating triangle. The guarantee holds for the doubles in memory;
     export needs round-trip writers or an output grid.
   - Even exact construction alone would not be enough. Section 2.4 shows three ways a TIN becomes steeper than
     the surface it samples: an inscribed fan, interpolation across a crease, and rounding.
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
- **Slope** s: s = rise / run, dimensionless, s > 0. Internally the library uses s only. Inputs come as ratio 1:n
  (n metres horizontal per metre vertical, s = 1/n; the reference input convention, decided in 2.5), as percent
  (s = p/100) or as an angle (s = tan α). All three converters are offered, and **the origin of the input is kept
  for the check**: the oracle checks against what the user specified, not only against a rounded double.
  - Ratio 1:n: exact check (nx² + ny²)·n² ≤ nz² on the stored triangle, no rounding involved.
  - Percent: exact check (nx² + ny²)·10⁴ ≤ p²·nz².
  - Angle: tan α is transcendental, so there is no exact rational check. Phase 1 guarantees the slope against the
    double s that is used and says so (`SlopeSpec.Guarantee = AgainstDouble`). Stepping `Math.Tan` down by "a few
    ulps" is not a formal guarantee against the angle as entered. A guarantee against the angle needs a proven
    conservative interval evaluation of tan (a later item, with a derived error bound, tested against a
    high-precision reference on net48 and net10).
- **Arc length** along the toe (for interpolation) is measured in XY.
- **Meaning of the input slope** (decided, 2.5): the maximum admissible **local** slope of the generated surface,
  checked for every output triangle. It is not an overall or inter-ramp angle; those are checked separately and
  later (section 10). Which slope is entered in a given planning task is decided by the application.
- **Slope of a triangle**: with n = (b−a)×(c−a), ‖∇z‖ = sqrt(nx² + ny²) / |nz|. It is independent of the
  direction, and the fall line has exactly this slope. The angle is measured against the horizontal plane, so the
  reference is the vertical axis. This is why only rotations **about z** are admissible similarities for this
  operation (section 7).
- **Catch line** (daylight line): where the slope surface meets the terrain.
- **Cut / fill**: cut where the final surface lies below the terrain, fill where it lies above.

## 2. What "never steeper" means

### 2.1 Interpolation along the toe

Decided (2.5): the interpolation is part of the API contract, not implicit.

- **Values per segment end**, not one value per shared vertex. A real jump at a sector boundary needs different
  values on the left and on the right of a vertex. One value per vertex can only express continuous change.
- **Supported cases:**
  - constant slope per segment (the simple standard);
  - piecewise constant with explicit jumps at sector boundaries;
  - continuous change along a segment (optional).
- **"Linear" is ambiguous:** linear in s, linear in n of 1:n, and linear in the angle give different surfaces.
  The library interpolates **linearly in s** by default and documents this. Input given as 1:n with linear
  interpolation in n must not be silently replaced by interpolation in s, so the interpolation is a parameter
  (`LinearInSlope`, `LinearInRatio`, `LinearInAngle`).
- **Conservative bounds stay cheap in all three cases.** n linear means s = 1/n is monotone along the segment, and
  α linear means tan α is monotone. So the smallest allowed slope over a sub-segment is always the smaller of its
  two end values.
- Arc length is measured in XY.

### 2.2 Two readings of "allowed slope at a point of the wall"

A wall point x is not on the toe, so it needs a rule for *which* toe point's slope applies:

- **R1, active cone source (cone-envelope model).** The allowed slope at x is the slope of the toe point whose
  slope cone is active (lowest, for cut) at x. It is defined through the active cone source, **not** through a
  traced fall line: within the region of one active cone the fall line points to its apex, but where the active
  cone changes or at a valley (Kehle) the fall line can change direction or be ambiguous. The surface that is as
  steep as allowed under R1 is the **lower envelope of the slope cones** (cut; the upper envelope for fill):

      S(x) = min over toe points q of  z(q) + s(q) · |x − q|xy

  This is the classical construction of descriptive geometry ("Böschungsfläche als Hüllfläche der Böschungskegel"
  in the kotierte Projektion). With a common slope s it is the McShane extension (1934) of the toe heights, the
  largest extension with Lipschitz constant s. With slopes s(q) varying along the toe it is a related construction:
  the largest surface below every cone, but the classical maximality and extension statements do not carry over
  unchanged (eg the result is not s(x)-Lipschitz in any field sense). Its gradient is ‖∇S‖ = s(q*), with q* the
  active apex. It uses cones as the precedence constraints of Lerchs–Grossmann
  pit optimization do (the variable-slope variants vary the cone by direction instead, see R3 below). The
  closed form needs no PDE solve. Every point's gradient is exactly the slope of the active cone's apex.
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
| straight ramp in the toe (grade g), slope varying along it | up to a factor **sqrt(1 + g²/s²)** steeper than the nearest toe point's slope (g = 0.1, s = 0.33: 4.5 %; s = 1: 0.5 %), in a narrow sector in front of the upper end | never steeper than σ by definition |
| curved or concave toe with a ramp (eg a circular toe, R = 100 m, g = 0.1), a steeper stretch on the far side | the far stretch's cone can win in front of a gentle stretch: **1.67 times** σ in the review's example (s = 0.55 against 0.33, toe not undercut). In general the factor is bounded only by s_max / s_n | never steeper than σ |
| point in the R2 transition zone | slope = the slope of its active apex | slope s_b, while the active R1 apex there is a gentle toe point (it violates R1) |

So neither reading's surface satisfies the other reading in all cases. Under R1, R2's transition zones are too
steep. Under R2, R1 overshoots: by up to sqrt(1 + g²/s²) on straight ramps, and up to s_max / s_n where a
curved or concave toe lets a steeper stretch "see" a gentler one. And R1 over-excavates massively at sector
transitions.

**Why R1 is defined through the active cone and not the fall line.** With varying slopes, a fall line of the min
of cones is not always straight. A gentle cone that wins at x may be cut, between x and its apex, by a region where
a steeper cone wins, and the fall line then bends towards the steeper apex. At valleys it is not unique at all.
A promise about globally traced fall lines would be neither true nor checkable. The checkable form of R1 used by
the oracle is per triangle (5.4): each output triangle lies below every cone of the toe piece it is attributed to,
and is no steeper than that piece's slope.

**The guarantee is always "not steeper than the chosen attribution rule allows".** R1 is the cone-envelope model;
it is not a general guarantee for geotechnical sectors (decided, 2.5).

**Further readings that mining uses** (not requested, recorded for later modes):
- **R3, by wall direction:** the slope depends on the dip direction (azimuth) of the wall, as geotechnical domains
  by bearing. The variable-slope Lerchs–Grossmann variants work this way (Mining Technology 2000: four principal
  directions per domain). R1 matches LG only in its use of cones, not in how the slope varies.
- **R4, by depth or geology:** eg overburden 1:3 above a horizon and rock below, typical for Hambach.

Both are out of scope for phase 1. The API leaves room for them: the slope is looked up through one function
s(piece, direction, z), which is the toe slope for R1.

If the toe slope is constant along long stretches (the usual case), both readings give the same result, so the
choice only matters at changes of slope. The user's decision is in 2.5.

### 2.3 Modes (decided, 2.5)

- **Phase 1: R1**, explicitly named as the cone-envelope mode (min of cones). It has a closed form, its facets are
  exact planes and cones, and the KO criterion follows from construction plus a per-triangle check without any
  PDE discretization. For a connected slope with a uniform slope it is a good starting point. It is **not** a
  guarantee for geotechnical sectors.
- **Sectors later, as a separate mode.** Where different slopes describe spatially fixed geotechnical areas, a
  spatial slope field fits better, which is R2-like. For those areas the plan is **explicit sector polygons**
  given by the caller. "The nearest point of the toe" is only one possible substitute definition of a sector and
  can produce unexpected attributions at concave edges, so it is not the target definition.
- **Modes that are not available are rejected explicitly** (`Statuses.NotImplemented`), never silently replaced
  by R1.
- **R2 construction (when it comes):** an eikonal solve on a refined triangulation (Fast Marching on triangles,
  Kimmel & Sethian 1998; or an ordered upwind method), followed by a conservative correction. Construction: an eikonal solve on a refined triangulation (Fast Marching on triangles, Kimmel &
  Sethian 1998; or an ordered upwind method), followed by a conservative correction. Each triangle is lowered
  (cut) until its gradient is ≤ min σ over the triangle, upwind in order of height. The same exact oracle then
  runs as in R1. More work, and not exact; plan it as a second phase.
- A strict variant of R1 that is also R2-safe is not cheap. Scaling by 1/sqrt(1 + g²/s²) covers straight ramps
  only, and curved toes need the slope of a piece capped by the slopes of every stretch whose region its cone can
  enter. Where sector safety is needed, the sector mode is the cleaner answer.
- The R2 correction step (lowering triangles upwind) still needs a convergence argument. It is a known issue of
  Fast Marching on obtuse triangles, so R2 would also need an acute or unfolded triangulation.

### 2.4 Why a TIN of the right surface can still be too steep

The construction must avoid three effects that each break the KO criterion, all reproduced in the prototype:

1. **Inscribed fan.** A convex corner approximated by a fan whose outer vertices lie *on* the cone has facets
   with slope s / cos(θ/2) (15° steps: +0.86 %). The fix is facets **tangent** to the cone: outer vertices
   lowered by the factor cos(θ/2), so each facet has slope exactly s and lies below the cone (cut).
2. **Interpolation across a crease.** If a triangle's vertices lie on a piecewise-linear surface but the triangle
   straddles a crease (ridge or valley), its slope can be arbitrarily large (vertices (−1,0), (1,0), (0,ε) on
   −s|x|: slope s/ε). So sampling the surface at points and triangulating is not enough. **Every output triangle
   must lie in a single facet** of the slope surface, so the TIN contains all creases as edges.
3. **Rounding.** Facet vertices are intersections of planes, so they are not doubles. Rounding to nearest moves
   them by up to ulp/2 per coordinate, about 4.7e-10 m at UTM northings. That changes the slope of a triangle by
   up to about 4e / h_min (e the vertex error, h_min its smallest altitude; derivation in 5.4): harmless for
   metre-sized triangles, unbounded for slivers. The fix is a margin, removing slivers, and an exact check
   (section 5.4).

### 2.5 Decisions of the user (review of 2026-10-27)

The answers to the open questions of the first version, worked into the sections named.

| # | question | decision | where |
|---|---|---|---|
| Q1 | R1 or R2 | R1 as the first implementation, explicitly as the cone-envelope model, not as a general guarantee for geotechnical sectors. Sectors later as a separate mode with explicit sector polygons. Modes that are not available are rejected explicitly. The guarantee always reads "not steeper than the chosen attribution rule allows". R1 is defined through the active cone source, not through a traced fall line | 2.2, 2.3 |
| Q2 | tolerances | keep three things strictly apart. **Geometric approximation:** an explicit maximum vertical deviation, given by the caller (1 cm is a sensible start for metre data, but it is not a universal library default). **Slope margin:** 1e-6 relative as a start; the check of the finished triangles decides. **Minimum feature size:** only a threshold for an allowed repair attempt, not a permission to delete small details wholesale. Also: no position or angle tolerance may exceed the slope limit. Collapsing slivers must not change the toe or the catch lines inadmissibly. If the margin is not enough at large coordinates or for small triangles, the computation improves or fails. A vertical tolerance does not bound the horizontal deviation of the catch line, so near-tangential sections are reported as ill-conditioned. Defaults must not depend only on the size of the UTM coordinates: professional accuracy and numerical representability are different quantities | 5.1, 5.4, 5.5 |
| Q3 | influence across the design area | for a closed ring, no influence through the interior of the design area: it is an obstacle / a bounded interior region. Several levels are allowed, but checked for consistency. Phase 1 supports only the Euclidean R1 envelope, so it **rejects** the problematic cases and names the causing edges. Cones are not simply clipped: restricting them to the exterior changes the mathematical problem. The exterior distance and its topology are a development step of their own. A clearly restricted Euclidean mode is better than an exterior mode that only appears to work | 5.2 |
| Q4 | how slopes change | support both steps and continuous change, with the interpolation fixed explicitly: constant per segment as the simple standard, piecewise constant with explicit jumps at sector boundaries, continuous change optional. Values per segment end (left / right of a vertex), because one value per shared vertex cannot express a real jump. Interpolation linear in s by default, documented as API contract; linear in n of 1:n must not be silently replaced | 2.1, 5.1, 6 |
| Q5 | open toe lines | by default limited laterally by the XY normals at the start and end segment, no automatic fan around the ends. Optional: explicit lateral limits given by the caller (lines or a computation domain); rounded end caps from the cone envelope. The caller chooses the side (left or right of the line), and this must be kept. Closed lines also need an explicit side (inside or outside); "closed" alone does not say it | 5.6, 6 |
| Q6 | angle convention | ratio 1:n is the reference input convention; internally s = rise / run. All three converters (1:n, percent, angle), with the origin of the input kept for the check. For angles, phase 1 guarantees against the double s used, a guarantee against the angle needs a proven interval evaluation. For 1:n the exact check s·n ≤ 1 is straightforward | 1 |
| Q7 | berms and ramps | berms first by absolute target heights (bench / floor levels as the primary input, relative heights as an optional convenience); berm width constant per section first, variable widths later with a clear transition rule; inclined toe sections allowed. Ramps are separate design geometry: a ramp needs width, grade and connection geometry, which do not follow from an inclined toe. The core already offers "build the slope up to a target height and return the line there" | 10, 6 |
| Q8 | output | all three, separately retrievable: slope surface, catch lines, composite terrain. The catch line alone is an important result, so building the full composite terrain must not be mandatory. Besides the triangle groups, a separate mapping to the original toe edge and its parameter interval, because internal piece ids change with subdivision. The slope guarantee applies to newly generated slope triangles, not to unchanged terrain in the composite | 5.5, 6 |

| Q9 | per triangle or overall / inter-ramp angle | per triangle for the constructed slope faces; the overall slope additionally and separately. These are different requirements: **bench face**: every generated triangle stays within the admissible face slope; **berms**: their own check of height, horizontal position and minimum width; **inter-ramp / overall slope**: an additional check across several benches and berms along defined profiles or measuring directions. An inter-ramp angle must not be used as the limit of every bench triangle, since bench faces may be steeper when berms reduce the overall slope. Phase 1: the input slope is the maximum admissible local slope of the generated surface, checked for every output triangle. An overall / inter-ramp check is a later, separate function. Which slope is entered in a given planning task is for the application to decide | 1, 10 |
| Q10 | export precision | no fixed number of decimals is assumed. The guarantee must include the state actually stored. Two cases: in-memory doubles or a text format with round-trip formatting (checked coordinates kept unchanged); quantized output (eg a mm grid): quantize to the output grid before the final check, then check slope, degeneracies and topology again. A larger minimum feature size alone guarantees nothing, since rounding can change the slope of larger triangles too. Flow: construct → map to the output precision → repair or reject → final check → store → read back and check in an integration test. Rounding later by an external exporter is outside the guarantee, and the documentation says so | 5.4 |
| Q11 | conservative rejection of influence through the design area | acceptable for phase 1, and better than an unnoticed wrong result, provided: a status of its own that is easy to understand; the causing toe edges and the affected areas named; no automatic change of toe heights or slopes; no silent fallback to another attribution rule; partial results only explicitly marked as incomplete / not released. The message distinguishes "this design is not supported by the restricted phase 1 method" from "this design is geometrically impossible". `CrossDesignInfluence` must not be read as a proof of impossibility; that is what the later exterior mode is for | 5.2, 6 |
| Q12 | horizontal uncertainty of the catch line | set by the caller in the library. 10 cm is a sensible configurable warning threshold to start with in an application, not a library default and not a proven accuracy requirement. There is no fixed relation like "1 cm vertical means 1 cm horizontal": locally δ_xy ≈ ε_D / \|∇(S − T)\|, with ε_D the vertical uncertainty of the height difference, so nearly parallel surfaces give a large horizontal uncertainty. Distinguish: warning threshold exceeded → result with the affected catch-line sections and their uncertainty; a binding horizontal accuracy required → refine, and fail explicitly if it cannot be reached; tangential or planar contact → marked separately, since there may be no unique catch line. Documented whether the number is a local estimate or a proven bound. Exact signs alone do not secure the exact position of the intersection, and survey errors of the terrain are not included without additional input data | 5.5, 6 |

Two corrections of the first version came with the answers and are worked in:
- "The fall line runs straight to the governing toe point" is not guaranteed for the lower envelope (2.2).
- The McShane maximality and extension statements hold for a common Lipschitz constant only; with s(q) varying
  they do not carry over unchanged (2.2, 4).

Variable geotechnical sectors, the exterior influence and variable berm widths are extensions of their own, not
hidden defaults.

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
| Lipschitz extension, Hamilton–Jacobi | McShane 1934 and Whitney 1934: with a common Lipschitz constant, the largest / smallest extension is the min / max of cones. Hopf–Lax formula (L. C. Evans, *Partial Differential Equations*, ch. 3 and 10) | For a uniform slope: no admissible surface lies above the min of cones (cut: least excavation). For slopes s(q) varying along the toe only the weaker statement holds: the min of cones is the largest surface below every cone. The classical extension results do not carry over unchanged | none |
| Weighted Voronoi diagrams, lower envelopes of cones | Additively / multiplicatively weighted Voronoi diagrams are projections of lower envelopes of cones (Aurenhammer & Edelsbrunner 1984; Fortune 1987 sweep with cones; Okabe, Boots, Sugihara, Chiu, *Spatial Tessellations*, 2000). Lower envelopes of triangles / patches in 3D (Edelsbrunner, Guibas, Sharir 1989; Sharir & Agarwal, *Davenport–Schinzel Sequences*, 1995) | The structure of the R1 surface (an additively and multiplicatively weighted, "compoundly weighted" diagram of the toe) and how to compute the envelope | CGAL has Apollonius graphs and envelopes: **GPL, not used** |
| Weighted straight skeleton | Aichholzer, Aurenhammer, Alberts, Gärtner 1995 (straight skeleton, roof model); Aichholzer & Aurenhammer 1996; Eppstein & Erickson 1999 (*Raising roofs, crashing cycles, and playing pool*, weighted variant); Kelly & Wonka 2011 (procedural extrusions, per-edge angles); Biedl, Held, Huber, Kaaser, Palfrader 2015 (*Weighted straight skeletons in the plane*, CGTA 48: with weights not unique by the procedural definition, can be non-planar and contain cycles) | Gives a roof (slope) surface from a polygon with per-edge slopes | CGAL straight skeleton (also weighted, since 6.0): **GPL**. Kelly's campskeleton (Java): license to be checked, not needed |
| Fast Marching, eikonal on meshes | Sethian 1996; Kimmel & Sethian 1998 (*Computing geodesic paths on manifolds*, PNAS, triangulated domains, obtuse triangles by unfolding); Sethian & Vladimirsky 2003 (ordered upwind, anisotropic); Bornemann & Rasch 2006 (FMM on triangles, consistency) | Solver for R2; approximate, first order on the mesh | many implementations; implement from the papers if R2 is chosen |
| Weighted region shortest paths | Mitchell & Papadimitriou 1991 (exact, refraction, high polynomial complexity); later approximations | Exact R2 with piecewise-constant σ: impractical | — |
| Open-pit design | Lerchs & Grossmann 1965 (ultimate pit by graph closure; slope as precedence cones); Khalokakaie 1999 (PhD Leeds, *Computer-aided optimal open pit design with variable slope angles*), the 2000 paper from that work (Mining Technology 109, DOI 10.1179/mnt.2000.109.2.77, author list to be confirmed at the publisher; variable slope angles in LG, by domains and four principal directions); Gilani & Sattarvand 2015 (Acta Montanistica Slovaca, variable slopes heuristic); Hustrulid, Kuchta, Martin, *Open Pit Mine Planning and Design* (2013): converting shells to designs bench by bench (toe → crest by face angle, crest → next toe by berm width, ramps) | Mining defines slopes by geotechnical domains / sectors (in plan) or by wall azimuth: closer to R2 than to R1. Vendor tools (Datamine Studio OP: toe + ramp + crest, contour design; Maptek Vulcan Automated Pit Designer: batter angle and berm width per region, line conditioning, "OUT" / "IN" for pits and dumps) build strings by offsetting, then surfaces from the strings | proprietary; documentation only |
| Road / earthworks daylighting | Civil 3D grading criteria: projection to a surface at a slope or grade. Its conflict resolution *Interior Corner Overlap* offers "average slope", "hold slope as maximum", "hold slope as minimum" for corners with different elevations, ie it admits steeper slopes in one option. Forum practice: round the inner corners until the grading stops overlapping itself. Bentley OpenRoads, 12d: side-slope conditions per template | Shows the same problems (corner overlaps, different heights at corners) handled by heuristics and user cleanup; "hold slope as maximum" is the KO-safe mode | proprietary; documentation only |
| GIS | Cut / fill as DEM of difference; TIN-TIN volumes; daylighting in GIS mostly raster-based (slope-limited "flattening" on grids) | Grids are excluded by the requirements; volume reference only | — |
| Overlay of planar subdivisions | Map overlay (de Berg et al., *Computational Geometry*, ch. 2); convex polygon intersection (O'Rourke, Chien, Olson, Naddor 1982); Simulation of Simplicity (Edelsbrunner & Mücke 1990) for consistent degeneracy handling | Topological overlay with exact predicates for the catch line | implement from papers |

What I did *not* find is any publication or open implementation that builds a slope surface with a *per-vertex*
slope and a verified "never steeper" guarantee. The vendor tools use offset strings and cleanup, and the
literature has the pieces (cone envelopes, eikonal, skeletons) but no guarantee for this problem. That gap is
the contribution here.

## 5. Chosen approach (R1)

Pipeline. Each step is local and runs in parallel over toe pieces or terrain triangles:

1. Validate the inputs (5.6).
2. Split the toe into pieces with a constant conservative slope (5.1).
3. Each piece defines a convex polyhedral function whose planes all have gradient ≤ the piece's slope. The cut
   surface S_cut is the minimum of the rising functions, the fill surface S_fill the maximum of the falling ones.
   `Direction = Cut` builds only S_cut, `Fill` only S_fill, `Auto` both (5.2). There is no choice per toe stretch:
   the clamp of 5.5 decides where cut or fill applies.
4. Compute the envelope combinatorially exactly, on planes with double coefficients (5.3).
5. Realize it as a TIN, verify every triangle exactly, and repair slivers (5.4).
6. Overlay with the terrain topologically: catch line, final surface, volumes (5.5).

Described for cut; fill is the same with z ↦ −z, with its own slopes per vertex (cut and fill slopes usually
differ, eg 1:1 and 1:1.5).

### 5.1 Pieces and conservative slopes

- **Sub-segments.** Split each toe segment into sub-segments so that the drop in s over a sub-segment stays
  within its share of the tolerance. Each sub-segment gets s_k = the minimum of s over it (the smaller endpoint
  value). A toe with constant slope is not split. The split points are also inserted into the design TIN's
  boundary edges, so the design and the slope share vertices (no T-junctions).
- **Tolerance budget.** The total over-excavation against the exact R1 surface is the sum of three terms, each
  given a share of `Tolerance`:
  - slope interpolation: Δs·D;
  - cone facets: s·D·(1 − cos(θ/2));
  - margin: δ·s·D.

  The reach D_max is (max terrain z − min toe z) / s_build,min + `Tolerance` / s for cut, and
  (max toe z − min terrain z) / s_build,min + `Tolerance` / s for fill.
- **`Tolerance` is an explicit input** (decided, 2.5): the maximum vertical deviation of the conservative surface
  from the exact R1 surface, in the units of the data. 1 cm is a sensible start for data in metres, but there is
  no library default. `Compute()` rejects a missing value (`InvalidInput`). It bounds heights only, not the
  horizontal position of the catch line (5.5).
- **Three quantities are kept apart:**
  - the geometric approximation (`Tolerance`, a professional accuracy);
  - the slope margin δ (numerical, 5.4);
  - the minimum feature size (numerical, only a threshold for a repair attempt, 5.4).

  None of them may loosen the slope limit, and the last two are derived from numerical representability, never
  from `Tolerance`.
- **Cost.** With a 0.5 → 1.0 change over one segment, D_max = 500 m and a 0.1 m share, there are 2500
  sub-segments for that segment. At a junction of two collinear sub-segments only the narrow wedge between their
  two strip directions needs cone planes, one or two per junction, so this is about 5000–7500 planes. Neighbouring
  planes differ by Δs = 2e-4, and their creases are ill-conditioned. 5.3 decides them exactly, and 5.4 removes the
  slivers. If slopes change continuously along long stretches, the tangent planes of the exact varying-slope
  envelope (a ruled surface) need far fewer planes. That is the follow-up.
- **Toe steeper than slope** is decided exactly against the slope actually built: Δz² ≥ s_build²·(dx² + dy²)
  → `ToeSteeperThanSlope`.

### 5.2 The slope surface as a minimum of convex polyhedral functions

For a sub-segment from P0 to P1 with grade g = Δz / L along it (unit direction u, outward normal n in XY) and a
constant slope s > |g|, the envelope of the cones of its points is:

- **in its strip** (the region swept from the segment along the fall direction): the plane through the segment with
  gradient G = g·u + sqrt(s² − g²)·n, so |G| = s. The strip's side lines are not perpendicular to the toe when
  g ≠ 0;
- **beyond the ends**: the cones at P0 and P1, which the plane touches along the strip's side lines (C¹).

This function f_k(x) = min over t of z(t) + s·|x − p(t)| is **convex**, because the cone term is jointly convex in
(x, t) and the minimum over t of a jointly convex function is convex. So f_k is the supremum of its tangent
planes. The conservative polyhedral version F_k keeps finitely many of them: both strip planes and tangent planes
of the end cones, spaced by an angle θ within the cone-facet share of the tolerance (D_max = 500 m, s = 1, 1 cm:
θ = 0.72°, 125 facets for a 90° corner). Then

    S̃(x) = min over pieces k of  F_k(x),   F_k = max over j of P_kj,   |∇P_kj| ≤ s_k

- **Every facet of S̃ lies in one plane P_kj with slope ≤ s_k**, so in exact arithmetic the KO criterion holds at
  every point. Because each tangent plane lies below its cone, S̃ ≤ S everywhere: conservative (more excavation),
  and by at most `Tolerance`.
- **Convex corners** get fans from the cone facets. **Concave corners** get the valley (Kehle) between the two
  strip planes, where the min clips everything that would cross. **Self-intersections of offset fronts cannot
  occur**: S̃ is a function, and its level sets are the offset curves after their self-intersections have been
  resolved. So the separate self-intersection step (further item 1) is unnecessary for this approach.
- **Toe steeper than slope** (|g| ≥ s on a sub-segment, eg a steep ramp with a flat fill slope): no plane through
  the segment has slope s. The lower end's cone undercuts the upper end, so the toe is not on S̃. Report
  `ToeSteeperThanSlope` with the segments. No silent fix: the design is infeasible as specified.
- **Toe undercut from elsewhere** (R1 is Euclidean): a far, low, gentle part of the toe, for example across a
  narrow pit with floors at different levels, can have its cone pass below another part of the toe.
  - **Check.** For every toe edge e and every candidate piece m (spatial index), take the exact minimum of
    F_m(q(t)) − z(t) over e. Both are piecewise linear in t, so it is attained at the end points of e or where e
    crosses a facet boundary of F_m. Evaluating at vertices only is not enough, because F_m is convex and can dip
    between them.
  - **Real undercut versus approximation artifact.** F_m lies below f_m, so close to a corner it can dip
    marginally below the toe while the exact f_m does not. If the dip is within the cone-facet share of the
    tolerance, refine F_m in that direction (more tangent planes) and check again. A dip beyond it is a real
    undercut → `ToeUndercut`.
  - **Why the check is enough.** If the toe is not undercut, a far cone that wins at x is gentler than the toe point
    where the segment from x to its apex crosses the toe (appendix A). So R1 holds at the first contact of that
    segment with the toe.
- **No influence through the design area** (decided, 2.5). For a closed ring the design area is an obstacle: a
  cone must not reach a wall point through the interior. Phase 1 builds the Euclidean R1 envelope only, so it
  **detects and rejects** such influence rather than clipping cones. Clipping would change the mathematical problem
  and could make the surface discontinuous.
  - **Check.** For every cell of S̃ (5.3) with supporting piece k, the convex hull of the cell and the piece's toe
    sub-segment contains every segment from a cell point to a point of the piece. If that hull meets the interior
    of the design area, or crosses another part of the toe, the cell may be supported through the interior. The
    test uses exact orient2d on the cell's implicit vertices and the toe. It is conservative: it may reject a case
    that is in fact fine, and it never accepts one that is not.
  - **Result.** `Statuses.CrossDesignInfluence` with the causing toe edges and the affected toe ranges.
  - Several levels within one toe are allowed. Their consistency is checked by the toe checks above
    (`ToeUndercut`, `ToeSteeperThanSlope`) and by this test.
  - A real exterior distance (shortest paths around the design area, with its topology) is a development step of
    its own, after phase 1. Until then, a clearly restricted Euclidean mode is preferred to an exterior mode that
    only appears to work.

### 5.3 Computing the envelope exactly (plane-based)

- **Planes with double coefficients.** Each plane is P(x, y) = a·x + b·y + c in local coordinates (5.8), with
  a, b, c doubles chosen conservatively:
  - (a, b) is the rounded gradient G·(1 − δ), stepped down until a² + b² ≤ s_build² holds exactly;
  - c is the largest double with P ≤ z at the toe points the plane belongs to (both ends for a strip plane, the
    apex for a cone tangent), decided exactly.

  From here on **the planes are exact input data**. The irrational sqrt(s² − g²) only enters through this
  rounding. The plane is then slightly below the toe, by about ulp × segment length; the toe triangles get the
  real toe heights and are covered by the margin and the check of 5.4.
- **Cells.** F_k is convex, so the region of its plane j within F_k, C_kj = {P_kj ≥ P_kj' for all j'}, is convex.
  The cell of plane (k, j) in S̃ is C_kj minus the union over the other pieces m of {x : F_m(x) < P_kj(x)}. Each of
  these sets is convex too, an intersection of the half-planes {P_mi < P_kj}. So every cell is a convex polygon
  minus a union of convex polygons, computed by exact convex clipping. The candidates m come from a spatial index
  over the pieces, bounded by D_max.
- **Exact predicates on implicit points.** Every boundary line is a difference of two planes. Every vertex is the
  intersection of three planes, a rational point; the cells are bounded by the domain box, which supplies the
  remaining lines. The predicate "sign of P_e − P_f at the vertex of planes (a, b, c)" is the sign of a 3×3
  determinant of differences of double coefficients. It is decided with a floating-point filter and exact
  expansion / `BigInteger` arithmetic, like the three-plane points of `ExactMeshArrangement` (Attene 2020;
  Cherchi et al. 2020).
- **Symbolic labels.** Each vertex keeps the set of planes that define it. Ties (four planes through one point,
  frequent at sub-segment junctions) are broken by Simulation of Simplicity on the plane offsets c (c_i + ε^i), so
  every decision is combinatorial and consistent, and no rounded crease is ever classified again.
- **Triangulation.** Each cell (a convex polygon minus convex holes) is triangulated with the exact CDT
  (`ExactTriangulation2` with orient / incircle on the implicit points). Every triangle lies in one cell by
  construction, so no triangle straddles a crease (2.4 item 2). Vertices are rounded only at the end (5.4).

Rejected: **evaluate and split** (sample the active facet at triangle vertices and centroids and insert creases
where they differ). Cells of a min of convex functions are not convex, so a triangle can have all its samples in
facet A and still contain a small fan region of another piece, or have a crease cutting off a corner between the
samples. Such triangles straddle a crease and can be arbitrarily steep. Also, rounded creases get classified
again at random, which loops or creates tiny edges. The review found both.

### 5.4 Realization, the KO check, margin, repair

**What is guaranteed (auditable on the output).** For every slope triangle T of the output, with the toe piece k
it is attributed to (its triangle group):

- **(i)** slope(T) ≤ s_k, decided exactly on the stored doubles;
- **(ii)** T's plane is ≤ the toe height at both end points of piece k, decided exactly.

Together they prove that T lies below every cone of piece k: with L the plane of T, L(q) ≤ z(q) holds on the whole
sub-segment (both are linear along it), so L(x) ≤ L(q) + s_k·|x − q| ≤ z(q) + s_k·|x − q|. So T is never steeper
than the toe piece that supports it. The check is public (`Slope.IsSupportedBy(a, b, c, piece)`), runs inside
`Compute()` on every slope triangle including the ones clipped at the catch line, and is the basis of oracle O1.
That the attribution is also the *right* piece is a property of the construction: T lies below all other pieces'
cones too (S̃ ≤ S). The tests check it independently (O2), but it is not part of the KO, which concerns slope.

The approximation can shift creases. A narrow strip (about the tolerance share / Δs wide) can then be supported by
a steeper neighbouring piece where the exact S has a gentler apex. Under the auditable definition above this is
fine, because the triangle is below all cones of the piece it is attributed to. Under a pointwise reading against
the exact S it would not be. The guarantee is stated for the attribution rule (2.3).

**Realization and margin** (a new error bound, to be checked in the review that CLAUDE.md requires):

- First, round the XY of each vertex directly to the final double (UTM) coordinates. Then evaluate z at exactly
  that XY from the vertex's plane, rounded to nearest. A vertex inside one plane is then on its plane up to the z
  rounding e_eval. A crease vertex (on two or more planes) can be on only one of them: its z error is
  e_z ≤ |G_a − G_b|·e_xy + e_eval ≤ 2s·e_xy + e_eval.
- The gradient of a triangle changes by at most 2e·(1/h_b + 1/h_c) ≤ 4e/h_min for vertex height errors e, with
  h_min the smallest altitude. Relative to s: (8·e_xy + 4·e_eval / s) / h_min.
- With s_build = s·(1 − δ), every triangle with h_min ≥ `MinFeatureSize` is safe if
  δ ≥ 2·(8·e_xy + 4·e_eval / s) / `MinFeatureSize` (factor 2 for safety). At UTM (e_xy = 4.7e-10 m) with
  δ = 1e-6: h_min ≥ 3.7 mm (7.5 mm with the factor 2). The e_eval term matters only for very flat slopes, which
  need a larger δ or feature size. Mapping from local to UTM *after* evaluating z would move XY with z fixed and
  add an error to every vertex. Hence the order: XY first, then z.
- δ starts at 1e-6 relative (decided, 2.5). `MinFeatureSize` follows from δ and the coordinate resolution by the
  bound above. Both are numerical quantities. They are derived from representability (the coordinates' ulp), not
  from the professional accuracy `Tolerance`, and they never serve as a default for it. The derivation is relative,
  so 2^k scaling gives the same result bit for bit.
- **The check of the finished triangles decides, not the bound.** If a triangle fails although the bound says it
  should pass (large coordinates, small triangles), the computation first improves: it rebuilds the affected
  pieces with a larger δ (doubling, up to a cap). Only if that does not help does it fail. It never accepts the
  triangle.

**Exact oracle (i)** (a new predicate): for a triangle (a, b, c) in doubles and a double s, decide
`nx² + ny² ≤ s²·nz²` with n = (b−a)×(c−a). It ignores the orientation (both sides are squares). s² is formed
exactly, as an expansion. The difference is a polynomial of degree 4 in the coordinates and degree 2 in s,
evaluated with a double filter (error bound derived like Shewchuk's), then exactly with expansions, or with
`BigInteger` outside [2^-100, 2^100], following `ExactPredicates`. It runs on the **output coordinates as stored**
(UTM) and rejects triangles that are degenerate in XY (nz = 0).

**Repair** (slivers with h_min < `MinFeatureSize`, including caps without a short edge). `MinFeatureSize` is only
the threshold for an allowed repair attempt. It is not a permission to delete small details wholesale: a repair is
accepted only if it keeps the geometry within the cell and passes the check.

- toe, design-boundary and catch-line vertices are never moved;
- collapse an edge only onto a vertex whose plane set contains the removed vertex's plane set, so the triangles
  stay within their cells;
- otherwise flip within the cell (caps);
- otherwise fail;
- after each change, run (i) and (ii) again on the touched triangles.

Every collapse removes a vertex. Flips are only accepted if they raise the smallest altitude. So the loop
terminates.

**Never silent.** If any triangle still fails, `Compute()` returns false with `SlopeViolation` and the triangle
ids. It never returns a violating result. `MaxSlopeRatio` is an upper bound (rounded up) of ‖∇‖ / s_k over the
slope triangles, < 1 on success; the exact pass/fail per triangle is what counts.

**Export** (decided, 2.5). No fixed number of decimals is assumed; the guarantee covers the state that is actually
stored. Two output contracts:

- **Doubles in memory, or a text format with round trip:** the checked coordinates are kept unchanged and written
  with round-trip formatting. `OBJWriter` writes with `"{0}"`, which on net48 gives 15 significant digits (about
  5e-9 m at UTM, more than an ulp), so it needs `"R"` / `"G17"` and a test. Binary STL (float32, about 0.25 m at
  UTM) cannot carry the guarantee.
- **Quantized output** (eg a mm grid, `OutputGrid`): the coordinates are quantized to the output grid **before**
  the final check, then slope, degeneracies and topology are checked again. A larger `MinFeatureSize` alone
  guarantees nothing: rounding to a grid can change the slope of larger triangles too (a 1 mm shift on a 1 m
  triangle changes its slope by up to about 0.4 %, which is far above the margin). So the margin and the repair
  run against the grid, and what does not pass is repaired or rejected.

Flow: construct → map to the output precision → repair or reject → final check → store → read back and check (an
integration test per format). Rounding afterwards by an external exporter is **outside the guarantee**; the class
documentation says so.

### 5.5 Overlay with the terrain, catch line, final surface

- **Topological overlay.** Each overlay face is the intersection of an S̃ triangle and a terrain triangle, as in
  `TerrainVolumeDifference`. Here, though, its vertices are identified symbolically:
  - a vertex of A inside B: (vertex id, triangle id);
  - a crossing of an A edge with a B edge: (edge id, edge id).

  All decisions use `ExactPredicates.Orient2d` on the stored doubles: point in triangle, segment crossing, and the
  combinatorial convex intersection (O'Rourke et al. 1982). So both faces next to an overlay edge agree on its
  end points, and every constructed point is computed once from its identity by one canonical function, which
  makes it bitwise identical wherever it appears.
- **Degeneracies (SoS).** They are frequent: grid-aligned survey data, coincident toe and terrain vertices,
  collinear edges.
  - XY: the terrain is shifted by (ε, ε²). The ε terms are decided by sign comparisons of doubles (not by
    computing their differences, which may round).
  - Heights: S̃ is shifted by ε_z, with ε_z ≫ the XY terms, towards "no work": up for cut (touching counts as
    "slope not below terrain"), down for fill. Otherwise, in fill, coplanar patches of zero thickness would
    connect regions beyond the first contact.
- **Exact sign of D = S̃ − T at overlay vertices** (checked by the review with exact rationals on 4798 random
  crossings):
  - S̃ vertex v over terrain triangle t: sign(D) = sign(Orient3d(t0, t1, t2, v)) · Orient2d(t0, t1, t2), with
    g3's sign convention for `Orient3d` (+1 above the plane of a counter-clockwise triangle). For a terrain vertex
    over an S̃ triangle the sign is negated.
  - Crossing of S̃ edge a with terrain edge b: sign(z_b − z_a at the crossing) =
    sign(Orient3d(a0, a1, b0, b1)) · Orient2d(a0, a1, b0), so sign(D) is the negation.

  Both use the existing exact predicates, with no rationals and no tolerance.
- **Catch line.** D is linear on each overlay face, so a face has at most one zero segment, from edge crossing to
  edge crossing. A crossing point is identified by its overlay edge and placed at t = D0/(D0 − D1), clamped to
  (0,1). The D values are doubles with their signs forced to the exact ones. Segments join through these shared
  identities into polylines. These are simple, closed or ending at the footprint boundary, by construction. **No
  chaining tolerance.** Grazing intersections give correct topology (possibly many small loops where the surfaces
  nearly coincide, which is the real geometry); only the positions are rounded.
- **Horizontal accuracy of the catch line** (decided, 2.5). A vertical tolerance does not bound where the catch
  line lies in plan. Locally δ_xy ≈ ε_D / |∇(S − T)|, with ε_D the vertical uncertainty of the height difference;
  nearly parallel surfaces make the denominator small and the horizontal uncertainty large.
  - **ε_D** = `Tolerance` (the conservative approximation) plus the terrain's vertical uncertainty
    (`TerrainVerticalUncertainty`, a caller input, default 0). Survey errors of the terrain are not included
    unless they are given. Exact signs decide the topology of the catch line, not its exact position.
  - Every catch-line segment carries |∇D| and δ_xy. In phase 1, δ_xy is a **local linearized estimate**,
    documented as such; a proven bound would need the minimum of |∇D| over the neighbourhood and is a later
    item.
  - **Warning threshold exceeded** (`CatchLineWarningThreshold`, set by the caller; 10 cm is a sensible start in
    an application, not a library default): the result is returned, with the affected catch-line sections and
    their uncertainty (`IllConditionedCatchLine`).
  - **Binding horizontal accuracy required** (`RequiredCatchLineAccuracy`): refine (a smaller `Tolerance` in the
    affected pieces) until δ_xy meets it; if that is not reachable (eg because of `TerrainVerticalUncertainty`),
    fail explicitly.
  - **Tangential or planar contact** (D = 0 over an edge or a face, or |∇D| = 0): marked separately
    (`TangentialContact`), since a unique catch line may not exist there. The SoS rule still gives a consistent
    topology, but it is a convention, not a measurement.
- **Realizing the clipped slope triangles** (so that the KO check of 5.4 passes):
  - catch-line vertices get their XY rounded to the final coordinates, and z from the plane of the *stored* S̃
    triangle (the plane through its three doubles), evaluated at that XY;
  - a catch-line vertex within `MinFeatureSize` of an S̃ vertex or edge is snapped onto it, and the terrain side
    absorbs the change, since it is not under the KO. The move is bounded by `MinFeatureSize`, far below the
    horizontal uncertainty of the line, and is added to the reported uncertainty, so the catch line is not
    changed inadmissibly. Toe vertices are never moved;
  - coincident points after realization are merged, which handles SoS-realized degeneracies and clamped t;
  - the clipped part of an S̃ triangle is triangulated as a fan from its S̃ vertex farthest from the catch line, so
    altitudes stay large even where the catch-line vertices are nearly collinear (planar terrain at UTM).
- **Region and final surface.** For cut, the excavation is the set of faces with D < 0 that is **connected to the
  toe** through face adjacency. Components of {D < 0} not connected to the toe, eg a hill behind a valley that
  the cone passes under, are not excavated. Final surface = design inside the toe, S̃ on the region, terrain
  outside, stitched along the catch line through the shared vertices.
- **What the KO covers in `Composite`:** all slope triangles, including the clipped ones. Terrain triangles clipped at
  the catch line are pieces of the terrain's own planes. They can be thin slivers, but their slope is the
  terrain's, not a generated one.
- **Mixed cut and fill** (the toe crosses the terrain): build S_cut (min of rising cones, cut slopes) and S_fill
  (max of falling cones, fill slopes). S_fill ≤ S_cut everywhere holds if and only if, for all toe points p and q,
  z_p − z_q ≤ min(sF_p, sC_q)·d(p, q) (appendix A). That is two toe checks: the cut surface does not undercut the
  toe (`ToeUndercut`, 5.2), and the fill surface does not overtop it (S_fill(q) ≤ z_q, `ToeOvertopped`). If both
  hold, the final surface is the **clamp** max(S_fill, min(T, S_cut)), restricted to the components connected to
  the toe. It is continuous, it is the terrain where the terrain already lies within the allowed band, and the
  change from cut to fill needs no special case: where the toe meets the terrain, the slope length is zero.
- **Efficiency.** Only terrain triangles in the band where D changes sign need the detailed overlay. For all
  others the sign at their vertices (point location in S̃ plus one `Orient3d`) and at the S̃ vertices inside them
  decides whether they are in or out.

### 5.6 Special cases and input validation

| case | handling |
|---|---|
| **No daylight** (cut: the terrain rises faster than s, eg a mountain flank; fill: the terrain falls away faster) | the region reaches the boundary of the terrain footprint: `NoDaylight`, with the boundary parts and the toe ranges that govern them (the pieces of the faces there). The partial result is kept, never presented as complete. In exact arithmetic the region cannot reach the D_max domain boundary; reaching it anyway is reported as an internal error |
| **Terrain holes, boundary; toe partly outside the terrain** | the overlay covers the common footprint only; catch lines that end at a hole or at the boundary are open and flagged (`CatchLine.Closed` = false, with the end reason) |
| **Flat slopes, grazing** | exact signs make the topology correct (5.5); the positions are as good as the rounding and `Tolerance` allow. Segments with a large horizontal uncertainty are reported as ill-conditioned (5.5) |
| **Open toe line** (design edge, not a loop) | decided (2.5): by default the slope is limited laterally by the XY normals at the start and end segment. There is no automatic fan around the ends. Options: explicit lateral limits from the caller (lines or a computation domain), or rounded end caps from the cone envelope. The lateral limits clip the computation domain (S̃ stays continuous inside it). The catch line ends at a lateral limit with the reason `LateralLimit`, and in the composite terrain the slope meets the terrain there along a side line, which is returned (a vertical step where they differ in height) |
| **Side** | the caller chooses the side explicitly: left or right of the direction of travel for open toes, inside or outside for closed ones. This choice is kept and never inferred from "closed" alone |
| **Several toe lines** (pit floor with islands, several pits or dumps) | all pieces of all toe lines enter one envelope, so they interact correctly (two pits whose walls meet get a ridge) |
| **Terrain steeper than s inside the cut zone** (gullies where T < S̃) | not generated, so not under the KO criterion. Reported as area and as triangles, because the final wall contains it |
| **s = 0 or very small** (berms, flat fills) | s must be > 0 for the cone model. Horizontal pieces are berms (section 10). Small s gives large D_max and needs a larger δ (5.4) |
| **Toe validation** | rejected with `InvalidInput` and the location: duplicate consecutive vertices, zero-length or vertical segments (in XY), a toe that self-intersects or pinches in XY (exact), a `Toe` that is not on the `Design` boundary when both are given, NaN or infinite coordinates or slopes. Missing `Side` or slopes that do not match the segments are rejected too. A toe that does not lie on the terrain is fine: that is the normal cut or fill case |
| **Not a height field** (overhangs, overlapping TINs) | rejected with `TerrainVolumeDifference.XYOverlaps` (exact), as `RequireHeightField` does today |

**What a failure means** (decided, 2.5). Every non-success status says which kind of failure it is
(`StatusKind`), so that "not supported by the restricted phase 1 method" is never mistaken for "geometrically
impossible":

| kind | statuses | meaning |
|---|---|---|
| `UnsupportedByMethod` | `CrossDesignInfluence`; `ToeUndercut` / `ToeOvertopped` when the causing cone reaches the toe through the design area; `NotImplemented` | the phase 1 method (Euclidean cone envelope) cannot handle this design. It is not a proof that the design is impossible; the later exterior mode is meant for these cases |
| `GeometricallyInfeasible` | `ToeSteeperThanSlope`; `ToeUndercut` / `ToeOvertopped` when the causing path runs outside the design area | no surface satisfies the specification, in any mode |
| `NotCertified` | `SlopeViolation`, `CatchLineAccuracyNotReached` | the computation could not certify its own result |
| (not a failure) | `NoDaylight` | the slope does not reach the terrain within its footprint; the result is partial |

Rules for every failure, in particular the conservative rejections:
- the causing toe edges and the affected areas are named (`ToeProblems`, `CrossDesignToeRanges`);
- toe heights and slopes are never changed automatically;
- there is no silent fallback to another attribution rule or mode;
- partial results are only returned explicitly marked as incomplete (`IsComplete = false`), never as a released
  result.

### 5.7 Volumes

On the same overlay, the region faces carry D linear on convex polygons, integrated exactly as in
`TerrainVolumeDifference` (split at D = 0, area times the centroid value), relative to a local origin. Cut and fill
volumes, areas, and per-toe-piece volumes (each slope triangle knows its piece) come out of one pass. The existing
`TerrainVolumeDifference(T, Composite)` serves as the independent check in the tests.

### 5.8 Robustness at UTM magnitudes

- Exact predicates run on the **stored doubles** (orient2d / orient3d are exact for all finite doubles).
- Constructions run in local coordinates. The origin is chosen **per axis** so that the shift is exact:
  - x0 = 0 if the coordinate range contains 0 (eg pit floors below sea level make z cross 0);
  - otherwise x0 is snapped towards zero to a multiple of a power of two ≥ ulp(max |x|), with |x0| ≤ min |x|.

  Then x − x0 is exact: both are multiples of ulp(x), and |x − x0| ≤ |x|. So local inputs give bitwise the same
  predicates. A test with z crossing 0 and one far from 0 checks this.
- Constructed points are rounded once, directly to the final coordinates, XY first and z after (5.4).
- `Tolerance` is a professional accuracy given by the caller. δ and `MinFeatureSize` are derived from the
  coordinate resolution (5.4). No default depends only on the size of the coordinates, and there are no hidden
  absolute constants. `float` appears nowhere in the path.
- Where exact predicates are needed:
  - the overlay (orient2d), the signs of D (orient3d), and SoS;
  - the slope oracle (i) and the support check (ii) (new);
  - the three-plane predicates of the envelope (new, 3×3 determinants of coefficient differences);
  - the XY-flip checks during repair.

  Constructed coordinates themselves stay doubles.

### 5.9 Rejected alternatives

| alternative | why not as the core |
|---|---|
| **Weighted straight skeleton** | weights per edge, but slopes are per vertex: linearly varying slopes are not planar faces. It mitres convex corners, which gives the max of planes, *below* the cone, so needless over-excavation (the cone is the least-excavation surface under R1). Not unique or planar with weights (Biedl et al. 2015). Event handling is fragile, and the only mature implementation is GPL (CGAL). Its useful idea, faces that meet in valleys, is contained in the min of cones |
| **Fast Marching / eikonal** | first-order approximation that can err on either side, so not conservative without a correction step. For R1 the closed form is exact. Kept as the solver for a later sector mode (2.3) |
| **Rays per point** (today) | breaks the KO criterion at convex corners and at changes of slope; needs clipping at concave corners and tolerance-based chaining (section 3) |
| **Offset strings per height + lofting** (vendor practice) | the offsets are the level sets of S̃ anyway, and lofting between them has no slope guarantee (further item 3) |
| **Grid / raster methods** | excluded by the requirements (TIN input), and no exact guarantee |

## 6. API sketch

Consistent with the existing terrain classes: public fields as options, `Compute()` returns bool, outputs as
fields, `CancelF`. A `Status` enum as in `ExactMeshBoolean` and `UVAtlasGenerator`.

```csharp
namespace gs
{
    /// A slope as the user specified it. The origin is kept so that the check can run against it (section 1).
    public struct SlopeSpec
    {
        public enum Kinds { Ratio /* 1:n, reference convention */, Percent, Degrees, Rise }
        public Kinds Kind; public double Value;
        public double S { get; }               // rise / run, conservative (never steeper than the spec)
        public bool ExactlyCheckable { get; }  // Ratio, Percent, Rise: exact; Degrees: against the double S only
        public static SlopeSpec Ratio(double n); public static SlopeSpec Percent(double p);
        public static SlopeSpec Degrees(double a); public static SlopeSpec Rise(double s);
    }

    /// One toe line with its slopes. Values per segment end, so that jumps at sector boundaries are possible.
    public class SlopeToe
    {
        public enum Sides { Left, Right, Inside, Outside }   // Left / Right for open toes, Inside / Outside for closed
        public enum Interpolations { LinearInSlope /* default */, LinearInRatio, LinearInAngle }
        public enum EndModes { NormalLimit /* default */, ExplicitLimit, RoundCap }

        public List<Vector3d> Points;
        public bool Closed;
        public Sides Side;                     // required, never inferred
        public SlopeSpec[] CutStart, CutEnd;   // per segment: slope at its start and at its end; null = no cut
        public SlopeSpec[] FillStart, FillEnd; // likewise for fill
        public Interpolations Interpolation = Interpolations.LinearInSlope;
        public EndModes EndMode = EndModes.NormalLimit;          // open toes only
        public List<Vector2d> StartLimit, EndLimit;               // EndModes.ExplicitLimit: lateral limit lines
        public static SlopeToe Constant(List<Vector3d> pts, bool closed, Sides side, SlopeSpec cut);   // the simple standard
    }

    /// Slope surface from toe lines, its catch lines with the terrain and, optionally, the composite terrain and
    /// cut / fill volumes. Mode: cone envelope (R1). Never steeper than the attribution rule of the mode allows:
    /// every slope triangle is checked exactly (Slope.IsSupportedBy) before Compute returns true.
    public class TerrainSlopeDesign
    {
        public enum Directions { Auto, Cut, Fill }
        public enum Modes { ConeEnvelope /* R1, phase 1 */, SectorPolygons /* later */, ExteriorDistance /* later */ }
        public enum Statuses { Success, InvalidInput, NotHeightField, NotImplemented /* modes not yet available */,
                               ToeSteeperThanSlope, ToeUndercut, ToeOvertopped, CrossDesignInfluence,
                               NoDaylight, CatchLineAccuracyNotReached, SlopeViolation, Cancelled }
        /// what a failure means (section 5.6): the method cannot handle the design, the design is impossible as
        /// specified, or the computation could not certify its own result
        public enum StatusKinds { Success, InvalidInput, UnsupportedByMethod, GeometricallyInfeasible,
                                  NotCertified, Cancelled }

        // inputs
        public DMesh3 Terrain;                 // TIN, height field
        public DMesh3 Design;                  // optional: design TIN (the obstacle of 5.2); each toe must lie on its boundary
        public List<SlopeToe> Toes = new List<SlopeToe>();   // several: pit floor with islands, several pits / dumps
        public Directions Direction = Directions.Auto;
        public Modes Mode = Modes.ConeEnvelope;
        public double Tolerance = double.NaN;  // required: max vertical deviation from the exact R1 surface (data units)
        public double TerrainVerticalUncertainty = 0;               // part of ε_D (5.5); survey errors are not known otherwise
        public double CatchLineWarningThreshold = double.NaN;       // required: horizontal; above it a section is reported
        public double RequiredCatchLineAccuracy = double.PositiveInfinity;  // binding: refine, else fail
        public double SlopeMargin = 1e-6;      // start value of δ; raised automatically if the check needs it (5.4)
        public double OutputGrid = 0;          // > 0: coordinates snapped to this grid before the check (export, 5.4)
        public List<double> TargetHeights;     // optional: build up to these heights and return the lines there (berms)
        // the input slope is the maximum admissible local slope; inter-ramp / overall checks are a separate later function
        public bool BuildComposite = false;    // the composite terrain is optional
        public bool ComputeVolumes = false;    // from the same overlay
        public Func<bool> CancelF = () => false;

        // outputs, each retrievable without the others
        public Statuses Status;
        public StatusKinds StatusKind;
        public bool IsComplete;                // false: partial results only, marked as incomplete / not released
        public DMesh3 SlopeSurface;            // new slope triangles only; group id = index into Pieces
        public List<SlopePiece> Pieces;        // group id -> (toe index, segment index, parameter interval, s_k, cut / fill)
        public List<CatchLine> CatchLines;     // 3D polyline, Closed, end reasons (FootprintBoundary, Hole, LateralLimit),
                                               // per segment |grad D| and the horizontal uncertainty
        public List<List<Vector3d>> TargetHeightLines;     // per target height, for follow-up slopes and berms
        public DMesh3 Composite;               // design + slope + terrain, if BuildComposite
        public List<List<Vector3d>> SideLines; // where the slope ends at a lateral limit
        public double CutVolume, FillVolume, CutArea, FillArea;   // if ComputeVolumes
        // diagnostics
        public List<ToeRange> NoDaylightToeRanges, ToeProblems, IllConditionedCatchLine, TangentialContact,
                              CrossDesignToeRanges;
        public List<int> ProblemTriangles;     // failing triangles if Status == SlopeViolation
        public double MaxSlopeRatio;           // upper bound of |grad| / s_k over the slope triangles, < 1 on Success
        public double UsedSlopeMargin;         // the δ in the end

        public TerrainSlopeDesign(DMesh3 terrain, DMesh3 design) { ... }
        public bool Compute() { ... }
    }

    public static class Slope
    {
        /// exact: |grad(a, b, c)| <= the spec (exactly for Ratio / Percent / Rise, against S for Degrees);
        /// orientation ignored; false for triangles degenerate in XY
        public static bool IsNotSteeper(Vector3d a, Vector3d b, Vector3d c, SlopeSpec spec);
        /// exact: IsNotSteeper, and the plane of (a, b, c) is <= z at both toe points p0, p1 (5.4 (i) and (ii))
        public static bool IsSupportedBy(Vector3d a, Vector3d b, Vector3d c, Vector3d p0, Vector3d p1, SlopeSpec spec);
    }
}
```

- The slope guarantee applies to the triangles of `SlopeSurface` (and their clipped parts in `Composite`), not
  to unchanged terrain in the composite.
- Group ids are internal and can change with subdivision. The stable reference is `Pieces`: toe, segment and
  parameter interval of the original input.
- Both checks are public so that users and existing pipelines can verify any TIN. A `g3cli slope` command can
  follow later in the pattern of the other commands.

## 7. Test plan and acceptance criteria

All test inputs are procedural (no mesh files): analytic terrains (planes, tilted planes, sine hills, cones,
valleys), grids triangulated in both diagonal directions, and random perturbations with fixed seeds.

**Oracles** (independent of the construction code):

- **O1, KO, exact:** every slope triangle passes `Slope.IsSupportedBy` against its piece (5.4 (i) and (ii)), on
  the stored output coordinates. A mutation that swaps the pieces of two triangles must make it fail.
- **O1b, attribution, independent:** a **brute-force R1 reference** (separate code, O(n) per point). At all three
  vertices and the centroid, find the minimal exact cone over all toe pieces, and require ‖∇‖ ≤ min s of all
  pieces within `Tolerance` of the minimum. This catches a construction that is supported but wrongly attributed.
- **O2, conservative and tight:** at random points of the slope region, 0 ≤ S_ref(x) − S̃(x) ≤ `Tolerance` (cut;
  mirrored for fill), with S_ref the brute-force min of exact cones.
- **O3, topology:**
  - the catch lines are closed, or end on the footprint boundary or a hole;
  - they are simple (no self-crossings: an exact segment test);
  - on a known analytic case they match the analytic line within the rounding resolution;
  - the final surface is a height field (`XYOverlaps` = 0) with no XY-inverted triangles (exact) and no gaps
    (boundary edges only on the outer boundary);
  - all coordinates are finite.
- **O4, volumes:** against `TerrainVolumeDifference(Terrain, Composite)` and against closed forms (a plane slope
  against a tilted plane terrain gives a prism or wedge volume).

**Oracle tests** (the new predicates themselves):

- triangles exactly at slope s, and perturbed by ±1 ulp in each coordinate: the oracle decides correctly, checked
  against `BigInteger` / rational arithmetic;
- coordinates at the `InRange` limits and beyond (the `BigInteger` fallback), very small and very large s;
- the three-plane predicates against rationals, including four planes through one point (SoS);
- `FromDegrees` / `FromRatio` / `FromPercent` against a high-precision reference, never steeper, on net10 and net48.

**Cases**:

| case | checks |
|---|---|
| straight toe, constant s, flat terrain | the catch line is the offset at distance H/s, exact up to rounding; the volume is analytic |
| convex toe polygon (square, regular n-gon), concave L and U shapes, star-shaped toe | fans at convex corners with slope ≤ s (no inscribed facets), valleys at concave corners, no overlaps (O3) |
| a small fan region of one piece entirely inside a large facet of another (the case that defeats sampling) | the facet is found and becomes its own triangles; O1, O1b |
| varying slope per point (jumps, linear ramps, random per vertex) | O1, O1b, O2; the R1 transition width matches 2.83 h for 1:3 → 1:1 (documents the semantics) |
| inclined toe (ramp, g = 0.1), and \|g\| = s exactly | planes tilted correctly (strip boundaries not perpendicular); `ToeSteeperThanSlope` decided exactly at g = s_build |
| two-level pit floor (toe undercut across the pit); a convex dip of a far piece between two toe vertices | `ToeUndercut` reported, also when every vertex passes |
| mixed cut and fill: the toe crossing a tilted terrain; the counterexample p = (0,0), z = 10, sF = 0.5 / q = (10,0), z = 0, sC = 2 | clamp continuous, both volumes, transition at the crossing points; `ToeOvertopped` for the counterexample |
| **flat slopes, grazing**: 1:10 slope against terrain with relief of a few cm; a slope exactly coplanar with a terrain region (cut and fill) | correct topology (O3), SoS rule applied consistently ("no work"), no gaps, no chaining failures |
| catch line over planar terrain at UTM; terrain vertices exactly on S̃ vertices and edges; a toe digitized on terrain vertices | realized clipped triangles pass O1 (snap, merge, fan from the far vertex) |
| no daylight (terrain rising faster than s), terrain with a hole, toe partly outside the terrain | `NoDaylight` / open catch lines, flagged, partial result consistent |
| several toes (pit with an island, two pits whose walls meet) | ridge between them, O1, O3 |
| z crossing 0 (floor below sea level) and far from 0 | the origin shift is exact (5.8): same predicates as on the raw coordinates |
| slivers forced by near-concurrent creases | repair removes them, O1 passes, or `SlopeViolation` (never a silent violation; a mutation that disables the repair must make this test fail) |
| invalid toes (duplicates, zero-length, self-intersecting, not on the design boundary, NaN) | `InvalidInput` with the location |
| slope jump at a vertex (different values left and right) against the same jump spread over a short segment | the jump is kept as specified, O1 per side; one value per vertex cannot express it |
| interpolation modes: the same 1:n input with `LinearInSlope`, `LinearInRatio`, `LinearInAngle` | three different surfaces, each checked against its own interpolated slopes; the mode is never swapped silently |
| slope input as 1:n and as percent | O1 exact against the spec as entered (no rounded double in between); angles checked against the double s and reported as such |
| open toe: default lateral limits at the end normals, explicit limit lines, round caps | the slope ends at the limits, catch lines end with `LateralLimit`, side lines returned; the chosen side is kept |
| closed toe without a side | `InvalidInput` |
| narrow pit, deep edge opposite a higher edge (influence through the design area) | `CrossDesignInfluence` with the causing edges, no silent wall |
| no `Tolerance` given | `InvalidInput` (no hidden default) |
| near-tangential catch line | reported as ill-conditioned with its horizontal uncertainty |
| catch lines only (`BuildComposite = false`, `ComputeVolumes = false`) | the catch lines are complete without building the composite terrain |
| export: OBJ round trip at UTM; `OutputGrid` = 1 mm | O1 still passes on the re-read mesh; with the grid, a larger triangle that the rounding would tip over is repaired or rejected before storing (not only slivers) |
| failure kinds | `CrossDesignInfluence` reports `UnsupportedByMethod`, `ToeSteeperThanSlope` reports `GeometricallyInfeasible`; toe heights and slopes unchanged; partial results have `IsComplete = false` |
| catch-line accuracy: warning threshold exceeded; a binding accuracy that refinement reaches; one it cannot reach (large `TerrainVerticalUncertainty`); planar contact | sections reported with δ_xy; refined result; `CatchLineAccuracyNotReached`; `TangentialContact` marked |
| missing `CatchLineWarningThreshold` | `InvalidInput` |
| cancel during each stage | `Cancelled`, no partial result presented as complete |

**Required by CLAUDE.md:**

- **Invariance** over `Similarity.All()` with rotations replaced by rotations about z (like the `ZUp` helper of
  `TerrainSafetyAlongSurfaceInvarianceTests`: the slope is defined against the vertical).
  - 2^k scales: bit for bit equal after mapping back.
  - UTM, 1e-3, 1e3, rotated about z: on inputs without grazing contact, the same topology (number and closedness of
    catch lines, same pieces per triangle) and positions within `Resolution`. With grazing contact the topology
    may legitimately differ (exact decisions on different doubles), so only O1 to O4 are required there.
  - **O1 passes in every transformed frame**, on the transformed output itself, the critical check for UTM
    rounding.
- **Reuse:** compute A, then B on the same instance, equal to a fresh instance (`Invariance.Difference`).
- **Determinism:** parallel equals serial and repeated runs are bitwise equal (the envelope and the overlay must not
  depend on the thread schedule; ties broken by ids).
- **Mutation checks:**
  - drop the margin δ, the tangent-plane lowering, the sliver repair, or the snapping of catch-line vertices
    → O1 fails;
  - swap piece labels → O1 fails;
  - replace the exact D signs with double signs → the grazing tests fail;
  - remove the connected-to-toe filter → the hill-behind-a-valley test fails;
  - check toe undercut at vertices only → the convex-dip test fails.

**Acceptance (phase 1 done):** all cases green including O1 at UTM. No status other than the documented ones.
The benchmark of section 8 runs at km scale with O1 and O3 green and the times recorded.

## 8. Benchmark plan

A new case `slope` in `benchmarks/TerrainBenchmark`, on the real Hambach data (DGM1 2022, dl-de/zero, downloaded by
`fetch_data.py`, nothing committed):

- **Toe lines:** level lines of the DGM in the pit (eg the pit bottom at a given elevation), simplified,
  1–20 km long, with procedural slopes per vertex (constant; sectors 1:3 / 1:1; random within 30–60°). Also the
  real bench toes of the pit, found by `TerrainBreakLines` (toes = concave break lines): real geometry with
  convex and concave corners, ramps and short segments.
- **Realistic slopes:** the slopes of the real bench faces, measured on the DGM between the toe and crest lines
  that `TerrainBreakLines` finds. They give plausible values per vertex, and a design from a real toe can then be
  compared with the real face behind it.
- **Real excavation as a plausibility check:** DGM 2022 against bDOM 2025 shows three years of real excavation
  (about 20 Mio. m³ per km², README of the benchmark). A slope designed from a 2025 toe against the 2022 terrain
  gives volumes and catch lines of a realistic order of magnitude, without claiming that the mine was designed
  that way.
- **Also available** (same source, dl-de/zero): the classified 3D point clouds of the pit edge and the interior,
  for TINs of different densities (`TerrainGreedyTIN`, dual contouring) under the same design.
- **Sizes:** 1, 4 and 12 km² terrain windows (2M–24M triangles), `coords=utm` and `coords=local`.
- **Recorded:**
  - time and peak memory per stage (pieces, S̃, verification, overlay, volumes);
  - triangle counts of S̃ and of the result;
  - `MaxSlopeRatio`, the counts of repaired slivers and of `NoDaylight` toe metres;
  - the O1 / O3 results.
- **Reference:** a numpy brute-force min of cones on a grid. It checks O2 and the volumes (the tolerance against
  the reference must stay within `Tolerance`·area).
- **Comparison:** R1 against the sector mode on the sector case, once that mode exists. This quantifies the
  difference of 2.2 on real data.
- **Target** (to be confirmed by the first implementation): a 4 km² window with a 10 km toe in minutes, dominated
  by the overlay. `TerrainVolumeDifference` does 8M + 8M triangles in 69 s today, and the overlay here is needed
  only in the catch-line band.

## 9. Further items

### 9.1 (Item 1) Self-intersections of open offset polylines

**Unnecessary for the slope construction.** S̃ is a function, so its level sets (the offset curves, open or closed)
are extracted from the TIN topologically and are simple by construction. The min resolves what would be
self-intersections of the fronts. Still relevant for 2D tools outside this design (eg offsetting a polyline for a
haul road): there the library should use Clipper2 (official C# package; see ROADMAP Phase 5 #3), which
handles open paths, rather than build its own. Clipper2 is under Boost-1.0, which is not one of the licenses
CLAUDE.md lists for porting. That does not matter here: it would be a package dependency under the same license as
this library, not ported code. Background: Held 1998 (Voronoi-based offsetting), Chen & McMains
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

**Decided** (2.5):
- Berms first by **absolute target heights** (bench and floor levels as the primary input); heights relative to
  the toe only as an optional convenience.
- Berm width constant per section at first. Variable widths come later, with a clear transition rule.
- Inclined toe sections are allowed.
- Ramps are separate design geometry (width, grade, connection geometry), not a by-product of an inclined toe.

**How the chosen approach extends:**

- For a flat toe and constant parameters, the stepped surface is z_toe + φ(dist(x, toe)), with φ the monotone
  staircase profile. A monotone profile commutes with the min (min of φ(d) = φ(min d)). The bench crests and toes
  are then exactly offset curves of the toe at cumulative distances (offsetting by a and then b is offsetting by
  a + b for the Euclidean distance), as in the vendor practice, with no self-intersections.
- **General case** (inclined toe, absolute bench levels, slopes and widths per section): **iterate the core**.
  1. Face k is the min of cones from toe line k (with s_f per vertex), clipped at level L_k.
  2. Crest k is the level line {S̃ = L_k}, extracted topologically from the TIN (a simple polyline).
  3. The berm is the horizontal region from crest k out to distance w (per vertex).
  4. Toe k+1 is the outer boundary of the berm. Equivalently, it is the level set of the min of "flat-then-vertical"
     functions, ie an offset of crest k by w, again a min of distances.
  5. Repeat until daylight.

  Every step uses the same machinery (envelope, exact level-line extraction, overlay). This is weaker than
  "berms are just a loop around phase 1", though. With variable widths, inclined toes and topology changes (a berm
  closing off, a level line splitting into several loops), the minimum berm width has to be **proven separately**
  for each step, by the oracle below, and not assumed from the construction.
- **New guarantees for berms** (three separate checks, decided in 2.5):
  - faces never steeper than s_f (O1 per face triangle). The face slope is the local limit; an inter-ramp angle is
    **not** used as the limit of every face triangle, because the berms reduce the overall slope and the faces may
    be steeper than it;
  - berms horizontal (z exactly L_k, an exact check);
  - **berm width never less than w**: this holds by the definition of the offset (every point of toe k+1 is at
    distance w from crest k), as long as the polyhedral approximation of the offset is conservative, ie the
    tangent planes keep it at distance ≥ w. Oracle: the exact minimum distance between the two polylines, as
    `TerrainSafetyLines` computes `MinLineDistance`.
  - **inter-ramp / overall slope** as an additional, separate check across several benches and berms, along
    defined profiles or measuring directions given by the caller. A later function of its own, not part of the
    per-triangle check.
- **Open points:**
  - how per-vertex slopes and widths pass from toe k to toe k+1: each vertex of toe k+1 takes the values of the
    piece of toe k that supports it (the attribution of 5.4);
  - `Tolerance` accumulates over the benches (each face and offset is conservative). Budget it per bench, or
    rebuild each face from the exact levels so that the error does not compound.
- **Catch line with berms:** the overlay of the stepped surface with the terrain. Grazing contact between a flat
  berm and flat terrain is handled by the exact signs and SoS as before.
- **Not covered:** ramps (haul roads crossing benches), which are a design element of their own with grade
  constraints along a path. Leave room: the toe-line input may carry a ramp as an inclined stretch, and the
  status `ToeSteeperThanSlope` already guards against ramps steeper than the face.

The core of phase 1 already offers what berms need from it: building the slope up to a target height and returning
the line there (`TargetHeights`, `TargetHeightLines`).

## 11. Implementation phases (after review)

1. The slope oracle (`SlopeSpec`, `Slope.IsNotSteeper`, `IsSupportedBy`, exact for 1:n and percent) with its exact
   predicate, error-bound review, and tests. Round-trip exact writers (OBJ `"R"`) and the quantized output
   path (`OutputGrid`: quantize, re-check, repair or reject), with read-back tests.
2. S̃ for constant slope per segment (the simple standard): pieces, plane-based exact envelope, realization with
   the margin, repair, O1 / O2 tests, invariance. Explicit side, lateral limits by the end normals.
3. Piecewise-constant slopes with jumps (values per segment end), then continuous change with the three
   interpolations; toe checks (`ToeSteeperThanSlope`, `ToeUndercut`, `ToeOvertopped`, `CrossDesignInfluence`).
4. Topological overlay with exact signs, catch lines with end reasons and horizontal uncertainty, connected region;
   optional composite terrain and volumes; special cases.
5. Mixed cut and fill (clamp).
6. Target heights and their lines (the hook for berms).
7. Benchmark at km scale; docs (OPERATIONS.md, SOURCES.md).
8. Later, each a development step of its own: inter-ramp / overall slope check along profiles, sector polygons,
   exterior distance, a proven bound for the catch-line uncertainty, berms with constant then variable
   widths, ramps, a proven interval tan for angle input, volume per cell, lofting utility.

## 12. Open questions for the user

All questions of the review rounds are answered (2.5: Q1–Q12). New questions will be added here as the
implementation raises them.

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
- **Mixed cut and fill.** S_fill ≤ S_cut everywhere iff for all toe points p, q and all x:
  z_p − sF_p·d(x, p) ≤ z_q + sC_q·d(x, q). Over x, the right side minus the left side has its minimum at p or at q
  (put x at the end with the larger slope; triangle inequality), and that minimum is
  z_q − z_p + min(sF_p, sC_q)·d(p, q). So the condition is z_p − z_q ≤ min(sF_p, sC_q)·d(p, q) for all p, q, ie
  both S_cut(p) ≥ z_p (cut does not undercut the toe) and S_fill(q) ≤ z_q (fill does not overtop it). A single
  global s_min, as in the first version of this document, is not enough. Counterexample from the review:
  p = (0,0), z = 10, sF = 0.5; q = (10,0), z = 0, sC = 2. Cut is consistent, but S_fill(q) = 5 > S_cut(q) = 0.
- **A winning far cone is gentler than the first toe contact** (used in 5.2). Let the cone of apex a win at x, and
  let the segment from x to a cross the toe at c first. If the toe is not undercut, then z_c ≤ z_a + s_a·|c − a|.
  So the cone of a at x is z_a + s_a·(|c − a| + |x − c|) ≥ z_c + s_a·|x − c|. If s_a ≥ s_c, the cone of c would be
  at least as low, so a wins strictly only if s_a < s_c.

## References

The sources this design was written from are listed in `docs/SOURCES.md` under "Terrain slope design (planned)".
The user's existing in-house code was not looked at and is not a source.
