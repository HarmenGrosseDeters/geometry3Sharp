# ContourLoft, stage 1: a band between two closed contours

Status: approved by the coordinator 2026-11-12; built in #741 (section 9 lists what changed while building and the measurements). Roadmap: [research.md](../roadmap/research.md) item 9; background
[TERRAIN.md](TERRAIN.md) §9.2; sources in [SOURCES.md](../SOURCES.md) (contour lofting row).

## 1. Problem

Input: two closed polygons P (m vertices) and Q (n vertices) in 3D, in distinct parallel planes, with similar
shapes. Output: a triangulated band whose boundary is exactly P and Q, every triangle having one edge on one contour
and its third vertex (the apex) on the other (the classic *tiling*; no Steiner points in the band). Optionally the two
planar caps, giving a closed solid.

Stage 2 (several contours per plane, branching) is not part of stage 1; its design is section 10.

## 2. Library search (AGENT_WORKFLOW §3.1)

| Need | Building block | Use |
|---|---|---|
| Slope admissibility | `gs.Slope.IsNotSteeper(a, b, c, SlopeSpec)` (`mesh_ops/TerrainSlope.cs`; exact, `ExactSlopePredicates`) | the first admissibility client, wrapped as `ContourLoft.NotSteeperThan(spec)` |
| Caps | `QualityMesh2` with an outer `Mesh2Ring`, the ring edges as locked `Mesh2Segment`s, `Heights` for the dropped coordinate | caps that keep every contour vertex and edge; Steiner points (if refinement is on) get interpolated heights |
| Result checks | `MeshRepairMetrics.HasSelfIntersections` (exact, box tree), `MeshRobustRepair.NotCleanReason` | self-intersection flag of the band; clean check of the closed solid |
| Generators | `TubeGenerator`, `RevolveGenerator` (sweeps of one profile, equal counts) | none fits: they assume one profile, no correspondence search |
| Polygon types | `Polygon2d` (2D only) | the input is `IList<Vector3d>`, since the planes are arbitrary (the invariance harness also tilts) |

No existing loft, tiling or toroidal-graph search in the library.

## 3. Prior art (short check, gaps only)

- **Optimal tiling** (Fuchs, Kedem & Uselton 1977): a tiling is a cycle in a toroidal grid graph that winds once in each
  direction; a cost per triangle; the optimum is the cheapest such cycle. Fixing P_0 to Q_s gives a monotone path in an
  (m+1) x (n+1) grid, solvable by dynamic programming in O(mn); all n starts naively in O(mn^2); with the non-crossing of
  optimal paths, divide and conquer over the starts in O(mn log n).
- **Keppel 1975**: maximum enclosed volume, meant for convex contours. Volume is separable per triangle (§5), so it fits
  the same search.
- **Christiansen & Sederberg 1978**: a greedy step (shorter diagonal) after **normalizing both contours to the unit
  square**; the normalization is their answer to offset or differently sized slices. Greedy is O(m + n) but not optimal
  and gets stuck on dissimilar contours.
- **Gaps found**:
  - *Orientation*: the papers assume both contours run the same way. Here Q is reversed when its (Newell) normal points
    against P's; vertex ids of the output still follow the input order.
  - *Alignment / dissimilar contours*: optimal tilings of offset or dissimilar contours give implausible, possibly
    self-intersecting surfaces (Meyers, Skinner & Sloan 1992; Bajaj, Coyle & Lin 1996 add a medial-axis treatment, stage
    2 material). Stage 1 offers the Christiansen & Sederberg normalization for the built-in costs (option
    `NormalizeForCost`) and **checks the band for self-intersections exactly** (status, never a silent band).

## 4. API (additive only, compatible with 1.0.324)

New file `mesh_generators/ContourLoft.cs`, namespace `g3`; nothing existing changes.

```csharp
public enum ContourLoftCost { MinimumArea, MaximumVolume, Custom }

public enum ContourLoftStatus
{
    Success,            // Mesh is the band (or the closed solid with caps), checked (§7)
    NoAdmissibleBand,   // every tiling has an inadmissible triangle; InadmissibleTriangles = the minimum count
    SelfIntersecting,   // the optimal admissible band intersects itself (exact check)
    CapFailed,          // QualityMesh2 failed, or a cap did not keep the contour's vertices and edges
    InvalidInput,       // < 3 vertices, non-finite, repeated consecutive vertices, zero area, planes not separated
    InvalidCost,        // a custom cost was not finite, or a sum overflowed
    TooLarge            // m * n above MaxCells
}

public struct ContourLoftTriangle
{
    public bool EdgeOnFirst;   // true: edge (First[I], First[I+1]) with apex Second[J]; false: edge (Second[J], Second[J+1]) with apex First[I]
    public int I, J;           // input indices (into First and Second as given, also when Second was reversed)
    public Vector3d A, B, C;   // A, B: the edge, C: the apex
}

public sealed class ContourLoft
{
    public ContourLoftCost Cost = ContourLoftCost.MinimumArea;
    public Func<ContourLoftTriangle, double> CustomCost;          // Cost == Custom; must be finite
    public Func<ContourLoftTriangle, bool> IsAdmissible;          // null: every triangle is admissible
    public bool NormalizeForCost;                                 // Christiansen & Sederberg normalization, built-in costs only
    public bool Caps;                                             // add both caps: a closed solid
    public QualityMesh2 CapMesher;                                // options of the caps; null: plain CDT (MinAngle 0)
    public bool CheckSelfIntersections = true;
    public long MaxCells = 1L << 22;                              // m * n limit (memory, §6)

    public static Func<ContourLoftTriangle, bool> NotSteeperThan(SlopeSpec spec);   // gs.Slope.IsNotSteeper

    public ContourLoftResult Compute(IList<Vector3d> first, IList<Vector3d> second, CancellationToken ct = default);
}

public sealed class ContourLoftResult
{
    public ContourLoftStatus Status;
    public string Reason;                 // a sentence for every status but Success
    public DMesh3 Mesh;                   // only on Success: vertices 0..m-1 = first, m..m+n-1 = second, then cap Steiner points
    public double TotalCost;              // the cost of the band (sum, in the order of the band)
    public int StartOffset;               // the J of the spanning edge (First[0], Second[J]) the band contains
    public int InadmissibleTriangles;     // 0 on Success; the exact minimum on NoAdmissibleBand
}
```

All options are read at the start of `Compute`; nothing is cached between calls (reuse test). No parallel path in stage 1
(the search is a sequential DP; a parallel split over starts is possible later and would need the determinism test).

## 5. Costs

- **MinimumArea** (FKU): area of the triangle, |(B - A) x (C - A)| / 2, computed relative to the centroid of both contours
  (translation invariance up to rounding; 2^k scaling exact).
- **MaximumVolume** (Keppel): the solid bounded by the band and the two planar caps has volume
  V = sum over all faces of the signed tetrahedron (O, a, b, c) for any O. The caps are fixed, so maximizing V means
  minimizing the cost -vol(O, A, B, C) per band triangle, oriented as in the output. O = the centroid. Negative costs are
  fine: the grid graph is acyclic, the DP needs no non-negativity. Meant for convex contours, as in the paper (documented).
- **Custom**: any finite double, may be negative. The delegate must be deterministic (called once per triangle, in a fixed
  order).
- **NormalizeForCost**: the built-in costs are evaluated on copies of the contours, each translated to its vertex centroid
  and scaled by 1 / sqrt(|area|), the second one at unit distance from the first along the normal. The admissibility test
  and the output always use the input positions.

## 6. Search and admissibility

Grid nodes (i, j), i in [0, m], unrolled j in [0, 2n]; node (i, j) is the spanning edge (P_{i mod m}, Q_{j mod n}). Step
down (i, j) -> (i+1, j): triangle P_i P_{i+1} Q_j; step right (i, j) -> (i, j+1): triangle P_i Q_j Q_{j+1}. A band with
start s is a monotone path (0, s) -> (m, s+n). P is the longer contour, so there are n = min(m, n) starts.

**Admissibility is a lexicographic weight**: each step weighs (k, c) with k = 1 for an inadmissible triangle, else 0, and c
its cost; paths compare by (sum k, then sum c), ties by the smaller start and then a fixed step preference. Two
consequences:

1. Every path has a weight, so the subpath optimality behind the non-crossing lemma holds also where no admissible band
   exists (with infinite weights it would not, and the divide and conquer could lose the only admissible band).
2. The integer part is exact: **NoAdmissibleBand is reported iff every tiling contains an inadmissible triangle**, given an
   exact admissibility test (the slope oracle is exact). Its minimum count is reported.

**Divide and conquer over starts** (FKU): solve starts 0 and n (the same band shifted), record each path's column span
[lo(i), hi(i)] per row; a start s between s1 and s2 is solved in the region with row spans [min(lo1, lo2), max(hi1, hi2)]
intersected with [s, s + n]. Shown in the design check: with these spans every row is non-empty and consecutive rows
connect, so the region always contains a path from (0, s) to (m, s+n), also when rounding makes the bounding paths
cross. The search is exact in the count and optimal in the cost up to the rounding of the cost sums (the splice argument
holds up to a few ulps of the sums); the brute-force oracle test compares against all n starts.

**Non-manifold fans** (found while building, by the brute-force oracle): FKU's graph also holds the two families of paths
whose right steps (or down steps) form a single block, `D^i R^n D^(m-i)` and `R^k D^m R^(n-k)`. They fan one vertex over
the whole other contour, use one spanning edge twice, and give a non-manifold mesh. Valid bands are the paths with at
least 3 turns. The DP tracks the first step's direction and the turns (0, 1, 2, then 3 / 4 for any odd / even count of
at least 3, so the parity still gives the last direction): 10 states per node. The non-crossing lemma still holds for the
valid paths: if the right envelope of two crossing valid paths were a fan, one of the two paths would be that fan (the
envelope takes its row-0 or row-m extent, or its full column, from one path), and likewise for the left envelope; so the
exchange argument stays inside the valid set. If a bounded region holds no valid path (only possible through rounding),
the start is solved again in the full region.

**Complexity**: O(mn) cost and admissibility evaluations (precomputed once into two m x n arrays), O(mn log n) node updates
of 10 states each. Memory: 2mn doubles + 2mn flags + 10 (m+1)(n+1) predecessor bytes + 2 (n+1)(m+1) span ints, about 36
bytes per cell: MaxCells = 2^22 (m = n = 2048) is about 150 MB. Above: `TooLarge` (checked in `long` before allocating).

**Upper bound** (estimated before building, AGENT_WORKFLOW §3): m = n = 1000, 2e6 triangle evaluations at 50-100 ns and
1e7 DP updates at ~5 ns, ~0.25 s. **Measured** ([ContourLoftBenchmark](../../benchmarks/ContourLoftBenchmark/README.md)):
0.63 s at 1024 x 1024 and 2.8 s at 2048 x 2048 (the default MaxCells), about 55 ns per m n log2 n for the search (10 states
per node, not 1, is the factor the estimate missed); the exact slope oracle adds about 4 s at 2048 x 2048. The naive
all-starts DP would be O(m n^2): at 2048 x 2048 about 2000 times the measured search.

## 7. Result checks (never a silently wrong band)

On every path to `Success`:
- the start reported is the smallest J with (First[0], Second[J]) an edge of the band, read off the band's steps (also when
  the first contour is the shorter one, which the search uses as its start contour);
- each band triangle is non-degenerate (exact: not all three of its coordinate-plane orientations, `ExactPredicates.Orient2d`
  on xy, yz and zx, are zero), every vertex finite;
- the band is a manifold with exactly two boundary loops, equal to P and Q as given (edge for edge);
- orientation consistent; with caps oriented outward (the signed volume of the closed solid is positive);
- `CheckSelfIntersections`: no intersecting pair of non-adjacent triangles (exact), else `SelfIntersecting`;
- with caps: every contour vertex is kept by its cap at its position and not merged (checked directly); a split contour edge
  shows as an open edge in `MeshRobustRepair.NotCleanReason(mesh)` ("not closed"), which must be `""`, else `CapFailed`.

## 8. Tests (as planned; section 9 lists the additions)

- **Oracle, closed form**: coaxial regular n-gons (same and rotated by half a step, m = n and m = 2n): the optimal band is
  known (the straight / zigzag correspondence); its area by formula.
- **Oracle, brute force**: random star-shaped contours, m, n <= 8: enumerate every tiling (all monotone paths of all
  starts, independent code) and compare the optimal cost (relative 1e-12) and, on inputs without near ties, the band; the
  same with random admissibility masks (minimum inadmissible count exact).
- **Admissibility**: a slope case with an admissible band that the unconstrained min-area band violates (the slope oracle
  picks another); a case with none -> `NoAdmissibleBand` with the exact minimum count; the reported band is checked with
  `Slope.IsNotSteeper` triangle by triangle.
- **Invariance** over `Similarity.All()`: the band's triangles (as index triples) identical, positions mapped back within
  resolution; bit for bit (scaled) at 2^k.
- **Reuse**: compute A then B on one instance, equal to fresh instances.
- **Orientation**: Q given reversed gives the same band (ids mapped).
- **Caps**: closed, manifold, `NotCleanReason == ""`, volume equals the prism / frustum formula for regular polygons.
- **Self-intersection**: two dissimilar contours whose optimal band intersects itself -> `SelfIntersecting`.
- **Invalid input / TooLarge / InvalidCost**, each status once.
- Mutation check of each guard by hand (break it, a test fails).

## 9. Changes while building, and measurements

- **Non-manifold fans** (section 6): the brute-force oracle showed that FKU's graph holds tilings that are not manifold; the
  10-state search excludes them, with the non-crossing argument kept.
- **Power-of-two working frame**: the double geometry (centroids, Newell normals, separation, orientation, the built-in
  costs) runs on the input scaled so that its largest coordinate is in [1, 2); the band is the same for every 2^k scaling
  that keeps the input normal and finite (tested from 2^-1000 to 2^700). The independent review found the band changing at
  2^-30 with `NormalizeForCost` (`Normalized` returns zero for short vectors) and at 2^-268 / 2^256 without it.
- **StartOffset** is read off the band's steps (review: with a shorter first contour it was an index into the wrong contour).
- **Performance**: a per-row reset of the state arrays made the search O(m n^2) (2048 x 2048 did not finish in 10 minutes);
  only the region's columns are reset now, and a work counter (`SearchWork`) is bounded by a test. The inner loop on local,
  column-major arrays and the slope factors computed once (`NotSteeperThan`) took 2048 x 2048 from 5.2 to 2.8 s and, with
  the slope oracle, from 9.9 to 6.9 s, with bit-identical bands.
- **Hand mutants**: 21 mutants of the search, the statuses, the costs, the caps and the guards, all killed by the tests (the
  region-rule mutants only once the full-region fallback was counted: the fallback made them correct but O(m n^2)).

## 10. Stage 2 design (approved by the coordinator 2026-11-19: option A, overlay in core, `BranchFraction` 0.5; built, section 10.9 lists the changes)

### 10.1 Problem

Input: a stack of slices in parallel planes (same normal, increasing offset); each slice has any number of closed contours
(simple, not crossing each other; nesting by containment gives outer contours, holes, islands in holes). Output: one closed
manifold triangle mesh whose cross-sections at the slice planes are the slices, with correspondence by overlap, one contour
splitting into several on the next slice (branch), several joining (merge), contours without a counterpart ending in a planar
cap (appear / vanish), holes lofted like outer contours (tunnels), a torus by slices as the nested case.

### 10.2 Library search (AGENT_WORKFLOW 3.1)

| Need | Building block | Verdict |
|---|---|---|
| 1:1 band | stage 1 search (`ContourLoft.Run`) | reused as the only tiling engine; a new internal entry returns the band's triangles (I, J, side) without the mesh checks |
| Exact 2D overlay of two ring sets | `ConstrainedDelaunayMesh2` (rings, segment `Marker`s, crossings noded exactly, `Subsegment.Markers`, `Triangles`, `Neighbours`) | enough for an overlay: triangles are labelled by a flood fill that toggles at constrained edges (combinatorial, no point-in-polygon in doubles) |
| 2D polygon booleans | `geometry3Sharp.Clipper2` addon (`Clipper2Polygons.Intersection`) | a separate package; rounds to an integer grid; core cannot reference it (see 10.7) |
| Caps | `QualityMesh2` (rings, holes, locked segments), as in stage 1 | reused, one cap per terminal contour with its children as holes |
| Exact orientation, self-intersection | `ExactPredicates.Orient2d`, `MeshRepairMetrics.HasSelfIntersections`, `MeshRobustRepair.NotCleanReason` | reused on the whole mesh |
| 3D Delaunay | `Delaunay3` (box-bounded, exact) | usable for option B (10.7), no constrained / conforming version |
| Medial axis, straight skeleton, offset | none in core (Clipper2 has offsets only) | not used; option C (10.7) |

### 10.3 Prior art (short check)

- **Boissonnat 1988** (Shape reconstruction from planar cross sections, CVGIP 44): 3D Delaunay of the contour points, the
  solid is a set of tetrahedra chosen by the contours; any topology, but the contour edges need not be Delaunay edges (Geiger
  1993 and later add Steiner points), so the boundary is not a tiling of the input contours.
- **Christiansen & Sederberg 1978; Ganapathy & Dennehy 1982; Meyers, Skinner & Sloan 1992 (composite contours, clefts)**: a
  branch is reduced to a 1:1 tiling by joining the several contours of one side with a zero-width bridge into one composite
  contour; the clefts of Meyers et al. repair the flat fin that a bridge leaves.
- **Bajaj, Coyle & Lin 1996**: overlap-based correspondence, branching by an intermediate contour inside the branching region
  (medial axis based, Steiner points), then 1:1 tilings.
- **Barequet & Sharir 1996; Barequet, Goodrich, Levi-Steiner & Steiner 2004** (straight-skeleton contour interpolation): best
  surfaces, needs a straight skeleton of the overlay, none in the library.
- **Zou et al. 2015 / Ekoule et al. 1991**: topology constrained / overlap-graph correspondence.
- Our construction (own composition of known parts, implemented from the papers' ideas, no code read): overlap graph from an exact
  overlay, the **intersection decomposition** of each connected group, bridging inside the intermediate plane, stage 1 for every
  band.

### 10.4 Construction (per slab k -> k+1; the stack is the union of its slabs)

1. **Frame and nesting.** The slices share one normal (the Newell normal of the first contour, flipped consistently; every
   plane must be orthogonal to it within the exact test, else `InvalidInput`). Contours are projected to 2D in a frame from that
   normal. Per slice: exact check that no two contours of the slice touch or cross (`InvalidInput`, status `CrossingContours`);
   nesting tree by containment; depth parity gives the material (even = solid, odd = air).
2. **Faces and overlap graph.** F(c) = interior of c minus the interiors of its direct children. A contour c of slice k and d of
   slice k+1 of equal depth parity are *related* iff F(c) and F(d) overlap with positive area. Decided exactly by one noded CDT
   per slab (both ring sets as marked segments, triangles labelled by flood fill). Connected components of the bipartite relation
   are the **groups** of the slab.
3. **Per group** with p contours below and q above (outer contours and holes in separate groups; the surface of a hole is a
   tunnel wall, lofted like an outer contour):
   - p = 0 or q = 0: terminal. The contour gets a planar **cap** (F(c) as ring + children as holes, through `QualityMesh2` as in
     stage 1) on its own plane. All children of a terminal contour must be terminal on the same side, else
     `UnsupportedNesting` (exotic case, reported, never wrong).
   - p = q = 1: **stage 1 band**, exactly as today (so a stack of single contours equals chained stage 1 calls; test, bit for bit).
   - otherwise: the **intersection decomposition**. Z_ij = components of A_i intersect B_j (from the same overlay; each Z lies
     in exactly one lower and one upper contour because the contours of a slice are disjoint). Intermediate plane at fraction
     t = `BranchFraction` (default 1/2) between the slices. Lower half-slab: each A_i -> the set {Z_ij}; upper half-slab: the set
     {Z_ij}_i -> each B_j. A set of r > 1 contours on one side is joined into **one composite contour** by r - 1 bridges
     (shortest visible vertex pair, visibility tested exactly against all contours of the intermediate slice, ties by index);
     the composite visits both ends of a bridge twice. The band is stage 1 between A_i and the composite; after the band the
     duplicate bridge vertices and the two copies of each bridge edge are identified, which keeps the mesh manifold
     (Euler: each bridge is -2 vertices, -1 edge, so chi = 2 - (p + q) for a genus 0 group; asserted in a test and checked
     on every result).
     Z contours that coincide with their parent B_j (B_j inside A_i) make a vertical prism for the upper half; that case is
     detected exactly and the bridge is made at the top plane instead (no prism, no Z).
4. **Assembly.** One DMesh3: vertex ids 0.. = the contour vertices of all slices in input order, then intermediate (Z) vertices,
   then cap Steiner points. Bands, caps and half-slabs are stitched by vertex ids (the stage 1 band keeps the contour vertices),
   so closure is by construction.
5. **Checks on every Success** (never a silently wrong mesh): closed 2-manifold with consistent orientation
   (`MeshRobustRepair.NotCleanReason == ""`), exact `HasSelfIntersections` over the whole mesh, each slice plane cut by the
   mesh in exactly the slice contours (the vertices on the plane are the slice's, plus bridge edges only at intermediate planes),
   finite. Otherwise a status: `CrossingContours`, `UnsupportedNesting`, `BridgeNotFound`, `BandFailed` (with the stage 1 status
   in the reason), `SelfIntersecting`, `CapFailed`, `InvalidInput`, `InvalidCost`, `TooLarge`.

### 10.5 Guarantees and limits

- **Closed, manifold, consistently oriented**: by construction (bands stitched by shared ids, bridges identified, caps on every
  terminal contour) and checked.
- **No self-intersection between slices**: **checked exactly, not guaranteed** (same as stage 1): bands between dissimilar
  contours can intersect; the answer is then `SelfIntersecting`. The intersection decomposition makes every band run between
  nested, similar contours (Z inside A and B), which is why it is preferred to joining the A side and the B side directly.
- **Deterministic**: serial pipeline, fixed iteration order (slab, group by smallest contour index, contour index), explicit
  tie-breaks (bridge pair, start offset), no hash-order dependence; a test compares repeated runs and the reuse of one instance.
- **Invariance**: Similarity.All(): index triples equal on inputs without near ties; bit for bit for 2^k scales (exact overlay and
  the existing power-of-two frame).
- **Quality**: the saddle is a ridge at the intermediate height (the known look of bridged composite contours); no medial axis.
  Cost 1 slab = the stage 1 cost O(m n log n) per band plus the overlay O(N log N) per slab.
- **Unsupported (reported)**: contours of one slice that touch or cross, a terminal contour with a through child, a bridge
  with no visible vertex pair.

### 10.6 API (additive; nothing existing changes; stage 1 results bit for bit unchanged, tested)

New file `mesh_generators/ContourLoftStack.cs`, namespace `g3`:

```csharp
public sealed class ContourLoftStack
{
    // options forwarded to every band: Cost, CustomCost, IsAdmissible, NormalizeForCost, CheckSelfIntersections, MaxCells
    public double BranchFraction = 0.5;       // intermediate plane of a group with p + q > 2, in (0, 1)
    public bool CapEnds;                      // caps on the first and last slice (a closed solid); internal terminals are always capped
    public QualityMesh2 CapMesher;
    public ContourLoftStackResult Compute(IList<IList<IList<Vector3d>>> slices, CancellationToken ct = default);
}
public enum ContourLoftStackStatus { Success, InvalidInput, CrossingContours, UnsupportedNesting, BridgeNotFound, BandFailed, SelfIntersecting, CapFailed, InvalidCost, TooLarge }
public sealed class ContourLoftStackResult { Status, Reason, Mesh, IReadOnlyList<ContourLoftGroupInfo> Groups /* slab, kind (Band, Branch, Merge, Complex, Cap), contour ids */ }
```

Internal only: `ContourLoft` gets an internal entry that returns the band triangles (I, J, EdgeOnFirst) of a pair of index
contours without building the mesh; `Compute` is unchanged (its tests plus a golden comparison of 27 inputs guard this).

### 10.7 Options for the owner, with the leaning

**How others solve it**: composite contours with bridges (Ganapathy & Dennehy 1982; Meyers et al. 1992), intermediate contours
(Bajaj et al. 1996; Levin 1986), 3D Delaunay (Boissonnat 1988), straight skeleton (Barequet et al. 2004). Terms searched: contour
reconstruction branching, planar cross sections, overlap graph.

| | A: overlay + intersection decomposition + bridges + stage 1 (**leaning**) | B: 3D Delaunay of the slice points (Boissonnat) | C: straight skeleton interpolation |
|---|---|---|---|
| Boundary | exactly the input contours, tilings | contour edges only after Steiner points | exact contours, skeleton Steiner points |
| New code | exact overlay (labelled CDT, boundary tracing), bridging, assembly | conforming Delaunay + inside/outside labelling | straight skeleton of the overlay (none in the library) |
| Branch look | ridge at t, vertical-ish upper part | smooth, may be ragged | best |
| Risk | self-intersection when contours are dissimilar (reported) | boundary conformity, many near-degenerate tets | skeleton robustness |
| Size | L | L+ | XL |

Where A's overlay lives is a second decision: in core with the exact CDT overlay (leaning: exact, deterministic, no extra
package, core stays standalone), or in the Clipper2 addon (ready booleans, rounded to a grid, a second public type in the
addon package, and the stage 1 internals would need `InternalsVisibleTo`).

### 10.8 Tests (planned)

Procedural inputs: two cylinders merging into one (and the reverse), a Y branch (one into three), a torus by slices (nested
contours, tunnel), a block with a hole whose hole vanishes (cap) and one that appears, offset partial overlaps (many to many:
2 below, 2 above, zig-zag overlap), a lone contour vanishing (cone tip) and appearing. Each: closed, manifold, no
self-intersection, volume against the analytic prism / frustum sums where known, Euler characteristic of the group
(2 - p - q), `NotCleanReason == ""`. Plus Similarity.All, reuse, determinism, chained stage 1 equality, a status test per status,
hand mutants of the matching, the overlay labelling, the bridge choice and the identification. Benchmark: stack of 64 slices,
branching every 8 slices, 100 / 1000 vertices per contour, in `benchmarks/ContourLoftBenchmark`.

### 10.9 As built (2026-11-19): changes against the proposal, tests, measurements

- **Files**: `mesh_generators/ContourLoftStack.cs` (public types), `ContourLoftStackRun.cs` (the run); `ContourLoft.ComputeBand`
  (internal) returns the band's triangles only, through the same `Run` (`BandOnly`), so stage 1 `Compute` is untouched. The
  composite contours repeat positions at non-consecutive vertices; the vertex ids of the repeats are the same mesh vertex, which
  is the identification of the bridge vertices and edges (a triangle that does not fit makes `AppendTriangle` fail: `BandFailed`).
- **Statuses**: `UnsupportedNesting` became `Unsupported` (nesting, and an overlap piece that touches itself); `NoAdmissibleBand` is
  its own status (the bands' exact minimum is not summed over bands). Extra checks on every Success: all contour edges are mesh
  edges, no bowtie vertex, consistent orientation, positive volume with `CapEnds`.
- **Scope kept narrower than 10.4**: a group that branches, merges or is complex (p + q > 2) must have contours without holes
  (`Unsupported` otherwise); holes of 1:1 groups, tunnels through many slices and holes that appear or vanish are supported. A
  terminal contour whose child continues is `Unsupported`.
- **Nesting and overlay**: the nesting tree of a slice and the labels of the overlay triangles come from one flood fill each over a
  constrained Delaunay triangulation of the contours as marked segments (a slice with crossings or touching contours is
  `CrossingContours`, detected by the failure of the no-crossing mode, a merged vertex, or a split segment). The overlay's vertices
  are numbered canonically: an input point by its index, a crossing by the sorted markers of its segments, and the loop of a piece
  starts at the smallest such key, so the numbering does not depend on the triangulation (tested under `Similarity.All()`).
- **Pieces equal to a contour**: "the piece is the contour" holds when the contour's vertices are in the loop in their order; the
  loop may hold more vertices (T-junctions of other contours on its edges), so a bar holding two legs that share its edges needs no
  intermediate plane (the torus test: 72 input vertices, 72 in the mesh).
- **Bridges**: besides the shortest visible pair of Prim's tree, a bridge must keep clear of the bridges already placed in the same
  plane (a slice, or the intermediate plane of the slab): without it the bridge of a branch and the bridge of the next merge of a
  torus are the same segment and the mesh has a non-manifold edge (found by the torus test). Visibility is exact (`Orient2d`):
  no touching or overlap with any contour edge of the plane, the midpoint inside the container contour. The direction test at the
  vertex ("in cone") that the design lists is not needed: a segment that leaves into a contour's interior crosses its boundary.
- **Planes**: the stacking normal is the Newell normal of the first contour, signed so that the slices' mean heights increase; a
  slice's height is the mean of its vertices' heights; the intermediate plane is `BranchFraction` between two such heights (its
  vertices are `frame * (x, y, h)`, exact in 2^k scalings through the power-of-two working frame).
- **Ties**: where a piece runs along a contour edge the band has a planar quad (same edge at two heights) whose diagonal is a tie of the
  search, decided by rounding: such inputs are bit for bit invariant under 2^k scalings, and invariant in their numbering, counts,
  areas and volumes (not in the diagonal) under the other similarities. Documented in the invariance test.
- **Tests** (`ContourLoftStackTests`, 38): two cylinders merging and splitting, 1 to 3 and 3 to 1 (bridges leave no non-manifold edge
  or vertex: closed, `NotCleanReason == ""`, every edge in two triangles, every fan one ring), cut pieces with the intermediate plane
  (and `BranchFraction`), a torus by slices (bar, two legs, bar: Euler characteristic 0), a washer (nested tunnels), a hole that
  appears and one that vanishes, a contour that ends / starts between slices, a zig-zag of 2 + 2 contours, two groups in one slab, caps
  with refinement, the chained stage 1 equality (exactly the same triangles as `ContourLoft.Compute` per slab), clockwise input,
  prism volume, every status once (each with a null mesh), `Similarity.All()`, reuse, determinism (repeated and concurrent).
  Hand mutants (killed unless noted): hole orientation, cap orientation, the container test of bridges, the clearance from earlier
  bridges, the parity of related contours, the intermediate-plane shortcut, the crossing detection, the terminal-child check, the
  canonical start of a piece, the pieces' group filter, `BranchFraction`, the end caps, the clean check, cap heights and hole rings.
  Equivalent or defensive survivors: the order tie-break of equal-distance candidates by loop, the contour-edge-in-mesh check.
- **Measured** ([ContourLoftBenchmark](../../benchmarks/ContourLoftBenchmark/README.md), `-- stack`): 33 slices, one and two contours
  alternating every 8 slices: 20 ms per slab at 100 vertices per contour, 190 ms at 400, 1.3 s at 1000 (the stage 1 band of a
  composite of 2n against n vertices dominates); contained and cut pieces cost the same.
