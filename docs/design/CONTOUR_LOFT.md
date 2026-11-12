# ContourLoft, stage 1: a band between two closed contours

Status: approved by the coordinator 2026-11-12; built in #741 (section 9 lists what changed while building and the measurements). Roadmap: [research.md](../roadmap/research.md) item 9; background
[TERRAIN.md](TERRAIN.md) §9.2; sources in [SOURCES.md](../SOURCES.md) (contour lofting row).

## 1. Problem

Input: two closed polygons P (m vertices) and Q (n vertices) in 3D, in distinct parallel planes, with similar
shapes. Output: a triangulated band whose boundary is exactly P and Q, every triangle having one edge on one contour
and its third vertex (the apex) on the other (the classic *tiling*; no Steiner points in the band). Optionally the two
planar caps, giving a closed solid.

Stage 2 (several contours per plane, branching, Clipper2) is not part of this.

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

**Upper bound** (to measure before tuning, AGENT_WORKFLOW §3): m = n = 1000 means 2e6 triangle evaluations (exact slope
test: interval filter, about 50-100 ns each, so ~0.2 s worst case) and 1e6 x log2(1000) = 1e7 DP updates (~0.05 s). The
naive all-starts DP would be 1e9 updates (seconds), which is why the divide and conquer is built from the start. The
benchmark `benchmarks/ContourLoftBenchmark` reports both at n = 250, 1000, 2000.

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
