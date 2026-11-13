# Continuous collision detection: licence, prior art, API, bound plan (ROADMAP Phase 5 priority 2, stage 1)

Status: stage 1 (2026-11-13), approved by the owner the same day (section 9); stage 2 built in #793 (section 10). Tier L
([AGENT_WORKFLOW.md](../AGENT_WORKFLOW.md)): the floating-point bound of section 6 gets an independent review at the
Opus level before the stage-2 code merges (owner 2026-11-08). Roadmap item: [research.md](../roadmap/research.md#open),
priority item 2.

## 0. Result in short

- **Method:** an inclusion-based root finder after Wang et al. 2021 ("Tight Inclusion"), implemented from the paper.
  It is *conservative*: `NoCollision` is a proof that the two primitives stay farther apart than the minimum
  separation over the whole time step; a reported collision may be a false positive, but only if the primitives came
  within a stated distance (section 5.3). It returns a time of impact `toi` with the guarantee "no contact on
  `[0, toi)`", which is what a simulator needs to clamp a step.
- **Licence:** the Tight-Inclusion repository is MIT (checked from its LICENSE file, section 1). We implement from the
  paper and our own derivation of the bound, so nothing goes into SOURCES.md as a port; the MIT code serves at most as a
  black-box reference in a benchmark job.
- **Building blocks** in the library cover most of it (section 3): `ExactInterval` (outward-rounded intervals, the
  correctness fallback and the test oracle for the filter), `ExactDyadic` (exact values for the tests), and
  `TriangleBoxTree` (closed, unpadded box BVH with `Query` and `SelfPairs`) for the broad phase over swept boxes.
- **API** (section 4): additive. `ContinuousCollision.VertexTriangle` / `EdgeEdge` (single queries, result struct,
  options object) and `MeshSweepCollision` (mesh-mesh and self sweep with a broad phase, `Compute()`, status).
- **Floating-point bound** (section 6): one bound, the forward error of the inclusion function evaluated at a box
  corner, `e = c * M * 2^-53 + eta` per axis and query (`M` the largest input coordinate magnitude of the query on that
  axis). Everything else is either exact (dyadic box corners, the domain test) or an argument without rounding (the
  convex-hull property, the earliest-time order). The bound is degree 1 in the coordinates, so 2^k scaling stays bit
  identical.
- **Feasibility** (section 7, a throwaway probe, not committed): with plain-double corners the probe needs
  0.1-0.2 µs per box; random broad-phase candidates take 24-57 boxes (3-8 µs), constructed near-contact queries
  180-360 boxes (23-70 µs). Corners in `ExactInterval` arithmetic cost 5-10x more. So the per-query static filter of
  section 6 is worth its review; the interval path stays as the fallback for inputs outside the filter's range and as
  the oracle.
- **Effort:** stage 2 fits the planned 2-3 days (section 8) if the bound review is booked for day 2.

## 1. Licences (checked 2026-11-13)

| Item | Licence | How checked | Use |
|---|---|---|---|
| Tight-Inclusion (Wang, Ferguson, Schneider, Jiang, Attene, Panozzo), `github.com/Continuous-Collision-Detection/Tight-Inclusion` | MIT, "Copyright (c) 2020 Bolun Wang, Zachary Ferguson, Teseo Schneider, Xin Jiang, Marco Attene, and Daniele Panozzo" | the repository's `LICENSE` on branch `main`, read raw | Not ported. Allowed as a source; optional black-box reference in a benchmark job (built from source on a runner, never vendored) |
| CCD-Wrapper (same authors; wraps several CCD methods for the benchmark) | MIT | `LICENSE` on `master` | Not used. It wraps other methods with their own licences; not read |
| Scalable-CCD (Belgrod, Ferguson et al.) | Apache-2.0 | `LICENSE` on `main` | Not used |
| ipc-toolkit (Ferguson) | MIT | `LICENSE` on `main` | Not used; it calls Tight-Inclusion |
| Sample-Scalable-CCD-Data (sample of the ToI dataset of Belgrod et al.) | Apache-2.0 | `LICENSE` on `main` | Candidate for the benchmark, fetched at run time only (section 7.3) |
| The CCD benchmark of Wang et al. 2021 (NYU Faculty Digital Archive, `archive.nyu.edu/handle/2451/61518`) and the full ToI dataset (`hdl.handle.net/2451/74508`) | **not verified**: the archive host is blocked from the cloud container (proxy 403), and the project pages name only the code licence | — | Fetched at run time only, and only after the licence is confirmed (open item, section 9) |

No GPL source is involved. CGAL has no CCD package used here; the root-finding literature below is papers only.

## 2. Prior art ("how others solve it")

Searched for: continuous collision detection vertex-face / edge-edge, conservative / exact CCD, inclusion functions,
Bernstein sign classification, root parity, minimum separation, .NET physics CCD. Citation data from the publishers'
or authors' pages; the classification of the methods' correctness follows the summary in Wang et al. 2021 and is to
be re-checked against its full text before the stage-2 brief.

| Approach | Source | Exact / conservative? | For us |
|---|---|---|---|
| Cubic coplanarity: solve the cubic in t for the coplanarity of the four points, then an inside / overlap test at each root | Provot 1997 (Graphics Interface); Bridson, Fedkiw, Anderson, *Robust treatment of collisions, contact and friction for cloth animation*, SIGGRAPH 2002 | Neither: floating-point roots, tolerance-based; false negatives in near-degenerate cases | No |
| Interval root finding over (t, u, v) | Snyder 1992, *Interval analysis for computer graphics*, SIGGRAPH 1992; Redon, Kheddar, Coquillart, *Fast continuous collision detection between rigid bodies*, Eurographics 2002 | Conservative with outward rounding; many false positives / slow for wide intervals | The ancestor of Tight Inclusion; our fallback path is exactly this with `ExactInterval` |
| Root parity on space-time bilinear patches with exact orientation predicates | Brochu, Edwards, Bridson, *Efficient geometrically exact continuous collision detection*, ACM TOG 31(4), 2012, doi:10.1145/2185520.2185592 | Exact yes / no for zero separation, no time of impact, no minimum separation; Wang et al. report failures on degenerate configurations of their benchmark | No: we need a `toi` and a minimum separation |
| Bernstein sign classification of the coplanarity and inside polynomials | Tang, Tong, Wang, Manocha, *Fast and exact continuous collision detection with Bernstein sign classification*, ACM TOG 33(6) (SIGGRAPH Asia 2014), doi:10.1145/2661229.2661237; *TightCCD*, Wang, Tang, Tong, Computer Graphics Forum 2015, doi:10.1111/cgf.12767 (error bounds) | Exactness claimed under conditions; Wang et al. report false negatives on their benchmark | No |
| **Inclusion-based root finding** with a floating-point filter and minimum separation | **Wang, Ferguson, Schneider, Jiang, Attene, Panozzo, *A Large-scale Benchmark and an Inclusion-based Algorithm for Continuous Collision Detection*, ACM TOG 40(5), art. 188, 2021, doi:10.1145/3460775** (arXiv:2009.13349) | Conservative: no false negatives on the benchmark, few false positives, a guarantee on the distance of any false positive | **The method we build** |
| Scalable CCD: broad + narrow phase on the GPU / many cores, ToI ground-truth dataset | Belgrod, Ferguson, Schneider, Zorin, Panozzo, *Time of Impact Dataset for Continuous Collision Detection and a Scalable Conservative Algorithm*, arXiv:2112.06300 | Conservative (same narrow-phase idea) | Data for the benchmark (sample is Apache-2.0); the GPU part is out of scope |
| Minimum separation as a requirement of the caller | Li et al., *Incremental Potential Contact*, ACM TOG 39(4), 2020, doi:10.1145/3386569.3392425 | — | Why `MinSeparation` is an option: IPC-style solvers need `toi` with a positive separation |

**.NET:** BEPUphysics v2 (Apache-2.0) has continuous detection for rigid bodies: speculative contacts plus an opt-in
sweep test that estimates the time of impact in floating point, tuned by a minimum sweep time step; no conservative
guarantee and no per-primitive vertex-face / edge-edge queries on deforming meshes. Jitter Physics and the engines
(Unity's PhysX CCD, closed or C++) are of the same kind. Nothing found in .NET with a no-false-negative guarantee
(terms: "continuous collision detection C#", "time of impact .NET", "conservative CCD NuGet").

### 2.1 The method in brief (from the paper, in our words)

Primitives move linearly in time: every vertex `x_i(t) = x_i0 + t (x_i1 - x_i0)`, `t` in `[0, 1]`.

- Vertex-triangle: `F(t, u, v) = p(t) - ((1 - u - v) a(t) + u b(t) + v c(t))` over `t` in `[0,1]`, `u, v >= 0`,
  `u + v <= 1`.
- Edge-edge: `F(t, u, v) = (a(t) + u (b(t) - a(t))) - (c(t) + v (d(t) - c(t)))` over `[0, 1]^3`.

A contact (distance <= `ms`) at `(t, u, v)` means `|F| <= ms`. `F` is affine in each of `t`, `u`, `v` separately (the
only products are `t u` and `t v`), so the image of a parameter box lies in the convex hull of the images of its eight
corners, and the axis-aligned box of the eight corner values per coordinate is an inclusion of `F(box)`. The solver
bisects parameter boxes, discards a box when some coordinate's corner range lies entirely above `ms + e` or below
`-(ms + e)` (`e` the evaluation error), and stops when the earliest surviving box has an image narrower than the
tolerance `delta` in every coordinate; its lower time is the `toi`.

## 3. Building blocks already in the library (searched first, AGENT_WORKFLOW section 3)

Searched: `Interval`, `Exact*`, `AdaptivePredicates`, the AABB trees and box trees, `MeshBatchQueries`, "swept",
"time of impact", "ccd" (no hit for the last three outside unrelated names).

| Need | Existing piece | Fit |
|---|---|---|
| Outward-rounded evaluation (fallback path, test oracle) | `math/ExactIntervals.cs`: `ExactInterval` (each `+ - *` rounds to nearest and widens by one ulp, NaN gives the whole line, `Sign()`) | Direct. Internal, same assembly |
| Exact values of `F` at dyadic points (oracle in tests) | `ExactDyadic` (same file, `BigInteger` mantissa, exact `+ - *`, `Compare`) | Direct; test-only use |
| Broad phase over swept boxes | `TriangleBoxTree` (`mesh_ops/ExactMeshArrangement.cs`): a BVH over arbitrary `AxisAlignedBox3d`, closed overlap tests, no padding, `Query(box, ...)`, `SelfPairs`, parallel build from 262144 boxes, deterministic | Direct, over the swept boxes of vertices, edges and triangles. Used as is: the file is on the locked list of Exact Boolean 3.0, so stage 2 does not edit or move it |
| Mesh-level trees | `DMeshAABBTree3` | No: it indexes triangles at one position; swept boxes differ |
| Exact orientation predicates | `ExactPredicates.Orient3d`, `AdaptivePredicates` | Not needed by the method; used in tests to build exact contact configurations (eg a vertex path that hits a triangle edge exactly) |
| Mechanical bound derivation | `tools/PredicateCompiler` (magnitude bounds of fixed-width predicates; its README plans a floating-point filter pass) | Option b of section 6.4 |
| Batch pattern (parallel over queries, results by index, deterministic) | `spatial/MeshBatchQueries.cs`, `gParallel.ForRange` | The sweep's narrow phase follows it |
| Invariance / determinism harness | `tests/.../Harness/Invariance.cs` (`Similarity.All()`) | Section 6.5 |

## 4. API proposal (additive to the 1.0.324 API)

New public types only, in namespace `g3`; files `intersection/ContinuousCollision.cs` (queries, options, result) and
`spatial/MeshSweepCollision.cs` (sweep). No existing signature changes, so `dotnet pack` API validation needs no
suppression.

```csharp
public enum CcdStatus
{
    NoCollision,      // proven: distance > MinSeparation for all t in [0, TimeLimit]
    Collision,        // possible contact; TimeOfImpact is a lower bound on the first contact (section 5.3)
    IterationLimit,   // MaxIterations reached: treated as Collision at TimeOfImpact (conservative)
    InvalidInput      // non-finite coordinates or options; nothing proven
}

public sealed class CcdOptions
{
    public double MinSeparation = 0;           // absolute distance, >= 0
    public double Tolerance = double.NaN;      // absolute width target of the image; NaN: RelativeTolerance * extent
    public double RelativeTolerance = 1e-6;    // of the query's (or, in the sweep, the scene's) largest box extent
    public int MaxIterations = 1_000_000;      // boxes per query; <= 0: no limit
    public double TimeLimit = 1;               // prove [0, TimeLimit] only (0 <= TimeLimit <= 1); a stop rule, section 5.1
}

public struct CcdResult
{
    public CcdStatus Status;
    public double TimeOfImpact;      // no contact on [0, TimeOfImpact); +infinity for NoCollision
    public double ToleranceReached;  // upper bound of the reported box's image width (<= Tolerance unless IterationLimit)
    public double ErrorBound;        // the e of section 6 that was used (max over the axes)
    public int Iterations;           // boxes evaluated
}

public static class ContinuousCollision
{
    // vertex p against triangle (a, b, c); positions at t = 0 and t = 1, linear in between
    public static CcdResult VertexTriangle(Vector3d p0, Vector3d a0, Vector3d b0, Vector3d c0,
                                           Vector3d p1, Vector3d a1, Vector3d b1, Vector3d c1,
                                           CcdOptions options = null);
    // edge (a, b) against edge (c, d)
    public static CcdResult EdgeEdge(Vector3d a0, Vector3d b0, Vector3d c0, Vector3d d0,
                                     Vector3d a1, Vector3d b1, Vector3d c1, Vector3d d1,
                                     CcdOptions options = null);
}

public enum MeshSweepStatus { NoCollision, Collision, IterationLimit, InvalidInput }
public enum SweepContactKind { VertexTriangle, EdgeEdge }

public struct SweepContact
{
    public SweepContactKind Kind;
    public int First, Second;   // VertexTriangle: vertex id, triangle id; EdgeEdge: edge ids
    public bool FirstOfMeshA;   // which mesh First belongs to (always true for a self sweep)
    public CcdResult Result;
}

public class MeshSweepCollision
{
    public MeshSweepCollision(DMesh3 mesh, Vector3d[] endPositions);                       // self sweep
    public MeshSweepCollision(DMesh3 meshA, Vector3d[] endA, DMesh3 meshB, Vector3d[] endB); // A against B
    public CcdOptions Options = new CcdOptions();
    public bool Parallel = true;
    public bool CollectAll = false;      // false: the contacts at the earliest time only; true: every candidate pair's result
    public MeshSweepStatus Status;
    public double EarliestTimeOfImpact;  // min over all pairs; +infinity if none
    public List<SweepContact> Contacts;  // sorted by (TimeOfImpact, Kind, FirstOfMeshA, First, Second)
    public int CandidatePairs;           // after the broad phase
    public MeshSweepStatus Compute();    // may be called again (eg after changing Options or endPositions)
}
```

Notes:
- Start positions are the meshes' vertices; `endPositions` is indexed by vertex id (length >= `MaxVertexID`). A rigid
  motion with rotation is *not* linear per vertex; the caller linearises it (non-goal, section 8).
- Self sweep skips pairs that share a vertex (a vertex against its incident triangles, adjacent edges): with
  `MinSeparation > 0` they would always "collide". Documented.
- `Tolerance` and `MinSeparation` are absolute lengths. The default `Tolerance` is relative to the extent, so a 2^k
  scaled input with scaled options gives the scaled result bit for bit (section 6.5).
- The sweep finds the earliest time with a shared bound: each pair query stops when its next box starts *after* the
  best `toi` found so far (strictly after, so ties are still found). The earliest time and the sorted contact list are
  then independent of thread timing (a query's pop sequence up to the bound is the same prefix as without it; ties are
  found by every tied pair). `CollectAll = true` disables the bound.
- CLI / MCP: a `ccd` command (two meshes and their end positions, prints the earliest `toi` and contacts) goes into
  the CLI / MCP update, designed alongside as the roadmap asks; not in stage 2.

## 5. Algorithm as it will be built

### 5.1 Narrow phase (per query)

1. Validate: all 24 coordinates finite, `MinSeparation >= 0` and finite, `0 <= TimeLimit <= 1`; else `InvalidInput`.
2. Per axis compute the filter bound `e_axis` (section 6) once. If `M_axis` exceeds the filter's range
   (`2^500`, so no product overflows), the query uses the `ExactInterval` path for that axis instead.
3. A priority queue of parameter boxes keyed by `(t_lo, depth, insertion index)` (a total order: deterministic). Start
   with `[0,1]^3` (not `[0, TimeLimit]`, so that every corner stays an exact dyadic, B2); a popped box with
   `t_lo > TimeLimit` ends the search with `NoCollision` (no contact on `[0, TimeLimit]`).
4. Pop the first box. Vertex-triangle: skip it if `u_lo + v_lo > 1` (exact: both are dyadics with few bits). Per axis:
   evaluate `F` at the 8 corners (hoisted: the four points at `t_lo` and `t_hi` once, then 4 `(u, v)` corners each),
   take min and max; discard the box if `min > ms + e` or `max < -(ms + e)` on some axis (the sums `ms + e` rounded up).
5. If not discarded and `max - min` (rounded up) `<= delta` on every axis: return `Collision`, `toi = t_lo`.
6. Else split the parameter whose interval contributes most to the image width (the paper's heuristic; ours: the
   largest corner-value difference along that parameter's box edges, ties `t` before `u` before `v`), at the exact
   midpoint, and push both halves. A parameter interval is not split below 2^-52 of its range (the midpoint would not be
   exact); a box with all three at that size is treated as in step 5 (`Collision`, honest `ToleranceReached`).
7. Queue empty: `NoCollision`. Iteration limit: `IterationLimit`, `toi = t_lo` of the popped box.

### 5.2 Broad phase (sweep)

Swept box of a vertex = box of its start and end position; of an edge / triangle = box of its 4 / 6 positions. Linear
motion keeps each point on the segment between its positions, so the primitive stays in the convex hull of those
points, which lies in the box. Each box is expanded by `ms` with the bounds rounded outward (`ExactInterval.Up/Down`),
so pairs closer than `ms` overlap. `TriangleBoxTree` over the triangle boxes of B, queried with the vertex boxes of A
(and vice versa), and over the edge boxes of B queried with A's edge boxes; self sweep: `SelfPairs` on the edge tree and
the vertex-in-triangle queries on one tree, incident pairs skipped. The tree's tests are closed and unpadded, so no
overlapping pair is missed. No floating-point bound beyond the outward rounding.

### 5.3 Guarantees (what the review checks, section 6.3)

- **G1 (no false negatives):** `NoCollision` implies `|F(t,u,v)|_inf > ms` for every `(t,u,v)` of the domain, hence
  Euclidean distance `> ms` for all `t` in `[0, TimeLimit]`. Reason: every discarded box has, on some axis, all exact
  corner values beyond `ms` (the computed ones are within `e`), and the box's image lies in the corners' convex hull.
- **G2 (earliest time):** for `Collision` or `IterationLimit` at `toi`: no contact on `[0, toi)`. Reason: the boxes
  form a partition of the domain; the popped box has the smallest `t_lo` of all surviving boxes; every point with
  `t < toi` lies in a discarded box.
- **G3 (false positives are near):** for `Collision`, every point of the reported box has `|F_axis| <= ms + 2e + delta`
  on each axis, so the primitives come within `sqrt(3) (ms + 2e + delta)` at some `t` in `[toi, toi + width_t]`.
  (`IterationLimit` gives no such bound and says so.)
- **G4 (determinism):** single queries are serial and use no FMA; the sweep's reduction is a min and a sort.

## 6. The floating-point bound plan

### 6.1 Which bounds the method needs

| # | Quantity | Kind | How it is made safe |
|---|---|---|---|
| B1 | Error of a corner value `fl(F(t,u,v))` against the exact `F` at the same exact dyadic corner, per axis | **the one hand-derived floating-point bound** | `e = c * M * 2^-53 + eta`, derived in 6.2, reviewed, tested in 6.5 |
| B2 | Corners are exact | exactness | `t, u, v` are dyadic midpoints of `[0,1]`; `TimeLimit` is only a stop rule (`t_lo > TimeLimit`), never a bisection end point, since `TimeLimit * s` would be rounded; depth <= 52 per parameter; `u_lo + v_lo` exact |
| B3 | Image of a box within the hull of the corner images | math, no rounding | Multilinearity in `t`, `u`, `v` separately (the function is a sum of terms `t^i u^j v^k` with exponents 0 or 1); for vertex-triangle the box may stick out of the triangle `u + v <= 1`: the hull still contains the image of the part inside |
| B4 | The comparisons `min > ms + e`, `max - min <= delta` | rounding of two operations | `ms + e` rounded up, `max - min` rounded up (`ExactInterval.Up`), or the comparison done as `min - e > ms` with both sides' rounding directions argued; the reviewer checks the direction |
| B5 | Swept boxes expanded by `ms` | rounding of one addition per bound | outward rounding |
| B6 | Default tolerance `RelativeTolerance * extent` | no correctness role | only steers precision; any value is safe |

### 6.2 Derivation sketch of B1 (to be completed in stage 2)

Evaluation order per axis (vertex-triangle; edge-edge analogous with `e0 = a - c`, `e1 = b - a`, `e2 = d - c` and
`F = (e0 + u e1) - v e2`):

```
d_i  = fl(x_i1 - x_i0)                  // once per query, i = p, a, b, c
x_i  = fl(x_i0 + fl(t * d_i))           // at t_lo and t_hi
e0 = fl(x_p - x_a); e1 = fl(x_b - x_a); e2 = fl(x_c - x_a)
F  = fl(fl(e0 - fl(u * e1)) - fl(v * e2))
```

With `u = 2^-53`, `|t|, |u_par|, |v_par| <= 1` and `M = max_i max(|x_i0|, |x_i1|)` on the axis, the standard model
`fl(a op b) = (a op b)(1 + d)`, `|d| <= u`, gives `|x_i - exact| <= ~4 M u`, `|e_k - exact| <= ~10 M u`, and a total
of a few dozen `M u` (a first count gives `c <= 64` including the second-order terms; the review checks the exact
constant). Subnormal results add an absolute `2^-1075` per operation: `eta = 16 * 2^-1074` covers the 11 operations
with margin. Overflow is excluded by the range check `M <= 2^500`.

Properties the reviewer checks:
- the bound is homogeneous of degree 1 in the coordinates (scale 2^k gives `e` times 2^k exactly, as long as `eta`
  is not the dominant term: at very small scales the comparison against `eta` can differ, which is why the invariance
  test's 2^-10 scale keeps coordinates far above the subnormal range);
- it uses the absolute magnitude `M`, not the size of the primitives: at UTM coordinates (`M` = 6e6) `e` is about 5e-8
  m against a `delta` of 1e-6 times a metre-size scene: conservative but usable; documented as the cost of absolute
  coordinates (a caller can translate first);
- no FMA: the JIT does not contract `a + t * d` in .NET, and the exact-code analyzers (G3E0001-3) are extended to
  `intersection/ContinuousCollision.cs` by adding it to the exact file list in `.editorconfig` (no FMA, no `float`,
  no parallel loops in the narrow phase);
- the hoisting (points at `t_lo` / `t_hi` computed once per box) is the evaluation order the bound was derived for.

The paper derives its own filter constants for its own evaluation order; we do not take them over. Our constant follows
from our expression, and the comparison with the paper's published values is only a plausibility check for the reviewer.

### 6.3 What goes to the bound review

One review of the stage-2 PR, by a reviewer that gets the code and this section, not the author's working:
1. B1: the constant `c` and `eta` for both evaluation orders, against the code as written (operation by operation).
2. B2-B5: exactness of the corners (midpoints, depth limit, the `TimeLimit` stop rule), the hull argument for the
   triangle domain, the rounding direction of every comparison, the outward rounding of the broad-phase boxes.
3. G1-G3 of section 5.3 as stated in the XML docs, including the `IterationLimit` and the "smallest box" cases.
4. The range switch to the `ExactInterval` path (`M > 2^500`, non-finite intermediate) never returns `NoCollision`
   without a proof.
Findings go into the same PR (REVIEW_CHECKLIST), as for the exact arithmetic.

### 6.4 How the constant is derived: options

- **(a) by hand (leaning):** the expression has 11 operations; a hand derivation with the standard model plus the
  oracle tests of 6.5 is small enough for the 2-3 day budget.
- **(b) mechanically:** add a forward-error pass to `tools/PredicateCompiler` (its README foresees one) and generate the
  constant, with a generator test as for `GeneratedPredicatesTests`. Less review risk, more work; worth it only when a
  second filter (eg a curved-trajectory or rigid-motion CCD) follows.
- Fallback if the review does not close: ship the `ExactInterval` path only (correct by construction, 5-10x slower
  per box, section 7) and add the filter later.

### 6.5 Tests (each bound and guarantee)

- **B1 oracle test:** random and adversarial queries (cancellation at UTM offsets, mixed magnitudes, tiny motions with
  subnormal products, `M` near `2^500`): `|fl(F) - F_exact| <= e` at random dyadic corners, with `F_exact` from
  `ExactDyadic`. It also reports the largest observed ratio (expected far below 1; a constant 10x above the observed worst
  is a sign of a loose derivation, not an error).
- **Mutants that must die:** dropping `eta` (subnormal case), `M` taken from one point only, `e = 0`, `>` vs `>=` in the
  discard test, the domain test `u_lo + v_lo > 1` dropped or with `>=`, the queue keyed by depth only (G2), midpoint not
  exact (eg `lo + 0.5 * w` with a rounded `w`), the broad-phase box not expanded by `ms`.
- **G1 end-to-end, one-sided oracle:** for each query, sample dyadic `(t, u, v)` densely and evaluate `F` exactly; if
  some sample has `|F|_inf <= ms`, the result must not be `NoCollision` and `toi <= t_sample`.
- **Constructed exact contacts:** a vertex crossing the triangle interior at a dyadic time, exactly through an edge, a
  vertex of the triangle; parallel and collinear edges; coplanar sliding; contact at `t = 0` and `t = 1`; zero motion;
  degenerate triangles and zero-length edges; near misses at `ms + small`. Each with the expected status and, where
  exact, `toi` within `[t* - width, t*]`.
- **G3:** for every `Collision`, `ExactDyadic` at the reported box's corners confirms `|F_axis| <= ms + 2e + delta`.
- **Invariance (`Similarity.All()`):** 2^k scales with scaled options give the same status, `toi` and iterations bit
  for bit; the other similarities (rotations, UTM) agree in status wherever the configuration is separated by more than
  `sqrt(3)(ms + 2e + delta)` or penetrates by more, and their `toi` agree within the reported widths.
- **Sweep:** reuse (`Compute()` twice, also after changing `endPositions`), determinism (parallel equals serial,
  repeated runs equal, `CollectAll` on and off give the same earliest time), broad phase against a brute-force pair
  loop on small procedural meshes (same candidate set), invariance as above. Success paths check the contacts (kinds,
  ids, times), not only the status.
- **Test time:** procedural inputs, all under 5 s; the benchmark data run is a benchmark (section 7.3), not a test.

## 7. Upper bound / feasibility check

### 7.1 Probe

A throwaway C# console probe (scratchpad, not committed; .NET 10, Release, one thread of a 4-core Xeon @ 2.1 GHz
container): the algorithm of 5.1 with a crude split rule (widest parameter interval), corners hoisted, `ms = 0`,
`delta = 1e-6`, 20000 queries per row; "double" evaluates the corners without any filter (an upper bound on speed,
*not* conservative), "interval" with an `ExactInterval`-equivalent struct.

| Queries | Kind | double | interval | boxes / query | hits |
|---|---|---|---|---|---|
| near-contact (constructed: the vertex / edge passes through the other primitive's neighbourhood) | VF | 59-71 µs | 298-334 µs | 347-357 | 52 % |
| | EE | 23-29 µs | 245-275 µs | 183-184 | 41 % |
| random candidates (unit-size primitives, small motions, swept boxes overlapping) | VF | 3.3-3.7 µs | 21-22 µs | 24-26 | 0.5 % |
| | EE | 6.5-7.5 µs | 43-49 µs | 57 | 1.3 % |

(Ranges over the coordinate offsets 0 and 632117.25, the UTM offset; the offset changes nothing measurable.)

Reading:
- Per box the double path costs 0.12-0.2 µs (24 corner values plus the priority queue), the interval path 5-10x more.
  The static filter costs one comparison more than the "double" row, so the double row is the expected speed of stage 2.
- The near-contact rows are the hard end; the paper's split heuristic (by image widths) should need fewer boxes than
  the crude rule here. Not measured against Tight-Inclusion itself (that needs its C++ build on a runner, optional,
  section 8).
- A sweep's cost is roughly (candidate pairs) x (a few µs): eg 10^5 candidate pairs of a deforming 10k-triangle mesh
  would be about 0.3-0.7 s serial with the filter, 2-5 s with intervals, divided by the core count in parallel. The
  broad phase is the box tree whose query throughput is known from the exact booleans (millions of box queries per
  second).
- Conclusion: feasible; the filter is worth its review; the interval path is an acceptable fallback, not the default.

### 7.2 What the probe does not show

False-positive rates and robustness on degenerate cases: these need the benchmark data (7.3) and the tests of 6.5.

### 7.3 Benchmark data (run time only)

`benchmarks/CcdBenchmark` (stage 2) fetches the query sets at run time into a temporary directory, never into the
repository: the Apache-2.0 sample of the ToI dataset (GitHub, `Sample-Scalable-CCD-Data`: queries with exact ground
truth per simulation step), and, once its licence is confirmed, the benchmark of Wang et al. 2021 (NYU archive). The
benchmark reports false negatives (must be 0), false positives, average and maximum time and boxes per query, double
filter vs interval path. The fetch script pins the source URL and SHA-256 of each file.

## 8. Plan for stage 2 (2-3 days, one PR)

Commits, each green:
1. `ContinuousCollision` with the `ExactInterval` path only (B2-B5, G1-G3), the tests of 6.5 except B1.
2. The static filter (B1) with its oracle test and mutants; the interval path stays for the range switch.
3. `MeshSweepCollision` with the broad phase, the sweep tests.
4. `benchmarks/CcdBenchmark` with the run-time fetch, numbers in its README; SOURCES.md entry (papers, design
   references).
Review: the bound review of 6.3 on commit 2 (day 2), the general review of the PR (REVIEW_CHECKLIST) before ready. The
new files are not in a mutation shard yet; the stage-2 PR proposes adding them to the shard of the predicates (a new
shard needs a local Stryker run to set its threshold).

Non-goals: rigid or curved trajectories (Ferguson et al. 2021, *Intersection-free rigid body dynamics*, need another
inclusion function), collision response, an exact true-positive certificate, the GPU / SIMD batch of Scalable CCD,
point-edge / point-point queries as separate APIs (they are limit cases of the two queries).

## 9. Decisions (owner, 2026-11-13)

1. Method, the API of section 4 and the stage-2 plan of section 8: go.
2. The bound B1 is derived by hand (option a of 6.4), with the bound review of 6.3, which the coordinator routes when
   the stage-2 PR is ready.
3. Until the licence of the Wang et al. data on the NYU archive is confirmed, only the Apache-2.0 sample of the ToI
   dataset is fetched, at run time, by the benchmark only (never committed; PR-tier tests never fetch).
4. Tight-Inclusion as a black-box reference on a runner: later, not now (runner backlog).

## 10. As built (stage 2, 2026-11-13)

Files: `intersection/ContinuousCollision.cs` (`CcdStatus`, `CcdOptions`, `CcdResult`, `ContinuousCollision`, the internal
`CcdSolver`; on the exact-code list of `.editorconfig`: no FMA, no `float`, no parallel loops), `spatial/MeshSweepCollision.cs`
(`MeshSweepStatus`, `SweepContactKind`, `SweepContact`, `MeshSweepCollision`), tests `ContinuousCollisionTests`,
`MeshSweepCollisionTests`, benchmark `benchmarks/CcdBenchmark`.

### 10.1 Differences from sections 4 and 5

- **Search order: dive, then tighten.** The pure start-time order of 5.1 is a breadth-first search over (u, v) at each start
  time: it explodes when the contact set has extent (a minimum separation, parallel edges, a degenerate triangle; 10^6 boxes
  in the first tests). As built: boxes are taken by start time; the first box that is not discarded starts a dive
  (depth-first, earlier half first) for a *near box* (image width <= delta); then only the boxes that start more than
  `TimeTolerance` before it are refined, those that reach past that time split in t. G2 rests on the partition, not on the
  order: discarded boxes, the queue and the near box always partition the domain, so `TimeOfImpact` = the smallest start of
  the queue's boxes and the near box is a lower bound of the first contact in every case (also when a budget stops early).
- **New fields:** `CcdOptions.TimeTolerance` (1e-6), `CcdOptions.MaxTighteningIterations` (1024: boxes after the near box;
  the earliest sweep time on the benchmark is the same as with 4096 at 0.7x the time), `CcdResult.NearContactTime` (the near
  box's start: its corner (t0, u0, v0) lies in the domain, so the primitives are within
  `sqrt(3) (ms + 2 e + ToleranceReached)` at that time, G3). `Collision` now means "a near contact at NearContactTime";
  `IterationLimit` means no near box was found within `MaxIterations`.
- **Vertex-triangle boxes are clipped to the triangle:** corners outside u + v <= 1 are left out of the inclusion and the two
  points where u + v = 1 crosses the box boundary are added (exact: 1 - u, 1 - v are multiples of 2^-53). F is affine in
  (u, v) for fixed t and affine in t for fixed (u, v), so F(box ∩ triangle) lies in the hull of F at the clipped polygon's
  vertices at t0 and t1. Without it the extrapolated corners made a contact on the hypotenuse look earlier (a box-face
  approach with a minimum separation: 10^5 boxes without reaching the tolerance; 197 with it).
- **Separating directions:** a box no axis separates is also tested along the triangle normal (b - a) x (c - a) or the
  edges' cross product (b - a) x (d - c) at t0 and t1. For any n, n . y <= ms |n|_1 on the cube [-ms, ms]^3, and n . F is
  linear, so n . F > ms |n|_1 at every hull vertex (or < -ms |n|_1 at every one) separates. n needs no accuracy; the dot
  products are evaluated in `ExactInterval` over the corner values' enclosures ([F - e, F + e] on the filter path), so no
  new error bound. Near-contact queries need 2.3x fewer boxes.
- **No shared bound across the sweep's threads** (4, notes): with the dive, a pair's result under a bound depends on the
  bound's value at the time, so the reported times would depend on the schedule. Each pair is solved independently;
  `EarliestTimeOfImpact` is the min of the pairs' times, `Contacts` (CollectAll false) the pairs with
  `TimeOfImpact <= EarliestNearContactTime`. Also new: `EarliestNearContactTime`, `SetEndPositions`.
- The zero values of the status enums prove nothing: `CcdStatus.InvalidInput` is first (a `default(CcdResult)`), and
  `MeshSweepStatus.NotComputed` is new (a sweep before `Compute`). `SetEndPositions(endA)` keeps B's end positions.
- Review fixes (code review of #793): an iteration limit whose remaining boxes all start after `TimeLimit` returns
  `NoCollision` (it had returned `IterationLimit` with a time after `TimeLimit`, which the sweep took as a proof up to
  that time); the broad phase's half separation is rounded up (`0.5 ms` underflows for `ms = 2^-1074`); a constant image
  has width 0 (so `Tolerance = 0` is met).

### 10.2 Bound B1 as derived (for the review)

Per axis, `u = 2^-53`, `M` the largest magnitude of the axis' 8 input coordinates, `|t|, |u_p|, |v_p| <= 1` exact, every
operation `fl(a op b) = (a op b)(1 + d) + eta_op`, `|d| <= u`, `eta = 0` for `+ -` (exact when subnormal) and `|eta| <= 2^-1075`
for `*`. Exact values with a star.

| step | operation | magnitude of the exact value | error bound (after the step) |
|---|---|---|---|
| 1 | `d = fl(x1 - x0)` | `abs(D*) <= 2M` | `2Mu` |
| 2 | `m = fl(t d)` | `abs(t D*) <= 2M` | `2Mu + 2M(1 + u)u + eta` |
| 3 | `X = fl(x0 + m)` | `abs(X*) <= M` (a convex combination) | `6Mu + 2 eta` |
| 4 | `e_k = fl(X_i - X_j)` | `abs(E*) <= 2M` | `15Mu + 5 eta` |
| 5 | `w = fl(u_p e_k)` | `<= 2M` | `18Mu + 7 eta` |
| 6 | `s = fl(e0 -/+ w1)` | partial sum `<= 4M` (each term `<= 2M`) | `38Mu + 13 eta` |
| 7 | `F = fl(s - w2)` | `abs(F*) <= 6M` | `62Mu + 21 eta` |

Each bound adds the propagated errors of the operands and `u` times the computed magnitude (exact magnitude plus the
operands' errors), rounded up to whole multiples of `Mu` and `eta` (the second-order terms are below `Mu` for `u = 2^-53`).
Used: `e = Up(M 2^-47 + 16 2^-1074)` (`64 M u`; `16 * 2^-1074 = 32 * 2^-1075 >= 21 eta`), threshold `Up(ms + e)`. Inputs with
`M > 2^500` take the interval path; above `2^1000` they are `InvalidInput` (10.7, S2). The crossing points of 10.1 use the same order
of operations with u_p, v_p in [0, 1]. Observed worst error / e on the adversarial test set: above 1/16 (a constant 16
times smaller fails the test; 4 times smaller passes).

### 10.3 What goes to the bound review (6.3, updated)

1. B1 as in 10.2, against `corners_filter` operation by operation (also the crossing points).
2. The clipping of 10.1 (exactness of 1 - u, 1 - v and of the inside tests; the hull argument).
3. The separating-direction test (any n; interval dot products over [F - e, F + e]; the threshold `ms |n|_1` rounded up).
4. G2 under the dive-then-tighten order, the budgets and `TimeLimit` (a dive child that starts after it goes back to the
   queue), and G3 at `NearContactTime`.
5. The broad phase: boxes widened by ms / 2 with outward rounding on both sides (so a pair closer than ms in every
   coordinate overlaps), closed overlap tests.

### 10.4 Tests and mutants

38 tests (`ContinuousCollisionTests`, `MeshSweepCollisionTests`, about 4 s together). Hand mutants killed: the domain test
dropped, the queue without the start-time key, no tightening, the time of impact from the near box only, half the width,
no threshold e in the discard test, eta = 0, the constant 4 instead of 64, M from 7 of the 8 points or from the start
positions only, no range switch, the interval path always, no clipping (the tightness test), each wrong crossing point,
the inside corners dropped, the separating test without ms |n|_1, without e, without the crossing points, without the
separating test. Survivors, equivalent: `lo >= ms` for `lo > ms` (ExactInterval widens exact results by an ulp, so a
corner never gives exactly lo = ms; the filter adds e > 0), `>=` in the domain test (a contact on u + v = 1 also lies in
the neighbouring box with u0 + v0 < 1), the constant 16 instead of 64 (a safety margin, see 10.2), outside corners kept in
the separating test (more points only make it more conservative).

### 10.5 Benchmark (benchmarks/CcdBenchmark/README.md)

ToI sample (Apache-2.0, fetched at run time): 271 852 queries, 0 false negatives, 2 false positives, 132 µs per query on
average (filter path; almost all queries collide), the interval path 3-4x slower with the same verdicts. Synthetic: random
broad-phase candidates 0.8-2.3 µs, near-contact queries 28-66 µs. Sphere sweeps of 11 532 triangles each: 0.24 s on 4
cores (minimum separation 0), 2.1 s (0.01).

### 10.6 Known limits

- Separation is tested along the axes and two directions per box; contact fronts with a minimum separation that none of
  them follows (around edges and vertices) still need many boxes. The tightening budget then stops with a correct, looser
  `TimeOfImpact` (`NearContactTime - TimeOfImpact` shows by how much; on the sphere sweeps up to 0.02 for single pairs,
  the sweep's earliest time within 1e-4 of its near time).
- The bound is absolute (`M`): at UTM coordinates of a millimetre scene the time of impact moves by up to e / speed
  (the invariance test allows 256 rounding units).
- One degenerate edge-edge query of the sample (cloth-funnel) takes 0.27 s (775 689 boxes); to look at.
- Performance left for later (code review of #793): the broad phase runs serially; with `CollectAll = false` every
  candidate pair still runs its tightening, also those the reduction drops (a first pass without tightening, then
  tightening only the pairs with `TimeOfImpact <= EarliestNearContactTime`, would keep the result and save time).

### 10.7 Bound review (2026-11-13)

Independent review of the head e97d285: no must-fix. B1 is sound; the reviewer's own derivation gives `44 Mu + 4 2^-1074`,
below the table's `62 Mu + 21 2^-1075`; the used `e = Up(M 2^-47 + 16 2^-1074)` covers both. The clipping, the separating
directions, G2 on every return path and determinism are sound. Should-fix items, fixed in #793:
- S1: the sweep's half separation was inexact for a subnormal `ms` (`0.5 * 5 2^-1074` ties to `2 2^-1074`): rounded up
  (also found by the code review); regression test with a vertex exactly `ms = 5 2^-1074` from a triangle.
- S2: coordinates near `2^1023` overflowed (every box survived, `ToleranceReached` infinite; an infinite extent made the
  first box near): coordinates above `InputRange = 2^1000` in magnitude are `InvalidInput` (query and sweep).
- S3: `NoCollision` under `TimeLimit < 1` reported `TimeOfImpact = +infinity`, which claimed more than `[0, TimeLimit]`:
  it is `TimeLimit` now, and the sweep's `EarliestTimeOfImpact` is at most `TimeLimit`.
- Nits: the per-operation table above; the status docs name the L-infinity statement and its Euclidean consequence.
