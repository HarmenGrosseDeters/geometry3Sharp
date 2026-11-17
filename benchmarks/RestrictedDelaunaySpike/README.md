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
