# RestrictedDelaunaySpike

Stage 1a of [docs/design/CERTIFIED_REMESHING.md](../../docs/design/CERTIFIED_REMESHING.md) (section 11): a spike of
restricted Delaunay refinement (RDR) of a clean closed mesh S, with floating-point surface tests, to measure the stop
rules of stage 1a before any library code: time per output vertex, the uncertified share at h_min = r_max / 16 on
smooth inputs, and the crease angle below which the TBP fails without feature protection. Not library code;
procedural inputs only.

```
dotnet build benchmarks/RestrictedDelaunaySpike -c Release
dotnet run -c Release --no-build --project benchmarks/RestrictedDelaunaySpike -- [only=<regex>] > rdr.jsonl
```

## What it does

- `Delaunay3` (exact) on a box around S enlarged by its diagonal; initial sample: input vertices at least 4 r_max apart.
- A Delaunay facet is restricted if its dual Voronoi edge (between the circumcentres of its two cells) meets S:
  `DMeshAABBTree3.FindAllHitTriangles` (exact ray-triangle decisions); the crossing points (surface Delaunay ball
  centres) are constructed in floating point; the largest ball is kept.
- Rules, worst first (largest ball, ties by vertex ids), each insertion refused closer than h_min (`Insert`'s
  `minSeparation`): R0 a box corner in a restricted facet; R1 (E') a dual edge meeting S twice or more, and a sample
  whose restricted facets are not one umbrella (link a single cycle); R2 a ball radius above r_max; R3 a radius-edge
  ratio above rho = 1. Then, in rounds until none is left or no point can be inserted: (C') proxy, each sample's
  restricted Voronoi region approximated by the input vertices nearest to it must be one disk (connected, Euler
  characteristic 1 of the fully labelled input triangles), and R2 for S -> D, the region's farthest input vertex at
  most r_max from its sample; a violating region gets that vertex inserted.
- Output: the restricted facets, oriented by the input normal at the ball centre. Checked: closed, appended without
  a non-manifold refusal, same genus and components as S (homeomorphic), `MeshRobustRepair.NotCleanReason`
  (including the exact self-intersection test), `MeshHausdorffDistance` (both ways, tolerance 0.01 r_max), angles.

Not done here (stage 1b): exact surface predicates (P1-P5 of the note, section 14.4); the exact counting checks of
(F') / (C') (here: the umbrella and the vertex-labelled region proxies, which see no feature smaller than the input's
edges); SoS for degenerate positions; the (F') facet check at all.

## Inputs

- smooth (12): unit sphere, ellipsoid (1, 0.6, 0.4) and bumpy sphere (normalized-cube spheres, 48k / 77k triangles),
  torus R 1 r 0.3 (240 x 72); r_max = diag / 30, / 60, / 120.
- lens (16): two spherical caps glued at their rim circle, 400 x 60 rings per cap (48k triangles), interior crease
  angle psi = 15 to 330 degrees (psi < 180: convex crease, the sharp side inside; psi > 180: reflex, the sharp side
  outside, like the union of two spheres); r_max = diag / 30.
- every input rotated by 23.7 degrees about (0.3, 0.7, 0.2).

## Results

Master 51270eaf plus this project, net10.0, 4 cores, one run each (repeat runs give the same result apart from the
times). `us/vertex`: refinement time per output vertex without the region-proxy passes (0.01-0.1 s per case).
Uncertified: restricted facets still violating E' or the umbrella at the end, over all restricted facets.

| case | out vertices | us / vertex | uncertified | homeomorphic, clean | Hausdorff / r_max | min angle (deg) |
|---|---:|---:|---:|---|---:|---:|
| sphere d30 / d60 / d120 | 626 / 2411 / 9557 | 933 / 250 / 240 | 0 | yes | 0.068 / 0.040 / 0.028 | 31.1 / 30.5 / 30.0 |
| ellipsoid d30 / d60 / d120 | 520 / 2026 / 8327 | 153 / 154 / 193 | 0 | yes | 0.20 / 0.10 / 0.063 | 31.6 / 31.6 / 30.0 |
| bumpy d30 / d60 / d120 | 616 / 2396 / 9444 | 145 / 167 / 205 | 0 | yes | 0.25 / 0.14 / 0.084 | 31.8 / 30.9 / 30.4 |
| torus d30 / d60 / d120 | 450 / 1850 / 7565 | 182 / 180 / 265 | 0 | yes | 0.21 / 0.11 / 0.063 | 31.5 / 30.5 / 30.1 |
| lens psi 15 | 10040 | 278 | 23.8 % | no (not closed) | 0.13 | 30.0 |
| lens psi 30 | 7695 | 240 | 13.4 % | no (not closed) | 0.13 | 30.0 |
| lens psi 45 | 2769 | 179 | 1.4 % | no (not closed) | 0.64 | 30.0 |
| lens psi 60 ... 300 (11 cases) | 503 - 627 | 126 - 220 | 0 | yes | 0.10 - 0.83 | 30.0 - 33.1 |
| lens psi 330 | 1803 | 235 | 7.5 % | no (not closed) | 0.11 | 30.0 |

(The first case's 933 us includes the JIT warm-up of the run.) Every min angle is at least 30 degrees, as the
radius-edge rule rho = 1 gives (no facet was exempted by h_min in the certified cases).

Reading against the stop rules of stage 1a:

1. **Time**: median 223 us per output vertex at r_max = diag / 120 (7.6k-9.6k vertices; 193-265), below the 300 us of
   the rule, but with floating-point surface tests and proxies; the exact predicates (mostly filtered) and the exact
   (F') / (C') counts of stage 1b come on top. The margin is about 1.35x.
2. **Uncertified share on the smooth set**: 0 in all 12 cases (rule: at most 1 %).
3. **Crease threshold**: without feature protection the refinement certifies creases whose sharper side is at least
   60 degrees (psi 60 to 300, convex and reflex) and fails at 45 degrees or sharper (psi 15, 30, 45, 330): there the
   points pile up at the crease until h_min refuses them. The rule's limit was 150 degrees: stage 1 covers 90 degree
   CAD edges; stage 2 (protection) is needed for creases sharper than about 45-60 degrees and for corners (not tested
   here).

## True restricted Voronoi cells against the region proxy (2026-11-17)

`dotnet run --project benchmarks/RestrictedDelaunaySpike -c Release -- rvc [only=<regex>]` computes the restricted Voronoi
cells of the samples in floating point (`RvcCheck.cs`: bisector clipping of the input triangles, security-radius stop,
propagation) and compares their (C') / R2 verdicts per sample with the proxy's, before the first region round and at the
end, on the inputs above plus coarse ones (input edges up to 8 r_max). Results and reading:
[GEOGRAM_MEASUREMENTS_2026-11.md](../../docs/research/GEOGRAM_MEASUREMENTS_2026-11.md), item 3.

## Stage 1b upper bound: incremental restricted Voronoi cells (2026-11-28)

The stop rule of stage 1b, measured before the library code: refinement with the exact clipping must cost at most 2x the
spike's 223 us per output vertex (446 us), on the smooth set at d120. `RvcIncremental.cs` keeps the restricted Voronoi
cells up to date in floating point during the spike's refinement. Every recompute is checked against a full `RvcCheck` on
the final triangulation, comparing chi, components and pieces per cell: 0 mismatches in every run below. Each polygon
vertex is tagged with the kind of point the exact clipping tests: 1 a mesh vertex (`BisectorSide1`), 2 a mesh edge on a
bisector (`BisectorSide2`), 3 a Voronoi edge in the plane of a triangle (`BisectorSide3`, prepared once per point). The
side tests and the R2 tests at the final polygon vertices (`DistanceAbove1-3`) are counted. Every 8th call's arguments are
sampled (up to 200k per kind) and the exact entries are timed on them.

```
dotnet build benchmarks/RestrictedDelaunaySpike -c Release
dotnet run -c Release --no-build --project benchmarks/RestrictedDelaunaySpike -- probe only=d120            # recompute
dotnet run -c Release --no-build --project benchmarks/RestrictedDelaunaySpike -- probe cut only=d120        # cut
dotnet run -c Release --no-build --project benchmarks/RestrictedDelaunaySpike -- probe lazy only=d120       # cut_lazy
PRUNE=poly dotnet run -c Release --no-build --project benchmarks/RestrictedDelaunaySpike -- probe lazy only=d120
PRUNE=tri  dotnet run -c Release --no-build --project benchmarks/RestrictedDelaunaySpike -- probe lazy only=d120
```

Raw output: [perf/stage1b-probe/](perf/stage1b-probe/) (master c855e5e7 plus this project, net10.0, 4 cores, one run each;
the `poly` file was run as `PRUNE=1`). Bound = spike + RVC (float) + exact, where exact = the sum over kinds of count × ns
per call. The spike's own time varies by about 15 % between runs (sphere 260-318 us).

| mode | what is recomputed after an insertion of p | side tests / insertion (kind 1 / 2 / 3) | RVC float us / vertex | exact us / vertex | bound us / vertex |
|---|---|---:|---:|---:|---:|
| recompute | every cavity vertex's cell, on its candidate triangles, against all its neighbours | 4.1k-6.7k / 1.3k-2.1k / 73-135 | 455-605 | 205-380 | 905-1241 |
| cut | old cells: their stored pieces clipped by bisector(j, p) only (V_new(j) = V_old(j) ∩ H(j, p)); p's cell: the triangles that were cut | 760-1197 / 603-916 / 67-126 | 143-201 | 55-80 | 403-596 |
| cut_lazy | as cut; the (C') summary (chi, components) of cut cells once per region round, not per insertion | as cut | 102-150 | 56-79 | 366-462 |
| cut_lazy_prune_poly | as cut_lazy; a piece is skipped when \|p - j\| > 2 max \|x - j\| over its float vertices | 361-552 / 273-352 / 34-35 | 95-132 | 29-42 | 368-430 |
| cut_lazy_prune_tri | as cut_lazy; the skip radius from the triangle's input vertices, so it involves no constructed point | 407-591 / 359-423 / 42-51 | 109-143 | 35-46 | 382-473 |

ns per exact call on the sampled arguments (the filter decides all of them; the plain predicate was 0 in none of the
samples, so the symbolic cascade was never reached on this set): `BisectorSide1` 26-34, `BisectorSide2` 52-73,
`BisectorSide3` 75-95 (prepared: 81-96 per prepare, 17-26 per call), `DistanceAbove1` 21-28, `DistanceAbove2` 48-57,
`DistanceAbove3` 72-87. For comparison, the float side test is 5-8 ns. Prepares: 16.7 per insertion.

The spike's dual-edge test (`compute_facet`: circumcentres, an AABB ray query and float crossings) is 94-151 us of the
spike's 228-318 us per vertex in the two prune runs. The clipping makes it redundant: a facet pqr is restricted iff a kind-3 vertex has the
sites {p, q, r}, and that point is the ball centre. Without it the cut_lazy_prune_tri bound is 285-322 us.
