# STRtree point/triangle oracle pilot

Tier M, isolated benchmark experiment requested by the owner. Compare the current
oracle's BoxPairs sweep, plain NetTopologySuite 2.6.0 STRtree queries, and traversal
of the same STRtree with certified triangle half-plane pruning. The owner then
requested re-judging current timeouts: this branch also provides an opt-in full
oracle variant. Mesher, core exact arithmetic and quality contracts are unchanged.

The component measurement covers the used-point/triangle part of Conformity. Edge crossings,
duplicate coordinates, domain/unused-point checks and all subsequent oracle
phases remain outside this experiment. A fast component is not a full oracle
speedup or a validity verdict for the mesh.

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

The experimental `G3_ORACLE_POINT_INDEX=strtree` path replaces only the used-point/
triangle search in the independent test oracle. The default and exact-reference
paths retain the existing sweep. NTS is a direct dependency of the test-oracle
project in this branch, not of geometry3Sharp. All remaining conformity,
Delaunay, quality, tolerance and budget rules stay in force.

```sh
G3_ORACLE_POINT_INDEX=strtree G3_ORACLE_TIMING=1 dotnet run --project benchmarks/Meshing2Benchmark -c Release -f net10.0 -- oracle --milestone M3 --input /tmp/input.txt --output /tmp/output.mesh.txt --row /tmp/pilot-row.json
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

## Candidate follow-up for the edge phase

A plain edge-envelope index retains the same large AABB candidate set. A next
experiment could augment STRtree nodes with exact convex hulls of their segment
endpoints and reject groups by certified separation before the unchanged
ProperCrossV leaf test. This needs its own exact differential, contact/collinearity
and mutation checks. A robust exact intersection sweep is the larger alternative.
Neither edge optimization is implemented in the current point-search pilot.
