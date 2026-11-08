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
