# Targeted meshing QA recheck, 2026-11-08

Production baseline: `a482d490716d6ad9fc3c91c9d2f3776070014de8`, after #392 and
#394–#396. Test/tool work only: no production mesher, predicate, tolerance or
oracle quality rule changed. This is a new meshing/oracle run on historical
inputs, not a re-judgment of archived outputs (those meshes were not retained).
The reference producers were not rerun and their implementations were not read.

The commands and pinned-source protocol are in [the recheck README](../../README.md).
All 163 base hashes and 79 available historical variant hashes match. The 84 old
timeouts lack variant hashes; their variants are reconstructed from verified
bases and the documented angle/Steiner settings. Historical comparisons use the
matched conforming/forkC and constrained/fork-cq pairs from #393.

## Complete bounded results

| Historical class | Mode | Operations | VALID | INVALID | Oracle timeout | Mesh timeout | Errors | VALID at Steiner limit |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| AngleBelowBound | conforming | 39 | 0 | 39 | 0 | 0 | 0 | 0 |
| AngleBelowBound | constrained | 40 | 2 | 38 | 0 | 0 | 0 | 0 |
| Oracle timeout | conforming | 44 | 24 | 0 | 20 | 0 | 0 | 24 |
| Oracle timeout | constrained | 40 | 31 | 4 | 4 | 1 | 0 | 9 |

All 163 operations are recorded. The acceptance runner returns **1**, because
the batch contains unresolved findings. This is not an entirely valid acceptance
campaign. Reached Steiner limits are reported separately: an oracle-valid bounded
output does not certify completion of every requested angle refinement.

The 79 historical AngleBelowBound operations give 77 INVALID and 2 VALID. The
two now-valid cases are constrained `TestOverlay-stmlf/77/q20` (225497 → 772
triangles) and `/839/q25` (180423 → 152406); neither reaches the limit. All 77
invalid outputs still have AngleBelowBound. Six conforming operations also have
NotDelaunayLocal/Global: `/1660` and `/1845`, each at 20/25/30 degrees. These
findings remain open, without changing or weakening the oracle.

## The twelve historically reference-only-valid operations

The reference column is historical evidence, not a fresh verdict from the current
oracle on the reference's output. All twelve were rechecked first.

| Case | Mode | Current result | Steiner limit |
|---|---|---|---|
| robust/overlay/TestOverlay-stmlf/1326/q20 | constrained | INVALID | no |
| robust/overlay/TestOverlay-stmlf/1326/q25 | constrained | INVALID | no |
| robust/overlay/TestOverlay-stmlf/1326/q25 | conforming | INVALID | no |
| robust/overlay/TestOverlay-stmlf/850/q25 | constrained | INVALID | no |
| robust/overlay/TestOverlay-stmlf/850/q30 | constrained | INVALID | no |
| misc/stmlf-20061020/10/q20 | constrained | VALID | no |
| robust/overlay/TestOverlay-stmlf/1044/q20 | conforming | ORACLE_TIMEOUT | not judged |
| robust/overlay/TestOverlay-stmlf/1044/q25 | conforming | ORACLE_TIMEOUT | not judged |
| robust/overlay/TestOverlay-stmlf/1044/q30 | constrained | VALID | no |
| robust/overlay/TestOverlay-stmlf/473/q20 | constrained | VALID | no |
| robust/overlay/TestOverlay-stmlf/638/q20 | constrained | VALID | yes |
| robust/overlay/TestOverlay-stmlf/638/q25 | constrained | VALID | yes |

## Current failures among the historical timeouts

| Case | Mode | Current violations | Steiner limit |
|---|---|---|---|
| robust/overlay/TestOverlay-stmlf/1473/q30 | constrained | AngleBelowBound | no |
| robust/overlay/TestOverlay-stmlf/1771/q25 | constrained | AngleBelowBound | no |
| robust/overlay/TestOverlay-stmlf/1771/q30 | constrained | AngleBelowBound | no |
| robust/overlay/TestOverlay-stmlf/954/q20 | constrained | AngleBelowBound | no |

These are newly completed judgments of current meshes. The historical outputs
had no completed verdict and are not available to re-judge; these results alone
do not establish a regression. They remain explicit quality findings.

Mesh-stage timeouts are distinct:

- `ext/geos/robust/overlay/TestOverlay-stmlf/697/q20` (constrained): 60 s mesher budget; no completed mesh to judge.

They cannot be re-judged by an oracle-only index pilot without first obtaining
a completed mesh under a separately recorded meshing budget.

## Oracle timeout diagnosis

A separate diagnostic call on the same hashed conforming `/1044/q30` output
(301453 triangles, 200045 vertices, Steiner limit reached) times out at 120 s
inside `Conformity`, before Delaunay and quality. Completed preceding phases total
about 2.7 s; process startup, parsing and self-check time are not included in
those phase totals. No finer timing inside Conformity is inferred.

An independent offline sweep counts 8958224310 point/triangle closed-box candidates
after excluding corners, and 22131316701 edge-box candidates after excluding
shared endpoints. These are candidates, not actual intersections. The counters
match exhaustive enumeration on 100 procedural batches. See `oracle-profile.json`,
`box-counts.json` and `../../count_boxes.py`; the profile and counts bind the same
output hash as the official batch record. Candidate counts explain why a spatial
index alone does not make these large overlapping-box cases cheap.

### Optimization ideas (not implemented in this QA change)

First split timings/counters for the point/triangle pass, edge-crossing pass and
remaining conformity checks. A first pilot can query a point hierarchy against
the actual triangle half-planes, pruning a whole point subtree only when it is
certifiably outside one edge. For edge crossings, a robust exact sweep can avoid
enumerating all overlapping AABB pairs; the usual target is O((E + K) log E),
with explicit handling of collinearity, coincident endpoints and T-junctions.
NetTopologySuite 2.6.0 (BSD-3-Clause, already used by the benchmark) is a possible
tree provider: an isolated public-API check confirms STRtree Root, ChildBoundables
and Bounds are accessible. See `nts-api-check.json`. Using that hierarchy with
our certified half-plane pruning could avoid writing a tree builder. Plain
envelope queries alone retain the same geometric AABB candidates. NTS geometric
validity predicates would not replace the independent exact oracle's decisions.
Both checks remain necessary; planarity alone does not establish non-overlap of
nested/disconnected components. The existing exact reference path should remain
as the differential checker on small procedural inputs and deliberately broken
meshes, preserving verdicts and deterministic reported examples. These are
proposals in response to the owner's question, not measured speedup claims or
production changes in this PR.

## Scope and audit

This targets every AngleBelowBound and ORACLE-TIMEOUT operation in run A's
`cases.tsv`, not the whole 6000-case campaign. Current budgets are 60 s meshing,
120 s oracle; the historical mesher had 600 s and its oracle changed from 600 s
to 120 s during run A. A timeout is an unresolved bounded check, not an invalid
geometry verdict or evidence of a speed regression. Single-run timings are
diagnostic, not speedup measurements. The environment has a two-CPU quota;
separate local validation and one diagnostic call overlapped parts of the batch.

The benchmark binaries remain fixed and are verified again after the batch.
Final CLI-only parser fixes and fresh-instance test assertions were validated in
a separate build directory; they do not alter the frozen mesh/oracle stages.
Eight initial records were retained after runner hardening only after verifying
unchanged binaries/manifest and the original process's empty variant environment;
the prefix hash and audit are retained. A later resume retained all durable rows.

`results-*.jsonl` concatenate byte-for-byte to the hash in `evidence.json` and
contain the commands, diagnostics, fingerprints and oracle rows. `verdicts.csv`
is the compact per-operation view. `prepared-manifest.json` preserves the exact
manifest bound by metadata, including local paths (data files are not committed).
`selection-audit.json` records the selection/hash checks. `validation.txt` and
`cli-controls.json` record 81 passing native tests, nine passing Python tests,
all-target builds/API-validating packs, clean CI gates and the controlled golden
row fault that makes CI fail. An independent read-only review found no remaining
blocking findings. Full multi-platform CI runs on the draft PR; no merge is made.

The separate constrained M3 golden file contains 52 oracle-accepted procedural
rows, with F-38 quality checks enabled; F-12/b and F-37 reach their budgets.
Historical failures are not promoted to golden baselines. Production fixes and
any change to the oracle contract require their own scoped work.
