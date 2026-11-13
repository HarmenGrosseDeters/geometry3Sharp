# Robust mesh offsetting with guarantees (study)

Status: **study; decided 2026-11-13** (coordinator, under the owner's autonomy mandate; section 9): option A.
**Stage A1 implemented** (`gs.MeshOffset`, section 10); **A2 shelved** after the probe (section 11), pending refinement. The study's
throwaway probe was run locally (public API only, not committed; section 5); its numbers are quoted with the command.

## 1. Question

An offset (d > 0) and an inset (d < 0) of a closed mesh S by a distance |d|: the boundary of S ⊕ B(d) (outward) or of
S ⊖ B(|d|) (inward), B a Euclidean ball. Wanted for the result R:

- (G1) closed, (G2) 2-manifold, (G3) free of self-intersections;
- (G4) within a proven band of the exact offset surface L = {x : sd(x) = d} (sd the signed distance to S, positive
  outside): every point x of R has |sd(x) - d| <= delta, and every point of L is within delta of R (two-sided);
- (G5) topology: R isotopic to L (where L is a surface; it is not at the distances where two sheets of the offset
  touch);
- (G6) scale-free: the result for 2^k S and 2^k d is 2^k times the result for S and d, bit for bit (`Similarity.All()`);
- sharp features where L has them, and the cost.

What "sharp features" means here: the Euclidean offset surface rounds every convex crease and corner (cylinder and
sphere patches) and has sharp creases only where the offset sheets of two parts of S meet (the concave creases of S,
moved out by d, and new creases over narrow gaps). A *mitered* offset (sharp convex creases, as in CAD shelling, Cao et
al. 2024) is a different surface; it is not L and has no band against L at a convex corner (the miter point is up to
d / sin(theta / 2) away). This note answers the Euclidean question and names mitered offsets as a separate item (9.4).

## 2. What the library has (searched before the literature)

| building block | what it gives an offset today | what it does not |
|---|---|---|
| `g3cli offset` (`tools/g3cli/OffsetCommand.cs`, #689; `docs/examples/OffsetSurfaceExample.cs`) | `MeshSignedDistanceGrid` (float samples, narrow band) + `MarchingCubes` at iso d, both directions; `linear_edges` root mode | states `enclosure_claim "none"`: no (G2)-(G6) claim; float samples; concave creases chamfered by the cell; degenerate triangles at fine cells (5.2) |
| `MeshAlphaWrap` (Portaneri et al. 2022) | closed, oriented, manifold, self-intersection-free by construction (exact `Delaunay3`); the input strictly inside; vertices on the offset surface at `Offset` from the input | outward only; wraps from outside, so it loses inner voids of S ⊕ B(d) (a hollow part's inner offset shell); band only measured (between vertices the sagitta comes closer than `Offset`; up to about `Offset + Alpha` farther where carved, 1.1 (Offset + Alpha) measured at the manifold step with `RefineNonManifold`); concave creases rounded by the empty alpha ball; cost about 1 / Alpha^2 |
| `SimplifyAlphaWrap`, `AlphaWrapQualityPass`, the K crease pass (owner 2026-11-12, being built) | fewer triangles, sharper creases, every move exactly checked against the input (`Reducer.KeepClearOf`) | keep "input inside", not the band to L (vertices measured down to 0.35 Offset) |
| `DualContouring` (manifold DC, QEF vertices strictly inside their cells) with options A and B of [DC_INTERSECTION_FREE.md](DC_INTERSECTION_FREE.md) (#552, #572) | sharp creases from Hermite data; (G3) by Wang's covered quads plus an exact check of the rest (`SelfIntersectionFree`); takes any `ImplicitFunction3d` | (G5) not certified today (2-3 of 20 clean inputs wrong topology, [CONTOURING_TOPOLOGY.md](CONTOURING_TOPOLOGY.md) on #749, section 2); no band statement; the repair moves of `ResolveSelfIntersections` have no termination argument (they report what is left) |
| `WindingContouring` / `MeshWindingNumberSolidify` | DC of the winding number (the repair path) | the level set lies on the input: no offset (inherent to the winding field, lever 6 study) |
| Contouring topology certificate (#749 note, stage 1 spike #779 branch `claude/contouring-certificate-stage1`) | Theorems 1-4 (PL, class P, decided: option A, stages 1-2 being built) and **Theorem 5 (class S: locally Lipschitz f with an enclosure of the generalized gradient over boxes, reviewed 2026-11-13)**: every cell passing (S0)-(S3) gives an isotopy between L and the DC mesh | class S needs the interval interface (stage 3, option B: "proof first", not scheduled); the distance field is not one of its listed families yet |
| `MarchingCubes.RootfindingModes.LinearEdges` + [CONSERVATIVE_MC_AUDIT.md](CONSERVATIVE_MC_AUDIT.md) (Jacobson 2026) | enclosure of the zero set for a true 1-Lipschitz f sampled exactly, at sigma > sqrt(3)/2 h | containment only, no (G2)/(G3)/(G5); our SDF grids are float and narrow-band, so the precondition does not hold (why `g3cli offset` says "none") |
| `ExactMeshBoolean` with an n-ary winding predicate (`ExactMeshBoolean(IEnumerable<DMesh3>, Func<int[], bool>)`) + `SafeRounding` | exact union / difference of many closed bodies, closed result after safe rounding | cost of large overlapping arrangements (5.1) |
| `MeshEnvelope` / `PolyhedralEnvelope` (Wang et al. 2020) | exact "triangle within epsilon of the mesh" | inner approximations of the epsilon envelope (prisms 0.8 epsilon along the normal, 0.554 / 0.577 epsilon sideways): a result at distance d next to an edge of S is *outside* the prisms of epsilon = d + delta unless delta is about 0.8 d. **Not usable for a tight offset band**; they are made for small epsilon |
| Distance queries: `DMeshAABBTree3.FindNearestTriangle`, `MeshQueries.TriangleDistance` (double), `CachingMeshSDF`, `MeshSignedDistanceGrid` (float) | point-to-mesh distance in double | no stated rounding bound on the point-triangle distance yet (needed for 4.2); the grids are float (exact-code rule: no `float` in certified code) |
| `MeshWallThickness`, `TerrainOffset` | domain-specific (wall thickness query, 2.5D terrain offset) | not a 3D solid offset |
| Clipper2 addon | exact integer 2D polygon offsets | 2D only |

Missing: a 3D `MeshOffset` operation; a double-precision distance implicit with an exact sign and stated error; a band
certificate; a class-S enclosure for the distance field; nothing in the library computes an inset with any guarantee.

## 3. Prior art (papers only; marks: [v] checked this session against the publisher / arXiv page or an index,
[m] recalled from memory, verify before it goes into docs/SOURCES.md)

- J. R. Rossignac, A. A. G. Requicha, *Offsetting operations in solid modelling*, CAGD 3(2), 1986, 129-148 [m]. The
  definitions: offset = S ⊕ B, inset = complement of (complement S ⊕ B), regularized.
- G. Varadhan, D. Manocha, *Accurate Minkowski sum approximation of polyhedral models*, Pacific Graphics 2004 (best
  paper), Graphical Models 68(4), 2006, doi 10.1016/j.gmod.2005.11.003 [v]: convex decomposition, pairwise convex sums,
  their union approximated by an adaptive grid, signed distance at the nodes and contouring; **same topology as the
  exact sum and a two-sided Hausdorff bound**, by per-cell tests (with Varadhan et al. 2004 on reliable contouring) [v
  for the claim, [m] for the details]. The closest prior art to the recommended option; limited to manifold
  boundaries [v, via a later survey].
- D. Pavić, L. Kobbelt, *High-Resolution Volumetric Computation of Offset Surfaces with Feature Preservation*,
  Computer Graphics Forum 27(2) (Eurographics 2008) [v title/venue]: octree, exact distances to the triangles at the
  samples, feature-sensitive extraction (Kobbelt et al. 2001) for the offset creases [m details]. No topology or
  self-intersection guarantee [m].
- Y. Chen, C. C. L. Wang, *Uniform offsetting of polygonal model based on Layered Depth-Normal Images*, Computer-Aided
  Design 43(1), 2011, 31-46 [m]: GPU ray sampling (LDNI), offset by Boolean of samples, contouring. Fast; no proof.
- S. Liu, C. C. L. Wang, *Fast Intersection-Free Offset Surface Generation From Freeform Models With Triangular
  Meshes*, IEEE T-ASE 8(2), 2011, 347-360 [v]: narrow-band signed distance on a uniform grid, then an
  intersection-free DC by convex / concave analysis; intersection-free, manifoldness not stated [v]. This is the
  "Wang" criterion our option A of DC_INTERSECTION_FREE.md already implements.
- D. Zint, N. Maruani, M. Rouxel-Labbé, P. Alliez, *Feature-Preserving Offset Mesh Generation from Topology-Adapted
  Octrees*, Computer Graphics Forum 42(5) (SGP 2023), doi 10.1111/cgf.14906 [v]: DC on an octree refined where the
  offset topology is complex, then offset-aware remeshing; the stated guarantee is a closed, combinatorially 2-manifold
  mesh (100 % of Thingi10K reported) [v]; intersections not guaranteed: the authors' 2025 paper reports intersecting
  offsets on about 5 % of models for their prior method [v].
- D. Zint, Z. Chen, Y. Zhu, D. Zorin, T. Schneider, D. Panozzo, *Topological Offsets*, ACM TOG (SIGGRAPH 2025),
  arXiv 2407.07725 [v]: embed the input in a tetrahedral mesh, insert the offset combinatorially, inflate / deflate
  towards the distance; manifold, watertight, self-intersection-free and strictly outside the input by construction
  [v]; the distance is reached as far as the inflation allows (band measured, not proven [m]). Probably the "manifold
  offsetting" work of the brief. Code exists (wildmeshing; licence not checked, not read; paper only by our rules
  since it builds on the TetWild family).
- Cao, Xu, Gao et al., *Robust and Feature-Preserving Offset Meshing*, arXiv 2412.15564, 2024 [v, already in
  research.md]: mitered offsets as the exact union of per-vertex / edge / triangle convex hulls (generalized winding
  number for classification). Same bottleneck as option C (5.1). Also: *A Parallel Feature-preserving Mesh Variable
  Offsetting Method with Dynamic Programming*, arXiv 2310.08997 [v title] (their earlier step).
- P. Hachenberger, *Exact Minkowski sums of polyhedra and exact and efficient decomposition of polyhedra into convex
  pieces*, Algorithmica 55, 2009 [v doi 10.1007/s00453-008-9219-6 seen in a search result; content [m]]. The CGAL
  approach (paper only, CGAL is GPL, not read). Exact sums of *polyhedra*: a ball is not one, so an exact offset needs a
  polytope in place of the ball (option C).
- M. Portaneri, M. Rouxel-Labbé, D. Hemmer, D. Cohen-Steiner, P. Alliez, *Alpha Wrapping with an Offset*, ACM TOG
  41(4), 2022 [v, in SOURCES.md]: the wrap's guarantees; sharp features named as a limit.
- Certified isosurfacing (the #749 note's section 1): Snyder 1992, Plantinga & Vegter 2004, Lin & Yap, Boissonnat &
  Oudot; and F. H. Clarke, *Optimization and Nonsmooth Analysis*, 1983 (generalized gradients) [m, as in #749].
- A. Jacobson, *A Sharp Conservative Offset for Marching Cubes*, arXiv 2609.13430, 2026 [as in research.md].

How others solve it, in one line: volumetric distance + contouring is the common route (Pavić-Kobbelt, Liu-Wang,
Zint 2023, Varadhan-Manocha); guarantees come either from per-cell tests on an adaptive grid (Varadhan-Manocha:
topology and Hausdorff), from an intersection-free contouring rule (Liu-Wang), or from a combinatorial construction in
a tet mesh (Topological Offsets, without a proven band). Exact constructions are used for mitered or polytope offsets
(Cao 2024, Hachenberger), at the cost of large unions.

## 4. The options

### Option A (recommended): certified contouring of the distance field, `MeshOffset` as a new operation (tier L, staged)

f(x) = s * sd(x) - |d| with s = +1 (offset) or -1 (inset); L = {f = 0}. For a clean closed S:

1. **Distance implicit** in double: nearest triangles by the existing AABB tree, point-triangle distance with a
   stated forward error bound e(x) (to be derived; it is a few ulps of the coordinates for the standard
   closest-point-in-triangle computation), sign from an exact inside test for clean closed meshes (the exact winding
   number / ray crossing the boolean already uses). No float grid. The grid is placed on the bounding box minimum with
   a cell h = |d| * 2^-m (or diagonal * 2^-m), so every node coordinate scales by 2^k exactly; sqrt and the products
   scale exactly as well, so (G6) holds by construction (tested over `Similarity.All()`).
2. **Contouring**: `DualContouring` (two-level or adaptive) on f with Hermite normals = the unit vector to the closest
   point (the gradient of sd where it exists), options A and B on: (G1), (G2) as today's manifold DC, (G3) proved by
   the covered quads plus the exact check, reported in `SelfIntersectionFree`. Concave offset creases come out sharp
   through the QEF (the planes and cylinders of the two sheets meet there), convex parts are smooth as in L.
3. **Band certificate (G4), new and cheap, proven**: sd is 1-Lipschitz. For a result triangle T with centroid c and
   r = max |v_i - c|: |sd(x) - sd(c)| <= r on T, so T lies in the band [f(c) - r - e(c), f(c) + r + e(c)] around L.
   Triangles whose bound exceeds the requested delta are split recursively (only for the check, an interval branch and
   bound) or the cell is refined. The other direction (every point of L near R) follows from the topology certificate
   (each certified cell L meets has a mesh vertex inside it, so the distance is at most the cell diagonal), or, without
   it, is only measured. The certificate is independent of how R was made, so it also serves options B and D.
   (The envelopes are *not* the tool here, section 2.)
4. **Topology certificate (G5)**: class S of the #749 note (Theorem 5) with an enclosure G(X) of the generalized
   gradient of f over a box X: the unit directions from X to the candidate nearest triangles (those whose distance to
   X can be at most the upper bound of sd over X), i.e. a cone, computed with outward rounding. Off the medial axis
   the cone is narrow and (S1)-(S3) pass; at a concave crease of L (two nearest parts) the cone contains two
   directions less than 180 degrees apart, so a separating direction exists and the cell can still pass (S3); where two
   sheets of L touch (a gap of exactly 2 |d|) the cone surrounds 0 and the cell is never certified, which is correct
   (L is not a surface there). Cells that fail are refined up to a limit and reported, as in stage 2 of the #749 plan.
   Note: this is a specific instance of option B of the #749 note (interval implicits), which the owner put on "proof
   first". The distance field needs no general interval interface, only this enclosure, and Theorem 5 is already
   reviewed; what is new for the proof is only that the cone is a valid enclosure of Clarke's generalized gradient of
   the distance to a polyhedron (it is the convex hull of the unit vectors to the nearest points, Clarke 1983 [m]), and
   the treatment of the sign at the surface (the offset level is |d| > 0 away from S, so f is the unsigned distance
   there and the sign enters only through the inside test of the nodes).
5. **Inputs that are not clean** (open, self-intersecting): first `MeshRobustRepair` (exact resolve), or fall back to
   option B; class W of the #749 note is not needed for offsets in the first stages.

Stages:

| stage | what | size | gate |
|---|---|---|---|
| A1 | `MeshOffset` (offset and inset), the double distance implicit with its error bound, DC with A/B, the band certificate (3), report: closed, manifold, `SelfIntersectionFree`, `BandCertified` with the bound reached | M | invariance, reuse, determinism; band certificate never true on a mutated result; inset of a hollow and offset over a narrow gap right on the procedural set; time against `g3cli offset` at the same cell |
| A2 | the class-S enclosure of the distance gradient and the certificate (4), refinement by uncertified cells, `TopologyCertified` and the cell list | M (exact-code review rules for the enclosure) | proof of the enclosure lemma reviewed by a second session; never certified on a wrong result (the polytope oracle of option C, 6); certified share on the lever-6 set and the procedural CAD solids |
| A3 | optional: CLI / MCP (`g3cli offset --certified`), replacing `enclosure_claim "none"` by the certificate's statement | S | CLI tests |

Total tier L (A1 + A2), A1 alone M. A1 is useful on its own: (G1)-(G4) and (G6) with proofs, (G5) measured.

### Option B: thin API over the alpha wrap (`MeshOffset.Outward` = `MeshAlphaWrap` with Offset = d), tier S

- Gives (G1)-(G3) by construction, the input strictly inside, (G6) (the wrap is already tested over
  `Similarity.All()`), any input (soups, open, broken).
- Does not give: insets; inner voids of S ⊕ B(d) (wrong topology for hollow parts: the wrap flows in from outside
  only); a proven band (the inner side has the sagitta between vertices, the outer side about Offset + Alpha, measured);
  concave offset creases (rounded by the empty alpha ball). Fidelity to L needs Alpha well below d, cost ~ 1 / Alpha^2.
- With the band certificate of A1 (3) attached, its band becomes a per-run proven statement (the certificate does not
  care how R was made). Cheap and robust; the right answer for "a safe outer hull at about d for any input".

### Option C: exact polytope Minkowski sum (tier L; measured in 5.1; recommended as a test oracle only)

Replace B(d) by a convex polytope P with d * rho * B ⊆ P ⊆ d * B (rho its in-radius ratio). Then exactly
S ⊕ P = S ∪ ⋃_t (t ⊕ P) and S ⊖ P = S \ ⋃_t (t ⊕ P) (P symmetric; t the triangles of S), one n-ary
`ExactMeshBoolean` with the predicates `w[0] > 0 || w[1] > 0` and `w[0] > 0 && w[1] == 0`, the hulls t ⊕ P concatenated
as the second mesh. (G1), (G3) exact up to safe rounding, the band [rho d, d] by construction, inner voids kept, both
directions, sharp concave creases exact, (G6) if P's coordinates are d times a fixed unit polytope. (G2): the exact
union can be non-manifold where solids touch (split by the boolean, `NonManifoldEdges`). Its result is the polytope
offset, not L: the band is set by rho (0.795 for an icosahedron, 0.934 for 42 vertices, about 0.98 needs about 160
vertices). The cost is the arrangement of F overlapping hulls and grows with (d / triangle size)^2 per triangle: it is
not a general operation, but on small procedural inputs it is an **exact sandwich oracle**: S ⊕ (rho-inscribed P) ⊆
S ⊕ B(d) ⊆ S ⊕ (circumscribed P), checked by exact booleans, for the tests of option A.

### Option D: harden `g3cli offset` (SDF grid + marching cubes), tier S

Switch to a double distance implicit, attach the band certificate and the exact self-intersection report, keep
`enclosure_claim` honest. Gives (G1) (MC on a closed grid region), (G4) per run, (G6) with the grid rule of A.1; no
(G2) guarantee at ambiguous cases beyond MC's tables, degenerate triangles (5.2), no sharp creases, no (G5). A cheap
improvement of the existing command, subsumed by A1.

### Option E: Topological Offsets (Zint et al. 2025), tier L+

Needs a robust tetrahedral mesher with an embedded input (our tet meshing is at stage 0; stage 1 waits, research.md
item 3); gives (G1)-(G3) and "strictly outside" by construction but, as far as the abstract says, no proven band to L.
Not now; reconsider when tet meshing stage 1 exists.

### Ranking

| | (G1) closed | (G2) manifold | (G3) no SI | (G4) band | (G5) topology | inset | voids | concave creases | any input | cost | tier |
|---|---|---|---|---|---|---|---|---|---|---|---|
| **A** | proven | proven (manifold DC) | proven (A/B + exact check) | proven per run | certified (A2), reported cells | yes | yes | sharp (QEF) | clean closed (repair first) | grid DC + tree queries (5.3) | M + M |
| B | proven | proven | proven | measured, or proven per run with A's certificate | no (outer shell only) | no | no | rounded | yes | 1 / Alpha^2 | S |
| C | proven | non-manifold contacts split | proven | [rho d, d] by construction (polytope offset) | exact for the polytope offset | yes | yes | exact | closed | superlinear, 50 ms per input triangle at d ~ edge length (5.1) | L |
| D | proven | MC tables | measured | proven per run | no | yes | yes | chamfered | closed | grid MC | S |
| E | proven | proven | proven | measured | (by construction of the embedding) | yes | yes | ? | yes | tet mesher | L+ |

## 5. Measurements (throwaway probe, not committed)

Probe: a console project against `geometry3Sharp_netstandard.csproj` (net10.0, Release, 4 cores of the cloud
container), inputs `Sphere3Generator_NormalizedCube` (radius 5) and `Procedural.Make("lshape")` of
`benchmarks/SharpFeatureProbe`, d = 0.05 x bounding box diagonal unless stated; P a generically rotated icosahedron
(12 vertices) or icosahedron plus edge midpoints (42), hulls t ⊕ P by brute force; vertex distances by
`DMeshAABBTree3` + `MeshQueries.TriangleDistance`; clean = `MeshRobustRepair.NotCleanReason` empty.

### 5.1 Option C (exact Minkowski union / difference)

| input | S tris | P | hull tris | op | boolean ms | result tris | clean | vertex sd / d |
|---|---|---|---|---|---|---|---|---|
| sphere (6 edge vertices) | 300 | 12 | 12972 | offset | 4466 | 550 | yes | [0.799, 1.000] |
| | | | | inset | 3265 | 684 | yes | [0.799, 0.970] |
| sphere, d = 0.01 diag | 300 | 12 | 12972 | offset / inset | 2342 / 1305 | 548 / 688 | yes | [0.799, 1.000] |
| sphere | 300 | 42 | 36540 | offset / inset | 8119 / 6864 | 782 / 892 | yes | [0.936, 1.000] / [0.936, 0.988] |
| sphere (11) | 1200 | 12 | 51452 | offset / inset | 64733 / 62198 | 1618 / 1998 | yes | [0.799, 1.000] / [0.810, 0.994] |
| lshape | 620 | 12 | 27240 | offset / inset | 11495 / 11470 | 1018 / 452 | yes | [0.802, 1.000] / [0.874, 0.999] |

Correct (all clean, the band [rho, 1] as predicted, rho = 0.795 / 0.934), but 4x the input triangles cost 15x the
time at a fixed relative d (the overlaps per hull grow with (d / h)^2): about 50 ms per input triangle at d about the
edge length, so a 100k-triangle part would take hours. Pruning (hull faces inside S for the offset, hulls inside the
union) would cut a constant, not the growth. Settles: C is an oracle, not the operation.

### 5.2 Option D baseline (`MeshSignedDistanceGrid` float + `MarchingCubes`, the `g3cli offset` path)

| input | cell | ms | tris | clean | vertex sd / d |
|---|---|---|---|---|---|
| sphere (11) | d / 4 | 410 | 27512 | yes | [0.9939, 1.0024] |
| | d / 8 | 1438 | 110264 | degenerate triangles | [0.9981, 1.0015] |
| | d / 16 | 8107 | 441260 | degenerate triangles | [0.9995, 1.0006] |
| lshape | d / 4 | 226 | 26732 | yes | [0.9925, 1.0000] |
| | d / 8 | 929 | 109972 | degenerate triangles | [0.9981, 1.0000] |
| | d / 16 | 7609 | 439212 | degenerate triangles | [0.9995, 1.0000] |

Vertices are close to L (the float SDF and the linear root), but nothing bounds the faces, the concave crease of the
L-shape's offset is cut by the cells, and fine grids give degenerate triangles. This is the floor option A must beat
in time per triangle at the same band.

### 5.3 Upper-bound estimate for option A (not measured; the A1 spike measures it first)

The DC of A1 evaluates f at the grid nodes and Hermite points: the same number of tree queries as the SDF grid of 5.2
(which does them in float with sweeping); the DC itself versus MC was not measured here, so the contouring is
estimated at 1-3x the 5.2 times at the same cell, an assumption the A1 spike checks first. The band certificate is one
distance query per result triangle plus splits only where triangles are larger than the band allows: below 10 % of the
contouring. The A2 enclosure needs the candidate set of nearest triangles per cell (a box-distance query in the same
tree) and a cone per cell: the #749 note's class-P check cost (section 3 there) is the closest analogue; estimate
1-2x the contouring. So A1 + A2 at about 2-6x today's `g3cli offset` at the same cell, with the stop rule: if the A1
spike exceeds 5x at the same band, the distance queries get the batch / fast-winding treatment first.

## 6. Guarantees: proven vs measured (option A)

Proven (with a proof in the A1 / A2 PR, reviewed): (G1), (G2) (manifold DC), (G3) (A/B coverage plus the exact check;
`SelfIntersectionFree` false is reported, never hidden), (G4) one side per run by the Lipschitz certificate (assumes
the distance error bound e, to be derived and reviewed), the other side and (G5) where all cells L meets are certified
(Theorem 5 plus the enclosure lemma), (G6) by the grid rule.

Measured only: the share of certified cells and of runs, triangle quality (min angle, > 170 degrees), crease
fidelity (crease normal error against the analytic offset of the procedural solids), time.

Not claimed: anything for inputs that are not clean and closed (they go through the repair first; B is the fallback);
(G5) at a touching of two sheets (never certifiable, by any method).

## 7. Test plan (repo style)

- **Procedural inputs only**: boxes, the `Procedural` CAD solids (lshape, octant, slot, vnotch, chamfer), spheres,
  a torus, a hollow cube (inner void: the offset must keep two shells, the inset must keep the wall), two boxes with a
  gap below, at and above 2 |d| (the offset must merge, be uncertified at the touching, stay separate).
- **Analytic oracle**: for a box, the offset surface is known in closed form (faces moved out, quarter cylinders,
  sphere octants); volume = V + A d + M d^2 + 4/3 pi d^3 (Steiner formula, M the integral mean curvature); for the
  inset of a box the shrunken box. Volume and per-vertex sd within the certified band.
- **Exact sandwich oracle (option C)** on inputs up to about 300 triangles: R lies outside S ⊕ P_in and inside
  S ⊕ P_out up to the band, checked with exact booleans; also the never-certified-when-wrong check for A2.
- **Invariance**: `Similarity.All()` (`tests/geometry3Sharp.Tests/Harness/Invariance.cs`), 2^k scales bit for bit,
  offset and inset, with the grid rule of 4.A.1.
- **Reuse**: `Compute()` twice on one instance, and after changing d and the direction.
- **Determinism**: parallel equals serial, repeated runs (the tree queries and the DC phases run in parallel).
- **Success paths check the result**: closed, manifold, finite, `MeshRobustRepair.NotCleanReason` empty, the band
  certificate true, not only the status.
- **Mutation**: mutants of the band certificate (drop r, drop e, `<` for `<=`), of the enclosure (a cone missing one
  candidate, no outward rounding) must be killed by the sandwich and analytic oracles; the certificate never true on a
  result moved by more than the band (a hand mutant that shifts one vertex).
- **Time**: every test below 5 s; the corpus runs (lever-6 set, Thingi10K sample) go to a benchmark
  (`benchmarks/MeshOffset`, before / after against `g3cli offset`) or the Nightly tier.

## 8. Risks

- The distance error bound e and the enclosure lemma are new proof obligations in near-exact code; the exact-code
  rules (no FMA, no float, reviewed parallel loops) should apply to the certificate files.
- The certified share may be low where L has many concave creases near grid planes (general position, as for the
  #749 class P); refinement cost there is unknown until A2's spike.
- Grid DC triangle quality (slivers near creases) is not guaranteed; a post-pass (the K pass / `SimplifyWithin`) must
  keep the band, which the certificate re-checks.
- Large |d| relative to the part: the grid grows with (size + 2d)^3 / h^3 while the band needs h ~ delta; an
  adaptive (octree) grid as in Zint 2023 / Varadhan-Manocha becomes necessary, which the two-level DC only partly is.
- Overlap with the #749 option B decision ("proof first"): A2 is a narrow instance of it and needs the owner's go.

## 9. Decisions for the owner

1. **Route**: option A (certified distance-field contouring, A1 now as M, A2 after the enclosure proof is reviewed)
   as `MeshOffset`, with B as the documented fallback for broken inputs? (Recommended.) Alternatively B alone (S) if an
   outward hull at about d is all that is needed now.
2. **A2 and the #749 option B**: may the distance-field enclosure go ahead as its own proof (docs first, then code),
   ahead of the general interval interface? It does not need the interval interface.
3. **Option C** only as a test oracle (no public API)? (Recommended.)
4. **Mitered offsets** (Cao et al. 2024; CAD shelling, sharp convex creases) as a separate later item, not part of
   `MeshOffset`? (Recommended: separate, with a patent check as research.md already notes.)
5. **`g3cli offset`**: switch it to `MeshOffset` once A1 is merged (its `enclosure_claim` then reports the
   certificate), or keep the MC path as a fast mode?

Decided (coordinator 2026-11-13, under the owner's autonomy mandate):

1. Option A; A1 now ((G1)-(G4), (G6)). It reuses the certified-DC pieces of the contouring-certificate work (#779),
   no second copy; the library search for building blocks comes first (AGENT_WORKFLOW.md section 8).
2. Yes: the distance-field enclosure is written as its own proof in this note first, reviewed by a fresh session the
   coordinator routes; A2 is built only after that.
3. Option C is a test oracle only, in the Nightly tier (its cost).
4. Mitered offsets (Cao et al. 2024) stay a separate later roadmap item (research.md, "Feature-preserving offsets").
5. `g3cli offset`: decided after A1 on benchmark numbers; first as an opt-in flag, the default changes only on the
   coordinator's go.

A1 also carries: the invariance test over `Similarity.All()`, a reuse test, mutation checks of the certificate and a
benchmark against SDF + marching cubes. Papers only (Zint 2023, Topological Offsets 2025, Varadhan & Manocha); their
code is not read.

## 10. Stage A1, implemented

`gs.MeshOffset` (`mesh_ops/MeshOffset.cs`), tests `MeshOffsetTests`, benchmark
[`benchmarks/MeshOffsetBenchmark`](../../benchmarks/MeshOffsetBenchmark/README.md).

- **Scale**: the input is processed as a copy scaled by 2^k to an extent in [1, 2) (as `MeshWindingNumberSolidify`);
  the cell is `RelativeCellSize` (default 1/4, at most 1/2) times |d| and the grid is shifted by an odd fraction of a
  cell. So (G6) holds bit for bit (tests over `Similarity.All()` and shifts 2^-300 .. 2^300).
- **Field**: the unsigned distance at the nodes (tree nearest query, capped at |d| + 2h), F = d - sd. Signs: a node
  farther than 1.5 h from the input shares the side of the input with its neighbours (the grid edge of length h cannot
  reach the input), so one exact inside test (`DMeshAABBTree3.IsInside`, crossing parity) per connected set of such
  nodes; nodes nearer than 1.5 h < |d| need no sign (F has the sign of d there).
- **Contouring**: `DualContouring` in sparse mode (the cells with a sign change), options A and B, `ResolveSelfIntersections`
  on. Hermite data: on a sign-changing edge the whole edge is on one side of the input (outside for an offset, inside for
  an inset: both ends are within h < |d| / 2 of a node beyond the offset level), so the crossing is found by bracketed
  regula falsi (Illinois) on d - side * u and the normal is side times the unit vector from the nearest input point.
- **Band certificate** (option A item 3, `MeshOffset.CertifyBand`, usable on any surface): per triangle, a centre c and
  r, an upper bound of the distance from c to the exact corners (interval boxes of the corners); sd(c) enclosed by
  `ExactInterval` arithmetic on the doubles: above by the distance to an explicit point v0 + b1 (v1 - v0) + b2 (v2 - v0)
  of the nearest triangle (b1, b2 >= 0, b1 <= 1 - b2 exactly), below by min over the triangles whose (padded) box can be
  that close of w . (c - x) / |w| over their corners x (w from c towards the triangle's nearest point; Cauchy-Schwarz
  and linearity over the triangle), the sign by the exact crossing parity where c is provably off the input. Then
  sd in [sLo - r, sHi + r] on the triangle (1-Lipschitz). A triangle above the tolerance is split by exact dyadic
  barycentric coordinates, up to `MaxBandSplitDepth` (6), and not split where its centre alone is beyond the tolerance.
  No hand-derived error constant: the intervals widen every operation by an ulp (ExactIntervals.cs). The default
  tolerance is half a cell; every one of the 32 benchmark cases reaches it.
- **Proven in A1**: (G1), (G2) (checked: `Status.Failed` otherwise), (G3) (exact), (G4) one side, (G6).
  **Measured**: the other side of (G4) and (G5) (the procedural oracles: Steiner volumes, components of a hollow part and
  across a gap). **Not promised**: triangle quality; degenerate (zero-area) triangles were measured in 1 of 32 benchmark
  cases until DualContouring's quad split avoided them (#800; 0 of 32 since), and a quad with two degenerate splits can still give one.
- **Numbers** (4 threads, 32 cases per method, procedural): band certified at half a cell 32 / 32 against 15 / 32 for
  SDF + MC (its insets fail by chamfered creases, up to 0.37 |d|); clean 31 / 32 (32 / 32 after #800) against 25 / 32; triangles 0.95-1.07x;
  time 0.6-4.7x of SDF + MC, median 1.6x (the estimate of 5.3 was 1-3x for the contouring plus below 10 % for the
  certificate; the certificate takes about half of MeshOffset's time at the default tolerance, eg 736 of 1455 ms on the sphere at |d| / 4).
- **Mutants** (manual, all killed by `MeshOffsetTests`): r without the far corners, r dropped, a lower distance bound 1.2x
  too large, an upper bound 0.8x too small, no sign flood, a wrong edge side, no sign in the certificate.

## 11. Stage A2: the topology certificate, probe result (2026-11-13): shelved

The certificate of [MESH_OFFSET_ENCLOSURE.md](MESH_OFFSET_ENCLOSURE.md) was probed locally on the level-set side
(`mesh_ops/MeshOffsetTopology.cs`, on the branch `wip/mesh-offset-a2-probe` only): every cell of `MeshOffset`'s grid is proven zero-free (on blocks of cells first),
certified, or listed; the node signs the contouring used were checked against the proven ones (0 mismatches in every
run). Inputs: a box 2 x 3 x 1.5, a sphere (radius 5, 600 triangles), both also rotated (non-axis-aligned), offset and
inset by 0.1 of the bounding box diagonal, cell |d| / 4 (|d| / 8 where noted; the table writes abs(d), as a bar would
break its columns). Share = certified / (certified + listed),
the cells that are not proven zero-free; time = certificate / A1. The segment rule (S1* / S2*): an edge or a face of a cell
may be zero-free except in one closed piece, on which the direction test is made, instead of passing the test as a whole.

| input | E (reviewed: directions to whole candidate triangles) | E' + segment rule (nearest-point trichotomy; S1* / S2*) | E'' + segment rule (edges: sigma cone of the two face normals, by convexity) |
|---|---|---|---|
| box offset | 73.8 % | 90.3 % (27x) | 90.3 % (25x) |
| box inset | 8.9 % | 54.4 % (59x) | 64.0 % (124x) |
| sphere offset | 21.4 % | 38.4 % (104x); 49.2 % at abs(d) / 8 | 75.3 % (334x) |
| sphere inset | 11.3 % | 21.3 % (84x) | 34.2 % (231x) |
| rotated box offset | 27.8 % | 62.0 % (50x); 68 % at abs(d) / 8 | 94.4 % (63x) |
| rotated box inset | 0 % | 0 % (14x); 0 % at abs(d) / 8 | 0.8 % (31x) |
| rotated sphere offset | - | 38.2 % (86x) | 80.1 % (149x) |
| rotated sphere inset | - | 19.1 % (42x) | 30.7 % (140x) |

Stop rule (coordinator 2026-11-13): 90 % on the box and sphere offsets and on the smooth insets of convex inputs, 50 % on
the rotated box inset, certificate time at most 3x A1. E'' fails on the sphere (75 %, smooth inset 34 %), the rotated box
inset (0.8 %) and the time (25-334x). **Decision (coordinator 2026-11-13): the A2 code is shelved.** An opt-in report at 25-334x the
offset's cost, with no claim attached, would add API and test surface without a guarantee for the user. Refinement
(finer cells where cells are listed, with #749 stage 2; the coordinator's route, not the options B of this note or of
#749) is the route that might make it a certificate: it can shrink the listed set, but it may not reach the stop rule
where the rotated box inset stays at 0 % at both cell sizes and where (S2') fails at every refinement (inset creases,
corners of three or more sheets; MESH_OFFSET_ENCLOSURE.md section 5). It is later work. The probe code (E'' with the segment rule, `mesh_ops/MeshOffsetTopology.cs`, an internal
`MeshOffset.CertifyTopology` hook and the measurement test `MeshOffsetTopologyProbe`) is kept on the branch
`wip/mesh-offset-a2-probe` (no PR), as the starting point for refinement. The mesh side of Theorem O exists in the contouring certificate (#779): `DualContouring.RecordCertificateData`
before `Generate()` gives, on the final positions, `CertificateMeshFailures` (vertex placement, no area, left out,
self-intersections, transitions), `CertificateMeshCells` (local refusals, (H4) and option B's facts (i)-(iii)) and
`ContourGrid()` (the pattern and the face rule); A2 reuses it when it resumes, without changes to DualContouring.cs.

What the probe showed, for a later attempt:
- The reviewed enclosure E is sound but loose by the size of the candidate triangles: a large triangle makes its
  direction set nearly a hemisphere. E' (the nearest point of a triangle is interior, on an edge, or a vertex, each
  with its own direction set, dropped where a linear test on the corners excludes it) and E'' (at an edge, the
  direction lies in sigma times the cone of the two face normals; empty where the edge is convex from the point's side;
  proof: the polar of the two half-planes in the plane orthogonal to the edge, appendix A) tighten it; neither is reviewed.
- What remains listed: cells whose edges or faces are nearly tangent to L (the direction set over the cell is still
  wider than the angle between the edge and L; refinement shrinks it), the insets (a cell near L sees two or more
  faces of the shrunken box or a wide cap of the sphere, as L is close to the medial axis there), and S3 cells at the
  inset's creases.
- The cost estimates were off by more than 10x: section 5.3 put A2 at 1-2x the contouring and MESH_OFFSET_ENCLOSURE.md
  section 8 at 1.3-2x A1; the probe measured 25-334x. Neither estimate had the segment rule (the zero-free proofs of up to
  256 pieces per edge and 4096 per face, one distance bound each) nor the exact fallbacks of the tighter enclosures.
- The cost is the zero-free proofs of the segment rule (up to 256 pieces per edge, 4096 per face, one distance bound
  each) and the exact fallbacks; a cheaper zero-free test (a monotone bound along the piece instead of the Lipschitz
  ball) would be the first lever.
- Prior art, recalled and not checked [m]: Varadhan and Manocha (2004 / 2006, section 3) certify the topology of a
  contoured union of convex Minkowski pieces with per-cell tests on an adaptive grid; as recalled, their tests act on the
  convex pieces themselves, not on a tessellation, which would avoid the loose edge and vertex parts found here. Not
  checked against the paper (it was needed only for an addendum, which the stop rule ruled out).

### Appendix A. The normal-cone claim at edges (E''), unreviewed

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
vertices**, convex, reflex or saddle, on either side; this part follows by the same argument as E' (unreviewed) and is narrow
(its directions spread by about diam(X) / u). So the inset side (sigma = -1) is covered by the same arguments as the offset side (unreviewed).
Not covered: non-manifold edges (excluded: the input is clean), degenerate triangles (N = 0; excluded the same way).
