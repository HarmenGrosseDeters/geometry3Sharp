# Certified STRtree oracle indexes

The independent meshing oracle uses certified STRtree pruning by default for
binary64 outputs: triangle half-planes exclude point nodes, and exact endpoint
hulls exclude edge nodes. The previous sweeps remain selectable for diagnosis;
exact-reference runs retain their sweeps. Mesher, core exact arithmetic and
quality contracts are unchanged.

This Tier M tool compares the previous BoxPairs sweep, plain NetTopologySuite
2.6.0 STRtree queries and the certified traversals. It also provides bounded,
hash-bound controllers for reproducing the original timeout campaigns.

Separate component measurements cover used-point/triangle and proper edge-crossing
search in Conformity. The full oracle variant can enable either index independently.
Duplicate coordinates, domain/unused-point checks and subsequent oracle phases
retain their rules. A component timing alone is not a validity verdict.

NTS supplies the packed point hierarchy through its public Root/ChildBoundables
API. An orientation is affine in the query point. Its maximum on a node's closed
box occurs at the corner chosen by the signs of its two coefficients. A strictly
negative maximum certifies that every point in the node is outside that triangle
edge; zero must remain searchable. The existing oracle's unchanged OrientV filter
is reused, with Exact.Orient on uncertainty. No NTS geometric validity predicate
is used. The pilot accepts finite binary64 output with positive triangles, as
required by the existing pipeline before Conformity.

```sh
dotnet run --project benchmarks/OracleIndexPilot -c Release -- selftest
dotnet run --project benchmarks/OracleIndexPilot -c Release -- boxes /tmp/output.mesh.txt 4096
dotnet run --project benchmarks/OracleIndexPilot -c Release -- nts /tmp/output.mesh.txt 4096
dotnet run --project benchmarks/OracleIndexPilot -c Release -- pruned /tmp/output.mesh.txt 4096
```

Omit the final query count to query all triangles. A reduced count selects
evenly spaced triangle indices, while indexing **all used vertices** of the same
mesh. Parsing, shared exact-point preparation, used-vertex selection and the
positive-orientation precheck precede the timer for every mode. All mode-specific
index construction, triangle-box construction and cached node-corner preparation
are included. CPU time is reported beside wall time. Point/triangle hits are
sorted and fingerprinted, independently of index traversal order.

Acceptance: compare all modes with exact exhaustive enumeration on procedural
overlapping/nested triangles, coincident vertices and edge contacts; include a
multi-subtree edge-contact case that actually prunes; check power-of-two scaling,
reuse and parallel determinism. A controlled `< 0` to `<= 0` pruning fault must
be rejected by the boundary test. Run repeated comparisons on the same hashed
mesh and report candidate counts and result equality beside timings. No data or
mesh assets are committed.

NTS is BSD-3-Clause, already a benchmark-only dependency elsewhere in this repo
([license](https://github.com/NetTopologySuite/NetTopologySuite/blob/v2.6.0/License.md)).
Only its public APIs/documentation are used; no implementation is inspected or
ported. The half-plane argument and traversal are own code using this repository's
independent oracle predicates. [Results, controls and limitations](results/2026-11-08/README.md)
record the completed measurements and full timeout recheck.

## Full oracle timeout recheck

The default binary64 path uses `strtree` for used-point/triangle search and
`strtree-hulls` for edge crossing search. No environment variables are required.
Set `G3_ORACLE_POINT_INDEX=sweep` and/or `G3_ORACLE_EDGE_INDEX=sweep` to use the
previous sweep for diagnosis. The exact-reference path always retains its sweep. NTS is a direct dependency of the test-oracle
project in this branch, not of geometry3Sharp. All remaining conformity,
Delaunay, quality, tolerance and budget rules stay in force.

```sh
G3_ORACLE_TIMING=1 dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- oracle --milestone M3 --input /tmp/input.txt --output /tmp/output.mesh.txt --row /tmp/pilot-row.json
```

`recheck.py` selects every current `ORACLE_TIMEOUT` from the baseline QS batch,
including ones completed while the follow-up runs. It regenerates outputs with
the frozen baseline mesher and requires the **same output hash** before judging.
Identical cached outputs can be reused. Commands and canonical input hashes are
inherited from each immutable baseline record. Mesh generation has 60 s, oracle
judging 120 s. Binary hashes, controller hash, environment and budgets bind resume.
Unexpected inherited meshing/oracle variant variables are rejected.

For this two-CPU environment the controller uses one worker while the baseline
runs, then two after its complete summary is available. Per-job phases and
verdicts are diagnostic bounded results, not controlled cross-machine timings.
It retains local mesh files for subsequent diagnosis; none are committed. The
actual oracle phase log starts at `ORACLE-JUDGE-BEGIN`; preceding self-check
timing lines are dropped from the stored per-case diagnostic, while its pass
summary is retained. Point/triangle, edge-crossing and remaining conformity
times are separate. Full-oracle validity still requires all subsequent checks.

## Hull index for the edge phase (second isolated variant)

A plain edge-envelope index retains the same large AABB candidate set. The second
variant augments STRtree nodes with exact convex hulls of descendant endpoints,
built bottom-up by a standard monotone chain. Hulls of child hulls suffice because
their convex hull equals the hull of all original endpoints.

Every descendant segment lies in its node hull. Two certificates exclude proper
crossings: all hull vertices lie on one closed side of the query's supporting
line; or both query endpoints lie in the closed exterior half-plane of a CCW
hull edge. Hulls of size one/two are handled by the same separation arguments.
Zero is deliberately permitted here because ProperCross requires strict opposite
orientation signs. Point/triangle and duplicate-coordinate checks continue to
handle boundary contacts and overlaps. The leaf crossing predicate is unchanged.

Binary64 outputs use this path by default; `G3_ORACLE_EDGE_INDEX=sweep` restores
the previous edge sweep. Exact-reference outputs always retain that sweep. Component
commands are `edge-boxes`, `edge-nts`, `edge-hulls`; `edge-selftest` compares against
exact all-pairs evaluation, including shared endpoints, collinearity, T contacts,
degenerate edges, real line/hull pruning, extreme scales, reuse and parallelism.
The existing sweep's unordered callback pairs are normalized only in its full
comparison path; query-based paths use increasing edge IDs for deduplication.

Literature considered: A. M. Andrew, *Another efficient algorithm for convex
hulls in two dimensions*, IPL 9(5), 1979,
[doi:10.1016/0020-0190(79)90072-3](https://doi.org/10.1016/0020-0190(79)90072-3)
(monotone-chain concept); Gottschalk, Lin and Manocha, *OBBTree*, SIGGRAPH 1996
(hierarchical geometric separation); Bentley and Ottmann, *Algorithms for
Reporting and Counting Geometric Intersections*, IEEE TC 1979 (the exact-sweep
alternative). Publication metadata/abstracts and public NTS APIs were consulted;
the hull, traversal and certificates are own implementations using the oracle's
existing certified orientation filter and exact fallback. No external code read.


Both full-oracle indexes run by default. The explicit `G3_ORACLE_POINT_INDEX=strtree`
and `G3_ORACLE_EDGE_INDEX=strtree-hulls` settings remain accepted. `recheck_edges.py` uses the same bounded,
hash-bound controller contract as `recheck.py`; supply original baseline records
selected by the point-only campaign's remaining timeouts. Both controllers have
procedural regression tests. Edge components accept the same mesh path and optional
query count as point components; omit the count for all unique edges. Shared
parsing/exact-point/unique-edge preparation is excluded, while mode-specific
index, rank, box and hull preparation is included. Result pairs are sorted and
fingerprinted. See [edge results and full-oracle recheck](results/2026-11-08-edge/README.md).

The point-only historical campaign controller explicitly requests the edge sweep,
so it remains point-only after default promotion. Historical result directories
describe their frozen source versions; new selection tests cover defaults, explicit
indexes, sweep fallback, invalid settings and the exact-reference path.

[Default promotion validation](results/2026-11-08-default/README.md) records the
selection tests, default/fallback integration checks and identical-row large-case
recheck after the owner requested standard behavior.

## Bounded hull tests (review of #411)

The review found the edge search O(E·h) on convex boundaries: on a lens every point
is on the convex hull, so the root's exact hull held nearly all of them, every chord
splits the lens (no line prune), and each visited inner node tested all h hull
vertices and edges. Now a node keeps a containing polygon of at most
`StrtreeEdgeSearch.HullLimit` = 16 vertices: the exact hull of its children's
polygons if that is small, else the four corners of its closed box (which contains
that hull, so the certificates stay sound; corner points are exact, as an NTS
envelope's bounds are input doubles). The test per visited node is O(16), and the
build is linear in the tree size. Leaf parents keep exact endpoint hulls, so the
fine pruning that paid on the former timeouts is unchanged where hulls are small.
`EdgeIndexResult` reports `PruneTests` (orientation tests of the certificates) and
`CoarseNodes` (nodes testing their box corners). Alternatives measured on the lens
and the comb below: limits 8/16/32 and keeping the coarse node's full hull of its
children's polygons (up to capacity × limit vertices); 16 with box corners was
fastest on the lens and equal elsewhere, within noise.

Procedural families, generated by this tool (no files committed): `edge-lens <m>`
is a zigzag strip between y = ±(1 - x²)/4 with m points per arc (the reviewer's
case); `lens-input <m> <file>` writes the lens as a mesher input (one outer ring,
2m - 2 points), meshed with `mesh --no-oracle --milestone M1`; `comb-input <k> <m>
<file>` writes k parallel diagonal constraint chains of m points in a square, where
edge boxes overlap across all chains and hulls pay. Component wall seconds, this
2-CPU container, median of 3 (PR head: one run; it is too slow to repeat). All
fingerprints equal the sweep's (no crossings).

| Case | Edges | Sweep | PR head hulls | Bounded hulls | Nodes / prune tests (bounded) |
|---|---:|---:|---:|---:|---:|
| strip m = 2000 | 7 993 | 0.036 | 0.494 | 0.114 | 0.92 M / 1.69 M |
| strip m = 8000 | 31 993 | 0.055 | 4.601 | 0.403 | 4.62 M / 8.89 M |
| strip m = 32000 | 127 993 | 0.223 | 74.82 | 0.917 | 22.9 M / 43.5 M |
| mesher lens, 60 000 points (M1) | 119 997 | 0.254 | 58.04 | 0.929 | 14.2 M / 33.0 M |
| comb k = 8000, m = 3 (M1) | 72 005 | 6.393 | 1.282 | 1.372 | 12.5 M / 27.6 M |
| comb k = 400, m = 100 (M1) | 120 005 | 0.418 | 1.703 | 1.560 | 12.4 M / 30.3 M |
| comb k = 1000, m = 60 (M1) | 180 005 | 1.063 | 2.277 | 2.382 | 21.3 M / 50.9 M |

The reviewer measured 0.48 / 4.5 / 65 s for the strips and 101.7 s for a lens mesh
of its own (PR head). The bounded search is 3-4 times the sweep on the lens
(the STRtree visits about 120-180 nodes per chord query, against the sweep's 64k
candidates in all) and grows like n log n: work (nodes + prune tests) 2.61 M,
13.5 M, 66.3 M for 4× steps (×5.2, ×4.9). `Meshing2OracleStrtreeTests` asserts the
growth at n and 4n (below ×6.5; without the limit it is about ×13 and fails). On
the comb, where boxes overlap across chains, the sweep is quadratic (256 M
candidates) and both hull variants keep their gain; where boxes overlap little
(m = 60, 100) the sweep is faster, at PR head as now. The former #402 timeouts
could not be re-meshed here (their inputs are converted from external fixtures by
the QA harness, which is not in this repository); their meshes have small leaf
hulls, which the bound does not change, so the edge component there should be
rechecked with `edge-hulls <mesh>` before and after.

```sh
dotnet run --project benchmarks/OracleIndexPilot -c Release -- edge-lens 32000 boxes hulls
dotnet run --project benchmarks/OracleIndexPilot -c Release -- lens-input 30001 lens.input.txt
dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- mesh --no-oracle --milestone M1 --input lens.input.txt --output lens.mesh.txt
dotnet run --project benchmarks/OracleIndexPilot -c Release -- edge-boxes lens.mesh.txt
dotnet run --project benchmarks/OracleIndexPilot -c Release -- edge-hulls lens.mesh.txt
dotnet run --project benchmarks/OracleIndexPilot -c Release -- comb-input 8000 3 comb.input.txt
```

Input hashes (SHA-256): lens 60 000 `a934309c…5633ea`, its mesh `a639e9b0…83b2`;
comb 8000×3 `82991bf9…daeb33`, mesh `02fc831b…413d`; comb 400×100 `7c08535f…94db`,
mesh `0417c71c…7edf`; comb 1000×60 `cd71a68f…c72b`, mesh `bd44d215…9927`.
