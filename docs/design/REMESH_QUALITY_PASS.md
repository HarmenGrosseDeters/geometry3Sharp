# Remesh quality post-pass (`RemeshQualityPass`)

Stage 1 of the remesher quality layer (owner-approved 2026-11-27). This is a public, opt-in, worst-first post-pass after a remesh, after Hu et al. 2017,
with crease operations after Wang et al. 2019 (papers only; [sources](../sources/meshing-and-repair.md#remesh-quality-post-pass-remeshqualitypass-remeshqualitycoverage)).
`Remesher` / `RemesherPro` are unchanged. Stage 2 (the band guard inside the Remesher and `RemeshCertificate`, [CERTIFIED_REMESHING.md](CERTIFIED_REMESHING.md) section 10)
and stage 3 (curvature-adaptive sizing) follow as separate PRs.

## 1. Building blocks found in the library

- `mesh_ops/AlphaWrapQualityPass.cs` (internal) already has the shape of the operations. It visits worst first and makes flips, tangential moves,
  needle collapses and cap splits. Each one is kept only if the smallest angle rises, the exact self-intersection test passes and `IsWithin` holds.
  The new class reuses its geometric helpers (`box`, `unit_normal`; made `internal static`) and its acceptance pattern. The alpha-wrap pass itself is
  unchanged: its output is pinned bit for bit (`AlphaWrapQualityPassTests.ImproveWrapQuality_OutputPinned`, the same hash on master and on this branch).
- `TriangleDeviationBounds.IsWithin` (with the patch bound), the guard of `Reducer.MaxDeviation`: the mesh-to-reference direction.
- `ReducerReverseCoverage`: the reference-to-mesh direction, but only for collapses and only from a start with identical ids. It is generalised in
  `RemeshQualityCoverage` (section 3.3).
- `TriangleTriangleFilter.IntersectsBeyondShared` and `TriangleHashGrid`: the exact self-intersection guard, as in the alpha-wrap pass and the Reducer.
- `TriangleQualityStatistics`: the min angle at the 1 % rank reported in the result and in the benchmark.

## 2. Operations

Each pass runs, in this order (every list sorted by the smallest angle, ties by id):
1. **Needle collapses.** The shortest edge of a triangle below `TargetMinAngleDegrees` is collapsed.
   - A non-crease edge collapses into the end on a crease or pinned (at that end's position), otherwise at the midpoint or either end.
   - A crease edge removes a *sliding* crease vertex into its crease neighbour (Wang 2019, removal).
   - Never: two crease curves joined, a pinned vertex moved, or a collapse against the link condition.
2. **Splits.**
   - Caps (largest angle above 120) are split at the foot of the altitude. No new triangle may have a smaller smallest or a larger largest angle.
   - Then needles: their edges are tried in the order crease edges first, then by decreasing length, each split at its midpoint, the first whose four
     new triangles' smallest angle rises is made. This is graded refinement next to short crease edges.
   - On a crease edge the new point is put onto the reference's crease (Wang 2019, insertion). At most `MaxTriangleGrowth` (5 %, owner-approved) more triangles than at the first `Compute` of the instance.
3. **Flips** of non-crease edges.
4. **Relocations.**
   - A free vertex moves to the centroid of its ring in its tangent plane, followed onto the reference at its own offset.
   - A sliding crease vertex (two crease edges, turn at most 45 degrees) moves towards the midpoint of its crease neighbours, and half way, onto the
     reference's crease.

Creases are mesh edges above `CreaseAngleDegrees` that lie within the band of a reference crease (section 3.2), or the constrained edges of
`Constraints`. Corners, junctions, span ends and `Fixed` vertices keep their position. Two variants that free them are internal (section 5).

## 3. Guards and certificates

An operation removes a set R of triangles and adds new ones; every other triangle keeps its id and geometry. It is made only if all of the following hold.

### 3.1 The smallest angle (exact monotonicity)
The smallest angle of the triangles that change must rise by at least `MinGainDegrees` (0.1). For a collapse these are the two deleted triangles, the
triangles of the removed vertex, and the kept vertex's only if it moves: a kept vertex that stays leaves its other triangles as they are, and a bad
triangle among them must not block the collapse (a crease vertex next to a corner whose fan holds the mesh's worst triangle,
`CreaseCollapse_RefusedWhenTheNewCreaseEdgeCutsABend`). A cap split instead needs a smallest angle of at least that of R and a smaller largest angle. The measure is `RemeshQualityPass.MinAngle`: atan2 of twice the area and the dot product, with the
corners sorted lexicographically first. So a triangle has the same value whatever corner order `DMesh3` stores, and the value checked is the value in
the mesh. Hence

    min angle(mesh after) = min(min over unchanged, min over new) >= min(min over unchanged, min over R) = min angle(mesh before)

bit for bit, by induction over the operations. The first benchmark run, with an order-dependent acos measure, showed one case 1e-10 relative below
(5.40386976337e-05 to 5.40386976253e-05 degrees, on one Thingi10K mesh). With the canonical measure the count is 0
(`MinAngle_IndependentOfTheCornerOrder`). Normals may not turn by more than 30 degrees, and no new triangle may be degenerate. An old triangle of zero
area has no normal: its replacement is held to the area-weighted normal of the triangles the operation replaces (a collapse or relocation), to the
other old triangle (a flip, a split); two flat old triangles refuse. So exactly flat needles are repaired (`ZeroAreaTriangle_IsRepaired`); before,
every operation on them was refused. Edges of zero-area triangles are not creases, on the mesh or on the reference (their dihedral is noise).

### 3.2 The band, mesh to reference, and the crease band
- Every new triangle passes `IsWithin(reference, MaxDeviation)`, as the Reducer's `MaxDeviation` does (floating point, no explicit rounding term).
- Every new crease edge [p, q] must lie within delta = `MaxDeviation` of the set F of the reference's crease segments. F is the reference edges above
  `CreaseAngleDegrees`, plus its boundary edges. Let f(x) = dist(x, F), dp = f(p), dq = f(q), L = |pq|. The test is `chord_on_feature`:
  1. **Lipschitz.** f is 1-Lipschitz, so for x on [p, q] at distance t from p, f(x) <= min(dp + t, dq + L - t) <= (dp + dq + L) / 2. The minimum of
     two numbers is at most their mean. If that is at most delta, the chord is certified.
  2. **Convexity.** For one segment S, g(x) = dist(x, S) is convex along a line (the distance to a convex set), and f <= g. If p and q are both within
     delta of the same S, then g <= max(g(p), g(q)) <= delta on [p, q]. The test tries the segment nearest p at q, and the segment nearest q at p.
  3. **Otherwise** the chord is split at its midpoint m (f(m) evaluated), and both halves must certify, up to `MaxDeviationDepth` (10) levels.
  
  The chord is refused if a sample point has no crease segment within delta, or at the depth limit. f is computed in floating point, like `IsWithin`.
  Checked in `CreaseBand_ChordsOnAndOffTheBoxEdges`: across a box corner with the midpoint 0.009 off, certified at delta 0.01; 0.011 off, refused;
  parallel to an edge at 0.009 and 0.011, the same.
- The crease band is one-sided: an output crease edge near F does not imply that every removed crease point is near the output crease. An L-shaped
  crease with its corner removed shows it. The chord between points at s on the two legs is within s / 2 of F, while the corner is s / sqrt(2) from the
  chord. This counterexample is the reason corners stay pinned by default (coordinator, 2026-11-28; it also answers the Z3 question of 5.5). The reference-to-mesh direction comes from `TwoSided` (3.3), for the surface.

### 3.3 The band, reference to mesh (`TwoSided`, `RemeshQualityCoverage`)
Every covered reference triangle T keeps a set D(T) of current mesh triangle ids with the invariant

    (I) every point of T is within delta of the union of D(T), with their current geometry.

- **Start.** D(T) is certified against the mesh triangles whose box meets T's box grown by delta. If that fails, T is *uncovered*: counted, and left
  out of the promise. This start differs from `ReducerReverseCoverage`, whose start is the identity.
- **Operation.** (I) can only break for the T whose D(T) meets R. Each such T is certified again against its members outside R plus the new triangles.
  The operation is refused if one fails.
- **Commit.** D(T) = the members used. If any new triangle was used, all new ids are added; a superset keeps (I).
- **Why it holds.** The distance to a subset is at least the distance to the mesh, so by induction h(T, mesh) <= delta for every covered T after every
  operation.

The certificate is the cascade of `ReducerReverseCoverage.Within`. It runs breadth first, with the same answer, and with at most 256 sub-triangles per
certificate, refused beyond. That bound sets the cost: on two of the meshes the depth-first, unbounded cascade took 148 and 88 s, mostly in certificates
that end refused at depth 10. With the bound they take 3.5 and 8.5 s, and 6739 instead of 6087 of the two meshes' reference triangles are uncovered.
Tests: `Coverage_RefusesCuttingASpike_AcceptsAFlatChange`, `TwoSided_AcceptRefusesFlatteningASpike` (accepted one-sided, refused two-sided),
`CoverageWithin_CornersNearButInteriorFar_NotCovered`, and `TwoSided` in `Invariance_OverSimilarities`.

**Limit: a reference much coarser than the mesh.** The certificate refines a reference triangle until a piece's corners are near one candidate, or its
covering radius is below the band. Along every mesh edge inside a large reference triangle that takes pieces of the band's size, so a reference of
the box's 12 triangles against a remesh at 0.08 needs far more than 256 pieces. Such reference triangles count as uncovered: `StartWithinBand` is
false, and the pass keeps no promise for them, though it reports it. A remesh of a coarse CAD input gets the one-sided band only. Fixing it needs
a certificate that accepts a piece covered by several coplanar candidates together (open item 5.4).

### 3.4 Self-intersections
With `PreventSelfIntersections` (default), every new triangle is tested exactly against the other new ones and against the mesh triangles in its grid
cells, except R (`TriangleTriangleFilter.IntersectsBeyondShared`, the exact test behind the filter). This is the same test as in `AlphaWrapQualityPass`,
with its exception: zero-area triangles of the mesh are not tested against. That is safe in practice: a new triangle that crosses such a triangle also
crosses the proper triangle across its longest edge, which is tested, and a zero-area triangle in R is replaced by tested triangles. Only a chain of
zero-area triangles can hide a crossing (a defect `NotCleanReason` reports anyway). A collapse whose two opposite vertices are joined already (a
tetrahedron, a fold) is refused.

### 3.5 Certified flags
At the start every triangle is tested with `IsWithin`; every new triangle is certified by construction. `StartWithinBand` / `ResultWithinBand` say
whether all triangles (and, with `TwoSided`, all reference triangles) are certified. The flags equal an independent `IsWithin` on every result triangle
(`PartlyOutOfBandStart_CertifiedFlagsMatchAnIndependentCheck`). Out-of-band regions are not pulled back (open item 5.1).

### 3.6 Termination
Each pass visits a list fixed at its start, so every pass ends. The passes stop when one makes no operation, or at `MaxPasses` (10). Splits are bounded
by `MaxTriangleGrowth`, counted from the first `Compute` of the instance (repeated `Compute`s do not grow the mesh further).

### 3.7 The reference-crease grid
Each reference crease segment is put into the cells along it, in pieces of at most a quarter cell, not into every cell of its box. A long skew segment
over small cells (cells are 2 x the mean crease length, at least 1 / 256 of the box) would otherwise fill up to 257^3 cells
(`CreaseGrid_LongSkewSegment_StaysSmall`).

## 4. Measurements

`benchmarks/RemeshQualityBenchmark`, `post=0.005 runs=3 fidelity=0` on the 20 Thingi10K meshes of `benchmarks/RemeshBenchmark`
(`fetch_data.py`, nothing committed): g3cli remesh settings with `MinTriangleQuality = 0.2`, then the pass with a band of 0.5 % of the input's
diagonal. 4 cores, net10.0, Release, run in a session container (a GitHub-hosted re-run is to come); the raw output, summaries only, is `benchmarks/RemeshQualityBenchmark/perf/post_pass_2026-11-28.txt`. The metric is the min angle at the 1 % rank
(`TriangleQualityStatistics`), mean / median over the 20. The sector bound (last but one column) is the angle at the 1 % rank of the sectors between two
crease edges at one vertex of the result, the most any triangulation that keeps those creases can reach there.

| variant (Thingi10K, 20) | p01 mean before / after | p01 median before / after | min angle lower | new self-int. pairs | result in band | sector bound < 20: cases; p01 mean of the others before / after | remesh s / pass s |
|---|---:|---:|---:|---:|---:|---:|---:|
| default (corners pinned), band 0.5 % | 9.67 / 11.72 | 5.66 / 6.87 | 0 | 0 | 3 | 5; 11.99 / 14.55 | 8.8 / 9.4 (1.07x) |
| band 0.2 % | 9.67 / 11.31 | 5.66 / 5.90 | 0 | 0 | 0 | 4; 11.35 / 13.27 | 9.1 / 10.6 |
| band 2 % | 9.67 / 12.45 | 5.66 / 7.32 | 0 | 0 | 15 | 8; 14.44 / 18.64 | 9.9 / 7.3 |
| `TwoSided` | 9.67 / 11.72 | 5.66 / 6.87 | 0 | 0 | 1 | 5; 11.99 / 14.55 | 9.0 / 20.1 (2.2x) |
| corners free (internal `PinCreaseCorners = false`) | 9.67 / 13.18 | 5.66 / 7.53 | 0 | 0 | 3 | 7; 13.51 / 18.22 | 9.6 / 15.1 |
| corners free + strips merged (internal `MergeCreases`) | 9.67 / 13.82 | 5.66 / 7.55 | 0 | 0 | 3 | 7; 13.51 / 19.13 | 9.4 / 16.7 |
| creases only above 60 degrees (`CreaseAngleDegrees = 60`, pass only) | 9.67 / 15.61 | 5.66 / 7.38 | 0 | 0 | 3 | 2; 10.46 / 17.03 | 9.1 / 10.2 |
| no creases (`CreaseAngleDegrees = 180`, pass only) | 9.67 / 17.60 | 5.66 / 12.72 | 0 | 0 | 3 | 0; 9.67 / 17.60 | 8.7 / 20.2 |

`MaxTriangleGrowth` is the owner-approved 0.05 here; the previous run at 0.1 (before the review fixes) gave 11.76 for the default, so the lower bound
costs 0.04 degrees.

Procedural groups, default, band 0.5 % (`post=0.005 runs=3 fidelity=0` without `thingi=`):

| group | cases | p01 mean before / after | p01 median before / after | min angle lower | new self-int. pairs | remesh s / pass s |
|---|---:|---:|---:|---:|---:|---:|
| primitives | 150 | 27.76 / 31.22 | 29.27 / 32.33 | 0 | 0 | 4.8 / 1.2 |
| gears | 18 | 4.50 / 11.42 | 2.64 / 8.96 | 0 | 0 | 0.3 / 2.0 (6.7x; 0.03 - 0.4 s per case) |
| tubes | 24 | 4.37 / 4.43 | 3.45 / 3.45 | 0 | 0 | 0.2 / 0.1 |

On the gears (200 - 1000 triangles) the pass costs 6-7 times the remesh: half of it is `IsWithin` of the new triangles and a quarter the start
certificates (a profile with stopwatches, not committed; large flat faces triangulated as fans make the cascade subdivide). Accepted by the
coordinator (2026-11-28, opt-in, at most 0.4 s per case); no speed-up is built. On the Thingi10K set it is 1.07x.

Reading:
1. **Crease-angle limit.** 5 of the 20 have crease sectors below 20 degrees at the 1 % rank (sectors 0 to 11
   degrees). Any triangulation that keeps those creases keeps those angles. This is the surface analogue of small input angles, which Delaunay
   refinement cannot remove, only isolate (Shewchuk 2002). The other 15 are in the column after it (11.99 to 14.55 for the default).
2. **Noisy crease networks.** Most of the remaining worst triangles are needles whose short edge is a crease edge between junctions, or crosses a narrow
   strip between two creases (on one mesh: 129 of 171). Fixing them needs merging or dropping crease fragments, a fidelity decision
   ([REMESHER_CREASE_SLIVERS.md](REMESHER_CREASE_SLIVERS.md)), or graded refinement of both creases (triangle count about strip length over width).
3. **The band.** 8 of the 20 remesh outputs are 1 to 25 % of the diagonal off the input, and 14 already self-intersect, so a band of 0.5 % refuses most
   operations there. Without any creases the pass reaches 17.6 at this band (table), and 23.5 without creases and band (the plan's upper bound, a scratch probe).

Prior art: Shewchuk 2002 (small input angles are kept). Hu et al. 2017 and Wang et al. 2019 report 30+ degrees on clean CAD and scan models with few,
long features. For fragmented, noisy crease networks nothing was found besides cleaning the feature graph first (terms searched: feature curve cleanup,
short feature removal, sharp edge detection noise).

## 5. Open items

1. **Out-of-band regions** (coordinator 2026-11-27): the pass takes the reference as given and certifies what it makes. Pulling regions that the remesh
   left outside the band back into it is not in this PR.
2. **Crease-graph cleanup** (dropping or merging short crease spans before the remesh and the pass) changes fidelity. It is parked as an owner
   decision, with REMESHER_CREASE_SLIVERS.md.
3. **Internal variants**, public names to be proposed. `PinCreaseCorners = false` moves corners, junctions and ends along the creases, each crease edge
   within the band. `MergeCreases = true` collapses strip edges between two creases. Measured in section 4.
4. **`TwoSided` against a coarse reference** (3.3): a certificate for pieces covered by several coplanar candidates.
5. **Z3** (owner 2026-11-27, only if cheap). The monotonicity is the one-line induction of 3.1 once the measure is canonical, and the canonical measure
   is tested directly, so a solver adds nothing there. The crease-collapse implication suggested for a check does not hold in general (3.2, the L
   counterexample), so there is no obligation to prove. Not built.
