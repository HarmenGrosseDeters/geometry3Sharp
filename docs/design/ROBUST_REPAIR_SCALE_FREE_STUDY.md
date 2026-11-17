# Scale-free robust repair: study and go / no-go (hard problem 9, 2026-11-17)

**Question (owner, 2026-11-17).** Is a larger effort on a scale-free `MeshRobustRepair` worth it: how much could a repair whose
cleanups do not depend on the global box win at most, on real data and on scale-disparity cases, and what would it cost?

**Answer.** **No-go for a larger effort**, by criteria fixed before the corpus run (`study/CRITERIA.md` on the data branch).
- **Part-wise repair** (the library's repair run per part, as an estimate of what option B could win, section 2): on 800 Thingi10K
  meshes it gains 2 strict repairs (closed parts only, net 0) or 5 (all components, 13 lost; 7 gained and 1 lost when degeneracy is
  judged per own component, option D's view). The pre-fixed bar was 8.
- **Tolerances and translation**: one global factor on the repair stage's tolerances (x0.01, x100) and a translation to 5e6 gain
  nothing.
- **Disparity on the corpus**: the 52 inputs with two or more box-rule parts have no small separable part (the smallest is 8.46 % of
  the scene). Small components exist, but inside one part (down to 1e-7 of the scene), where no oracle here reaches.
- **The misses**: of the 252 strict misses, 242 are Hausdorff misses; the scale oracles repair 2 of those.
- **What remains** is the open half of the owner's decision 2 of 2026-11-10 ("the loss of a part is reported"): on procedural scenes
  29 of 54 still lose a small part with `Apply()` true, in three different stages (section 3.1). The minimal fix is to report the loss
  (F1, S); preventing it needs per-part references in several cleanups, which is the core of the larger effort and is not
  recommended now. Recommendation in section 7.

Builds on [ROBUST_REPAIR_SCALE_FREE.md](ROBUST_REPAIR_SCALE_FREE.md) (#547; options A to D, the stage map of 2026-11-10 and the owner
decisions of 2026-11-10). Already decided or built, not repeated here as new: A, the working frame for 2^k scaling, with the wrap
guard G (#560); B stage 0, clean closed parts with disjoint boxes pass through (#578); "no silent dust deletion, the loss of a part is
reported"; C and D "not now". No library code was changed for this study. Raw data, probe and scripts are on the data branch
`runs/scale-free-study` (never merged).

## 1. Inventory on master 86e94029

Every tolerance, cleanup and threshold of `Apply()` that is relative to a global size, after A, G and B stage 0. "Scene" is the
diagonal of the bounds of all triangles, "largest" the diagonal of the largest connected component, "own" the component's own
diagonal; A has scaled everything into a frame of extent [1, 2), so no absolute constant is left in the path (the S class of
#547 is closed by construction). Line numbers are of 86e94029.

| # | Stage, quantity | Relative to | Local / scale-free formulation | Exact predicates cover it? | Measured here |
|---|---|---|---|---|---|
| 1 | Entry: range check `[2^-200, 2^400]`, extent >= 2^-40 of the magnitude (`MeshRobustRepair.cs:904`) | magnitude (by design) | none needed: it is the domain of the doubles | -- | -- |
| 2 | Entry: working frame (A), `unit_scale_for_mesh` | scene (power of two) | none needed: exact | yes (exact scaling) | -- |
| 3 | Far-part switch `global_scale_share` (`:1787`): >= 2 components and a triangle below 1e-12 scene^2 | scene | per component: exactly degenerate, or below 1e-12 own^2 | partly: an exact collinearity test exists (`NotCleanReason`'s second test) | corpus: fires on 234 inputs, only 57 of them with a triangle degenerate by the scene but not by its own component (3.2); gates the lost-part check (`:629-634`) |
| 4 | B stage 0 (split `:480`), the union check (`:528-546`): `NotCleanReason` of the union, else the whole input again | scene (via `NotCleanReason`) | judge the union per part (closed, manifold, no intersections exactly; degeneracy per own component) | yes but for the area test | procedural: the fallback that leads to the silent loss of section 3.1 |
| 5 | `MeshAutoRepair` `RelativeRepairTolerance` 1e-8 (crack merge 1x and 2x, boundary collapse 0.5x, `sizeTolerance`, occlusion offset), `RelativeMinEdgeLength` 1e-6 (final collapse), cap 0.01 median edge of the whole mesh; far-part mode: largest (`MeshAutoRepair.cs:102`) | scene / largest | own component, or the local edge length (option C) | no: merging near, not equal, vertices needs a distance | S / L oracles (x0.01, x100) |
| 6 | `MeshRepairOrientation` degenerate area 1e-10 scene^2, ray length 2 scene | scene | exact orientation of each triangle (orient3d); the ray length is harmless | yes for the degeneracy | not separated |
| 7 | `MeshPostRepairCleanup` `DegenerateAreaFactor` 1e-12 `ReferenceDiagonal`^2 (scene; largest in the far-part mode, which needs a closed input with at most 64 components) | scene / largest | own component | yes, as in 6 | procedural: collapses the open small parts (v2: 314 triangles to 4, traced on one scene) |
| 8 | `NotCleanReason` (public) degenerate area 1e-12 scene^2 | scene, by definition | option D (opt-in criterion next to it) | yes, as in 6 | column "own-component degeneracy" of section 3 |
| 9 | Volume check: IoU >= 0.99 of the whole solid | scene volume | per part (a part below 1 % of the volume is invisible today) | no (sampling), but a per-part lost check is exact enough | part criterion of section 3 |
| 10 | Lost-part check: closed parts of at least `MinComponentDiagonal` 1e-3 of the largest (`MeshRepairMetrics.cs:702`), only when row 3 fires; open components never | largest | every separable part (closed or open), judged against its own diagonal | -- | procedural: lets all 29 losses of 3.1 through |
| 11 | Exact resolve (`ExactMeshBoolean.Resolve`) | none | -- | yes | -- |
| 12 | Safe rounding (`MeshRoundingRepair.component_scales`, `:886`): drift bound `MaxDisplacement` 1e-5 x largest for every component (only the ulp term is per component); degenerate area 1e-12 x scene^2, or largest^2 when the scene box exceeds `FarApart` 100 x largest; closed components that keep a degenerate triangle or were edited are removed when tiny (area below 1e4 x 1e-12 x largest^2) or thin (`RemoveTinyComponents`) | largest / scene | bound and areas per own component: the drift proof is already per component ("edits stay within one component"); but the comment at `:899-912` records a measured regression when the area reference shrank from the box to the largest component | the rounding is the step out of exact arithmetic; its edits are exact-checked | procedural: removes the clean small boxes (v0, tiny test; 3.1) |
| 13 | After the resolve: `MeshPostRepairCleanup` + `collapse_slivers` (length 1e-4 x scale, closed components below 1e-8 x scale^2 with a sliver removed, `:1553`) | scene / largest | own component | as 6 | procedural: probably removes the rest of the open parts (v2; not traced to the line) |
| 14 | `SeparateTouchingVertices` 1e-4 of the fan distance | local | -- (already local) | -- | -- |
| 15 | Solidify: cell = scene / 128 (adaptive octree, `MeshWindingNumberSolidify.cs:46`) | scene | per part (B), or refinement by local feature size inside a part | no (resampling) | G (256) and P / Q |
| 16 | Alpha wrap: alpha = scene / 300, offset = scene / 3000; `WrapTooCoarse` guard at 5 % of largest (G) | scene | per part (B); adaptive alpha inside a part is research (#547 section 4) | no (resampling) | P / Q |
| 17 | Wrap simplification: bound 0.5 offset | follows 16 | follows 16 | -- | -- |

Reading: after A, the scene-relative quantities that can delete or erode a part are 3, 4, 5, 7, 10, 12, 13, 15 and 16. The measured
silent losses happen in three of them: 12 (the rounding's tiny test: clean closed boxes), 7 (the repair stage's cleanup: open parts,
because the far-part mode is off for open inputs) and 13 (the rest of those). 3, 4 and 10 are why they are silent. Row 5 was probed
only with one global factor (S / L), which changes nothing measurable. 15 and 16 are resolution: part-wise repair fixes them between
parts, and nothing cheap fixes them inside one part.

## 2. Method

**Oracles** (estimates, not proven upper bounds: P and Q run the library's own repair per part, and only where the rule gives two or
more parts; disparity inside one part is out of their reach) (`benchmarks/MeshRepairBenchmark/ScaleFreeProbe.cs` and `scale_free_study.py` on `runs/scale-free-study`; probe code
only on that branch): R the library defaults; P every part of the box rule of option B stage 1 (closed components linked when their
boxes meet; an open component makes the whole input one part) repaired by its own `MeshRobustRepair`, results appended; Q the same
with every component (open ones too) linked by its box padded by 1e-3 of its own diagonal (an estimate for stage 2); S / L the
relative tolerances of the repair stage (`RelativeRepairTolerance`, `RelativeMinEdgeLength`, row 5 only, still capped by 0.01 of
the median edge) times 0.01 / 100, a global factor and not a local formulation; G solidify resolution 256; T the input translated by (5e5, 5e6, 0).
P, Q and G rows are copied from R where they cannot differ (one part; no solidify). Components are those of
`MeshConnectedComponents` after the STL reader's default weld, not after the exact weld that #547's gate for B stage 1 names (the
reader welds identical positions, so the difference is limited to soups the reader does not weld).

**Measures.** The strict criterion of `criteria.py` (clean, no exact self-intersections, Hausdorff < 1 % of the scene diagonal both
ways, IoU > 0.99), which is itself scene-relative; next to it two scale-free ones: the same criterion with the degenerate area per
own component (option D's view), and a **part criterion**: no closed input component (at least 4 triangles) has a vertex more than
1 % of its own diagonal from the result, nor a result vertex charged to it (nearest input triangle) more than 1 % of its diagonal
away. "Lost" is more than 25 % of the own diagonal. Both directions are samples (64 vertices per component forward, up to 20000
result vertices backward), and a component absorbed into another one's solid, or a lost feature of a component, counts as lost:
the measure is a screen, not a verdict.

**Data.** Thingi10K, the first and the holdout selection of `fetch_data.py` (800 meshes), fetched at run time on GitHub-hosted
runners; 24 shards, timeout 600 s per mesh and configuration. Procedural: a unit icosphere (coordinates on 2^-12) plus a part of half
size 2^-r (r = 4 .. 24) at distance 2^f (f = 2, 8, 16): v0 a clean box, v1 two overlapping boxes, v2 an open sphere; run locally
(net10.0 Release, linux x64).

## 3. Measured gains (estimates of the upper bound)

### 3.1 Procedural scale disparity (54 scenes)

Per cell: `Apply()` true / false; **R** strict criterion met, **D** met with own-component degeneracy, **m** neither; **x** a
component lost. Columns R P Q S T for f = 2 | 8 | 16.

```
v0 (clean box)
r= 4 TR. TR. TR. TR. TR. | TR. TR. TR. TR. TR. | TR. TR. TR. TR. TR.
r= 8 TR. TR. TR. TR. TR. | TR. TR. TR. TR. TR. | FD. TD. TD. FD. FD.
r=12 TR. TR. TR. TR. TR. | TR. TR. TR. TR. TR. | FD. TD. TD. FD. FD.
r=16 TR. TR. TR. TR. TR. | Tmx TD. TD. Tmx Tmx | FD. TD. TD. FD. FD.
r=20 Tmx TD. TD. Tmx Tmx | Tmx TD. TD. Tmx Tmx | Tmx TD. TD. Tmx Tmx
r=24 Tmx TD. TD. Tmx Tmx | Tmx TD. TD. Tmx Tmx | Tmx TD. TD. Tmx Tmx
v1 (two overlapping boxes)
r= 4 Tm. Tm. Tm. Tm. Tm. | TR. TR. TR. TR. TR. | TR. TD. TD. TR. TR.
r= 8 TR. TR. TR. TR. TR. | TR. TR. TR. TR. TR. | FD. TD. TD. FD. FD.
r=12 TR. TR. TR. TR. TR. | TRx TD. TD. TRx TR. | FD. TD. TD. FD. FD.
r=16 Tmx TD. TD. Tmx Tmx | Tmx TD. TD. Tmx Tmx | FD. TD. TD. FD. FD.
r=20 Tmx TD. TD. Tmx Tmx | Tmx TD. TD. Tmx Tmx | Tmx TD. TD. Tmx Tmx
r=24 Tmx TD. TD. Tmx Tmx | Tmx TD. TD. Tmx Tmx | Tmx TD. TD. Tmx Tmx
v2 (open small sphere)
r= 4 TR. TR. TR. TR. TR. | TR. TR. TR. TR. TR. | Tmx Tmx TD. Tmx Tmx
r= 8 TR. TR. TR. TR. TR. | TR. TR. TR. TR. TR. | Tmx Tmx TD. Tmx Tmx
r=12 TR. TR. TR. TR. TR. | Tmx Tmx TD. Tmx Tmx | Tmx Tmx TD. Tmx Tmx
r=16 Tmx Tmx TD. Tmx Tmx | Tmx Tmx TD. Tmx Tmx | Tmx Tmx TD. Tmx Tmx
r=20 Tmx Tmx TD. Tmx Tmx | Tmx Tmx TD. Tmx Tmx | Tmx Tmx TD. Tmx Tmx
r=24 Tmx Tmx TD. Tmx nc  | Tmx Tmx TD. Tmx nc  | Tmx Tmx TD. Tmx nc
```

- **Silent loss on master: 29 of 54** (`Apply()` true, a component of the input more than 25 % of its own diagonal from the result:
  7 v0, 9 v1, 13 v2), against the open half of owner decision 2 of 2026-11-10. One case (v1, r = 12, f = 8) passes the strict
  criterion with a part lost (a part 2^-12 of the scene is inside its 1 % Hausdorff).
- **Already known, now traced.** #547 3.1 measured the same scene family (deletion with true for v0 to v2, part by part as if
  alone, D3 the only obstacle) before A, G and B stage 0. New here: #547 5.2 expected stage 0 to remove the deletion of clean far
  boxes (v0); after #578 they are still lost (f = 8, r >= 16; f = 16, r >= 20). The paths, public API only:
  - **v0** (a scene with r = 20, f = 12): B stage 0 passes the box, but the union fails `NotCleanReason` (row 4: its triangles are
    degenerate by the scene), so the input is repaired as a whole. The repair stage's result is not clean ("degenerate triangles");
    the exact resolve's safe rounding (`ExactResolveRounding`: 2 moves, `RemovedComponents` 1) removes the box as a tiny component
    (row 12; sw_v0_r16_f8 shows it is the tiny test, not the drift bound: its box diagonal 5.3e-5 is above the bound 3.5e-5, its
    area 5.6e-9 below 1e4 x 1e-12 x largest^2 = 1.2e-7). The lost-part check skips it (row 10).
  - **v2** (r = 4, f = 16): the input is open, so the far-part mode is off; `MeshAutoRepair`'s cleanup (row 7, relative to the scene)
    collapses the small sphere from 314 triangles to 4, the rounding edits nothing (`ExactRoundingEdits` [0, 0], as at f = 16 for
    r <= 16), and the 4 triangles are gone after the post-resolve cleanup (row 13, not traced to the line). In the other 9 v2 losses
    the rounding does make edits (4 to 53), so it may take part there; not traced. The rounding's tiny-component removal and the
    lost-part check skip open components.
  - **v1** r = 12 f = 8 and r = 16 f = 2: the far-part switch (row 3) does not fire, so no lost-part check runs.
  So no single change of one row removes all 29; reporting needs a lost-part check over every separable part (F1, section 6).
- **P** (closed parts alone) keeps every closed part; **Q** (all components) keeps every part, open ones too: 54 / 54 without a
  loss. Where Q is not strictly clean, it is clean by own-component degeneracy (**D**) in every cell but one (v1, r = 4, f = 2, a
  miss in every configuration). So on these scenes a part-wise stage wins almost everything by the part criterion and nothing by
  the strict criterion, which is scene-relative (D3 of #547).
- **S** (tolerances x 0.01) changes nothing. **T** (translation) changes 4 cells: v1 r = 12 f = 8 keeps its part, and the three "nc"
  are a 2^-24 part at 5e6, about 2^-46 of its magnitude, near the resolution of the doubles (row 1's limit is 2^-40 for the whole
  scene, not per part).

### 3.2 Corpus (800 Thingi10K meshes)

Run 44123357838 (24 shards, probe on master 86e94029), raw rows and summary in `results/run-44123357838` of the data branch;
`scale_free_study.py analyze`. Gained / lost are paired against R on the strict criterion.

| | first | holdout | all |
|---|---|---|---|
| repaired (strict), R | 277 | 271 | 548 |
| misses | 123 | 129 | 252 (Hausdorff 242, IoU 10) |
| misses by path: kept / exact / solidify / wrap | | | 12 / 136 / 85 / 19 |
| inputs with >= 2 components | 233 | 212 | 445 |
| inputs with >= 2 parts, box rule (B stage 1) | 29 | 23 | 52 (misses among them: 9) |
| inputs with >= 2 parts, padded rule (stage 2) | 68 | 65 | 133 (misses: 50) |
| smallest part of a multi-part input (box rule), share of the scene diagonal | | | >= 0.0846 in all 52 (smallest component inside them: 9.9e-5) |
| far-part switch fires (row 3) | 125 | 109 | 234 |
| ... with a triangle degenerate by the scene but not by its component | 31 | 26 | 57 |
| far-part mode used (`FarPartsHandled`) | 10 | 5 | 15 |
| B stage 0 passed parts through | 25 | 27 | 52 |
| `Apply()` false, `LostFarPart` | | | 2 |

| oracle | run / copied | repaired | gained | lost | net | repaired, own-component degeneracy (R: 548) | part criterion ok (R: 678) | wall of the rows run (s), R on the same meshes |
|---|---|---|---|---|---|---|---|---|
| P (closed parts alone) | 52 / 748 | 548 | 2 | 2 | 0 | 550 | 677 | 275 / 268 |
| Q (all components, padded) | 133 / 667 | 540 | 5 | 13 | -8 | 554 | 677 | 1064 / 965 |
| S (tolerances x0.01) | 800 / 0 | 548 | 0 | 0 | 0 | 548 | 678 | 4599 / 4644 |
| L (tolerances x100) | 800 / 0 | 545 | 0 | 3 | -3 | 545 | 676 | 4582 / 4644 |
| G (solidify 256) | 108 / 692 | 553 | 5 | 0 | +5 | 553 | 681 | 1981 / 1246 |
| T (translated to 5e6) | 800 / 0 | 547 | 0 | 1 | -1 | 547 | 677 | 4583 / 4644 |

- **Gains of the part oracles**: 100032 and 95777 (P and Q: the solidify of the whole had IoU < 0.99; per part it passes), 1619332
  and 56535 (Q: exact path, Hausdorff), 697602 (Q). G gains 100032 and 697602 too: two of the five scale gains are also
  resolution gains. In the own-component view Q gains 7 (the five, 55134, 55136) and loses 1 (63467), P gains 2 and loses 0. **Losses of Q**: 12 of 13 are "not clean (degenerate by the scene only)". The per-part results are clean, but
  the union of parts at their own scale has triangles below 1e-12 of the scene's squared diagonal. That is D3 of #547, the public
  criterion. The 13th, 63467, is a Hausdorff loss: open pieces repaired apart are no longer merged with their neighbours.
- **Part criterion**: no scale oracle gains more than 2 (Q 2 / 3 lost, P 0 / 1; their union gains 4); G gains 5 and loses 2. R fails
  it on 122 meshes.
- **Silent losses on the corpus**: 28 meshes return `Apply()` true with a closed component more than 25 % of its own diagonal from
  the result (46 with open components counted too). None is a separable part: all 28 are one part by the box rule (overlapping or
  nested boxes). 17 have IoU >= 0.99 (eg 50405: 68 of 70 components, IoU 1.0000), which fits components absorbed into the union;
  the others do not: 51323 and 51324 are one component each, solidified with IoU 0.92 / 0.93 (eroded geometry), and 77958, 79136 and
  87345 are wrap results with IoU 0.53, 0.07 and 0.28 and `Apply()` true (the wrap fallback is accepted without a volume check by
  design, #547 G; worth its own look, not a scale question). The 28 were not inspected one by one. A lost-part check over every
  component would flip 46 results to false, 4 of them strict repairs: F1 must judge separable parts only.
- **Tolerances**: S changes no outcome; L loses 3 (eg 318843: exact -> solidify). A global factor on row 5 is not what fails on these
  inputs; rows 6, 7, 12 and 13 were not varied, so this is weak evidence against option C, not a measurement of it.
- **Translation**: the strict outcome differs on 1 mesh (1037029, exact -> solidify, IoU), the path on 7 (#547 section 3.2 measured
  the resampling stages' translation dependence).
- **Against the criteria**: 1 no-go (P / Q gain 2 / 5 < 8, 7 in the own-component view; part criterion 0 / 2 < 8); 2 no-go (S 0,
  L -3); 3 no follow-up (1 < 4); 4 does not trigger (G gains 5, as many as the scale oracles; G ran on 108 meshes at +59 % of R's
  time on them); 5 the procedural silent losses are a bugfix. This run also answers #547's gate before B stage 1 ("count how many
  inputs have two or more stage-1 parts, and how many of the strict misses do"): 52 of 800, and 9 of the 252 misses.
- **What the 242 Hausdorff misses are** is outside this study. 136 of them are on the exact path, which keeps the geometry, so the
  deviation is to parts of the input surface that the solid does not contain. #547 and the benchmark README list such cases
  (fins, inner shells); the rest are solidify and wrap resampling. Not analysed further here.

## 4. Prior art (papers only; what they guarantee about scale)

Read for this study: Lévy 2024 / TOG 2025 (*Exact predicates, exact constructions and combinatorics for mesh CSG*, arXiv 2405.12949;
full text partly: introduction, 2.1), Attene 2018 (*As-exact-as-possible repair of unprintable STL files*, Rapid Prototyping J.;
arXiv 1605.07829, about half verbatim), abstracts of Attene 2014 (*Direct repair of self-intersecting meshes*, Graphical Models 76),
Cherchi, Livesu, Scateni & Attene 2020 (*Fast and robust mesh arrangements using floating-point arithmetic*, TOG 39(6); full text
not reachable from this container), and the bibliographic records (not the text) of Valque & Lazard 2025 (*Resolving self-intersections in 3D meshes while
preserving floating-point coordinates*, CGF 44(5)) and Guo et al. 2024 (*Exact and efficient intersection resolution for mesh
arrangements*, TOG). #547 section 4 covers Zhou et al. 2016, Attene 2010, TetWild, the alpha wrap
and local feature size.

- **Exact arrangement methods have no tolerance and so no scale** in the intersection step: Lévy represents intersection points
  exactly, merges duplicates by lexicographic sorting and states that converting the exact points back to floating point is not
  addressed; Cherchi et al. use indirect predicates on implicit points; Attene 2014 is a hybrid that falls back to exact arithmetic
  where floating point cannot decide. Their limit is the exponent range of the expansions (Lévy adds multi-precision for overflow and
  underflow), the same domain question as row 1, not a tolerance.
- **The scale enters at the way back to doubles.** Valque & Lazard's title says they resolve self-intersections while keeping
  floating-point coordinates; their method was not read here (unverified: a snap rounding to the doubles, whose grid cell, an ulp,
  is relative to the magnitude, not to the scene). #547 cites Devillers, Lazard & Lenhart 2020 with one uniform grid. This library's
  safe rounding judges size and degeneracy against the largest component (row 12), one of the three places where the measured
  losses happen; per own component is the scale-free variant.
- **Repair methods keep absolute or global thresholds**: Attene 2018 deletes degenerate triangles and thickens sheets to an absolute
  0.4 mm (the printer's nozzle), after normalising models to a 100 mm box (from a summary of its later sections, not verbatim);
  MeshFix (Attene 2010) keeps the largest component (from memory, #547; unverified). No paper found guarantees invariance of a repair under
  scaling or translation, or per-part treatment of scale-disparate scenes (#547 searched four queries; this study added
  "exact repair scale invariant" and the titles above).

How others solve it, in one line: exact arithmetic removes the tolerance from the intersection step; every published pipeline then
either rounds to a uniform grid (doubles' ulp or a global grid) or keeps global thresholds and accepts the loss of small parts. A
per-part repair with a per-component rounding bound is not taken from a paper; its pieces (exact predicates, per-component bounds,
ratio-based separation) are standard.

## 5. Candidate designs

| | What | Wins (measured) | Risk | Effort |
|---|---|---|---|---|
| F1 (report, the open owner decision) | A lost-part check over every separable part (Q's padded rule: closed and open components; a part judged against its own diagonal), run whether or not row 3 fires, reporting `LostFarPart` | the 29 silent losses of 3.1 become `Apply()` false (none is kept) | false reports where the repair rightly removes a separable speck (the floor of row 10 exists for those); the corpus count of true -> false flips decides | S |
| F2 (prevent) | Per-part references in rows 7, 12 (tiny test, bound) and 13, and the far-part mode for open inputs | expected: the parts kept (Q keeps all 54; not measured for F2 itself); Apply() stays false with `ScaleDisparity` while `NotCleanReason` is scene-relative (the D cells) | four stages; the comment at `MeshRoundingRepair.cs:899-912` records a regression when the area reference shrank before; a tighter bound rejects more edits, so more solidify / wrap fallbacks | M (about B1's cost, with less gain) |
| B1 | Part-wise repair, stage 1 (box rule, closed parts), scale-only frames, union verified exactly, merge on conflict (#547 5.2) | P column | merge loop, cost of one fallback per part | M |
| B2 | Stage 2: open components by padded box | Q column | crack merging across parts: pieces closer than the padding are linked, farther ones never merged (today merged within 2e-8 scene) | M on top of B1 |
| D | Opt-in criterion: degeneracy per own component | turns the "D" cells into true | a public status change (owner) | S |
| C | Local tolerances in every stage | S / L columns | invasive, no single place to test | L |

## 6. First cheap experiment, with criteria fixed in advance

F1 on a branch, with the A/B. It is the cheap experiment because it is the only candidate with a measured need, and it is the open
half of an owner decision. Criteria, fixed now:

1. Change (library; coordinator / owner decision first): `MeshRobustRepair` checks, at the end of every `Apply()` that returns true,
   every part of the padded box rule (components linked when their boxes padded by 1e-3 of their own diagonals meet; closed and
   open), and returns false with `LostFarPart` when no result triangle is within 25 % of a part's own diagonal of one of its sampled
   vertices. Independent of row 3 and of `MinVolumeIoU`; inputs with more than 4096 components are not checked (soups).
   `NotCleanReason`, the rounding and the cleanups unchanged. Scope: on a single-part input (667 of the 800) F1 judges the whole input
   against its own diagonal, so it is also a general check against silent success (it may flip the wrap results with IoU 0.07 to
   0.53 of 3.2), not only a scale check; the corpus gate covers that.
2. **Go** (open the bugfix PR) if all hold:
   - on the 54 procedural scenes of 3.1, the 28 losses where a whole part is gone are false with `LostFarPart`, and the 25 cells
     without a loss are unchanged. The 29th (v1, r = 12, f = 8: one of two overlapping boxes lost inside one part, 25.5 % of the
     part's diagonal against the 25 % threshold) is out of F1's scope by design: F1 judges parts, not components inside a part, and
     its outcome there is no criterion;
   - on the 800 corpus meshes (R against F1, same workflow): at most 2 `Apply()` true -> false flips (counted from
     `apply_returned`, not from the strict criterion, which ignores it), the strict criterion unchanged on every mesh (F1 changes no
     mesh), no new timeout, wall time at most +2 %;
   - the tests of CLAUDE.md: the 3.1 scenes as fixed examples that fail on master (one per path: v0, v1 without the switch, v2),
     invariance over `Similarity.All()`, reuse.
3. **No-go** if the corpus flips more than 2: then report the flips; the owner decides between reporting more failures and keeping
   the dust floor.
4. F2 only if the owner wants the parts kept rather than reported; its own A/B (strict criterion, part criterion, fallback count)
   then decides.
5. Effort of F1: S (a day with the corpus A/B; the check is a variant of the probe's measure: per padded part, present when any sample is reached, where the probe takes the farthest sample per component). No exact-arithmetic file changes, so no
   mutation gate before the merge.

### 6.1 F1 as built (owner 2026-11-17: "ja, F1 starten"; F2 not now)

`MeshRobustRepairLostParts`, called at the end of `Apply()` in the working frame. It differs from item 1 above in these points,
decided with the coordinator or found by the tests and the review:
- **Two or more parts only.** An input that is one part by the padded rule is not checked (coordinator), so it is no general check
  against silent success on single-part inputs.
- **Parts of fewer than 4 triangles, or without extent, are not judged.** Stray triangles that the repair removes on purpose are
  not reported.
- **Only with `MinVolumeIoU > 0`.** That is the documented meaning of 0, "no checks against the input", as for the other lost-part
  checks.
- **Reasons.** `LostFarPart` keeps its text and meaning: a closed part lost where the components lie far apart (the far-part switch,
  row 3). The two new constants are additive (coordinator: `LostFarPart` is not widened):
  - `LostFarOpenPart`: a lost open part;
  - `LostSeparatePart`: a lost closed separable part outside the far-part case.

  A reason the stages set (eg an unclean solidify) is kept after `"; "`. g3cli maps the new reasons to `lost_far_open_part` and
  `lost_separate_part`.
- **Visible skips.** Why the check judged nothing is recorded in `LostPartCheckSkipped` (internal, for tests): off, no volume
  checks, empty, one part, too many components, no part judged, not reached.
- **No underflow.** The part's diagonal and reach avoid squaring tiny lengths, so a part of 2^-600 in the frame is judged too.

Procedural criterion (54 scenes of 3.1, with and without the check, one build): 28 cases true -> false (`LostFarPart` /
`LostFarOpenPart`); the 29th, v1 r = 12 f = 8 (one box of two lost inside one part), stays true, out of scope as stated above. The
25 others are unchanged, and no result mesh differs (hash).

Three existing tests had pinned a silent loss with `Apply()` true:
- a `RepairF` that drops a separate small sphere;
- a 1e-4 speck dropped next to a unit sphere;
- a test hook that empties the result of the others.

They now expect the report; the hook test runs with the check off to keep its line covered, and checks the report separately.

**What F1 does not cover.** The R row of the corpus still has 28 meshes with `Apply()` true and a closed component more than 25 %
of its own diagonal from the result (section 3.2):
- 22 of them are one part by the padded rule, so not checked by design;
- the other 6 (39246, 48950, 48954, 50405, 697602, 96307) lose a component inside a part, or part of one, which F1 does not judge
  (it reports a part only when no sample of it is reached).

So F1 closes the silent loss of separable parts, not every component loss. The procedural rows (R / N) are in `study/f1-sweep` on
`runs/scale-free-f1`.

Corpus criterion: run 44161038527 on GitHub-hosted runners, 800 meshes, R (with the check) and N (`NoLostPartCheck`), one build, the
two configurations interleaved per mesh; data branch `runs/scale-free-f1`, `scale_free_study.py f1`.
- `Apply()` true -> false: 0; false -> true: 0.
- Result hash differs: 0, so the strict criterion is unchanged on every mesh.
- Timeouts: 0.
- Wall time: R 4537 s, N 4461 s (+1.7 %, bar +2 %).

That run predates the `MinVolumeIoU` gate and the second constant. Neither changes a default run's decision: `MinVolumeIoU` is
0.99 by default, and the second constant changes only the reason text.

## 7. Recommendation

1. **No larger effort on scale-free repair.** B stage 1 with frames, B stage 2, C and D stay parked. On the corpus they would win 2
   to 7 meshes by this estimate, below the bar fixed in advance (section 3.2), and the corpus has no small separable parts.
   Revisit with a use case of assemblies or scenes with separable parts below about 1e-3 of the scene. The probe and driver on
   `runs/scale-free-study` measure such a cohort in one dispatch.
2. **F1 as a bugfix** (section 6): it closes the open half of the owner's decision 2 of 2026-11-10 (the loss is reported). F2
   (keeping the parts) only on an owner decision.
3. **Hard problem 9** in the roadmap: "study done, no-go for the larger effort; F1 open". The "183 misses" pointer is probably 400 - 217
   of the holdout run of 2026-10-25, before the exact resolve stage (benchmark README, "Holdout selection"; the README does not
   state 183 itself). On master 86e94029 the strict misses are 252 of 800 (123 of
   the first 400, 129 of the holdout). The scale oracles repair at most 5 of them.

## 8. Independent review

An agent that got the diff, the raw rows and the code, not the author's reasoning, reviewed the first version (19 findings). Fixed:
- F was wrong: it reached at most 16 of the 29 losses. 13 are open parts, which the rounding's tiny-component removal and the
  lost-part check skip; 2 have no far-part switch, so no check runs. Traced again (3.1, v2 path added); F split into F1 (report) and F2 (prevent),
  with new criteria.
- The v0 mechanism is the rounding's tiny test, not its drift bound (counterexample sw_v0_r16_f8).
- F's corpus gate counted strict losses, which cannot see `Apply()` true -> false: it now counts the flips. The risks named in the
  review are added: F2 ends in `ScaleDisparity`, not "kept"; the earlier area regression; more fallbacks.
- Wrong numbers corrected: 7 / 9 / 13 per variant, two (not three) shared gains with G, T changes 4 cells, Q has one "m" cell,
  "no oracle" -> "no scale oracle" on the part criterion. The "242 untouched" claim is gone (Q repairs 2 of them).
- Overclaims weakened: "upper bound" / "perfect" (P and Q are estimates, copied where one part), "no disparity" (only between
  separable box-rule parts), S / L as evidence on option C, the reading of the 28 corpus rows (only 17 fit absorption; two eroded
  single components and three wrap results with IoU 0.07 to 0.53 named), criterion 4.
- Overlap with #547 named: the scene family and the D2 decision are #547's; new is the trace after A / G / stage 0 and that stage 0
  does not remove the v0 deletion, against #547 5.2. The corpus count is named as #547's gate for B stage 1; the weld difference is
  stated.
- Prior art: method claims for sources read only as records marked unverified.
- A wrong line number (row 4), the wall column (now R on the same meshes), row 3's "measured" text, the source of "183".
