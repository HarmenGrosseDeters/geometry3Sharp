# Isolated STRtree pilot and timeout recheck, 2026-11-08

This report describes the point-only pilot preserved in commit `4fd98250`.
The later edge-hull variant is a separate experiment; its results do not replace
the frozen binaries, source hashes or judgments recorded here.

The [baseline QA draft #402](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/402)
records 163 operations on a482d490, with 24 current oracle timeouts. This follow-up
re-judges exactly those 24 outputs, regenerated with the frozen baseline mesher
and required to have the same SHA-256. All input/output hashes match.

## Complete oracle results

**24/24 completed bounded rechecks: 6 VALID, 18 ORACLE_TIMEOUT; no tool, mesh or
hash-mismatch errors.** The six newly completed judgments are:

| Case | Mode | Oracle seconds | Steiner limit |
|---|---|---:|---|
| ext/geos/robust/overlay/TestOverlay-stmlf/60/q30 | constrained | 76.843 | yes |
| ext/geos/robust/overlay/TestOverlay-stmlf/638/q20 | conforming | 56.595 | yes |
| ext/geos/robust/overlay/TestOverlay-stmlf/697/q30 | constrained | 99.952 | yes |
| f-12/b/q25 | conforming | 95.500 | yes |
| f-12/b/q25 | constrained | 76.796 | yes |
| f-12/b/q30 | conforming | 91.964 | yes |

All six reach the Steiner limit. They satisfy the existing bounded oracle
contract; this does not certify completion of every requested angle refinement.
The mesh geometry and quality rules have not changed. These are completed
judgments of identical outputs, not mesher fixes.

Sixteen remaining timeouts occur before the edge-crossing phase completes.
Two finish it and time out later: stmlf/548 q20 after `segments: coincidences`,
q30 after `segments`. Their edge phases alone take 111.487 and 104.199 seconds,
so this does not establish a separate segment/coverage bottleneck. No subsequent
check is skipped and no unresolved result is accepted as valid. The controller
returns 1 for the remaining timeouts, preserving all evidence.

## Component comparison

One hashed mesh: stmlf/1044 q30 conforming, 301453 triangles/200045 vertices.
4096 evenly spaced triangle queries index all used vertices; three process runs
per mode in rotated order have identical point/triangle hit fingerprints.

| Point/triangle search | Median wall seconds | Median CPU seconds | Leaf candidates |
|---|---:|---:|---:|
| boxes | 4.446 | 4.537 | 120082963 |
| nts | 5.264 | 5.292 | 120082963 |
| pruned | 0.431 | 0.413 | 28245 |

The pruned traversal is about 10.3 times faster in this sampled component
comparison. Plain NTS queries retain the same AABB candidates and are slower here.
NTS supplies the hierarchy; the certified geometric exclusion provides the gain.
Parsing, shared exact-point preparation, used-vertex selection and the positivity
precheck are excluded for all modes. All mode-specific index and triangle-box
preparation is included. Timings are diagnostic on one machine with a two-CPU
quota and a concurrent baseline run, not a general whole-oracle speedup claim.

A full pruned point/triangle diagnostic finishes in 4.257 wall/3.033 CPU seconds,
with 2118919 leaf candidates and no hits. Its wall time overlaps a short selftest
as well as the baseline batch. The optional full baseline comparison was stopped
to prioritize the owner's timeout recheck request; it has no completed timing and
is not used for a full-mesh speedup ratio. The 128/4096 sampled comparisons are
complete. Pre-integration source is retained as text under `component-version`;
its SHA matches the frozen component metadata. The mathematical search/filter
was then moved into the test-oracle project for full judging, with compatible
hash/API calls and unchanged geometric decisions.

## Exactness, controls and provenance

The affine maximum of orientation over a closed node box selects a corner by
coordinate comparisons, avoiding difference overflow. Only a strictly negative
maximum excludes points; boundary contacts remain. The existing oracle's
unchanged filter falls back to Exact.Orient. NTS validity predicates are not used.
The existing default and exact-reference point sweeps remain available; the
experimental path requires `G3_ORACLE_POINT_INDEX=strtree` on binary64 outputs.
All edge, duplicate/domain, segment, coverage, Delaunay and quality checks remain.

Both default and opt-in paths pass all 396 oracle self-checks. 312 component
checks compare with exact exhaustive enumeration, including nested/coincident
and multi-subtree boundary cases, actual pruning, scales 2^-500/1/2^500, reuse
and parallel determinism. Changing the strict exclusion to `<= 0` makes the
boundary comparison fail; restoring the original passes again. Five controller
tests cover partial producer records, identity/verdict checks, wrong-mesh refusal,
phase markers and changed runtime dependencies. Final repository validation records 651 passing opt-in native tests,
all-target/net48 builds and both API-validating packs in `validation.txt`.

NetTopologySuite 2.6.0 is a BSD-3-Clause test-tool dependency; no geometry3Sharp
library dependency is added. Only public tree APIs/documentation are used. Public
GEOS XML data is fetched externally, under its pinned LGPL-2.1 provenance; no
fixture or mesh asset is committed. `input-provenance.json` identifies the large
diagnostic input. The baseline QA preparation tool supplies the verified inputs
for the full campaign. No Triangle/Triangle.NET/reference implementation is read.

## Audit and reproduction

See [the tool README](../../README.md), per-record commands and frozen hashes.
Mesh generation has 60 s and oracle judging 120 s. One worker runs alongside the
baseline; two run after it completes. These are bounded diagnostic outcomes,
not controlled cross-version timing benchmarks. Local mesh files remain outside
the repository for future diagnosis.

The controller binds input/output fingerprints, baseline record hashes, both
binary sets, every runtime DLL, deps/runtimeconfig manifests and runtime assets,
plus its own source, environment and budgets. A review hardening retained one
durable record only after checking unchanged bound binaries and identical NTS
copies against the release package and frozen component. The adopted prefix,
backup, old/new controller hashes and runtime audit are retained explicitly.
The in-flight attempt was discarded. `recheck-results-*.jsonl` concatenate to
the hash in `evidence.json`; the baseline hash matches #402's complete evidence.

The next edge experiment proposed by the owner discussion is STRtree nodes
augmented by exact endpoint convex hulls, with certified separation and unchanged
ProperCrossV leaves; an exact intersection sweep is the larger alternative.
Neither edge optimization is implemented or claimed as measured here.
