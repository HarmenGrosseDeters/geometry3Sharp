# A plane filter for the exact triangle-triangle test: upper bound (November 2026)

Question (owner, via the coordinator, 2026-11-24): can a coarse filter make the exact 3D triangle-triangle test
(`ExactTriangleIntersection.Intersects` / `IntersectsBeyondShared`, behind `TriangleTriangleFilter`) faster? The
candidate: compute each triangle's normal and one static error bound once, and decide the six plane-side signs of
`Intersects` (today six `Orient3d` calls, each recomputing the cross product and its bound) by one dot product each,
with the exact `Orient3d` as the fallback.

**Verdict: stop (AGENT_WORKFLOW section 3).** Even an ideal plane filter (no error bound, no fallback, so it also
"decides" the calls the exact stage decides today) saves at most 0.02 % of a self-intersection caller (Reducer,
Remesher) and at most 8.0 % of an `ExactMeshBoolean` (a box-sphere difference whose plane tests are mostly exactly
coplanar, where a real filter falls back to the exact stage; 2.2 % and 1.1 % on the other two boolean cases). On every
workload the bound is below the 10 % stop line. Not built.

## Method

Probe: `benchmarks/SelfIntersectionBenchmark/probe/` (README there and in the benchmark README, section (f)). The
patch `tritri_probe.patch` (never merged) wraps the entry points in `Stopwatch` timers (per site, outermost call only)
and captures their inputs:
`IntersectsBeyondShared`, `Intersects` (called directly or from `IntersectsBeyondShared`),
`TriangleTriangleFilter.BeyondShared`, and the pair search of `ExactMeshArrangement` (`separated`: the six plane tests
of pairs of different meshes; `touch_only`: the plane tests of the non-shared corners, then the filter and the exact
test, for pairs within one mesh). The callers run serially (`gParallel.SerialOnThisThread`) so the site times add up
to the wall time; the probe's own cost (calibrated, 80-95 ns per timed call on this machine) is subtracted. Nested
sites keep the overhead of their children, and first calls include the JIT, so the shares are upper bounds.

The saving of a plane filter is bounded on the captured pairs: the `Orient3d` calls as the code makes them (with the
early exit after the first three) against an ideal filter (two cross products, one dot product per corner, the same
early exit), times the number of calls. Cloud machine (Intel Xeon @ 2.10 GHz, 4 cores), net10.0 Release,
`DOTNET_TieredCompilation=0`, library built with `-p:G3PredicateStats=true` (exact-stage counts). Raw output:
`benchmarks/SelfIntersectionBenchmark/probe/results/` (`reducer_remesher.txt`: the reducer rows; its remesher rows ran in
parallel and are superseded by `remesher_si_serial.txt`).

## Results

Shares of the caller's net time. "Exact tri-tri, all free" is the ceiling of any filter in front of the exact test
(`IntersectsBeyondShared` + direct `Intersects`; for the boolean `separated` + `touch_only`, which include the existing
filter). "Ideal plane filter" is the saving bound above.

| workload | caller net ms | `TriangleTriangleFilter` | exact tri-tri, all free | `Orient3d` exact stage (all calls) | ideal plane filter |
|---|---:|---:|---:|---:|---:|
| reducer, harness plate 50, jitter 0.5 | 5054 | 40.1 % | 0.00 % | no calls | 0.00 % |
| reducer, harness tilted plate 50 | 4213 | 32.6 % | 0.32 % | 53.1 % of 2219 | 0.00 % |
| reducer, jitter plate tilted | 4242 | 36.3 % | 0.00 % | 10 calls | 0.00 % |
| reducer, bumpy sphere | 324 | 20.2 % | 0.00 % | no calls | 0.00 % |
| reducer, box grid | 2649 | 28.9 % | 5.30 % | 0.4 % of 6.2 M | 0.02 % |
| other 6 reducer cases | 122-3847 | 24-35 % | 0.00 % | 0-20 calls | 0.00 % |
| remesher (prevent), bumpy sphere | 2881 | 13.3 % | 0.46 % | 1.3 % of 985 | 0.00 % |
| remesher (prevent), jitter plate z = 0 | 425 | 14.0 % | 0.00 % | 17 calls | 0.00 % |
| remesher (prevent), jitter plate tilted | 409 | 14.4 % | 0.46 % | 47.9 % of 1293 | 0.01 % |
| remesher (prevent), box grid | 1020 | 19.1 % | 0.00 % | no calls | 0.00 % |
| boolean, union of two bumpy spheres | 167 | 1.6 % | 4.7 % | 0.0 % of 537 k | 2.2 % |
| boolean, box grid minus bumpy sphere | 170 | 6.6 % | 26.2 % | 58.8 % of 295 k | 8.0 % |
| boolean, union of two tilted slabs (coplanar faces) | 1309 | 1.4 % | 1.5 % | 30.0 % of 457 k | 1.1 % |

Per pair (captured pairs, minimum of 7 runs): the six `Orient3d` 68-101 ns where the filter decides, 174-293 ns where
about half reach the exact stage; the ideal filter 16-44 ns; the full `Intersects` 107-338 ns.

Why so little: in the self-intersection callers, `TriangleTriangleFilter` (exact `Orient2d` on projections) decides
almost every pair, and of the pairs left most share a vertex or an edge, where `IntersectsBeyondShared` takes the
segment-triangle path instead of the six plane tests: in the reducer box grid 1.19 M calls of `IntersectsBeyondShared`
reached `Intersects` only 6243 times. In the boolean, the six plane tests (`separated`) and the plane tests of
`touch_only` are already cheap where `Orient3dFilter` decides; the expensive calls are the exact-stage ones of
coplanar faces, which any filter with a correct bound leaves to the exact stage, so the realizable part of the 8.0 % is
smaller.

The other candidates: a box pre-reject is moot (every caller already gets its pairs from a box query: grid, tree or
`boxes_meet`; direct `Intersects` calls are a few thousand per run). Reusing a triangle's plane across many pairs (an
API change) has the same ceiling as the ideal filter above: it removes the same cross products.

## What the numbers point at instead (not measured further)

The cost of the self-intersection checks is the existing projection filter: 15-40 % of the reducer (a re-run of the bumpy sphere gave 15 % instead of 20 %) and 13-19 % of the
remesher (prevent), at 60-100 ns per call over 0.3-33 M calls, so it is the number of candidate pairs times a cheap
test. Two directions, each needing its own upper bound before any work: fewer candidate pairs (the clearance and cache
bounds of the benchmark README's "Measured and closed" section cover parts of it), and, for neighbours with a shared
vertex or edge, a plane-side pretest of the non-shared corners in front of the projection (as `touch_only` of the
boolean does: corners strictly on one side of the other's plane meet it only in the shared part). That pretest cannot
help on flat or nearly coplanar meshes (the plates above), where its `Orient3d` calls are 0 or in the exact stage.

## Literature

The test is Devillers and Guigue, "Faster triangle-triangle intersection tests" (INRIA RR-4488, 2002), from the paper;
its plane-side signs are the classic Möller (1997) interval test. A shared normal with one error bound per triangle is
a semi-static filter in the sense of Shewchuk, "Adaptive precision floating-point arithmetic and fast robust geometric
predicates" (1997), and Meyer and Pion, "FPG: a code generator for fast and certified geometric predicates" (2008); the
library already shares differences between predicates where it pays (`ExactRayTriangle` through `Orient3dFilter`). No
code was read.
