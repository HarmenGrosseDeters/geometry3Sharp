# Certified edge-hull pilot and timeout recheck, 2026-11-08

This report records the opt-in experiment at commit `db02cfbb`. Both indexes
were subsequently promoted to binary64 defaults; see the tool README. Frozen
source/runtime hashes below continue to describe that measured experiment.

The [point-only pilot](../2026-11-08/README.md), commit `4fd98250`, completed
6 of the 24 oracle timeouts from [baseline QA #402](https://github.com/HarmenGrosseDeters/geometry3Sharp/pull/402).
This second variant enables both point pruning and endpoint-hull edge pruning,
and re-judges exactly the remaining 18 identical mesh outputs.

## Complete oracle results

**18/18 bounded rechecks completed: 18 VALID.** No input/output hash mismatch,
mesh error or tool error occurred. Every full oracle phase remains enabled.

| Case | Mode | Oracle seconds | Steiner limit |
|---|---|---:|---|
| ext/geos/misc/TestBufferExternal-2/92/q25 | conforming | 54.165 | True |
| ext/geos/misc/TestBufferExternal-2/92/q30 | conforming | 53.939 | True |
| ext/geos/misc/stmlf-20061020/10/q20 | conforming | 53.399 | True |
| ext/geos/misc/stmlf-20061020/10/q25 | conforming | 54.141 | True |
| ext/geos/misc/stmlf-20061020/10/q30 | conforming | 38.614 | True |
| ext/geos/robust/overlay/TestOverlay-stmlf/1044/q20 | conforming | 31.355 | True |
| ext/geos/robust/overlay/TestOverlay-stmlf/1044/q25 | conforming | 44.763 | True |
| ext/geos/robust/overlay/TestOverlay-stmlf/1044/q30 | conforming | 45.370 | True |
| ext/geos/robust/overlay/TestOverlay-stmlf/1056/q30 | constrained | 62.105 | True |
| ext/geos/robust/overlay/TestOverlay-stmlf/1771/q20 | conforming | 50.920 | True |
| ext/geos/robust/overlay/TestOverlay-stmlf/1771/q25 | conforming | 43.551 | True |
| ext/geos/robust/overlay/TestOverlay-stmlf/1771/q30 | conforming | 44.412 | True |
| ext/geos/robust/overlay/TestOverlay-stmlf/473/q20 | conforming | 41.588 | True |
| ext/geos/robust/overlay/TestOverlay-stmlf/473/q25 | conforming | 38.891 | True |
| ext/geos/robust/overlay/TestOverlay-stmlf/473/q30 | conforming | 51.071 | True |
| ext/geos/robust/overlay/TestOverlay-stmlf/548/q20 | conforming | 42.321 | True |
| ext/geos/robust/overlay/TestOverlay-stmlf/548/q25 | conforming | 31.169 | True |
| ext/geos/robust/overlay/TestOverlay-stmlf/548/q30 | conforming | 30.487 | True |

All newly valid meshes reach the Steiner limit. VALID means they satisfy the
existing bounded oracle contract; it does not certify every requested angle was
achieved. These are completed judgments of unchanged meshes, not mesher fixes.
Together, the two sequential campaigns resolve all 24 baseline oracle timeouts:
6 with the point-only variant, then 18 with both indexes. The six earlier cases
were not rerun as part of this second campaign. The separate baseline mesh timeout
has no completed mesh and is outside these oracle-only campaigns.
Combining the latest completed judgment per operation across the baseline and
these two follow-ups gives 81 VALID (57 at the Steiner limit), 81 INVALID and
1 MESH_TIMEOUT out of the original 163 operations; this is a joined status view,
not a single rerun with one variant. Invalid geometries remain unchanged.

## Sampled edge component comparison

The same stmlf/1044 q30 conforming mesh has 301453 triangles, 200045 vertices and
501497 unique edges. All edges remain indexed; 4096 evenly spaced lower-edge-ID
queries select pairs, with increasing IDs preventing duplicate tests. Three
rotated process runs per mode give identical hit fingerprints (zero crossings).
Runs are launched sequentially; other host activity is not instrumented. These
are diagnostic timings on a two-CPU quota, not isolated performance guarantees.

| Edge search | Median component wall s | Median CPU s | Median process s | Leaf candidates |
|---|---:|---:|---:|---:|
| boxes | 5.913 | 6.052 | 7.171 | 179870080 |
| nts | 10.382 | 10.506 | 11.616 | 179870080 |
| hulls | 1.747 | 2.079 | 3.056 | 188022 |

The hull variant is about 3.4 times faster than the sweep in this sampled component;
plain NTS envelope queries are slower and retain the same candidates. Parsing,
shared exact-point preparation and unique sorted edge selection precede the timer
for every mode. All mode-specific rank/box/index/hull preparation is included.
Process times include the excluded shared work. The existing oracle's mixed edge
key comparer is reused during preparation to avoid packed-long XOR hash collisions.
No full-mesh sweep timing completed, so no full-mesh speedup ratio is claimed.

The full hull component completes 501497 queries in 7.947 wall / 8.282 CPU seconds,
with 23162899 leaf candidates, 36485960 line-separated nodes, 1500164 hull-separated
nodes and zero crossings. This is a one-machine diagnostic, not a general speed
guarantee. Component command arrays are retained with the raw runs; the full run
uses the same hashed binary and mesh, omitting the final `4096` argument.

## Exactness and verification

NTS supplies its public STRtree hierarchy. Own monotone-chain hulls are built
bottom-up using the oracle's unchanged certified orientation filter and exact
fallback. Every descendant segment is contained in its node's endpoint hull.
Pruning requires either all hull vertices on one closed side of the query line,
or both query endpoints in the closed exterior half-plane of a CCW hull edge.
Both exclude strict proper crossings, including degenerate hulls. Zero remains
safe here; the separate point/contact checks retain their previous rules.
The ProperCross leaf predicate, default sweep and exact-reference path are unchanged.
See [sources and derivation](../../README.md#hull-index-for-the-edge-phase-second-isolated-variant).

442 procedural edge checks compare against exact exhaustive enumeration, including
crossings, contacts, shared endpoints, collinearity, degenerate edges, actual
line/hull pruning, extreme scales, translation, reuse and parallel determinism.
Two deliberately reversed pruning decisions are rejected by those tests.
The point suite has 312 checks; integrated oracle self-checks have 396. Controller
tests exercise both variants. Final build/test/package validation is retained in
`validation.txt`. No core library API or arithmetic changes, external code copies,
fixture files or mesh assets are introduced. NTS 2.6.0 remains test-tool only.

## Audit and reproduction

The [tool README](../../README.md) documents both opt-in variables and commands.
`recheck_edges.py` uses the original 163-row baseline records selected by the
point pilot's remaining timeouts; `selection.lineage.json` binds all three raw
result hashes. Identical cached meshes regenerated by the frozen baseline mesher
are rechecked by SHA-256 before judging. Input, output, original baseline-record,
controller, binary and runtime dependency hashes are bound. This campaign starts
with the final controller/runtime binding; no adoption of earlier records occurs.

Mesh budget is 60 seconds; oracle budget is 120 seconds, with two workers on the
two-CPU environment. Per-case durations are bounded diagnostics. The controller
returns success only when every judgment completes without unresolved results.
`recheck-results-*.jsonl` concatenate to the SHA-256 in `evidence.json`.
Public input provenance remains in the point report and baseline QA evidence;
all external geometry and local mesh caches stay outside the repository.
