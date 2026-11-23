# Alpha wrap accuracy: snap back and "wrap only the gaps" (spikes, 2026-11)

Status: measurement spikes, no library change. Probe: [benchmarks/WrapAccuracyProbe](../../benchmarks/WrapAccuracyProbe/README.md) (commands,
self-test, raw JSONL and tables in its `results/`). Roadmap context: [alpha-wrap-and-repair.md](../roadmap/alpha-wrap-and-repair.md).
Origin of the question: behaviour observed by the owner on private inputs (the wrap is never equal to a clean input, however small the
offset). All numbers below come from the commands in the probe README; timings are relative (other sessions and up to four probe processes
ran at the same time; a wrap is the best of up to three runs when one run takes under 5 s, the other numbers are single deterministic runs).

## 1. Question

`MeshAlphaWrap` returns a closed, manifold, intersection-free surface that strictly encloses the input at a distance of about `Offset`; by
contract it never equals the input. How accurate can a wrap-based result get on valid closed solids and on solids with small defects, at
what cost?

- **Spike A, snap back:** wrap for the topology, input for the geometry: every wrap vertex moves onto its nearest input point, kept valid by
  exact checks, studied together with `Alpha` (the wrap bridges every concavity narrower than the ball; that error does not shrink with the offset).
- **Spike B, keep the input, wrap only the gaps:** for a solid with holes the ideal result keeps every input triangle and adds surface only
  across the holes; the wrap would decide what is inside and supply the patches.

## 2. What the library and the roadmap already have

- The wrap (Alpha Wrap 2.0 / 3.0, done) and its contract: input strictly inside, offset band, no contact with the input (the contact bugfix of
  2026-11-09 made "the input is inside" the wrap's promise). `MeshRobustRepair` uses the wrap as the last resort after the topology-preserving
  repair, the exact resolve and the solidify, with `alpha = diagonal / 300`, `offset = diagonal / 3000` and a simplification within half the offset.
- The **sharp-feature pass** (`AlphaWrapFeaturePass`, lever 6 K, #736, opt-in) is the closest existing relative of the snap back: it moves
  vertices toward the QEF points of the input's planes under exact checks (no self-intersection, no contact with the input, vertices at least 0.9
  offset away from it, angle floors) and keeps the contract; on the T1 set edge F 0.61 -> 0.875 and the crease normal error 14.7 -> 4.67 degrees.
  The snap back is the contract-breaking cousin: vertices end on the input. The probe reuses its building blocks (the ring check of
  `benchmarks/SharpFeatureProbe/Snap.cs`: `ExactTriangleIntersection.IntersectsBeyondShared`, `IsDegenerate`, `TriangleHashGrid`; the same checks as
  `AlphaWrapQualityPass`), and what it does not check (distance to the input, intersection with it) is exactly what the contract requires there.
- The **adaptive alpha spike** (2026-11-15, no-go, stays a study): per-triangle alpha levels need a cavity-aware predictor; distance-only
  predictors are wrong on 28 % of the tail's area because coarse wraps close cavities the fine wrap enters. That is the same alpha floor seen here
  from the other side; this note does not reopen it. Dai, Wang and Yan (CAGD 2024) was not accessible then and only an abstract is here (section 3).
- **Dual contouring on the winding number** (`WindingContouring`, variant B of the sharp-feature study) already reaches the input's sharp edges on
  the procedural CAD solids (edge chamfer 0.0012 against 0.6 for the wrap, crease normal error 1e-7 degree) but is not valid by contract (T1: 19 of 20
  clean, minimum angle 0.09 degree, 17765 triangles above 170 degrees) and has no offset band.
- Hole fill and cleanup: `MeshAutoRepair` (hole filling, Minimal or Smoothed), `MinimalHoleFill`, `PlanarHoleFiller`, `AutoHoleFill`;
  `MeshRobustRepair` passes clean parts through (`PassThrough`), runs the exact resolve and the solidify (`MeshWindingNumberSolidify`) before the wrap.
- Exact building blocks for a hybrid: `ExactMeshImprint` (imprint a cutter's boundary into a target), `ExactMeshBoolean`, the winding-number tree
  (`WindingNumberOctree`, `DMeshAABBTree3.WindingNumber`), `MeshHausdorffDistance` (certified Hausdorff interval, Sacht and Jacobson 2024).

## 3. Prior art (how others solve it)

| source | status | what it says for this question |
|---|---|---|
| Portaneri, Rouxel-Labbe, Hemmer, Cohen-Steiner, Alliez, *Alpha Wrapping with an Offset*, ACM TOG 41(4), 2022 | read earlier in the project ([ALPHA_WRAP_PAPER_NOTES.md](../design/ALPHA_WRAP_PAPER_NOTES.md)) | the offset is the distance of the result from the input and the price of speed and of triangle quality (a larger offset gives more rule-1 insertions and better elements); the wrap never touches the input; the alpha ball decides which cavities are entered. Nothing in the paper moves the result onto the input. |
| Dai, Wang, Yan, *Feature-preserving shrink wrapping with adaptive alpha*, CAGD 2024 | abstract only (a web search for this note; the paper itself is not accessible) | a sizing field that measures the discretisation density of defective meshes and adaptive wrapping by normal projection; tested on Thingi10K, ABC and GrabCAD; claims better fidelity than the plain wrap with the manifold property kept. The same idea family as the snap back (project along normals), with a locally smaller alpha, and its authors' route to the alpha floor. Not reproduced here. |
| Kobbelt, Vorsatz, Labsik, Seidel, *A shrink wrapping approach to remeshing polygonal surfaces*, CGF 18(3), 1999 | abstract only (web search, not opened in full) | shrink a coarse mesh onto the input by iterative projection with a regularising (smoothing) term that keeps the triangle quality; projection alone distorts the mesh. This is the classic form of "project the vertices onto the input"; the snap back is one projection step with an exact validity gate instead of smoothing. |
| Cohen et al., *Simplification envelopes*, SIGGRAPH 1996; Wang et al., exact polyhedral envelope, 2020 | from memory (the project's own [ENVELOPE.md](../design/ENVELOPE.md) and [ENVELOPE_GUARANTEED_SIMPLIFICATION_2026-11.md](ENVELOPE_GUARANTEED_SIMPLIFICATION_2026-11.md) are the checked text) | the standard way to keep a result within a tolerance of the input is an envelope, a one-sided guarantee; a result that touches the input needs a two-sided check, which is what `MeshHausdorffDistance` computes after the fact. |
| Ju, *Robust repair of polygonal models*, SIGGRAPH 2004 (PolyMender); Jacobson, Kavan, Sorkine-Hornung, *Robust inside-outside segmentation using generalized winding numbers*, 2013; Barill et al., *Fast winding numbers for soups and clouds*, 2018 | from memory | repair by a volume decision (signed distance or winding number) and a contouring, the surface leaves the input; the winding number as an inside / outside oracle of an open surface is the tool for "is this a solid with a hole". |
| Sacht, Jacobson, *Cascading upper bounds for triangle soup Pompeiu-Hausdorff distance*, SGP 2024 | implemented in the library (`MeshHausdorffDistance`) | the certificate that can replace the wrap's guarantee after a snap back. |

Search terms for "how others solve it": "alpha wrapping offset projection onto input", "shrink wrapping adaptive alpha", "mesh repair keep input faces fill holes
winding number". No source found that snaps an alpha wrap onto the input with an exact validity gate; the closest are the shrink-wrap family (projection plus smoothing) and Dai et al.

## 4. Method (short)

Probe, commands and the self-test: [benchmarks/WrapAccuracyProbe/README.md](../../benchmarks/WrapAccuracyProbe/README.md). Public API only
(`MeshAlphaWrap`, `MeshHausdorffDistance`, `ExactTriangleIntersection`, `TriangleHashGrid`, `MeshRobustRepair.NotCleanReason`,
`MeshRepairMetrics`, `MeshAutoRepair`, `MeshRobustRepair`); no `InternalsVisibleTo` was needed.

- **Wrap:** the raw `MeshAlphaWrap` result (no simplification), `Alpha` = diagonal / 4, 20, 100, `Offset` = diagonal * 1e-3, 1e-4, 1e-5. Thingi10K
  runs use alpha 20 and 100 and offsets 1e-3 and 1e-4 only (the 1e-5 wraps of 17,000-triangle inputs reach 340,000 triangles and 20 s; see section 9).
- **Naive snap:** every vertex to its nearest input point at once. **Safe snap:** the same targets; a vertex moves only while the exact ring
  check holds (no flip, no degenerate triangle, no intersection of the ring with the rest of the surface beyond shared vertices or edges), else
  half, a quarter, an eighth of the way, up to four rounds in a fixed vertex order (deterministic, serial).
- **Error:** two-sided Hausdorff upper bound `max(h(result, input), h(input, result))` from `MeshHausdorffDistance` (tolerance 0.05 offset; a certified
  interval, not the rounding-certified mode), relative to the diagonal; and p50 / p99 of 20,000 area-stratified sample distances each way (the
  Hausdorff distance is the worst point, the percentiles say how much of the surface is affected).
- **Validity:** `MeshRobustRepair.NotCleanReason` (closed, no bowtie, no degenerate triangle of area below 1e-12 diagonal^2, consistent orientation,
  no self-intersection) and an independent self-intersection count (`MeshRepairMetrics`, capped at 1000 pairs).
- **Spike B shares:** area-stratified samples (60,000), tau = 4 offsets for all four methods. Added share = result area farther than tau from the
  input; lost share = input area farther than tau from the result.
- Inputs: 11 procedural solids (box, L-shape, slots of 0.20 / 0.05 / 0.01 of the diagonal, plates of 1e-2 / 1e-3 / 1e-4 of the diagonal, 64-gon cylinder,
  sphere of 12 * 13^2 triangles, the box translated by 2^22 in each coordinate) and 11 hole inputs (box, cylinder, sphere with a removed face, patches of
  1 %, 10 %, 30 % around the top centre or a vertical edge, a slit, plus a bent open sheet); Thingi10K: the 20 T1 meshes and a seeded sample of 32
  ([thingi_selection.md](../../benchmarks/WrapAccuracyProbe/results/thingi_selection.md): seed 2611, 8 per class C / S / H / O, at most 100,000 triangles).
- **Coverage actually run:** Spike A procedural: box, L-shape, slot 0.20 on the full 3 x 3 grid; slot 0.05, slot 0.01 and plate 1e-2 on the full grid
  (first run, same code without the percentile columns); plates 1e-3 / 1e-4, cylinder, sphere and box_far on alpha 4 / 20 / 100 x offset 1e-3 / 1e-4 (the
  1e-5 offset was dropped for them: the certification took minutes per row). Spike A Thingi10K: 15 closed T1 meshes and 16 sample meshes (C and S),
  alpha 20 / 100 x offset 1e-3 / 1e-4 (plus the alpha 4 and offset 1e-5 rows of the first runs for part of the meshes), 197 configurations, no timeouts or failures at the 60 s limit.
  Spike B: the 11 procedural inputs x alpha 4 / 20 / 100 x offset 1e-3 / 1e-4 (66 configurations), 21 open Thingi10K meshes x alpha 20 / 100 x offset
  1e-3 / 1e-4 (84 configurations), no failures.

## 5. Spike A: results

### 5.1 Accuracy and validity

Procedural solids, safe snap against the wrap (the two-sided Hausdorff upper bound relative to the diagonal; all procedural configurations in
[tables_A_procedural.md](../../benchmarks/WrapAccuracyProbe/results/tables_A_procedural.md), a condensed view in
[condensed_A_procedural.md](../../benchmarks/WrapAccuracyProbe/results/condensed_A_procedural.md)):

| input | alpha | offset | wrap H | safe H | safe H / offset |
|---|---|---|---:|---:|---:|
| box | d/20 | 1e-3 | 1.05e-3 | 5.2e-4 | 0.5 |
| box | d/20 | 1e-4 | 1.05e-4 | 1.1e-5 | 0.1 |
| box | d/100 | 1e-3 | 1.05e-3 | 6.4e-4 | 0.6 |
| box | d/100 | 1e-4 | 1.05e-4 | 1.0e-4 | 1.0 |
| box_far (2^22) | d/20 | 1e-4 | 1.05e-4 | 1.1e-5 | 0.1 |
| sphere (faceted) | d/100 | 1e-4 | 4.4e-4 | 3.4e-4 | 3.4 (input facets) |
| cylinder (64-gon) | d/100 | 1e-4 | 3.0e-3 | 1.3e-3 | 13 (input facets) |
| plate 1e-3 | d/20 | 1e-3 | 1.05e-3 | 2.6e-4 | 0.3 |
| plate 1e-4 | d/100 | 1e-4 | 5.5e-4 | 5.6e-4 | 5.6 |
| L-shape | d/100 | 1e-3 | 9.6e-3 | 8.9e-3 | 9 (alpha floor, 0.84 alpha) |
| L-shape | d/100 | 1e-4 | 8.4e-3 | 8.4e-3 | 84 |
| slot 0.20 | d/20 | 1e-4 | 3.7e-2 | 3.6e-2 | 360 |
| slot 0.05 | d/20 | 1e-3 | 1.63e-1 | 1.62e-1 | 160 (slot bridged: depth 0.162 d) |
| slot 0.01 | d/100 | 1e-4 | 1.62e-1 | 1.62e-1 | 1620 (slot about as wide as alpha: bridged) |

Thingi10K, 31 closed meshes (T1 and sample C and S), medians over the meshes (relative to the diagonal;
[tables_A_thingi.md](../../benchmarks/WrapAccuracyProbe/results/tables_A_thingi.md)):

| alpha | offset | wrap p50 | safe p50 | wrap p99 | safe p99 | wrap H | safe H |
|---|---|---:|---:|---:|---:|---:|---:|
| d/20 | 1e-3 | 1.0e-3 | 7.7e-5 | 2.9e-2 | 2.9e-2 | 4.3e-2 | 4.3e-2 |
| d/20 | 1e-4 | 1.0e-4 | 3.2e-7 | 3.0e-2 | 2.9e-2 | 4.2e-2 | 4.2e-2 |
| d/100 | 1e-3 | 1.0e-3 | 1.3e-9 | 4.6e-3 | 3.9e-3 | 1.08e-2 | 1.03e-2 |
| d/100 | 1e-4 | 1.0e-4 | 5.7e-10 | 3.6e-3 | 3.6e-3 | 1.10e-2 | 1.09e-2 |

Validity of the snap back (all configurations; `summarize.py`, summary files in the probe's `results/`):

| | procedural, 86 configs | Thingi10K, 197 configs |
|---|---|---|
| naive: clean by `NotCleanReason` | 20 | 19 |
| naive: with self-intersections (independent count) | 65 | 170 |
| naive: flipped or collapsed triangles, share of the wrap's area | median 0.0007 %, max 0.33 % | median 0.0017 %, max 0.12 % |
| safe: clean and 0 self-intersections | 86 of 86 | 197 of 197 |
| safe: vertices that moved fully to their nearest input point | median 99.2 %, min 81.6 % (plate 1e-3, alpha d/4) | median 99.2 %, min 88.3 % |
| safe H / naive H | median 1.00, max 7.7 (thin plates at coarse offset) | median 1.00, max 1.002 |
| safe snap time / wrap time | median 1.9 (small wraps), max 4.8 | median 0.68, max 3.5 |

The self-test ([results/selftest.txt](../../benchmarks/WrapAccuracyProbe/results/selftest.txt), all pass) checks the ring check on constructed moves and shows by
mutation that the self-intersection check is needed (cylinder: with only that check off the result has self-intersections, found by the independent verdict).

### 5.2 The four hypotheses

| hypothesis (the owner's local probe; not quoted as a result) | verdict | numbers |
|---|---|---|
| 1. Tightening `Offset` alone stalls at a floor set by `Alpha`; time and triangles grow by about 4 to 5 per decade | **floor: confirmed where a concavity or gap is narrower than the ball; growth: partly** | Floor: on the concave solids and on every Thingi10K mesh the wrap error does not change with the offset (L-shape d/100: 9.6e-3 / 8.4e-3 / 7.3e-3 for offset 1e-3 / 1e-4 / 1e-5; slots 0.162 constant; Thingi10K medians 1.08e-2 -> 1.10e-2 over a decade). On the convex solids there is no floor: the error falls with the offset (box d/20: 1.05e-3 -> 1.05e-4). Growth per decade (geometric mean over inputs): Thingi10K, offset 1e-3 -> 1e-4 on 31 meshes: triangles x2.8 (d/100), x4.9 (d/20), x3.6 (d/4), time x3.2, x2.8, x1.6; the step 1e-4 -> 1e-5 (a subset of the meshes) x3.1 to x3.8 triangles, x2.5 to x3.7 time; procedural (6 inputs on the full grid): triangles x1.1 to x2.6, time x0.6 to x2.1 (`summary_A_*.txt`). The 4 to 5 holds for the first decade at d/20 on Thingi10K only. |
| 2. A naive projection removes most of the offset error at a coarse offset, so a cheap wrap plus snap back is as accurate as a wrap with an offset several decades smaller | **confirmed for the typical surface, refuted for the worst point** | p50 at d/100, offset 1e-3 on Thingi10K: 1.0e-3 -> 1.3e-9: the offset error vanishes on at least half of the area (the safe version: the same). The Hausdorff distance does not follow: it stays at the alpha floor on Thingi10K (1.08e-2 -> 1.03e-2) and, at sharp convex edges, at 0.3 to 0.6 offset (box d/20: 5.2e-4 against 1.05e-3; d/100: 6.4e-4), because the triangles that round an edge of the offset surface have vertices about one offset from it and their snapped images still cut the corner. The snap at offset 1e-3 beats the wrap at 1e-5 on 1 to 2 of 6 to 8 inputs in each group (the inputs that have 1e-5 rows), and on Thingi10K it equals the wrap at 1e-4 on 15 of 31 meshes (a coin flip: both sit at the alpha floor); never by "several decades". |
| 3. With a smaller `Alpha` the error after the snap back keeps falling by orders of magnitude | **refuted as stated: it falls about in proportion to alpha, with a step when the ball enters a gap** | Thingi10K safe H median (offset 1e-3): d/4 0.092, d/20 0.043, d/100 0.0103 (9 times for 25 times smaller alpha); p99 0.073 / 0.029 / 0.0039. Procedural concave: slot 0.20 0.162 -> 0.040 -> 0.0086; slot 0.05 stays 0.162 until the ball (d/100 = 0.12) is narrower than the slot (0.62), then 0.0092 (a 17-fold step); slot 0.01 (0.12 wide, as wide as alpha at d/100) stays 0.162 at every alpha tested. After the snap the error is about 0.8 alpha at a re-entrant 90 degree edge (L-shape: 0.0084 at alpha 0.01). |
| 4. The naive projection flips or collapses well under 1 % of the area, so a validity-preserving version loses little | **confirmed for the area; the naive result is nevertheless invalid** | Flipped or collapsed: max 0.33 % (procedural), 0.12 % (Thingi10K). But the naive result is clean in only 20 of 86 and 19 of 197 configurations, and 65 and 170 have self-intersections (counted up to 1000 pairs). The safe version is clean in all of them, with about 99 % of the vertices moved fully, and its error equals the naive one (median ratio 1.00; the exceptions are thin plates at a coarse offset, up to 7.7x, where unmoved vertices keep an offset-sized error). |

Where error remains after the snap: sharp convex edges (0.3 to 0.6 offset); thin features whose two sides snap to the same surface (plate 1e-4: the safe
version leaves up to 9 % of the vertices not fully moved); faceted curved inputs (the wrap's triangles are larger than the input's facets, so sphere and cylinder
keep several offsets set by the facet sag); and every bridged concavity (the alpha floor, which the snap does not touch: a bridged triangle's vertices sit on the
rim, already on or near the input).

### 5.3 Cost

Wrap: Thingi10K median 0.49 s, p90 2.4 s, maximum 20.8 s (mesh 37384, 17,596 triangles, d/100, offset 1e-5, 340,692 triangles; one run on a machine with several probe
processes; the time follows the size of the offset-1e-5 wrap as in the 800 run, so no separate issue was opened). Snap (serial, exact checks): median 0.7 times the wrap time on
Thingi10K, up to 3.5 times where many vertices are rejected (a rejected trial re-runs the ring check, up to four per round). Certifying the result: median 0.5 s per result
on Thingi10K (both directions), up to 15 s; not converged within 40 s in 44 of 387 procedural rows (plates and 1e-5 offsets: the bound is loose there, marked `*` in the tables), none on Thingi10K.

## 6. What the snap back gives up, and what could replace it

- **The wrap's guarantee "the input is strictly inside" is gone** (the result touches the input at every moved vertex), as are the offset band and the "no contact with
  the input" property of `AlphaWrapFeaturePass`. Code that uses the wrap as an envelope or as a shell that can be unioned with the input without coincident faces would
  break; the exact booleans handle coincident faces, with more degenerate cases.
- **What stays, by construction (proven):** by induction over the accepted moves, each gated by exact predicates, the result is closed, has the topology of the wrap, no flipped
  and no degenerate triangle in the sense of the gate, and no self-intersection. The completeness of the check rests on `TriangleHashGrid` returning every triangle whose box meets the
  box of a ring triangle and on `IntersectsBeyondShared` being exact. The independent check (`NotCleanReason`, `MeshRepairMetrics`) agreed in all 283 configurations: evidence for the
  implementation, not a proof. The wrap's topology (genus, components, bridged cavities) is kept: the snap cannot repair a spurious handle or reopen a bridged slot.
- **Not kept, and not checked by the probe:** the wrap's angle floors (the sharp-feature pass checks them; the probe forbids only flips and degenerate triangles), so triangle quality after the snap is
  unmeasured here.
- **What could replace the guarantee:** a certified two-sided Hausdorff bound from `MeshHausdorffDistance` (with `CertifiedUpper`, rounding included): "result and input are within eps of each other",
  computed after the fact: provable for the answer it returns, measured for its cost (section 5.3). An a priori bound is not available: result -> input follows from the vertices lying on the input only through
  triangle deviation bounds, and input -> result is the alpha floor, which depends on the input's concavities. A one-sided "inside" guarantee could be restored only by a different construction (a two-sided envelope
  check per move, as the feature pass does), at a cost in moved vertices that this probe did not measure.

## 7. Spike B: results

Procedural solids with holes, alpha d/4, d/20, d/100 at offset 1e-4 ([condensed_B_procedural.md](../../benchmarks/WrapAccuracyProbe/results/condensed_B_procedural.md), all 66 configurations in
[tables_B_procedural.md](../../benchmarks/WrapAccuracyProbe/results/tables_B_procedural.md)); added / lost share at tau = 4 offsets; the hole is the removed share of the solid's area:

| input | hole | alpha | wrap added / lost | wrap volume / solid | patches (matched) | patch / hole area | auto added | robust added / lost (stage) |
|---|---:|---|---|---:|---|---:|---:|---|
| box, top face removed | 25.3 % | d/4 | 34.8 % / 0 | 0.64 | 1 (1) | 1.6 | 25.3 % | 25.3 % / 0 (Repair) |
| | | d/20 | 11.3 % / 0 | 0.04 | 1 (1) | 0.65 | | |
| | | d/100 | 2.2 % / 0 | 0.00 | 1 (1) | 0.14 | | |
| box, patch 1 % | 1.0 % | d/4 | 1.0 % / 0 | 1.00 | 0 (0) | 0 | 1.0 % | 1.0 % / 0 (Repair) |
| | | d/20 | 14.5 % / 0 | 0.06 | 4 (3) | 27 | | |
| | | d/100 | 2.9 % / 0 | 0.00 | 7 (6) | 5.9 | | |
| box, patch 10 % | 10.0 % | d/4 | 9.9 % / 0 | 1.00 | 2 (2) | 0.97 | 10.0 % | 10.0 % / 0 (Repair) |
| | | d/20 | 16.2 % / 0 | 0.06 | 8 (8) | 2.8 | | |
| | | d/100 | 3.3 % / 0 | 0.00 | 14 (11) | 0.62 | | |
| box, patch 30 % | 30.1 % | d/4 | 40.7 % / 0 | 0.63 | 5 (5) | 1.6 | 29.9 % | 29.5 % / 0.3 % (Solidify) |
| box, edge patch 10 % (non-planar rim) | 10.0 % | d/4 | 7.0 % / 0 | 0.96 | 1 (1) | 0.68 | 6.9 % | 6.0 % / 0.1 % (Solidify) |
| box, slit | 0.9 % | d/4, d/20 | 1.0 %, 0.9 % / 0 | 1.00 | 2 (2), 1 (1) | 1.5, 1.1 | 0.9 % | 0.9 % / 0 (Repair) |
| sphere, patch 10 % | 10.0 % | d/4 | 10.2 % / 1.0 % | 0.98 | 9 (9) | 0.96 | 9.3 % | 9.3 % / 0 (Repair) |
| sphere, patch 30 % | 30.0 % | d/4 | 50.0 % / 0.6 % | 0.01 | 12 (12) | 2.5 | 23.8 % | 24.3 % / 0.3 % (Solidify) |
| bent open sheet | - | d/4 | 17.1 % / 0 | - | 26 (26) | - | 33.0 %, self-intersections | 50.7 % / 6.6 % (Solidify) |
| | | d/20, d/100 | 0 / 0 | - | 0 (0) | - | | |

- **Patches against boundary loops.** When alpha is at least about the size of the hole (d/4 for every hole up to 30 %), the made-up surface is a small number of patches, all matched to the hole rim
  (unmatched area 0), but it is not the cap: its area is 0.65 to 1.6 times the hole's for a face and patches of 10 % or more, and 27 times for a 1 % hole at d/20 (the ball enters and the wrap
  wraps around the rim). When alpha is well below the hole (d/20, d/100) the ball enters the solid through the hole: the wrap is a thin shell (volume 0.00 to 0.08 of the solid), there is no cap
  (patch / hole area 0.014 to 0.62 for the larger holes), and at offset 1e-3 the far area is 90 to 99 % unmatched fragments (93 to 180 patches) of the interior side that the ball could not reach.
  Bridged concavities that are not holes show up as unmatched patches (Thingi10K below).
- **Hole area, cap, and the existing repair.** `MeshAutoRepair` adds exactly the hole (lost 0; clean on 60 of 66 configurations, unclean only on the sheet); `MeshRobustRepair` returns the `Repair`
  stage for holes up to 10 % (lost 0, added = the hole) and `Solidify` for the 30 % and edge patches (lost 0.1 to 0.3 %, added within 1.5 points of the hole). So on valid solids with holes **the hybrid
  ideal already exists**: every input triangle kept, one cap added.
- **Thingi10K, 21 open meshes** ([summary_B_thingi.txt](../../benchmarks/WrapAccuracyProbe/results/summary_B_thingi.txt), [tables_B_thingi.md](../../benchmarks/WrapAccuracyProbe/results/tables_B_thingi.md));
  medians over meshes and the two offsets, class H = small holes (8 sample + 3 T1), class O = sheets and soups (8 + 2):

| class | alpha | wrap added / lost | snap added / lost | `MeshAutoRepair` added / lost (clean configs) | `MeshRobustRepair` added / lost (clean configs) |
|---|---|---|---|---|---|
| H | d/20 | 27.1 % / 38.9 % | 26.3 % / 37.5 % | 0 / 0 (2 of 22) | 0 / 0, max 1.2 % / 18.4 % (22 of 22) |
| H | d/100 | 5.8 % / 12.1 % | 4.4 % / 10.9 % | 0 / 0 | same |
| O | d/20 | 16.8 % / 30.3 % | 16.6 % / 28.3 % | 0.1 % / 0 (0 of 20) | 0.2 % / 1.0 %, max 56.6 % / 72.0 % (20 of 20) |
| O | d/100 | 2.9 % / 5.0 % | 2.1 % / 4.8 % | 0.1 % / 0 | same |

  For Thingi10K the wrap's far area is mostly not hole patches: at d/100 the median wrap has 96 far patches of which 3 are matched to a boundary loop, 95 % of the far area is unmatched (bridged concavities
  and thin features, the alpha floor again), and the wrap loses 5 to 39 % of the input's area (input farther than 4 offsets from the result): it smooths what the ball cannot enter. The snap back does not change this
  (the shares move by 1 to 2 points). `MeshRobustRepair` loses more than 5 % of the area on 5 of 21 meshes at (d/100, 1e-4) (42331 0.16, 37731 0.14, 1038438 0.47, 66112 0.18 by `ExactResolve`; 100040 0.72 by
  `Solidify`), the wrap on 18 of 21.
- **A criterion separating "a solid with a hole" from "an open sheet".** The wrap's deep volume share (share of the wrap's volume farther than 4 offsets from the input): sheet 0 to 6.5 % over all six
  configurations (6.5 % only at d/4 and offset 1e-4), solids with holes 93 to 100 % at d/4 (34 % for the 30 % sphere patch at offset 1e-3, 68 % at 1e-4), at d/20 34 to 58 % (offset 1e-3) and 89 to 95 % (offset 1e-4) for the box and cylinder
  holes but 0 to 17 % for the spheres, and 0 to 3 % at d/100 with offset 1e-3 (38 to 50 % at 1e-4 for the box holes). So the margin is large (sheet at most 6.5 %, holes at least 34 %) only when alpha is of the order of the hole or larger, and the offset small; for alpha
  well below the hole the wrap is a shell and the criterion cannot tell the cases apart (sheet 0 %, solid 0 to 3 %). The collapse of the snapped volume does not help: the snapped volume is the wrap's (a shell) in both
  cases. A usable criterion is the generalized winding number of the input at the wrap's deep interior (not measured here), or whether the hole-filling stage of the repair succeeds (it decides this today).

## 8. Is a hybrid feasible, and what would it cost

Feasible with existing blocks, not worth building first:

- **Blocks:** the wrap supplies a candidate inside and patches; the snap back puts the patch vertices on the input's rim; `ExactMeshImprint` / `ExactMeshBoolean` imprint the rim vertices into the input's boundary edges and
  weld; `MeshHausdorffDistance` certifies the result; `MeshAutoRepair` / `MinimalHoleFill` already supply the cap for a clean rim.
- **The hard part:** a watertight seam. The wrap's patch ends about one offset from the rim; the snapped patch ends on the rim at the wrap's vertices, which are not the input's boundary vertices; stitching needs
  T-junction free imprints of both loops, a decision per hole (which wrap triangles belong to which loop, and which are bridged concavities that must stay input), and the case that the ball entered (alpha below the hole),
  where there is no cap to cut out of the wrap. Second: the "solid or sheet" criterion per connected component (section 7). Third: the weld of rounded points on the input (the snapped vertices are not on the exact planes of its triangles).
- **First version (estimate, not measured):** the snap back as an opt-in post-pass: the probe's check is about 150 lines on top of the existing grid and exact predicates; 2 to 4 days to move it into the library with the tests CLAUDE.md
  asks for (invariance over `Similarity.All()`, reuse, determinism), angle floors and a certified-Hausdorff report option. The hybrid for holes on top: about 2 to 3 weeks (seam, component criterion, tests with holes of every kind) plus a corpus run.
- **Why not first:** on valid solids with holes `MeshRobustRepair` already keeps every input triangle (Repair or Solidify stage, lost at most 0.3 %); the hybrid's room is the inputs where the repair falls to `ExactResolve` or
  `Solidify` and loses area (5 of 21 open Thingi10K meshes, up to 72 %), and the wrap would not be better there (it loses 5 to 39 %). The wrap's strength, an input that cannot be repaired, is exactly where its patches are not caps (the ball bridges or enters).

## 9. Proven, measured, not done

- **Proven (by construction, given exact predicates):** the validity of the safe snap back (section 6). The Hausdorff intervals are the library's certified intervals in the default mode, not the rounding-certified `CertifiedUpper`.
- **Measured only:** every accuracy number, the four hypotheses, the shares, the criterion margins, the costs. The percentiles and shares are sample statistics (20,000 and 60,000 points).
- **Not done / not verified:** angle quality after the snap; the rounding-certified Hausdorff mode; a wrap simplified as `MeshRobustRepair` runs it (the probe uses the raw wrap, so the triangle counts are the raw ones); offset 1e-5 on
  Thingi10K and for the plates, cylinder, sphere and box_far (dropped for time: 17,000-triangle meshes give 340,000-triangle wraps and the certification took minutes; the 1e-5 rows exist for box, L-shape, slot 0.20, slot 0.05, slot 0.01 and plate 1e-2);
  the generalized-winding-number criterion; any prototype of the hybrid; adaptive alpha (not reopened); Dai, Wang and Yan was not read (abstract only). Timings come from a loaded machine (up to four probe processes plus other sessions), so only ratios are meaningful.
  The sphere and cylinder areas used for the hole-to-alpha ratios in `summarize.py b` are approximate. The first run of Spike A (without percentile columns) and of Spike B (before the far test of a triangle used four points) are kept as
  `*_run1_partial.jsonl`; the tables use the second run where both exist and the first run for slot 0.05, slot 0.01 and part of plate 1e-2 and the 1e-5 rows (same code otherwise); `spikeA_proc_run1_partial.jsonl` was cut when the process was stopped.
- `condensed_*.md` and `make_condensed_*.py` in `results/` are generated from the JSONL files (run from the probe directory).

## 10. Options for the owner, recommendation, go / no-go

| option | what | cost | risk | how others solve it |
|---|---|---|---|---|
| A0 | Do nothing; document that after any projection the accuracy floor is alpha at concavities and 0.3 to 0.6 offset at convex edges | 0.5 day | none | the CGAL documentation describes the offset as the accuracy knob |
| A1 | Opt-in post-pass of the wrap: the safe snap back with the exact ring check, angle floors added, a certified-Hausdorff report | 2 to 4 days | the contract changes only for opt-in users; the CLAUDE.md tests are redone for the pass | shrink wrapping projects with regularisation (Kobbelt 1999); Dai et al. 2024 project along normals with an adaptive alpha |
| A2 | A1 with a kept band (move to 0.1 offset from the input instead of onto it) | +1 day | little gain: the measured gain is the offset error only | envelope-style tolerance (Cohen 1996), the feature pass |
| B1 | Hybrid of wrap patches and input (section 8) | 2 to 3 weeks | high: seam, component criterion; little to gain where `MeshRobustRepair` already keeps the input | repair by volume decision and contouring (PolyMender) leaves the input; keeping input triangles is what hole fillers do |
| B2 | Diagnose the failing repair stages instead (`ExactResolve` / `Solidify` losing 14 to 72 % on 5 of 21 meshes) | 1 to 2 days of measurement | low | - |

**Recommendation:** A1 only as an opt-in follow-up if a user needs a result that touches the input and accepts losing "the input is inside". Go if (a) the snapped result is clean on at least 99 % of an 800-mesh run (it was
clean in 283 of 283 configurations here), (b) the angle floors can be kept (not measured) with at least 95 % of the vertices still moving fully, and (c) a certified Hausdorff report within the wrap's time budget is acceptable
(median 0.5 s, up to 15 s here). No-go if (b) fails or if the owner wants "the input is inside" kept: then the feature pass (contract kept) is the right tool. Do not expect the snap back to beat the alpha floor: it does nothing
at bridged concavities (Hausdorff unchanged on the 31 Thingi10K meshes at the median, 8.9e-3 -> 8.4e-3 on the L-shape), so for the owner's observation (never equal to a clean input) the answer is: the typical surface yes
(p50 reaches 1e-9), the worst point no.

B1: **no-go now.** Go criterion for a later look: a corpus in which `MeshRobustRepair` loses more than 5 % of the area on at least 5 % of the open meshes with holes that a cap could fix (here 5 of 21 meshes lose it, but those are
`ExactResolve` / `Solidify` cases, not caps; B2 first decides whether they are a hole problem at all). A prototype of B was not built: the measurement does not support it.

## Sources

- Portaneri et al. 2022 (as in [SOURCES.md](../SOURCES.md) and [ALPHA_WRAP_PAPER_NOTES.md](../design/ALPHA_WRAP_PAPER_NOTES.md)); Sacht and Jacobson, SGP 2024 (implemented, as in SOURCES.md); Kobbelt et al., CGF 1999 and the 2024 CAGD
  paper on adaptive alpha (abstract level, via web search on 2026-11-24); Cohen et al. 1996, Ju 2004, Jacobson et al. 2013, Barill et al. 2018 (from memory). No code of any of them was read or ported; nothing is added to the library.
- Thingi10K (https://ten-thousand-models.appspot.com/, https://huggingface.co/datasets/Thingi10K/Thingi10K; models have individual licenses): a read-only local copy was used, the ids of the selection are in
  [thingi_selection.md](../../benchmarks/WrapAccuracyProbe/results/thingi_selection.md); no mesh is copied or committed, results name ids and numbers only.
