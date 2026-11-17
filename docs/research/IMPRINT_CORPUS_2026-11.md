# Imprint corpus run, 2026-11: exact contact imprint against the float cut

Run: workflow "Thingi10K contact pairs" (`thingi-contact.yml`), run id **44017929170**, ref `claude/imprint-corpus` (tooling PR #1019, head `4f1dd467`), library sha **`dee55367d4cad6214bd2e00cedfee467bd57d9bf`** (`lib_sha.txt` of the run). The run finished with conclusion `success`. The raw results (`run-44017929170/`, the `results-*.jsonl` and `report-*.md` files) are not committed; they stay in the run branch `runs/thingi-contact`.

Methods compared:
- `exact_imprint` / `float_cut`: the cut of one mesh by the other (`a_by_b`, `b_by_a`).
- `exact_union` / `float_union`: the boolean union.

Classes: `thingi-pairs` (1000 Thingi10K pairs, the operation as it occurs in the corpus), and the procedural variants `face-contact` (78), `partial-overlap` (73), `ulp-shift` (100), `ulp-shift-few` (100) and `mirror` (99). In total 1450 cases; the summary tool reports 50 cases as "not written" (placement refused as inexact, or load failed).

Definitions, as used by `benchmarks/BooleanComparison/data/thingi_imprint.py summary`:
- "good": status ok, result closed, `NotCleanReason` empty, and for `float_cut` no failed insertions.
- "nothing inserted": `float_cut` found intersection segments and snapped every point to a target vertex, so the result is the target unchanged.
- "rounding not clean": `exact_imprint` reports `RoundingClean = false`.

## Summary table

| class | method | pairs | operations | good | failed / exception / crash / timeout | opened / not closed | unclean | gaps | curve found | nothing inserted | rounding not clean | median ms | max ms |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| thingi-pairs | exact_imprint | 1000 | 2000 | 1999 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 1 | – | 1544 | – | 3 | 122 | 1199 |
| thingi-pairs | float_cut | 1000 | 2000 | 862 | 0 / 141 / 0 / 0 | 220 | degenerate triangles 715, self-intersections 52 | 10 | 1403 | 0 | – | 85 | 4646 |
| thingi-pairs | exact_union | 1000 | 1000 | 1000 | 0 / 0 / 0 / 0 | 0 | – | – | – | – | – | 173 | 2053 |
| thingi-pairs | float_union | 1000 | 1000 | 249 | 0 / 130 / 0 / 0 | 510 | bowties 51, degenerate triangles 54, self-intersections 6 | – | – | – | – | 842 | 21961 |
| face-contact | exact_imprint | 78 | 156 | 156 | 0 / 0 / 0 / 0 | 0 | – | – | 92 | – | 0 | 106 | 4082 |
| face-contact | float_cut | 78 | 156 | 140 | 0 / 1 / 0 / 0 | 5 | degenerate triangles 6, self-intersections 4 | 0 | 91 | 1 | – | 59 | 2277 |
| face-contact | exact_union | 78 | 78 | 70 | 0 / 0 / 0 / 0 | 0 | self-intersections 8 | – | – | – | – | 184 | 5908 |
| face-contact | float_union | 78 | 78 | 29 | 0 / 1 / 0 / 0 | 44 | bowties 1, self-intersections 3 | – | – | – | – | 509 | 7624 |
| partial-overlap | exact_imprint | 73 | 146 | 145 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 1 | – | 128 | – | 1 | 80 | 317 |
| partial-overlap | float_cut | 73 | 146 | 47 | 0 / 14 / 0 / 0 | 27 | degenerate triangles 55, self-intersections 3 | 0 | 114 | 0 | – | 56 | 5343 |
| partial-overlap | exact_union | 73 | 73 | 73 | 0 / 0 / 0 / 0 | 0 | – | – | – | – | – | 107 | 334 |
| partial-overlap | float_union | 73 | 73 | 10 | 0 / 12 / 0 / 0 | 47 | degenerate triangles 4 | – | – | – | – | 605 | 18543 |
| ulp-shift | exact_imprint | 100 | 200 | 18 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 182 | – | 200 | – | 182 | 2692 | 6373 |
| ulp-shift | float_cut | 100 | 200 | 200 | 0 / 0 / 0 / 0 | 0 | – | 0 | 200 | 200 | – | 244 | 1076 |
| ulp-shift | exact_union | 100 | 100 | 99 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 1 | – | – | – | – | 1206 | 6551 |
| ulp-shift | float_union | 100 | 100 | 0 | 0 / 0 / 0 / 0 | 100 | – | – | – | – | – | 1124 | 21945 |
| ulp-shift-few | exact_imprint | 100 | 200 | 8 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 192 | – | 200 | – | 192 | 2173 | 10441 |
| ulp-shift-few | float_cut | 100 | 200 | 199 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 1 | 0 | 200 | 198 | – | 193 | 629 |
| ulp-shift-few | exact_union | 100 | 100 | 100 | 0 / 0 / 0 / 0 | 0 | – | – | – | – | – | 956 | 21312 |
| ulp-shift-few | float_union | 100 | 100 | 0 | 0 / 0 / 0 / 0 | 100 | – | – | – | – | – | 1089 | 9929 |
| mirror | exact_imprint | 99 | 198 | 105 | 0 / 0 / 0 / 0 | 0 | degenerate triangles 93 | – | 198 | – | 93 | 262 | 8107 |
| mirror | float_cut | 99 | 198 | 37 | 0 / 40 / 0 / 0 | 72 | degenerate triangles 48, self-intersections 1 | 0 | 158 | 6 | – | 124 | 1177 |
| mirror | exact_union | 99 | 99 | 97 | 0 / 0 / 0 / 0 | 0 | self-intersections 2 | – | – | – | – | 255 | 12453 |
| mirror | float_union | 99 | 99 | 0 | 0 / 24 / 0 / 0 | 74 | degenerate triangles 1 | – | – | – | – | 1057 | 63203 |

Status counts over all records: `exact_imprint` and `exact_union` have no `exception`, `crash` or `timeout` status. `float_cut` has 196 `exception` operations (102 `a_by_b`, 94 `b_by_a`) and `float_union` has 167.

## Time ratios (float / exact)

Geometric mean of the per-operation time ratio, over operations where both methods ended `ok`. The float methods count as `ok` even with an open result, so a ratio can include an open float result. Exact timeouts are excluded, but none occurred (see below).

| class | cut: float_cut / exact_imprint | operations | union: float_union / exact_union | operations |
|---|---|---|---|---|
| thingi-pairs | 0.91 | 1859 | 4.67 | 870 |
| face-contact | 0.65 | 155 | 2.85 | 77 |
| partial-overlap | 1.00 | 132 | 6.36 | 61 |
| ulp-shift | 0.10 | 200 | 0.88 | 100 |
| ulp-shift-few | 0.08 | 200 | 0.83 | 100 |
| mirror | 0.56 | 158 | 5.06 | 75 |

Timeouts: none. No `timeout` status appears for any method in any class. The timeout covers the warm-up and the `NotCleanReason` checks, so a timeout would not necessarily be a slow cut.

## Caveats

1. **Curve length.** `MeshMeshCut.Segments` has one entry per triangle pair. A segment on a shared edge therefore counts twice, and coplanar overlaps give no segments. The float curve length is consequently not comparable with the exact one on the variants. The 105 "curve length" disagreements are listed for the variants only:
   - face-contact 75, partial-overlap 3, ulp-shift 4, mirror 23 (total 105). They are not treated as meaningful.
   - `thingi-pairs` has 0 curve-length disagreements; that is the class where the length comparison is meaningful.
2. **Time ratios.** Each ratio above covers only operations where both methods ended `ok`. The float ratio includes open results.
3. **Vacuous float "good" on the 1-ulp classes.** In `ulp-shift` (200 of 200) and `ulp-shift-few` (199 of 200) the float cut is counted `good`, but 200 and 198 of its operations inserted nothing, so the result is the target unchanged (`float inserted nothing`).

## Disagreements

The summary lists 3066 operation-level disagreements. By kind:

| kind | count | where |
|---|---|---|
| only exact good | 1888 (thingi-pairs), 594 (variants) | thingi-pairs, face-contact 57, partial-overlap 162, ulp-shift 99, ulp-shift-few 100, mirror 176 |
| float inserted nothing | 396 | ulp-shift 196, ulp-shift-few 198, mirror 2 |
| both fail | 70 | face-contact 5, ulp-shift 1, mirror 64 |
| only float good | 13 | partial-overlap 1, ulp-shift-few 1, mirror 11 (no `thingi-pairs` case) |
| curve length | 105 | variants only, see caveat 1 |

No `volume differs` or `curve only exact/float` disagreements were reported.

## Criteria

**(1) exact_imprint and exact_union: no exception, crash or timeout, and no opened result from a clean closed target, in every class: holds.** Every class has 0 in the `failed / exception / crash / timeout` and `opened / not closed` columns for both exact methods. The exact methods have 0 `exception`, `crash` and `timeout` statuses. The 11 exact-union non-good results and 469 exact-cut non-good operations are unclean results, not failures: they are listed below.

**(2) On thingi-pairs: exact_imprint good >= float_cut good, and exact_union good >= float_union good: holds.**
- exact_imprint 1999 good vs float_cut 862 good (out of 2000 operations).
- exact_union 1000 good vs float_union 249 good (out of 1000).

**(3) Every "only float good" case is explained: holds.** There are 13 such cases, all in the variants and none in `thingi-pairs`. Each has `exact_imprint` status ok, the exact result closed, the curve present (curve length 6 to 91, not 0), and `RoundingClean = false` with `NotCleanReason = "degenerate triangles"`. None has an exact result that was closed without a curve, and no case relies on "float inserted nothing". No "only float good" case exists for the union.

| case (pair id) | operations | exact outcome | curve (exact) |
|---|---|---|---|
| mirror_p0018_215846+215846 | a_by_b, b_by_a | unclean: degenerate triangles | 17.95 |
| mirror_p0020_375267+375267 | a_by_b, b_by_a | unclean: degenerate triangles | 10.14 |
| mirror_p0021_68742+68742 | b_by_a | unclean: degenerate triangles | 90.67 |
| mirror_p0023_567041+567041 | b_by_a | unclean: degenerate triangles | 48.66 |
| mirror_p0025_822698+822698 | a_by_b | unclean: degenerate triangles | 21.84 |
| mirror_p0033_67501+67501 | a_by_b, b_by_a | unclean: degenerate triangles | 56.15 |
| mirror_p0034_225956+225956 | a_by_b, b_by_a | unclean: degenerate triangles | 37.47 |
| partial-overlap_p0053_57500+111010 | a_by_b | unclean: degenerate triangles | 6.39 |
| ulp-shift-few_p0076_196190+196190 | b_by_a | unclean: degenerate triangles | 28.04 |

**(4) The float methods fail on a non-trivial share: holds.** Share of operations that are not `good` (float_cut / float_union):

| class | float_cut not good | float_union not good |
|---|---|---|
| thingi-pairs | 1138 / 2000 = 56.9% | 751 / 1000 = 75.1% |
| face-contact | 16 / 156 = 10.3% | 49 / 78 = 62.8% |
| partial-overlap | 99 / 146 = 67.8% | 63 / 73 = 86.3% |
| ulp-shift | 0 / 200 = 0% (all 200 "good" insert nothing) | 100 / 100 = 100% |
| ulp-shift-few | 1 / 200 = 0.5% (198 insert nothing) | 100 / 100 = 100% |
| mirror | 161 / 198 = 81.3% | 99 / 99 = 100% |

For the float cut on `thingi-pairs`, 220 operations are opened or not closed and 141 raise an exception; `float_union` on `thingi-pairs` has 510 opened or not closed and 130 exceptions.

## Failing or unexplained cases (by pair id)

- **Exact side, not good on `thingi-pairs`:** `p0522_1706462+58010`, `a_by_b`, `unclean: degenerate triangles`. This is the only exact-side non-good operation in `thingi-pairs`.
- **Exact union, unclean (criterion 1 does not count these; listed for completeness):**
  - `self-intersections`: `face-contact_p0012_67986+80364`, `face-contact_p0017_118317+112539`, `face-contact_p0021_1706461+636813`, `face-contact_p0055_387871+45814`, `face-contact_p0057_161094+63422`, `face-contact_p0067_161092+370887`, `face-contact_p0086_1620054+135808`, `face-contact_p0099_107558+567041`, `mirror_p0077_58879+58879`, `mirror_p0082_409635+409635`.
  - `degenerate triangles`: `ulp-shift_p0024_1514499+1514499`.
- **Exact cut, unclean `degenerate triangles` (468 operations in the variants):** all are `RoundingClean = false` slivers, reported by count per class in the summary table (ulp-shift 182, ulp-shift-few 192, mirror 93, partial-overlap 1). They are not listed by pair id.
- **Unexplained cases:** none. Every "only float good" case is one of the 13 above, and every exact-side non-good operation is an unclean result, not an exception, crash, timeout or opened result.

## Sliver count at 1-ulp contact

The `rounding not clean` count of `exact_imprint` operations:
- `ulp-shift`: 182 of 200 operations, in 93 of 100 cases with at least one flagged operation.
- `ulp-shift-few`: 192 of 200 operations, in 97 of 100 cases.

The per-operation flag is `RoundingClean = false`. The summary tool reports no per-sliver count; the figures above are counts of flagged operations and cases.

## Verdict

Criteria (1) to (4) all hold on this run. This document reports whether the criteria hold; the recommendation is the owner's.
