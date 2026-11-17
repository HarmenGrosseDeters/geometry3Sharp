# Scale-free robust repair: study and go / no-go (hard problem 9, 2026-11-17)

**Question (owner, 2026-11-17).** Is a larger effort on a scale-free `MeshRobustRepair` worth it: how much could a repair whose
cleanups do not depend on the global box win at most, on real data and on scale-disparity cases, and what would it cost?

**Answer.** **No-go for a larger effort**, by criteria fixed before the corpus run (`study/CRITERIA.md` on the data branch). On 800
Thingi10K meshes a perfect part-wise repair gains at most 5 meshes on the strict criterion (and loses 13), at most 2 on a
scale-free part criterion, and 0 net when only closed parts are split. Tolerances x0.01 or x100 and a translation to 5e6 gain
nothing. Real inputs have no scale disparity to speak of: in the 52 inputs with two or more separable parts, the smallest part is at
least 8 % of the scene. Of the 252 strict misses, 242 are Hausdorff misses that no scale oracle touches. The problem is real on
procedural scenes, where 29 of 54 lose a small part with `Apply()` true. That breaks the owner's rule of 2026-11-10 and is a
**bugfix** (F, section 5, size S), not a reason for options B to D. Recommendation in section 7.

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
| 3 | Far-part switch `global_scale_share` (`:1787`): >= 2 components and a triangle below 1e-12 scene^2 | scene | per component: exactly degenerate, or below 1e-12 own^2 | partly: an exact collinearity test exists (`NotCleanReason`'s second test) | corpus: how often it fires on inputs whose small triangles are degenerate by their own component too (section 3.2) |
| 4 | B stage 0, the union check (`:193`): `NotCleanReason` of the union, else the whole input again | scene (via `NotCleanReason`) | judge the union per part (closed, manifold, no intersections exactly; degeneracy per own component) | yes but for the area test | procedural: the fallback that leads to the silent loss of section 3.1 |
| 5 | `MeshAutoRepair` `RelativeRepairTolerance` 1e-8 (crack merge 1x and 2x, boundary collapse 0.5x, `sizeTolerance`, occlusion offset), `RelativeMinEdgeLength` 1e-6 (final collapse), cap 0.01 median edge of the whole mesh; far-part mode: largest (`MeshAutoRepair.cs:102`) | scene / largest | own component, or the local edge length (option C) | no: merging near, not equal, vertices needs a distance | S / L oracles (x0.01, x100) |
| 6 | `MeshRepairOrientation` degenerate area 1e-10 scene^2, ray length 2 scene | scene | exact orientation of each triangle (orient3d); the ray length is harmless | yes for the degeneracy | not separated |
| 7 | `MeshPostRepairCleanup` `DegenerateAreaFactor` 1e-12 `ReferenceDiagonal`^2 (scene, largest in the far-part mode) | scene / largest | own component | yes, as in 6 | inside S / P / Q |
| 8 | `NotCleanReason` (public) degenerate area 1e-12 scene^2 | scene, by definition | option D (opt-in criterion next to it) | yes, as in 6 | column "own-component degeneracy" of section 3 |
| 9 | Volume check: IoU >= 0.99 of the whole solid | scene volume | per part (a part below 1 % of the volume is invisible today) | no (sampling), but a per-part lost check is exact enough | part criterion of section 3 |
| 10 | Lost-part check: closed parts of at least `MinComponentDiagonal` 1e-3 of the largest (`MeshRepairMetrics.cs:702`) | largest | every closed part, judged against its own diagonal | -- | procedural: lets the losses of 3.1 through |
| 11 | Exact resolve (`ExactMeshBoolean.Resolve`) | none | -- | yes | -- |
| 12 | Safe rounding (`MeshRoundingRepair.component_scales`, `:886`): drift bound `MaxDisplacement` 1e-5 x largest for every component (only the ulp term is per component); degenerate area 1e-12 x scene^2, or largest^2 when the scene box exceeds `FarApart` 100 x largest; tiny / thin closed components removed below that bound (`RemoveTinyComponents`) | largest / scene | bound and area per own component: the drift proof is already per component ("edits stay within one component") | the rounding is the step out of exact arithmetic; its edits are exact-checked | procedural: the deletion path of section 3.1 (traced) |
| 13 | After the resolve: `MeshPostRepairCleanup` + `collapse_slivers` (length 1e-4 x scale, closed components below 1e-8 x scale^2 with a sliver removed, `:1553`) | scene / largest | own component | as 6 | inside P / Q |
| 14 | `SeparateTouchingVertices` 1e-4 of the fan distance | local | -- (already local) | -- | -- |
| 15 | Solidify: cell = scene / 128 (adaptive octree, `MeshWindingNumberSolidify.cs:46`) | scene | per part (B), or refinement by local feature size inside a part | no (resampling) | G (256) and P / Q |
| 16 | Alpha wrap: alpha = scene / 300, offset = scene / 3000; `WrapTooCoarse` guard at 5 % of largest (G) | scene | per part (B); adaptive alpha inside a part is research (#547 section 4) | no (resampling) | P / Q |
| 17 | Wrap simplification: bound 0.5 offset | follows 16 | follows 16 | -- | -- |

Reading: after A, the scene-relative quantities that can delete or erode a part are 3, 4, 5, 7, 10, 12, 13, 15 and 16. Of these,
12 (rounding) is where the measured silent losses happen (3.1); 4 and 10 are why they are silent; 5 (the tolerances) changes
nothing measurable (section 3); 15 and 16 are resolution, which part-wise repair fixes between parts and nothing cheap fixes inside
one part.

## 2. Method

**Oracles** (`benchmarks/MeshRepairBenchmark/ScaleFreeProbe.cs` and `scale_free_study.py` on `runs/scale-free-study`; probe code
only on that branch): R the library defaults; P every part of the box rule of option B stage 1 (closed components linked when their
boxes meet; an open component makes the whole input one part) repaired by its own `MeshRobustRepair`, results appended; Q the same
with every component (open ones too) linked by its box padded by 1e-3 of its own diagonal (an upper bound for stage 2); S / L the
relative tolerances of the repair stage times 0.01 / 100; G solidify resolution 256; T the input translated by (5e5, 5e6, 0).
P, Q and G rows are copied from R where they cannot differ (one part; no solidify).

**Measures.** The strict criterion of `criteria.py` (clean, no exact self-intersections, Hausdorff < 1 % of the scene diagonal both
ways, IoU > 0.99), which is itself scene-relative; next to it two scale-free ones: the same criterion with the degenerate area per
own component (option D's view), and a **part criterion**: no closed input component (at least 4 triangles) has a vertex more than
1 % of its own diagonal from the result, nor a result vertex charged to it (nearest input triangle) more than 1 % of its diagonal
away. "Lost" is more than 25 % of the own diagonal.

**Data.** Thingi10K, the first and the holdout selection of `fetch_data.py` (800 meshes), fetched at run time on GitHub-hosted
runners; 24 shards, timeout 600 s per mesh and configuration. Procedural: a unit icosphere (coordinates on 2^-12) plus a part of half
size 2^-r (r = 4 .. 24) at distance 2^f (f = 2, 8, 16): v0 a clean box, v1 two overlapping boxes, v2 an open sphere; run locally
(net10.0 Release, linux x64).

## 3. Upper bound

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

- **Silent loss on master: 29 of 54** (`Apply()` true, a part gone: 8 v0, 8 v1, 13 v2), against the owner's rule of 2026-11-10. One
  case (v1, r = 12, f = 8) even passes the strict criterion with a part lost (a part 2^-12 is below its 1 % Hausdorff).
- **Traced** (a v0 scene with r = 20, f = 12, public API only): B stage 0 passes the box, but the union fails `NotCleanReason` (row 4:
  its triangles are degenerate by the scene), so the input is repaired as a whole. The repair stage's result is not clean
  ("degenerate triangles"), the exact resolve runs, and its safe rounding (`ExactResolveRounding`: 2 moves, `RemovedComponents` 1)
  removes the box: its diagonal is below the drift bound 1e-5 x largest of row 12. The lost-part check skips it (row 10: below
  1e-3 of the largest). `Apply()` true, `ResultReason` "", 12 triangles fewer.
- **P** (closed parts alone) keeps every closed part; **Q** (all components) keeps every part, open ones too: 54 / 54 without a loss,
  and every result that is not strictly clean is clean by own-component degeneracy (**D** in every such cell). So on these scenes a
  scale-free stage wins everything by the part criterion and nothing by the strict criterion: the strict criterion is scene-relative
  (D3 of #547) and calls every far small part degenerate.
- **S** (tolerances x 0.01) and **T** (translation) change nothing; T's three "nc" are a 2^-24 part at 5e6, about 2^-46 of its
  magnitude, near the resolution of the doubles (row 1's limit is 2^-40 for the whole scene, not per part).

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
| smallest part of a multi-part input (box rule), share of the scene diagonal | | | >= 0.085 in all 52 |
| far-part switch fires (row 3) | 125 | 109 | 234 |
| ... with a triangle degenerate by the scene but not by its component | 31 | 26 | 57 |
| far-part mode used (`FarPartsHandled`) | 10 | 5 | 15 |
| B stage 0 passed parts through | 25 | 27 | 52 |
| `Apply()` false, `LostFarPart` | | | 2 |

| oracle | run / copied | repaired | gained | lost | net | repaired, own-component degeneracy | part criterion ok (R: 678) | wall (s; R 4644) |
|---|---|---|---|---|---|---|---|---|
| P (closed parts alone) | 52 / 748 | 548 | 2 | 2 | 0 | 550 | 677 | 275 |
| Q (all components, padded) | 133 / 667 | 540 | 5 | 13 | -8 | 554 | 677 | 1064 |
| S (tolerances x0.01) | 800 / 0 | 548 | 0 | 0 | 0 | 548 | 678 | 4599 |
| L (tolerances x100) | 800 / 0 | 545 | 0 | 3 | -3 | 545 | 676 | 4582 |
| G (solidify 256) | 108 / 692 | 553 | 5 | 0 | +5 | 553 | 681 | 1981 |
| T (translated to 5e6) | 800 / 0 | 547 | 0 | 1 | -1 | 547 | 677 | 4583 |

- **Gains of the part oracles**: 100032 and 95777 (P and Q: the solidify of the whole had IoU < 0.99; per part it passes), 1619332
  and 56535 (Q: exact path, Hausdorff), 697602 (Q). G gains 100032 and 697602 too: three of the five scale gains are also
  resolution gains. **Losses of Q**: 12 of 13 are "not clean (degenerate by the scene only)". The per-part results are clean, but
  the union of parts at their own scale has triangles below 1e-12 of the scene's squared diagonal. That is D3 of #547, the public
  criterion. The 13th, 63467, is a Hausdorff loss: open pieces repaired apart are no longer merged with their neighbours.
- **Part criterion**: no oracle gains more than 2 (Q 2 / 3 lost, P 0 / 1). R fails it on 122 meshes, almost all inside one part.
- **Silent losses on the corpus**: 28 meshes return `Apply()` true with a closed component more than 25 % of its own diagonal from
  the result. All 28 are one part by the box rule (overlapping or nested boxes), mostly with IoU >= 0.99 (eg 50405: 68 of 70
  components, IoU 1.0000). That fits components absorbed into the union, which is correct. None is a separable part (0 among the
  52 multi-part inputs). The 28 were not inspected one by one.
- **Tolerances**: S changes no outcome; L loses 3 (eg 318843: exact -> solidify). The global tolerances are not what fails on these
  inputs.
- **Translation**: the strict outcome differs on 1 mesh (1037029, exact -> solidify, IoU), the path on 7 (#547 section 3.2 measured
  the resampling stages' translation dependence).
- **Against the criteria**: 1 no-go (P / Q gain 2 / 5 < 8, part criterion 0 / 2 < 8); 2 no-go (S 0, L -3); 3 no follow-up (1 < 4);
  4 G gains as much as all scale oracles together (5 = 5), so neither scale nor resolution is the lever on the misses; 5 the
  procedural silent losses are a bugfix.
- **What the 242 Hausdorff misses are** is outside this study. 136 of them are on the exact path, which keeps the geometry, so the
  deviation is to parts of the input surface that the solid does not contain. #547 and the benchmark README list such cases
  (fins, inner shells); the rest are solidify and wrap resampling.

## 4. Prior art (papers only; what they guarantee about scale)

Read for this study: Lévy 2024 / TOG 2025 (*Exact predicates, exact constructions and combinatorics for mesh CSG*, arXiv 2405.12949;
full text partly: introduction, 2.1), Attene 2018 (*As-exact-as-possible repair of unprintable STL files*, Rapid Prototyping J.;
arXiv 1605.07829, about half verbatim), abstracts of Attene 2014 (*Direct repair of self-intersecting meshes*, Graphical Models 76),
Cherchi, Livesu, Scateni & Attene 2020 (*Fast and robust mesh arrangements using floating-point arithmetic*, TOG 39(6); full text
not reachable from this container), Valque & Lazard 2025 (*Resolving self-intersections in 3D meshes while preserving
floating-point coordinates*, CGF 44(5); bibliographic record only) and Guo et al. 2024 (*Exact and efficient intersection
resolution for mesh arrangements*, TOG; title only). #547 section 4 covers Zhou et al. 2016, Attene 2010, TetWild, the alpha wrap
and local feature size.

- **Exact arrangement methods have no tolerance and so no scale** in the intersection step: Lévy represents intersection points
  exactly, merges duplicates by lexicographic sorting and states that converting the exact points back to floating point is not
  addressed; Cherchi et al. use indirect predicates on implicit points; Attene 2014 is a hybrid that falls back to exact arithmetic
  where floating point cannot decide. Their limit is the exponent range of the expansions (Lévy adds multi-precision for overflow and
  underflow), the same domain question as row 1, not a tolerance.
- **The scale enters at the way back to doubles.** Valque & Lazard round the resolved mesh back to floating-point coordinates
  (iterated snap rounding on the doubles' grid, whose cell is an ulp, i.e. relative to the magnitude, not to the scene); #547 cites
  Devillers, Lazard & Lenhart 2020 with one uniform grid. This library's safe rounding has a drift bound relative to the largest
  component (row 12), which is exactly where the measured losses happen: a per-component bound is the scale-free variant, and the
  ulp grid of Valque & Lazard is the scale-free limit.
- **Repair methods keep absolute or global thresholds**: Attene 2018 deletes degenerate triangles and thickens sheets to an absolute
  0.4 mm (the printer's nozzle), after normalising models to a 100 mm box (from a summary of its later sections, not verbatim);
  MeshFix (Attene 2010) keeps the largest component (from memory, #547). No paper found guarantees invariance of a repair under
  scaling or translation, or per-part treatment of scale-disparate scenes (#547 searched four queries; this study added
  "exact repair scale invariant" and the titles above).

How others solve it, in one line: exact arithmetic removes the tolerance from the intersection step; every published pipeline then
either rounds to a uniform grid (doubles' ulp or a global grid) or keeps global thresholds and accepts the loss of small parts. A
per-part repair with a per-component rounding bound is not taken from a paper; its pieces (exact predicates, per-component bounds,
ratio-based separation) are standard.

## 5. Candidate designs

| | What | Wins (measured) | Risk | Effort |
|---|---|---|---|---|
| F (fix, not the larger effort) | No silent loss: the rounding's drift bound and degenerate area per own component (row 12); the lost-part check without the 1e-3 floor for closed parts (row 10), reporting `LostFarPart` | the 29 silent losses of 3.1 become kept parts or reported failures | the bound per component lets a small component be edited at its own scale only: no weaker certificate (the proof is per component); a tiny real sliver component between two solids is no longer removed when it is below the largest's bound but above its own: needs the corpus A/B | S |
| B1 | Part-wise repair, stage 1 (box rule, closed parts), scale-only frames, union verified exactly, merge on conflict (#547 5.2) | P column | merge loop, cost of one fallback per part | M |
| B2 | Stage 2: open components by padded box | Q column | crack merging across parts: pieces closer than the padding are linked, farther ones never merged (today merged within 2e-8 scene) | M on top of B1 |
| D | Opt-in criterion: degeneracy per own component | turns the "D" cells into true | a public status change (owner) | S |
| C | Local tolerances in every stage | S / L columns | invasive, no single place to test | L |

## 6. First cheap experiment, with criteria fixed in advance

The bugfix F, as a branch with the A/B. It is the cheap experiment because it is the only candidate with a measured win.
Criteria, fixed now:

1. Change (library, owner / coordinator decision first): in `MeshRoundingRepair.component_scales`, the drift bound and the
   size-area of the tiny test per own component (the ulp term already is); in `MeshRobustRepair.lost_part`, every closed
   component judged against its own size (no 1e-3-of-largest floor), reporting `LostFarPart`. B stage 0's union check and
   `NotCleanReason` unchanged.
2. **Go** (open the bugfix PR) if all hold:
   - on the 54 procedural scenes of 3.1, no `Apply()` true with a lost part; each case is kept or false with `LostFarPart`;
   - on the 800 corpus meshes (R against F, same workflow), at most 2 strict losses, each explained; no new timeout; wall time
     at most +5 %;
   - the tests of CLAUDE.md: the 3.1 scenes as fixed examples that fail on master, invariance over `Similarity.All()`, reuse.
3. **No-go** for the rounding part if the corpus loses more than 2. Then only the lost-part check without the floor goes in, so
   the loss is reported (`Apply()` false), not prevented.
4. Effort: S (one to two days with the corpus A/B). Mutation gate: the incremental run, since `MeshRoundingRepair` is part of the
   exact pipeline.

## 7. Recommendation

1. **No larger effort on scale-free repair.** B stage 1 with frames, B stage 2, C and D stay parked: the corpus has no inputs they
   would win (section 3.2), and the procedural cases are covered by F. Revisit only with a use case that has assemblies or scenes
   with parts below about 1e-3 of the scene. The probe and driver on `runs/scale-free-study` can measure such a cohort in one
   dispatch.
2. **F as a bugfix** (section 6): the silent loss is against an owner decision and has a short traced cause.
3. **Hard problem 9 closed** in the roadmap as "no-go for the larger effort (this note); bugfix F open". The "183 misses" pointer is
   stale: on master the strict misses are 252 of 800 (123 of the first 400, 129 of the holdout). What drives them is resampling
   fidelity and the criterion's reference surface, not scale.

## 8. Independent review

REVIEW
